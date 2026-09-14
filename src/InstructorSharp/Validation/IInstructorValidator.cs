namespace InstructorSharp.Validation;

/// <summary>
/// A custom rule applied to an extracted object before it is handed back. Any failure you
/// return is fed to the model verbatim on the next attempt, so write messages as an
/// instruction to the model rather than as a message to a user.
/// </summary>
/// <typeparam name="T">The extracted type.</typeparam>
/// <remarks>
/// This is the seam for rules a schema cannot express: cross-field arithmetic, a value that
/// must exist in your database, a total that must equal the sum of its lines. Register
/// implementations on <see cref="InstructorBuilder"/> or pass them per call.
/// </remarks>
public interface IInstructorValidator<in T>
{
    /// <summary>
    /// Validates the candidate object.
    /// </summary>
    /// <param name="value">The deserialized candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The reasons the object is unacceptable, or an empty sequence when it is fine.
    /// </returns>
    ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(T value, CancellationToken cancellationToken = default);
}
