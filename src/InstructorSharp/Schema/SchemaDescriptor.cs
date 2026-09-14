using System.Text.Json;

namespace InstructorSharp.Schema;

/// <summary>
/// A generated schema plus the one fact the parser needs afterwards: whether the target type
/// had to be wrapped in a single-property object to satisfy the provider.
/// </summary>
internal sealed class SchemaDescriptor
{
    /// <summary>Property name used when a non-object type is wrapped for transport.</summary>
    internal const string EnvelopePropertyName = "value";

    internal SchemaDescriptor(JsonElement schema, bool isEnveloped)
    {
        Schema = schema;
        IsEnveloped = isEnveloped;
        SchemaText = schema.GetRawText();
    }

    /// <summary>The schema as sent to the provider.</summary>
    internal JsonElement Schema { get; }

    /// <summary>The schema serialized once, for embedding in prompts.</summary>
    internal string SchemaText { get; }

    /// <summary>
    /// True when the target type is not an object and was wrapped under
    /// <see cref="EnvelopePropertyName"/>. The parser unwraps it again.
    /// </summary>
    internal bool IsEnveloped { get; }
}
