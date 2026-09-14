namespace InstructorSharp;

/// <summary>
/// The record of one round trip to the model. A failed extraction carries the full list of
/// these so you can see what the model actually said and why each answer was rejected.
/// </summary>
public sealed class ExtractionAttempt
{
    internal ExtractionAttempt(
        int attemptNumber,
        ExtractionMode mode,
        string? rawResponse,
        IReadOnlyList<ValidationFailure> failures,
        Exception? exception,
        long inputTokens,
        long outputTokens)
    {
        AttemptNumber = attemptNumber;
        Mode = mode;
        RawResponse = rawResponse;
        Failures = failures;
        Exception = exception;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    /// <summary>1-based index of this attempt.</summary>
    public int AttemptNumber { get; }

    /// <summary>The mode actually used for this attempt.</summary>
    public ExtractionMode Mode { get; }

    /// <summary>Raw text the model returned, before parsing. Null if the call itself threw.</summary>
    public string? RawResponse { get; }

    /// <summary>Why this attempt was rejected. Empty when the attempt succeeded.</summary>
    public IReadOnlyList<ValidationFailure> Failures { get; }

    /// <summary>The transport or deserialization exception, when one occurred.</summary>
    public Exception? Exception { get; }

    /// <summary>Prompt tokens billed for this attempt, or 0 when the provider did not report usage.</summary>
    public long InputTokens { get; }

    /// <summary>Completion tokens billed for this attempt, or 0 when the provider did not report usage.</summary>
    public long OutputTokens { get; }

    /// <summary>Whether this attempt produced a valid object.</summary>
    public bool Succeeded => Failures.Count == 0 && Exception is null;
}
