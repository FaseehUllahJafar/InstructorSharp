using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace InstructorSharp.Tests.Fakes;

/// <summary>
/// A chat client that replays a scripted list of responses and records every request it was
/// given. Lets the repair loop be tested deterministically: the script says "answer badly,
/// then answer well" and the assertions check that the second request actually contained the
/// validation errors from the first.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse>> _script = new();
    private readonly ChatClientMetadata _metadata;

    internal FakeChatClient(string providerName = "test")
        => _metadata = new ChatClientMetadata(providerName, new Uri("https://example.test"), "test-model");

    /// <summary>Every request the client received, in order.</summary>
    internal List<RecordedCall> Calls { get; } = [];

    /// <summary>Number of times the client was asked for a response.</summary>
    internal int CallCount => Calls.Count;

    /// <summary>Queues a raw text response.</summary>
    internal FakeChatClient RespondWith(string text, long inputTokens = 0, long outputTokens = 0)
    {
        _script.Enqueue((_, _) =>
        {
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
            if (inputTokens > 0 || outputTokens > 0)
            {
                response.Usage = new UsageDetails
                {
                    InputTokenCount = inputTokens,
                    OutputTokenCount = outputTokens,
                };
            }

            return response;
        });

        return this;
    }

    /// <summary>Queues a response delivered as a tool call, the way Anthropic answers.</summary>
    internal FakeChatClient RespondWithToolCall(string toolName, IDictionary<string, object?> arguments)
    {
        _script.Enqueue((_, _) => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", toolName, arguments)])));

        return this;
    }

    /// <summary>Queues a transport failure.</summary>
    internal FakeChatClient Throws(Exception exception)
    {
        _script.Enqueue((_, _) => throw exception);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        Calls.Add(new RecordedCall(snapshot, options));

        if (_script.Count == 0)
        {
            throw new InvalidOperationException(
                $"FakeChatClient ran out of scripted responses on call {Calls.Count}.");
        }

        return Task.FromResult(_script.Dequeue()(snapshot, options));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);

        // Deliver the scripted text in small slices so partial-JSON handling is genuinely exercised.
        string text = response.Text;
        const int ChunkSize = 7;

        for (int i = 0; i < text.Length; i += ChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                text.Substring(i, Math.Min(ChunkSize, text.Length - i)));
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata) ? _metadata : null;

    public void Dispose()
    {
    }

    internal sealed record RecordedCall(List<ChatMessage> Messages, ChatOptions? Options)
    {
        /// <summary>The text of the last message sent, which is the repair prompt on a retry.</summary>
        internal string LastMessageText => Messages.Count == 0 ? string.Empty : Messages[^1].Text;

        /// <summary>All message text concatenated, for coarse assertions.</summary>
        internal string AllText => string.Join("\n", Messages.Select(m => m.Text));
    }
}
