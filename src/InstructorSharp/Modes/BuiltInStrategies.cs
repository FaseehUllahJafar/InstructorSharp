using System.Text.Json;
using Microsoft.Extensions.AI;

namespace InstructorSharp.Modes;

/// <summary>
/// Attaches the schema as a native response format. The strongest mode where it is supported:
/// the provider constrains decoding, so the shape is guaranteed rather than requested.
/// </summary>
public sealed class JsonSchemaStrategy : ExtractionStrategy
{
    /// <summary>Providers known to accept a JSON schema on the response format.</summary>
    private static readonly string[] SupportedProviders =
    [
        "openai", "azure", "azureopenai", "azure.ai.openai", "azureaiinference",
        "gemini", "google", "googleai", "vertexai", "mistral", "groq", "fireworks", "together",
    ];

    /// <inheritdoc />
    public override ExtractionMode Mode => ExtractionMode.JsonSchema;

    /// <inheritdoc />
    public override int Priority => 100;

    /// <inheritdoc />
    public override bool CanHandle(ChatClientMetadata? metadata) =>
        ProviderMatcher.Matches(metadata, SupportedProviders);

    /// <inheritdoc />
    public override void Apply(StrategyContext context)
    {
        context.ChatOptions.ResponseFormat =
            ChatResponseFormat.ForJsonSchema(context.Schema, context.SchemaName);
    }
}

/// <summary>
/// Declares the target type as the parameters of a single tool and forces the model to call
/// it. The most reliable mode on Anthropic, and on any model whose tool calling is stronger
/// than its JSON mode.
/// </summary>
public sealed class ToolCallStrategy : ExtractionStrategy
{
    private static readonly string[] SupportedProviders =
    [
        "anthropic", "claude", "bedrock", "aws", "openai", "azure", "azureopenai", "cohere",
    ];

    /// <inheritdoc />
    public override ExtractionMode Mode => ExtractionMode.ToolCall;

    /// <inheritdoc />
    public override int Priority => 80;

    /// <inheritdoc />
    public override bool CanHandle(ChatClientMetadata? metadata) =>
        ProviderMatcher.Matches(metadata, SupportedProviders);

    /// <inheritdoc />
    public override void Apply(StrategyContext context)
    {
        var tool = new SchemaTool(
            context.SchemaName,
            $"Record the extracted {context.TargetType.Name}. You must call this tool exactly once.",
            context.Schema);

        context.ChatOptions.Tools = [tool];
        context.ChatOptions.ToolMode = ChatToolMode.RequireSpecific(tool.Name);
        context.ChatOptions.AllowMultipleToolCalls = false;
    }

    /// <inheritdoc />
    public override string? ExtractPayload(ChatResponse response)
    {
        foreach (ChatMessage message in response.Messages)
        {
            foreach (AIContent content in message.Contents)
            {
                if (content is FunctionCallContent call && call.Arguments is not null)
                {
                    return Parsing.ToolArgumentWriter.Write(call.Arguments);
                }
            }
        }

        // Some providers answer a forced tool call with plain text anyway.
        return response.Text;
    }

    /// <summary>A declaration-only tool: it exists to carry a schema, never to be invoked.</summary>
    private sealed class SchemaTool : AIFunction
    {
        private readonly JsonElement _schema;

        internal SchemaTool(string name, string description, JsonElement schema)
        {
            Name = name;
            Description = description;
            _schema = schema;
        }

        public override string Name { get; }

        public override string Description { get; }

        public override JsonElement JsonSchema => _schema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "This tool exists only to carry a schema to the provider and is never invoked.");
    }
}

/// <summary>
/// Asks for generic JSON output and puts the schema in the prompt. Used where a provider has
/// a JSON mode but cannot accept a schema, which is the usual shape of local runtimes.
/// </summary>
public sealed class JsonModeStrategy : ExtractionStrategy
{
    private static readonly string[] SupportedProviders =
    [
        "ollama", "llamacpp", "llama.cpp", "lmstudio", "lm studio", "lm-studio",
        "vllm", "deepseek", "openai", "azure", "mistral",
    ];

    /// <inheritdoc />
    public override ExtractionMode Mode => ExtractionMode.Json;

    /// <inheritdoc />
    public override int Priority => 50;

    /// <inheritdoc />
    public override bool CanHandle(ChatClientMetadata? metadata) =>
        ProviderMatcher.Matches(metadata, SupportedProviders);

    /// <inheritdoc />
    public override void Apply(StrategyContext context)
    {
        context.ChatOptions.ResponseFormat = ChatResponseFormat.Json;
        context.AddSystemInstruction(DescribeSchema(context.SchemaText));
    }
}

/// <summary>
/// Pure prompting. Assumes nothing about the provider beyond its ability to emit text, and is
/// therefore the mode that always works. Pairs with the defensive parser, which digs the JSON
/// out of whatever prose comes with it.
/// </summary>
public sealed class MarkdownJsonStrategy : ExtractionStrategy
{
    /// <inheritdoc />
    public override ExtractionMode Mode => ExtractionMode.MarkdownJson;

    /// <inheritdoc />
    public override int Priority => 10;

    /// <inheritdoc />
    public override bool CanHandle(ChatClientMetadata? metadata) => true;

    /// <inheritdoc />
    public override void Apply(StrategyContext context)
    {
        context.AddSystemInstruction(
            "Respond with a single JSON value that conforms to the JSON Schema below, wrapped " +
            "in a ```json fenced code block. Do not include any other text.\n\n" +
            "JSON Schema:\n" + context.SchemaText);
    }
}

internal static class ProviderMatcher
{
    internal static bool Matches(ChatClientMetadata? metadata, string[] providers)
    {
        string? name = metadata?.ProviderName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (string candidate in providers)
        {
            if (name!.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
