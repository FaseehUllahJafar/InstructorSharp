using System.Text;
using System.Text.Json;

namespace InstructorSharp.Parsing;

/// <summary>
/// Turns a prefix of a JSON document into a valid JSON document, so a half-received stream
/// can be deserialized into a partly-filled object on every chunk.
/// </summary>
/// <remarks>
/// The hard part of streaming structured output is that a prefix like
/// <c>{"name":"Fase</c> is not valid JSON and never becomes valid by waiting for one more
/// character. Rather than hand-rolling a JSON parser, this leans on
/// <see cref="Utf8JsonReader"/> constructed with <c>isFinalBlock: false</c>, which is the
/// same machinery the BCL uses to read JSON off a socket: it stops cleanly at the last
/// complete token and reports how many bytes that was. From there we only have to re-close
/// the containers that are still open, plus salvage a trailing partial string so that text
/// fields visibly fill in rather than appearing all at once.
/// <para>
/// One deliberate asymmetry: a trailing number or literal is withheld until a delimiter proves
/// it finished, while a trailing string is shown as it arrives. That is not an oversight. A
/// half-received string reads as obviously mid-word, but a half-received <c>123</c> looks
/// exactly like a confident <c>1</c>, and a UI that renders a wrong number is worse than one
/// that renders nothing for another few milliseconds.
/// </para>
/// </remarks>
internal static class PartialJsonCompleter
{
    private const byte ObjectFrame = 0;
    private const byte ArrayFrame = 1;

    /// <summary>
    /// Completes a partial JSON document.
    /// </summary>
    /// <param name="partial">Text received so far.</param>
    /// <param name="json">A syntactically valid JSON document representing what has arrived.</param>
    /// <returns>True when something parseable could be produced.</returns>
    internal static bool TryComplete(string? partial, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrEmpty(partial))
        {
            return false;
        }

        return TryComplete(Encoding.UTF8.GetBytes(partial!), out json);
    }

    /// <summary>
    /// Completes a partial JSON document held as UTF-8 bytes.
    /// </summary>
    /// <param name="utf8">Bytes received so far.</param>
    /// <param name="json">A syntactically valid JSON document representing what has arrived.</param>
    /// <returns>True when something parseable could be produced.</returns>
    /// <remarks>
    /// The byte overload is the one the streaming path uses, so that an accumulating buffer is
    /// never re-encoded from scratch on every chunk.
    /// </remarks>
    internal static bool TryComplete(ReadOnlySpan<byte> utf8, out string json)
    {
        json = string.Empty;

        // '{' and '[' cannot occur inside a multi-byte UTF-8 sequence -- continuation bytes are
        // all >= 0x80 -- so scanning raw bytes for the document start is safe.
        int start = -1;
        for (int i = 0; i < utf8.Length; i++)
        {
            if (utf8[i] is (byte)'{' or (byte)'[')
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = utf8.Slice(start);

        var stack = new List<byte>();
        var safeStack = new List<byte>();
        long safeConsumed = 0;

        try
        {
            var reader = new Utf8JsonReader(bytes, isFinalBlock: false, state: default);
            while (reader.Read())
            {
                bool atValueBoundary = true;

                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        stack.Add(ObjectFrame);
                        break;
                    case JsonTokenType.StartArray:
                        stack.Add(ArrayFrame);
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        if (stack.Count > 0)
                        {
                            stack.RemoveAt(stack.Count - 1);
                        }

                        break;
                    case JsonTokenType.PropertyName:
                        // A key with no value yet is not a place we can truncate.
                        atValueBoundary = false;
                        break;
                }

                if (atValueBoundary)
                {
                    safeConsumed = reader.BytesConsumed;
                    safeStack.Clear();
                    safeStack.AddRange(stack);
                }
            }
        }
        catch (JsonException)
        {
            // Genuinely malformed rather than merely incomplete: fall back to the last
            // boundary we already proved good.
        }

        if (safeConsumed == 0)
        {
            return false;
        }

        // safeConsumed counts bytes, and a token boundary never falls inside a UTF-8 sequence,
        // so both halves decode cleanly.
        var builder = new StringBuilder(ToUtf8String(bytes.Slice(0, (int)safeConsumed)));

        string leftover = ToUtf8String(bytes.Slice((int)safeConsumed));
        AppendSalvagedStringValue(builder, leftover, safeStack);

        for (int i = safeStack.Count - 1; i >= 0; i--)
        {
            builder.Append(safeStack[i] == ObjectFrame ? '}' : ']');
        }

        json = builder.ToString();
        return true;
    }

    /// <summary>
    /// Recovers a trailing <c>"key":"partial text</c> (or a bare partial string inside an
    /// array) that the reader held back, so streamed text appears as it arrives instead of
    /// snapping into place only once the closing quote is received.
    /// </summary>
    private static void AppendSalvagedStringValue(StringBuilder builder, string leftover, List<byte> stack)
    {
        if (stack.Count == 0 || leftover.Length == 0)
        {
            return;
        }

        int i = 0;
        bool sawComma = false;

        SkipWhitespace(leftover, ref i);
        if (i < leftover.Length && leftover[i] == ',')
        {
            sawComma = true;
            i++;
            SkipWhitespace(leftover, ref i);
        }

        string? key = null;

        if (stack[stack.Count - 1] == ObjectFrame)
        {
            if (i >= leftover.Length || leftover[i] != '"')
            {
                return;
            }

            if (!TryReadString(leftover, ref i, out string keyText, out bool keyClosed) || !keyClosed)
            {
                return;
            }

            key = keyText;

            SkipWhitespace(leftover, ref i);
            if (i >= leftover.Length || leftover[i] != ':')
            {
                return;
            }

            i++;
            SkipWhitespace(leftover, ref i);
        }

        if (i >= leftover.Length || leftover[i] != '"')
        {
            return;
        }

        // Only an unterminated string is worth salvaging. A complete one would already have
        // been consumed by the reader, and partial numbers or literals are not safely repairable.
        if (!TryReadString(leftover, ref i, out string value, out bool closed) || closed)
        {
            return;
        }

        if (sawComma)
        {
            builder.Append(',');
        }

        if (key is not null)
        {
            // key is the raw source text between the quotes, with its escapes still written as
            // the model wrote them. Re-escaping it here would turn a legitimate \" into \\" and
            // break the document, so it is emitted verbatim.
            builder.Append('"').Append(key).Append("\":");
        }

        builder.Append('"').Append(TrimDanglingEscape(value)).Append('"');
    }

    private static string ToUtf8String(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return string.Empty;
        }

#if NETSTANDARD_POLYFILL
        return Encoding.UTF8.GetString(utf8.ToArray());
#else
        return Encoding.UTF8.GetString(utf8);
#endif
    }

    private static void SkipWhitespace(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }
    }

    /// <summary>
    /// Reads a JSON string literal starting at an opening quote, returning its raw inner text
    /// (escapes left as written) and whether a closing quote was reached.
    /// </summary>
    private static bool TryReadString(string text, ref int i, out string value, out bool closed)
    {
        value = string.Empty;
        closed = false;

        if (i >= text.Length || text[i] != '"')
        {
            return false;
        }

        int start = ++i;
        bool escaped = false;

        while (i < text.Length)
        {
            char c = text[i];

            if (escaped)
            {
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else if (c == '"')
            {
                closed = true;
                value = text.Substring(start, i - start);
                i++;
                return true;
            }

            i++;
        }

        value = text.Substring(start);
        return true;
    }

    /// <summary>
    /// Drops a truncated escape sequence from the end of a salvaged string, which would
    /// otherwise make the repaired document invalid.
    /// </summary>
    private static string TrimDanglingEscape(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        // A partial \uXXXX.
        int u = value.LastIndexOf("\\u", StringComparison.Ordinal);
        if (u >= 0 && value.Length - u < 6 && CountTrailingBackslashesBefore(value, u) % 2 == 0)
        {
            return value.Substring(0, u);
        }

        // A trailing lone backslash.
        int backslashes = 0;
        for (int i = value.Length - 1; i >= 0 && value[i] == '\\'; i--)
        {
            backslashes++;
        }

        return backslashes % 2 == 1 ? value.Substring(0, value.Length - 1) : value;
    }

    private static int CountTrailingBackslashesBefore(string value, int index)
    {
        int count = 0;
        for (int i = index - 1; i >= 0 && value[i] == '\\'; i--)
        {
            count++;
        }

        return count;
    }

}
