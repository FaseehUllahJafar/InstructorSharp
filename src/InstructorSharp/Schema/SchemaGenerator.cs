using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace InstructorSharp.Schema;

/// <summary>
/// Turns a CLR type into the JSON schema sent to the provider.
/// </summary>
/// <remarks>
/// Two things happen here that a plain schema exporter does not do.
/// <para>
/// First, strict shaping. Providers that enforce a schema natively accept only a narrow
/// subset of JSON Schema: every property must appear in <c>required</c>, every object must
/// set <c>additionalProperties:false</c>, and keywords such as <c>pattern</c>, <c>format</c>
/// and <c>minimum</c> are rejected outright. Those keywords still carry real intent, so
/// rather than dropping them we fold them into the property description, where the model
/// reads them, and enforce them afterwards with validation.
/// </para>
/// <para>
/// Second, envelope wrapping. A strict schema root has to be an object, so asking for a
/// <c>List&lt;Person&gt;</c> or an <c>int</c> is rejected by the provider. Those are wrapped
/// in a single-property object and unwrapped again on the way back, which is why
/// primitives and collections work here and do not work through the built-in path.
/// </para>
/// </remarks>
internal static class SchemaGenerator
{
    private static readonly ConcurrentDictionary<SchemaCacheKey, SchemaDescriptor> Cache = new();

    /// <summary>Keywords the strict provider subset rejects, mapped to how they read in prose.</summary>
    private static readonly (string Keyword, string Label)[] DemotedKeywords =
    [
        ("pattern", "must match regular expression"),
        ("format", "format"),
        ("minimum", "minimum"),
        ("maximum", "maximum"),
        ("exclusiveMinimum", "exclusive minimum"),
        ("exclusiveMaximum", "exclusive maximum"),
        ("minLength", "minimum length"),
        ("maxLength", "maximum length"),
        ("minItems", "minimum items"),
        ("maxItems", "maximum items"),
        ("uniqueItems", "unique items"),
        ("multipleOf", "multiple of"),
    ];

    internal static SchemaDescriptor For(Type type, InstructorOptions options)
    {
        var key = new SchemaCacheKey(type, options.UseStrictSchema, options.SerializerOptions);
        return Cache.GetOrAdd(key, static k => Build(k.Type, k.Strict, k.SerializerOptions));
    }

    private static SchemaDescriptor Build(Type type, bool strict, JsonSerializerOptions serializerOptions)
    {
        AIJsonSchemaCreateOptions createOptions = strict
            ? new AIJsonSchemaCreateOptions
            {
                TransformOptions = new AIJsonSchemaTransformOptions
                {
                    DisallowAdditionalProperties = true,
                    RequireAllProperties = true,
                    MoveDefaultKeywordToDescription = true,
                    ConvertBooleanSchemas = true,
                },
            }
            : AIJsonSchemaCreateOptions.Default;

        JsonElement generated = AIJsonUtilities.CreateJsonSchema(
            type,
            description: null,
            hasDefaultValue: false,
            defaultValue: null,
            serializerOptions: serializerOptions,
            inferenceOptions: createOptions);

        JsonNode root = JsonNode.Parse(generated.GetRawText())
                        ?? throw new InstructorException($"Could not generate a JSON schema for {type.Name}.");

        if (strict)
        {
            root = DemoteUnsupportedKeywords(root);
        }

        bool enveloped = RequiresEnvelope(root);
        if (enveloped)
        {
            root = Envelope(root);
        }

        using var document = JsonDocument.Parse(root.ToJsonString());
        return new SchemaDescriptor(document.RootElement.Clone(), enveloped);
    }

    /// <summary>
    /// A strict schema root must be a closed object with named properties. Anything else --
    /// a bare string, a number, an array, a dictionary -- gets wrapped.
    /// </summary>
    private static bool RequiresEnvelope(JsonNode root)
    {
        if (root is not JsonObject obj)
        {
            return true;
        }

        if (!obj.TryGetPropertyValue("type", out JsonNode? typeNode) || typeNode is null)
        {
            // Composite roots (anyOf/oneOf/$ref only) are not valid strict roots either.
            return !obj.ContainsKey("properties");
        }

        string? typeName = typeNode is JsonArray array
            ? array.Select(n => n?.GetValue<string>()).FirstOrDefault(n => n is not null and not "null")
            : typeNode.GetValue<string>();

        if (!string.Equals(typeName, "object", StringComparison.Ordinal))
        {
            return true;
        }

        // An "object" with no declared properties is a dictionary; strict mode cannot express it.
        return !obj.ContainsKey("properties");
    }

    private static JsonObject Envelope(JsonNode inner)
    {
        JsonObject? defs = null;
        if (inner is JsonObject innerObject)
        {
            // $defs must stay at the document root or the $ref pointers break.
            foreach (string defsKeyword in new[] { "$defs", "definitions" })
            {
                if (innerObject.TryGetPropertyValue(defsKeyword, out JsonNode? node) && node is JsonObject)
                {
                    innerObject.Remove(defsKeyword);
                    defs = node.DeepClone().AsObject();
                    break;
                }
            }

            innerObject.Remove("$schema");
        }

        var envelope = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { [SchemaDescriptor.EnvelopePropertyName] = inner.DeepClone() },
            ["required"] = new JsonArray(SchemaDescriptor.EnvelopePropertyName),
            ["additionalProperties"] = false,
        };

        if (defs is not null)
        {
            envelope["$defs"] = defs;
        }

        return envelope;
    }

    /// <summary>
    /// Strips keywords the strict subset rejects, appending what they meant to the node's
    /// description so the model still sees the constraint.
    /// </summary>
    private static JsonNode DemoteUnsupportedKeywords(JsonNode node)
    {
        switch (node)
        {
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } child)
                    {
                        array[i] = DemoteUnsupportedKeywords(child.DeepClone());
                    }
                }

                return array;

            case JsonObject obj:
                var demoted = new List<string>();

                foreach ((string keyword, string label) in DemotedKeywords)
                {
                    if (obj.TryGetPropertyValue(keyword, out JsonNode? value) && value is not null)
                    {
                        demoted.Add($"{label}: {value.ToJsonString().Trim('"')}");
                        obj.Remove(keyword);
                    }
                }

                if (demoted.Count > 0)
                {
                    string existing = obj.TryGetPropertyValue("description", out JsonNode? d) && d is not null
                        ? d.GetValue<string>()
                        : string.Empty;

                    string appended = "(" + string.Join(", ", demoted) + ")";
                    obj["description"] = existing.Length == 0 ? appended : existing + " " + appended;
                }

                foreach (string key in obj.Select(p => p.Key).ToArray())
                {
                    if (obj[key] is { } child)
                    {
                        obj[key] = DemoteUnsupportedKeywords(child.DeepClone());
                    }
                }

                return obj;

            default:
                return node;
        }
    }

    private readonly struct SchemaCacheKey : IEquatable<SchemaCacheKey>
    {
        internal SchemaCacheKey(Type type, bool strict, JsonSerializerOptions serializerOptions)
        {
            Type = type;
            Strict = strict;
            SerializerOptions = serializerOptions;
        }

        internal Type Type { get; }

        internal bool Strict { get; }

        internal JsonSerializerOptions SerializerOptions { get; }

        public bool Equals(SchemaCacheKey other) =>
            Type == other.Type &&
            Strict == other.Strict &&
            ReferenceEquals(SerializerOptions, other.SerializerOptions);

        public override bool Equals(object? obj) => obj is SchemaCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Type.GetHashCode();
                hash = (hash * 397) ^ Strict.GetHashCode();
                hash = (hash * 397) ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(SerializerOptions);
                return hash;
            }
        }
    }
}
