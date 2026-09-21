using System.Text.Json;
using Microsoft.Extensions.AI;

namespace InstructorSharp.Modes;

/// <summary>
/// Shapes an outgoing request so a given provider returns JSON, and pulls the JSON back out
/// of the response.
/// </summary>
/// <remarks>
/// This seam is public deliberately. Provider behaviour is the part of structured extraction
/// that rots fastest -- a model family changes what it accepts and the library is wrong until
/// someone ships a release. Implementing this and registering it on
/// <see cref="InstructorBuilder"/> lets you fix that in your own codebase the same afternoon,
/// without waiting for us.
/// <para>
/// A single instance is shared by every caller of the owning <see cref="IInstructor"/> and may be
/// invoked concurrently. Implementations must be thread-safe and must hold no per-call state.
/// Only <c>virtual</c> members will be added to this type in future versions, so a subclass will
/// not break on upgrade.
/// </para>
/// </remarks>
public abstract class ExtractionStrategy
{
    /// <summary>The mode this strategy implements.</summary>
    public abstract ExtractionMode Mode { get; }

    /// <summary>
    /// How strongly this strategy should be preferred when several can serve a client.
    /// Higher wins. The built-in strategies use 100 for native schema support down to 10
    /// for the universal prompted fallback.
    /// </summary>
    public abstract int Priority { get; }

    /// <summary>
    /// Whether this strategy's output arrives as streamed text, and so can serve
    /// <see cref="IInstructor.StreamAsync{T}"/>. Defaults to true for every mode except tool
    /// calling, whose partial arguments providers deliver inconsistently.
    /// </summary>
    public virtual bool SupportsStreaming => Mode != ExtractionMode.ToolCall;

    /// <summary>
    /// Whether this strategy can serve the given client, judged from its advertised metadata.
    /// </summary>
    /// <param name="metadata">Client metadata, which may be null when the client reports none.</param>
    /// <returns>True when this strategy is applicable.</returns>
    public abstract bool CanHandle(ChatClientMetadata? metadata);

    /// <summary>
    /// Mutates the request so the model is asked for the schema, for example by setting a
    /// response format or declaring a forced tool.
    /// </summary>
    /// <param name="context">The request being prepared.</param>
    public abstract void Apply(StrategyContext context);

    /// <summary>
    /// Pulls the JSON payload out of a response. The default reads the response text, which
    /// is correct for every mode except tool calling.
    /// </summary>
    /// <param name="response">The model response.</param>
    /// <returns>The JSON text, or null when the response carried none.</returns>
    public virtual string? ExtractPayload(ChatResponse response) => response.Text;

    /// <summary>
    /// Renders the schema as prompt text for modes that cannot attach it to the request.
    /// </summary>
    /// <param name="schemaText">The JSON schema.</param>
    /// <returns>An instruction block to append to the conversation.</returns>
    protected static string DescribeSchema(string schemaText) =>
        "Respond with a single JSON value that conforms to this JSON Schema. " +
        "Output JSON only, with no commentary and no markdown fence.\n\n" +
        "JSON Schema:\n" + schemaText;
}

/// <summary>
/// The mutable request being prepared for one attempt. Strategies write to
/// <see cref="Messages"/> and <see cref="ChatOptions"/>.
/// </summary>
public sealed class StrategyContext
{
    /// <summary>Creates a context. Public so that custom strategies can be unit tested.</summary>
    /// <param name="messages">The messages for this attempt.</param>
    /// <param name="chatOptions">Options for the call.</param>
    /// <param name="schema">The generated JSON schema.</param>
    /// <param name="schemaText">The schema, serialized.</param>
    /// <param name="schemaName">A provider-safe schema name.</param>
    /// <param name="targetType">The CLR type being extracted.</param>
    public StrategyContext(
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        JsonElement schema,
        string schemaText,
        string schemaName,
        Type targetType)
    {
        Messages = messages;
        ChatOptions = chatOptions;
        Schema = schema;
        SchemaText = schemaText;
        SchemaName = schemaName;
        TargetType = targetType;
    }

    /// <summary>
    /// The messages for this one attempt. This is a fresh copy per attempt, so anything a
    /// strategy adds here is discarded before the next one.
    /// </summary>
    public IList<ChatMessage> Messages { get; }

    /// <summary>
    /// Adds a system-level instruction, placing it with any system messages the caller already
    /// supplied rather than at the end of the conversation.
    /// </summary>
    /// <param name="text">The instruction.</param>
    /// <remarks>
    /// Strategies must add instructions through this rather than appending directly. On a repair
    /// attempt the conversation ends with the model's rejected answer and the correction request,
    /// and dropping a schema dump after those buries the very thing the model needs to act on.
    /// </remarks>
    public void AddSystemInstruction(string text)
    {
        int index = 0;
        while (index < Messages.Count && Messages[index].Role == ChatRole.System)
        {
            index++;
        }

        Messages.Insert(index, new ChatMessage(ChatRole.System, text));
    }

    /// <summary>Options for the call, which the strategy may set a response format or tools on.</summary>
    public ChatOptions ChatOptions { get; }

    /// <summary>The generated JSON schema.</summary>
    public JsonElement Schema { get; }

    /// <summary>The generated JSON schema, already serialized.</summary>
    public string SchemaText { get; }

    /// <summary>A provider-safe name for the schema.</summary>
    public string SchemaName { get; }

    /// <summary>The CLR type being extracted.</summary>
    public Type TargetType { get; }
}
