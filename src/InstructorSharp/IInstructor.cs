using Microsoft.Extensions.AI;

namespace InstructorSharp;

/// <summary>
/// Extracts validated, strongly-typed objects from a chat model.
/// </summary>
/// <remarks>
/// Obtain one with <see cref="ChatClientInstructorExtensions.AsInstructor(IChatClient)"/> or
/// through dependency injection. Implementations are safe to share across threads and are
/// intended to be registered as singletons.
/// </remarks>
public interface IInstructor
{
    /// <summary>
    /// Extracts a <typeparamref name="T"/>, retrying with corrective feedback until the object
    /// validates or the attempt budget runs out.
    /// </summary>
    /// <typeparam name="T">The type to extract. Primitives, arrays and records all work.</typeparam>
    /// <param name="messages">The conversation to send.</param>
    /// <param name="options">Per-call overrides, or null to use the configured defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated object.</returns>
    /// <exception cref="ExtractionFailedException">No valid object was produced within the budget.</exception>
    /// <exception cref="TokenBudgetExceededException">The token budget was exhausted.</exception>
    Task<T> ExtractAsync<T>(
        IEnumerable<ChatMessage> messages,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a <typeparamref name="T"/> without throwing, returning the outcome and the full
    /// attempt history either way.
    /// </summary>
    /// <typeparam name="T">The type to extract.</typeparam>
    /// <param name="messages">The conversation to send.</param>
    /// <param name="options">Per-call overrides, or null to use the configured defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result, successful or not.</returns>
    Task<ExtractionResult<T>> TryExtractAsync<T>(
        IEnumerable<ChatMessage> messages,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams progressively more complete snapshots of a <typeparamref name="T"/> as the model
    /// writes it, so a UI can fill in field by field instead of waiting for the whole object.
    /// </summary>
    /// <typeparam name="T">The type to extract.</typeparam>
    /// <param name="messages">The conversation to send.</param>
    /// <param name="options">Per-call overrides, or null to use the configured defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A sequence of snapshots. Every snapshot but the last may have unset members; the final
    /// one is the complete, validated object.
    /// </returns>
    /// <remarks>
    /// Validation runs once, on the final snapshot. There is no repair loop here: a stream that
    /// has already been shown to the user cannot be silently retried.
    /// </remarks>
    IAsyncEnumerable<T> StreamAsync<T>(
        IEnumerable<ChatMessage> messages,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the elements of a collection one at a time, yielding each element as soon as it
    /// is complete rather than waiting for the whole array.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="messages">The conversation to send.</param>
    /// <param name="options">Per-call overrides, or null to use the configured defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The elements, in the order the model produced them.</returns>
    /// <remarks>
    /// This is the right shape for "extract every action item from this transcript": the caller
    /// starts processing the first item while the model is still writing the fifth.
    /// </remarks>
    IAsyncEnumerable<T> StreamListAsync<T>(
        IEnumerable<ChatMessage> messages,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default);
}
