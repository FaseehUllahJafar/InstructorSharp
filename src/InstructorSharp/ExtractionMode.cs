namespace InstructorSharp;

/// <summary>
/// How the request is shaped so the model returns JSON. Different providers support
/// different mechanisms; <see cref="Auto"/> picks the strongest one the client advertises.
/// </summary>
public enum ExtractionMode
{
    /// <summary>
    /// Inspect the client's <c>ChatClientMetadata</c> and pick the strongest supported
    /// mode. This is the default and is correct for almost every caller.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Send the schema as a native response format (OpenAI <c>json_schema</c>,
    /// Gemini <c>responseSchema</c>). The most reliable mode where it is supported.
    /// </summary>
    JsonSchema = 1,

    /// <summary>
    /// Declare a single tool whose parameter schema is the target type and force the
    /// model to call it. The most reliable mode on Anthropic and on older OpenAI models.
    /// </summary>
    ToolCall = 2,

    /// <summary>
    /// Ask for generic JSON output (<c>response_format: json_object</c>) and describe the
    /// schema in the prompt. Used by Ollama and similar local runtimes.
    /// </summary>
    Json = 3,

    /// <summary>
    /// Pure prompting: describe the schema and ask for a fenced JSON block, then parse it
    /// out of the prose. The universal fallback that works against any model at all.
    /// </summary>
    MarkdownJson = 4,
}
