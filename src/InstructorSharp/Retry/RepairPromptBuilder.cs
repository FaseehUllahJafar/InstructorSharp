using System.Text;

namespace InstructorSharp.Retry;

/// <summary>
/// Builds the follow-up turn sent after a rejected answer.
/// </summary>
/// <remarks>
/// This is the difference between a retry and a repair. A plain retry re-rolls the same
/// request and hopes for better luck, which fails at the same rate every time when the model
/// has simply misread the instruction. Putting the model's own bad answer back in the
/// conversation as an assistant turn, followed by the precise per-field reasons it was
/// rejected, converts the next call into a correction task, which models are markedly better
/// at than the original extraction.
/// </remarks>
internal static class RepairPromptBuilder
{
    internal static string BuildValidationRepair(IReadOnlyList<ValidationFailure> failures)
    {
        var sb = new StringBuilder();
        sb.Append("Your previous response was rejected because ")
          .Append(failures.Count == 1 ? "of this problem:" : "of these problems:")
          .Append('\n');

        foreach (ValidationFailure failure in failures)
        {
            sb.Append("- ").Append(failure.Path).Append(": ").Append(failure.Message).Append('\n');
        }

        sb.Append('\n')
          .Append("Return the corrected JSON value. Fix only the fields listed above and keep ")
          .Append("every other field exactly as you already had it. Output JSON only.");

        return sb.ToString();
    }

    internal static string BuildParseRepair(string? rawResponse, string reason)
    {
        var sb = new StringBuilder();
        sb.Append("Your previous response could not be read as JSON matching the required schema: ")
          .Append(reason)
          .Append('\n');

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            sb.Append("\nYour previous response was empty.");
        }

        sb.Append('\n')
          .Append("Return a single JSON value conforming to the schema. Output JSON only, with no ")
          .Append("commentary before or after it.");

        return sb.ToString();
    }
}
