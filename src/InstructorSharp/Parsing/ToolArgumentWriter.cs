using System.Text.Json;

namespace InstructorSharp.Parsing;

/// <summary>
/// Re-serializes the argument dictionary of a tool call back into a JSON object.
/// </summary>
/// <remarks>
/// Written by hand with <see cref="Utf8JsonWriter"/> rather than by calling
/// <c>JsonSerializer.Serialize(object, ...)</c>, because the reflection-based overload is not
/// trimming- or AOT-safe and would poison the whole package for anyone publishing
/// Native AOT. Provider clients hand back <see cref="JsonElement"/> values in practice, which
/// write through verbatim; the primitive cases below cover clients that pre-convert.
/// </remarks>
internal static class ToolArgumentWriter
{
    internal static string Write(IDictionary<string, object?> arguments)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            foreach (KeyValuePair<string, object?> pair in arguments)
            {
                writer.WritePropertyName(pair.Key);
                WriteValue(writer, pair.Value);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case JsonDocument document:
                document.RootElement.WriteTo(writer);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case IDictionary<string, object?> nested:
                writer.WriteStartObject();
                foreach (KeyValuePair<string, object?> pair in nested)
                {
                    writer.WritePropertyName(pair.Key);
                    WriteValue(writer, pair.Value);
                }

                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable sequence:
                writer.WriteStartArray();
                foreach (object? item in sequence)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
