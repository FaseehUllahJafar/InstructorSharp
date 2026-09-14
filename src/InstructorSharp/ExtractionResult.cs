namespace InstructorSharp;

/// <summary>
/// The outcome of a non-throwing extraction. Carries the value on success and the full
/// attempt history either way, so callers can log what the model did without catching.
/// </summary>
/// <typeparam name="T">The type that was being extracted.</typeparam>
public sealed class ExtractionResult<T>
{
    internal ExtractionResult(T? value, bool succeeded, IReadOnlyList<ExtractionAttempt> attempts)
    {
        Value = value;
        Succeeded = succeeded;
        Attempts = attempts;
    }

    /// <summary>The extracted, validated value. Meaningful only when <see cref="Succeeded"/> is true.</summary>
    public T? Value { get; }

    /// <summary>Whether a valid value was produced within the attempt budget.</summary>
    public bool Succeeded { get; }

    /// <summary>Every attempt made, in order, including the successful one.</summary>
    public IReadOnlyList<ExtractionAttempt> Attempts { get; }

    /// <summary>Failures from the final attempt. Empty when <see cref="Succeeded"/> is true.</summary>
    public IReadOnlyList<ValidationFailure> Failures =>
        Attempts.Count == 0 ? Array.Empty<ValidationFailure>() : Attempts[Attempts.Count - 1].Failures;

    /// <summary>Total prompt tokens across every attempt, including the ones that were thrown away.</summary>
    public long TotalInputTokens
    {
        get
        {
            long total = 0;
            for (int i = 0; i < Attempts.Count; i++)
            {
                total += Attempts[i].InputTokens;
            }

            return total;
        }
    }

    /// <summary>Total completion tokens across every attempt, including the ones that were thrown away.</summary>
    public long TotalOutputTokens
    {
        get
        {
            long total = 0;
            for (int i = 0; i < Attempts.Count; i++)
            {
                total += Attempts[i].OutputTokens;
            }

            return total;
        }
    }

    /// <summary>
    /// Returns the value, or throws <see cref="ExtractionFailedException"/> with the full
    /// attempt history if the extraction did not succeed.
    /// </summary>
    /// <returns>The extracted value.</returns>
    public T ValueOrThrow()
    {
        if (!Succeeded)
        {
            throw new ExtractionFailedException(typeof(T), Attempts);
        }

        return Value!;
    }
}
