using System.Text;

namespace InstructorSharp;

/// <summary>Base type for every error raised by InstructorSharp.</summary>
public class InstructorException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public InstructorException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public InstructorException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when the model could not produce a valid object within the configured budget.
/// Never thrown with a partial or unvalidated value: either the object is good or this is raised.
/// </summary>
public sealed class ExtractionFailedException : InstructorException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="targetType">The type that could not be extracted.</param>
    /// <param name="attempts">Every attempt made, in order.</param>
    public ExtractionFailedException(Type targetType, IReadOnlyList<ExtractionAttempt> attempts)
        : base(BuildMessage(targetType, attempts), attempts.Count > 0 ? attempts[attempts.Count - 1].Exception : null)
    {
        TargetType = targetType;
        Attempts = attempts;
    }

    /// <summary>The type that could not be extracted.</summary>
    public Type TargetType { get; }

    /// <summary>Every attempt made, in order, with the model's raw output and the reasons it was rejected.</summary>
    public IReadOnlyList<ExtractionAttempt> Attempts { get; }

    private static string BuildMessage(Type targetType, IReadOnlyList<ExtractionAttempt> attempts)
    {
        var sb = new StringBuilder();
        sb.Append("Failed to extract ").Append(targetType.Name)
          .Append(" after ").Append(attempts.Count)
          .Append(attempts.Count == 1 ? " attempt." : " attempts.");

        if (attempts.Count > 0)
        {
            ExtractionAttempt last = attempts[attempts.Count - 1];
            if (last.Failures.Count > 0)
            {
                sb.Append(" Last failures: ");
                for (int i = 0; i < last.Failures.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append("; ");
                    }

                    sb.Append(last.Failures[i]);
                }
            }
            else if (last.Exception is not null)
            {
                sb.Append(" Last error: ").Append(last.Exception.Message);
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Thrown when an extraction would exceed the token budget configured on
/// <see cref="InstructorOptions.TokenBudget"/>. Raised before the offending call is made.
/// </summary>
public sealed class TokenBudgetExceededException : InstructorException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="spent">Tokens already consumed.</param>
    /// <param name="budget">The configured budget.</param>
    public TokenBudgetExceededException(long spent, long budget)
        : base($"Token budget exhausted: {spent} tokens used against a budget of {budget}. " +
               "Raise InstructorOptions.TokenBudget or lower MaxAttempts.")
    {
        TokensSpent = spent;
        Budget = budget;
    }

    /// <summary>Tokens already consumed when the budget was hit.</summary>
    public long TokensSpent { get; }

    /// <summary>The configured budget.</summary>
    public long Budget { get; }
}
