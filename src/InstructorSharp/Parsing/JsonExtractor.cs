namespace InstructorSharp.Parsing;

/// <summary>
/// Pulls the JSON document out of whatever the model actually sent.
/// </summary>
/// <remarks>
/// Even with a schema attached, models prepend "Here is the JSON you asked for:", wrap the
/// payload in a markdown fence, add a closing remark, or emit a chain-of-thought preamble.
/// Calling <c>JsonSerializer.Deserialize</c> on the raw text fails on all of those, which is
/// the single most common reason hand-rolled extraction is flaky. This walks the text with a
/// brace-matching scanner that understands string literals and escapes, so a brace inside a
/// string value never throws off the balance.
/// </remarks>
internal static class JsonExtractor
{
    /// <summary>
    /// Finds the outermost JSON object or array in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">Raw model output.</param>
    /// <param name="json">The extracted JSON document text.</param>
    /// <returns>True when a balanced document was found.</returns>
    internal static bool TryExtract(string? text, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text!.Trim();

        // Fast path: the whole response is already a document.
        if (IsBalancedDocument(trimmed))
        {
            json = trimmed;
            return true;
        }

        // Markdown fences, with or without a language tag.
        if (TryExtractFenced(trimmed, out json))
        {
            return true;
        }

        // Otherwise scan for the first structural character that opens a balanced document.
        return TryScan(trimmed, out json);
    }

    private static bool TryExtractFenced(string text, out string json)
    {
        json = string.Empty;
        int index = 0;

        while (index < text.Length)
        {
            int fenceStart = text.IndexOf("```", index, StringComparison.Ordinal);
            if (fenceStart < 0)
            {
                return false;
            }

            int contentStart = fenceStart + 3;

            // Skip an optional language tag on the rest of the line.
            int lineEnd = text.IndexOf('\n', contentStart);
            if (lineEnd >= 0 && text.AsSpan(contentStart, lineEnd - contentStart).Trim().Length <= 8)
            {
                contentStart = lineEnd + 1;
            }

            int fenceEnd = text.IndexOf("```", contentStart, StringComparison.Ordinal);
            string body = fenceEnd < 0
                ? text.Substring(contentStart)
                : text.Substring(contentStart, fenceEnd - contentStart);

            if (TryScan(body.Trim(), out json))
            {
                return true;
            }

            index = fenceEnd < 0 ? text.Length : fenceEnd + 3;
        }

        return false;
    }

    /// <summary>
    /// Scans for the first <c>{</c> or <c>[</c> that begins a balanced document. Tries each
    /// candidate in turn, because a preamble may itself contain a stray brace.
    /// </summary>
    private static bool TryScan(string text, out string json)
    {
        json = string.Empty;

        for (int start = 0; start < text.Length; start++)
        {
            char c = text[start];
            if (c is not ('{' or '['))
            {
                continue;
            }

            if (TryMatchFrom(text, start, out int end))
            {
                json = text.Substring(start, end - start + 1);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks forward from an opening bracket tracking nesting depth, ignoring brackets that
    /// appear inside string literals and honouring backslash escapes.
    /// </summary>
    private static bool TryMatchFrom(string text, int start, out int end)
    {
        end = -1;
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
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
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                case '[':
                    depth++;
                    break;
                case '}':
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        return true;
                    }

                    if (depth < 0)
                    {
                        return false;
                    }

                    break;
            }
        }

        return false;
    }

    private static bool IsBalancedDocument(string text)
    {
        if (text.Length < 2)
        {
            return false;
        }

        char first = text[0];
        if (first is not ('{' or '['))
        {
            return false;
        }

        return TryMatchFrom(text, 0, out int end) && end == text.Length - 1;
    }
}
