namespace InstructorSharp;

/// <summary>
/// A single reason a candidate object was rejected, addressed by JSON path so the model
/// is told exactly which field to fix rather than being asked to try again generically.
/// </summary>
public sealed class ValidationFailure
{
    /// <summary>Creates a failure.</summary>
    /// <param name="path">JSON path to the offending value, for example <c>$.lines[2].total</c>.</param>
    /// <param name="message">Human-readable description of what is wrong.</param>
    public ValidationFailure(string path, string message)
    {
        Path = path ?? "$";
        Message = message ?? string.Empty;
    }

    /// <summary>JSON path to the offending value, for example <c>$.lines[2].total</c>.</summary>
    public string Path { get; }

    /// <summary>Human-readable description of what is wrong with the value at <see cref="Path"/>.</summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Path}: {Message}";
}
