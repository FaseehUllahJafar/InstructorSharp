using Microsoft.Extensions.AI;

namespace InstructorSharp;

/// <summary>
/// Entry points that turn any <see cref="IChatClient"/> into an <see cref="IInstructor"/>, plus
/// the one-line overloads that cover the common case.
/// </summary>
public static class ChatClientInstructorExtensions
{
    /// <summary>Wraps a chat client with default settings.</summary>
    /// <param name="client">The chat client.</param>
    /// <returns>An instructor over that client.</returns>
    public static IInstructor AsInstructor(this IChatClient client) =>
        new Instructor(client);

    /// <summary>Wraps a chat client with the given settings.</summary>
    /// <param name="client">The chat client.</param>
    /// <param name="options">Settings applied to every call.</param>
    /// <returns>An instructor over that client.</returns>
    public static IInstructor AsInstructor(this IChatClient client, InstructorOptions options) =>
        new Instructor(client, options);

    /// <summary>Starts fluent configuration of an instructor over this client.</summary>
    /// <param name="client">The chat client.</param>
    /// <returns>A builder.</returns>
    public static InstructorBuilder AsInstructorBuilder(this IChatClient client) =>
        new(client);

    /// <summary>
    /// Extracts a <typeparamref name="T"/> from a single user prompt. The shortest path from a
    /// string to a validated object.
    /// </summary>
    /// <typeparam name="T">The type to extract.</typeparam>
    /// <param name="instructor">The instructor.</param>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="options">Per-call overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated object.</returns>
    public static Task<T> ExtractAsync<T>(
        this IInstructor instructor,
        string prompt,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default) =>
        instructor.ExtractAsync<T>([new ChatMessage(ChatRole.User, prompt)], options, cancellationToken);

    /// <summary>
    /// Extracts a <typeparamref name="T"/> from a system instruction plus a user prompt.
    /// </summary>
    /// <typeparam name="T">The type to extract.</typeparam>
    /// <param name="instructor">The instructor.</param>
    /// <param name="systemPrompt">Instruction describing the extraction task.</param>
    /// <param name="prompt">The content to extract from.</param>
    /// <param name="options">Per-call overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated object.</returns>
    public static Task<T> ExtractAsync<T>(
        this IInstructor instructor,
        string systemPrompt,
        string prompt,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default) =>
        instructor.ExtractAsync<T>(
            [new ChatMessage(ChatRole.System, systemPrompt), new ChatMessage(ChatRole.User, prompt)],
            options,
            cancellationToken);

    /// <summary>Non-throwing extraction from a single user prompt.</summary>
    /// <typeparam name="T">The type to extract.</typeparam>
    /// <param name="instructor">The instructor.</param>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="options">Per-call overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result, successful or not.</returns>
    public static Task<ExtractionResult<T>> TryExtractAsync<T>(
        this IInstructor instructor,
        string prompt,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default) =>
        instructor.TryExtractAsync<T>([new ChatMessage(ChatRole.User, prompt)], options, cancellationToken);

    /// <summary>Streams progressively complete snapshots from a single user prompt.</summary>
    /// <typeparam name="T">The type to extract.</typeparam>
    /// <param name="instructor">The instructor.</param>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="options">Per-call overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Snapshots of the object as it is written.</returns>
    public static IAsyncEnumerable<T> StreamAsync<T>(
        this IInstructor instructor,
        string prompt,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default) =>
        instructor.StreamAsync<T>([new ChatMessage(ChatRole.User, prompt)], options, cancellationToken);

    /// <summary>Streams collection elements from a single user prompt as each one completes.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="instructor">The instructor.</param>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="options">Per-call overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The elements, in order.</returns>
    public static IAsyncEnumerable<T> StreamListAsync<T>(
        this IInstructor instructor,
        string prompt,
        InstructorOptions? options = null,
        CancellationToken cancellationToken = default) =>
        instructor.StreamListAsync<T>([new ChatMessage(ChatRole.User, prompt)], options, cancellationToken);
}
