using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace InstructorSharp;

/// <summary>Shared serializer configuration used when the caller does not supply their own.</summary>
public static class InstructorJson
{
    /// <summary>
    /// Case-insensitive, camelCase, enums as strings. Models produce camelCase far more
    /// reliably than PascalCase, and they name enum members rather than numbering them.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = Create();

    /// <remarks>
    /// These defaults are reflection-based by nature -- they exist so that the zero-configuration
    /// path works. A caller publishing Native AOT supplies their own source-generated options via
    /// <see cref="InstructorOptions.SerializerOptions"/> and never touches this member, so the
    /// warning is suppressed here rather than pushed out to every caller.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "AOT", "IL3050",
        Justification = "Reflection-based defaults for the convenience path; AOT callers supply source-generated options.")]
    [UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Reflection-based defaults for the convenience path; trimmed callers supply source-generated options.")]
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        options.Converters.Add(new JsonStringEnumConverter());

        // The resolver has to be set explicitly before freezing. JsonSerializerOptions normally
        // populates a reflection-based resolver lazily on first use, but MakeReadOnly closes
        // that door, so freezing without one throws on the very first extraction.
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        options.MakeReadOnly();
        return options;
    }
}
