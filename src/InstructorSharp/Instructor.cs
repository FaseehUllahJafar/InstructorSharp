using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using InstructorSharp.Diagnostics;
using InstructorSharp.Modes;
using InstructorSharp.Parsing;
using InstructorSharp.Retry;
using InstructorSharp.Schema;
using InstructorSharp.Validation;
using Microsoft.Extensions.AI;

namespace InstructorSharp;

/// <summary>
/// The default <see cref="IInstructor"/>. Wraps any <see cref="IChatClient"/>.
/// </summary>
public sealed class Instructor : IInstructor
{
    private readonly IChatClient _client;
    private readonly InstructorOptions _defaults;
    private readonly IReadOnlyList<ExtractionStrategy> _strategies;
    private readonly IReadOnlyList<object> _validators;

    /// <summary>
    /// Creates an instructor over a chat client using the default strategies.
    /// </summary>
    /// <param name="client">The underlying chat client.</param>
    /// <param name="options">Defaults for every call, or null for the built-in defaults.</param>
    public Instructor(IChatClient client, InstructorOptions? options = null)
        : this(client, options, strategies: null, validators: null)
    {
    }

    internal Instructor(
        IChatClient client,
        InstructorOptions? options,
        IReadOnlyList<ExtractionStrategy>? strategies,
        IReadOnlyList<object>? validators)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _defaults = options ?? new InstructorOptions();
        _validators = validators ?? Array.Empty<object>();
        _strategies = strategies ?? DefaultStrategies;
    }

    internal static IReadOnlyList<ExtractionStrategy> DefaultStrategies { get; } =
    [
        new JsonSchemaStrategy(),
        new ToolCallStrategy(),
        new JsonModeStrategy(),
        new MarkdownJsonStrategy(),
    ];

    /// <inheritdoc />
    public async Task<T> ExtractAsync<T>(
        IEnumerable<ChatMessage> messages,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ExtractionResult<T> result = await TryExtractAsync<T>(messages, options, cancellationToken)
            .ConfigureAwait(false);

        return result.ValueOrThrow();
    }

    /// <inheritdoc />
    public async Task<ExtractionResult<T>> TryExtractAsync<T>(
        IEnumerable<ChatMessage> messages,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (messages is null)
        {
            throw new ArgumentNullException(nameof(messages));
        }

        InstructorOptions effective = options ?? _defaults;
        SchemaDescriptor schema = SchemaGenerator.For(typeof(T), effective);
        ExtractionStrategy strategy = SelectStrategy(effective.Mode);
        JsonTypeInfo<T> typeInfo = ResolveTypeInfo<T>(effective);

        var conversation = new List<ChatMessage>(messages);
        var attempts = new List<ExtractionAttempt>(effective.MaxAttempts);
        long tokensSpent = 0;

        using Activity? activity = InstructorDiagnostics.StartExtraction(typeof(T), strategy.Mode);

        for (int attemptNumber = 1; attemptNumber <= effective.MaxAttempts; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (effective.TokenBudget is { } budget && tokensSpent >= budget)
            {
                InstructorDiagnostics.RecordOutcome(activity, succeeded: false, attempts.Count);
                throw new TokenBudgetExceededException(tokensSpent, budget);
            }

            ChatOptions chatOptions = BuildChatOptions(effective);

            // The strategy gets a copy, not the durable history. Letting it write to the
            // history would re-append the schema instruction on every retry, so a three-attempt
            // extraction would pay for the schema three times and bury the repair request
            // underneath it.
            var attemptMessages = new List<ChatMessage>(conversation);

            var context = new StrategyContext(
                attemptMessages,
                chatOptions,
                schema.Schema,
                schema.SchemaText,
                effective.SchemaName ?? SanitizeSchemaName(typeof(T).Name),
                typeof(T));

            strategy.Apply(context);

            ChatResponse response;
            try
            {
                response = await _client.GetResponseAsync(attemptMessages, chatOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A transport failure is not something the model can repair, so it ends the
                // attempt loop rather than burning the remaining budget on a dead endpoint.
                attempts.Add(new ExtractionAttempt(
                    attemptNumber, strategy.Mode, rawResponse: null,
                    Array.Empty<ValidationFailure>(), ex, 0, 0));

                InstructorDiagnostics.RecordOutcome(activity, succeeded: false, attempts.Count);
                return new ExtractionResult<T>(default, succeeded: false, attempts);
            }

            long inputTokens = response.Usage?.InputTokenCount ?? 0;
            long outputTokens = response.Usage?.OutputTokenCount ?? 0;
            tokensSpent += inputTokens + outputTokens;

            string? payload = strategy.ExtractPayload(response);

            AttemptOutcome<T> outcome = await InterpretAsync(payload, schema, typeInfo, effective, cancellationToken)
                .ConfigureAwait(false);

            attempts.Add(new ExtractionAttempt(
                attemptNumber, strategy.Mode, payload, outcome.Failures, outcome.Exception,
                inputTokens, outputTokens));

            if (outcome.Succeeded)
            {
                InstructorDiagnostics.RecordOutcome(activity, succeeded: true, attempts.Count);
                return new ExtractionResult<T>(outcome.Value, succeeded: true, attempts);
            }

            if (attemptNumber == effective.MaxAttempts)
            {
                break;
            }

            AppendRepairTurn(conversation, payload, outcome);
        }

        InstructorDiagnostics.RecordOutcome(activity, succeeded: false, attempts.Count);
        return new ExtractionResult<T>(default, succeeded: false, attempts);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<T> StreamAsync<T>(
        IEnumerable<ChatMessage> messages,
        InstructorOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (messages is null)
        {
            throw new ArgumentNullException(nameof(messages));
        }

        InstructorOptions effective = options ?? _defaults;
        SchemaDescriptor schema = SchemaGenerator.For(typeof(T), effective);
        ExtractionStrategy strategy = SelectStreamingStrategy(effective.Mode);
        JsonTypeInfo<T> typeInfo = ResolveTypeInfo<T>(effective);

        var attemptMessages = new List<ChatMessage>(messages);
        ChatOptions chatOptions = BuildChatOptions(effective);

        strategy.Apply(new StrategyContext(
            attemptMessages, chatOptions, schema.Schema, schema.SchemaText,
            effective.SchemaName ?? SanitizeSchemaName(typeof(T).Name), typeof(T)));

        var buffer = new StreamingJsonBuffer();
        string lastEmitted = string.Empty;

        await foreach (ChatResponseUpdate update in
            _client.GetStreamingResponseAsync(attemptMessages, chatOptions, cancellationToken).ConfigureAwait(false))
        {
            string chunk = update.Text ?? string.Empty;
            if (chunk.Length == 0)
            {
                continue;
            }

            buffer.Append(chunk);

            if (!buffer.TryComplete(out string completed))
            {
                continue;
            }

            // Re-emitting an identical snapshot is noise for the consumer.
            if (string.Equals(completed, lastEmitted, StringComparison.Ordinal))
            {
                continue;
            }

            lastEmitted = completed;

            if (TryDeserializeSnapshot(completed, schema, typeInfo, out T? snapshot))
            {
                yield return snapshot!;
            }
        }

        // The final value is the only one that gets validated, and it is always emitted.
        string finalText = buffer.GetText();
        if (!JsonExtractor.TryExtract(finalText, out string finalJson))
        {
            throw new ExtractionFailedException(typeof(T),
            [
                new ExtractionAttempt(1, strategy.Mode, finalText,
                    [new ValidationFailure("$", "the streamed response contained no JSON document")],
                    null, 0, 0),
            ]);
        }

        T final = Deserialize(finalJson, schema, typeInfo);
        IReadOnlyList<ValidationFailure> failures = await ValidateAsync(final, effective, cancellationToken)
            .ConfigureAwait(false);

        if (failures.Count > 0)
        {
            throw new ExtractionFailedException(typeof(T),
            [
                new ExtractionAttempt(1, strategy.Mode, finalText, failures, null, 0, 0),
            ]);
        }

        if (!string.Equals(finalJson, lastEmitted, StringComparison.Ordinal))
        {
            yield return final;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<T> StreamListAsync<T>(
        IEnumerable<ChatMessage> messages,
        InstructorOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        InstructorOptions effective = options ?? _defaults;
        int emitted = 0;
        List<T>? latest = null;

        await foreach (List<T> snapshot in
            StreamAsync<List<T>>(messages, effective, cancellationToken).ConfigureAwait(false))
        {
            latest = snapshot;

            // Mid-stream, the last element is still being written; only the ones behind it are
            // settled, so they are the only ones safe to hand out.
            int settled = snapshot.Count - 1;
            for (; emitted < settled; emitted++)
            {
                yield return snapshot[emitted];
            }
        }

        // The stream has ended and the final snapshot has been validated, so the element that
        // was being withheld is now settled too. Without this the last item is silently dropped.
        if (latest is not null)
        {
            for (; emitted < latest.Count; emitted++)
            {
                yield return latest[emitted];
            }
        }
    }

    /// <summary>
    /// Picks a strategy whose output arrives as streamed text.
    /// </summary>
    /// <remarks>
    /// Tool calling is excluded from streaming on purpose. Providers deliver partial tool
    /// arguments inconsistently -- some send JSON fragments, some re-send the whole argument
    /// object each update -- so appending what arrives produces either duplicated or corrupt
    /// JSON depending on who is on the other end. Rather than guess, streaming drops to the
    /// strongest text-producing mode the provider supports, which is deterministic and correct
    /// everywhere. An explicitly requested mode is still honoured.
    /// </remarks>
    private ExtractionStrategy SelectStreamingStrategy(ExtractionMode mode)
    {
        ExtractionStrategy selected = SelectStrategy(mode);

        if (mode != ExtractionMode.Auto || selected.Mode != ExtractionMode.ToolCall)
        {
            return selected;
        }

        var metadata = _client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;

        ExtractionStrategy? best = null;
        foreach (ExtractionStrategy candidate in _strategies)
        {
            if (candidate.Mode == ExtractionMode.ToolCall)
            {
                continue;
            }

            if (candidate.CanHandle(metadata) && (best is null || candidate.Priority > best.Priority))
            {
                best = candidate;
            }
        }

        return best ?? selected;
    }

    private bool TryDeserializeSnapshot<T>(
        string json,
        SchemaDescriptor schema,
        JsonTypeInfo<T> typeInfo,
        out T? snapshot)
    {
        try
        {
            snapshot = Deserialize(json, schema, typeInfo);
            return snapshot is not null;
        }
        catch (JsonException)
        {
            // A snapshot that cannot yet be shaped into T is simply not ready; the next chunk
            // will carry more of it.
            snapshot = default;
            return false;
        }
        catch (NotSupportedException)
        {
            snapshot = default;
            return false;
        }
    }

    private async ValueTask<AttemptOutcome<T>> InterpretAsync<T>(
        string? payload,
        SchemaDescriptor schema,
        JsonTypeInfo<T> typeInfo,
        InstructorOptions options,
        CancellationToken cancellationToken)
    {
        if (!JsonExtractor.TryExtract(payload, out string json))
        {
            return AttemptOutcome<T>.Failed(
                [new ValidationFailure("$", "the response contained no JSON document")],
                exception: null,
                parseFailureReason: "no JSON document could be found in the response");
        }

        T value;
        try
        {
            value = Deserialize(json, schema, typeInfo);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return AttemptOutcome<T>.Failed(
                [new ValidationFailure("$", ex.Message)],
                ex,
                parseFailureReason: ex.Message);
        }

        if (value is null)
        {
            return AttemptOutcome<T>.Failed(
                [new ValidationFailure("$", "the response deserialized to null")],
                exception: null,
                parseFailureReason: "the response deserialized to null");
        }

        IReadOnlyList<ValidationFailure> failures =
            await ValidateAsync(value, options, cancellationToken).ConfigureAwait(false);

        return failures.Count == 0
            ? AttemptOutcome<T>.Ok(value)
            : AttemptOutcome<T>.Failed(failures, exception: null, parseFailureReason: null);
    }

    private static T Deserialize<T>(string json, SchemaDescriptor schema, JsonTypeInfo<T> typeInfo)
    {
        if (!schema.IsEnveloped)
        {
            return JsonSerializer.Deserialize(json, typeInfo)!;
        }

        using JsonDocument document = JsonDocument.Parse(json);

        // Models sometimes ignore the envelope and return the bare value; accept either.
        JsonElement inner = document.RootElement.ValueKind == JsonValueKind.Object &&
                            document.RootElement.TryGetProperty(SchemaDescriptor.EnvelopePropertyName, out JsonElement wrapped)
            ? wrapped
            : document.RootElement;

        return JsonSerializer.Deserialize(inner.GetRawText(), typeInfo)!;
    }

    private async ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync<T>(
        T value,
        InstructorOptions options,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

        if (options.ValidateDataAnnotations)
        {
            failures.AddRange(RunDataAnnotations(value, options));
        }

        foreach (object candidate in _validators)
        {
            if (candidate is IInstructorValidator<T> validator)
            {
                IReadOnlyList<ValidationFailure> custom =
                    await validator.ValidateAsync(value, cancellationToken).ConfigureAwait(false);

                failures.AddRange(custom);
            }
        }

        return failures;
    }

    /// <summary>
    /// Isolates the one reflective call in the library so the trim warning stops here instead of
    /// spreading to every caller of <see cref="ExtractAsync{T}"/>. This is sound because the
    /// behaviour is opt-out: an AOT consumer sets
    /// <see cref="InstructorOptions.ValidateDataAnnotations"/> to false and never reaches this
    /// line, which is what the property documentation tells them to do.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Guarded by InstructorOptions.ValidateDataAnnotations, documented as off for trimmed apps.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050",
        Justification = "Guarded by InstructorOptions.ValidateDataAnnotations, documented as off for AOT apps.")]
    private static IReadOnlyList<ValidationFailure> RunDataAnnotations<T>(T value, InstructorOptions options)
    {
        var validator = new DataAnnotationsValidator(options.SerializerOptions, options.MaxValidationDepth);
        return validator.Validate(value);
    }

    private static void AppendRepairTurn<T>(List<ChatMessage> conversation, string? payload, AttemptOutcome<T> outcome)
    {
        // The model's own words go back as an assistant turn so the next call reads as a
        // correction of something it said, not as a fresh request.
        conversation.Add(new ChatMessage(ChatRole.Assistant, payload ?? string.Empty));

        string repair = outcome.ParseFailureReason is { } reason
            ? RepairPromptBuilder.BuildParseRepair(payload, reason)
            : RepairPromptBuilder.BuildValidationRepair(outcome.Failures);

        conversation.Add(new ChatMessage(ChatRole.User, repair));
    }

    /// <summary>
    /// Produces a fresh <see cref="ChatOptions"/> per attempt. It has to be per attempt and it
    /// has to be a copy: strategies mutate it (setting tools, response formats), and reusing one
    /// instance would leak a forced tool call from one call into the next.
    /// </summary>
    private static ChatOptions BuildChatOptions(InstructorOptions options) =>
        options.ChatOptions?.Clone() ?? new ChatOptions();

    private ExtractionStrategy SelectStrategy(ExtractionMode mode)
    {
        if (mode != ExtractionMode.Auto)
        {
            foreach (ExtractionStrategy strategy in _strategies)
            {
                if (strategy.Mode == mode)
                {
                    return strategy;
                }
            }

            throw new InstructorException($"No strategy is registered for mode {mode}.");
        }

        var metadata = _client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;

        ExtractionStrategy? best = null;
        foreach (ExtractionStrategy strategy in _strategies)
        {
            if (strategy.CanHandle(metadata) && (best is null || strategy.Priority > best.Priority))
            {
                best = strategy;
            }
        }

        // MarkdownJsonStrategy handles everything, so this only trips if someone registers a
        // custom strategy set with no universal fallback in it.
        return best ?? throw new InstructorException(
            "No extraction strategy could handle this client. Register a fallback strategy, " +
            "or set InstructorOptions.Mode explicitly.");
    }

    private static JsonTypeInfo<T> ResolveTypeInfo<T>(InstructorOptions options)
    {
        // Going through JsonTypeInfo rather than the reflection-based Deserialize overload is
        // what lets a caller supply a source-generated context and stay trimming-safe.
        if (options.SerializerOptions.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> typeInfo)
        {
            return typeInfo;
        }

        throw new InstructorException(
            $"No JSON contract is available for {typeof(T).Name}. When using a source-generated " +
            "JsonSerializerContext, add [JsonSerializable(typeof(" + typeof(T).Name + "))] to it.");
    }

    /// <summary>
    /// Providers restrict schema names to identifier characters; a generic type's CLR name
    /// contains backticks and brackets that would be rejected.
    /// </summary>
    private static string SanitizeSchemaName(string name)
    {
        // Providers cap schema names well below this; the limit also keeps the stackalloc
        // bounded rather than trusting the length of an arbitrary generated type name.
        const int MaxLength = 64;

        if (name.Length > MaxLength)
        {
            name = name.Substring(0, MaxLength);
        }

        Span<char> buffer = stackalloc char[name.Length];
        int length = 0;

        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
            {
                buffer[length++] = c;
            }
        }

        return length == 0 ? "response" : buffer.Slice(0, length).ToString();
    }

    private readonly struct AttemptOutcome<T>
    {
        private AttemptOutcome(
            bool succeeded,
            T? value,
            IReadOnlyList<ValidationFailure> failures,
            Exception? exception,
            string? parseFailureReason)
        {
            Succeeded = succeeded;
            Value = value;
            Failures = failures;
            Exception = exception;
            ParseFailureReason = parseFailureReason;
        }

        internal bool Succeeded { get; }

        internal T? Value { get; }

        internal IReadOnlyList<ValidationFailure> Failures { get; }

        internal Exception? Exception { get; }

        /// <summary>Set when the answer was unreadable rather than merely invalid.</summary>
        internal string? ParseFailureReason { get; }

        internal static AttemptOutcome<T> Ok(T value) =>
            new(true, value, Array.Empty<ValidationFailure>(), null, null);

        internal static AttemptOutcome<T> Failed(
            IReadOnlyList<ValidationFailure> failures,
            Exception? exception,
            string? parseFailureReason) =>
            new(false, default, failures, exception, parseFailureReason);
    }
}
