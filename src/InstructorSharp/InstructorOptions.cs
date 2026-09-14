using System.Text.Json;
using Microsoft.Extensions.AI;

namespace InstructorSharp;

/// <summary>
/// Settings for an <see cref="IInstructor"/>. Every property has a sane default; the common
/// case is to construct this with no changes at all.
/// </summary>
public sealed class InstructorOptions
{
    private int _maxAttempts = 3;

    /// <summary>
    /// How many times to ask the model in total, including the first try. A value of 1 disables
    /// the repair loop entirely. Defaults to 3.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxAttempts
    {
        get => _maxAttempts;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxAttempts must be at least 1.");
            }

            _maxAttempts = value;
        }
    }

    /// <summary>
    /// Which request shaping to use. Defaults to <see cref="ExtractionMode.Auto"/>, which
    /// picks the strongest mode the underlying client advertises support for.
    /// </summary>
    public ExtractionMode Mode { get; set; } = ExtractionMode.Auto;

    /// <summary>
    /// Serializer used for both schema generation and deserialization. Set this to a
    /// source-generated JsonSerializerContext's options to stay trimming- and AOT-safe.
    /// Defaults to a case-insensitive, camelCase configuration.
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = InstructorJson.Default;

    /// <summary>
    /// Ceiling on total tokens (prompt plus completion) across all attempts of a single
    /// extraction. Null means no ceiling. This is the guard against a repair loop quietly
    /// costing ten times what the first call did.
    /// </summary>
    public long? TokenBudget { get; set; }

    /// <summary>
    /// Whether to shape schemas to the strict subset that providers such as OpenAI enforce:
    /// every property required, additionalProperties false, and unsupported validation
    /// keywords moved into descriptions. Defaults to true.
    /// </summary>
    public bool UseStrictSchema { get; set; } = true;

    /// <summary>
    /// Whether to run recursive System.ComponentModel.DataAnnotations validation over the
    /// deserialized object graph. Defaults to true.
    /// </summary>
    public bool ValidateDataAnnotations { get; set; } = true;

    /// <summary>
    /// Maximum depth the recursive validator will walk before giving up on a branch.
    /// Guards against pathological object graphs. Defaults to 32.
    /// </summary>
    public int MaxValidationDepth { get; set; } = 32;

    /// <summary>
    /// Name given to the generated schema when the provider wants one. Defaults to the
    /// target type's name.
    /// </summary>
    public string? SchemaName { get; set; }

    /// <summary>
    /// Underlying chat options -- model id, temperature, max output tokens and so on. These are
    /// copied for each attempt and then shaped by the selected strategy, so anything you set
    /// here survives except the response format and tools, which the strategy owns.
    /// </summary>
    public ChatOptions? ChatOptions { get; set; }

    /// <summary>
    /// Creates a shallow copy. Used internally so per-call overrides never mutate shared options.
    /// </summary>
    /// <returns>A copy of these options.</returns>
    public InstructorOptions Clone() => new()
    {
        MaxAttempts = MaxAttempts,
        Mode = Mode,
        SerializerOptions = SerializerOptions,
        TokenBudget = TokenBudget,
        UseStrictSchema = UseStrictSchema,
        ValidateDataAnnotations = ValidateDataAnnotations,
        MaxValidationDepth = MaxValidationDepth,
        SchemaName = SchemaName,
        ChatOptions = ChatOptions?.Clone(),
    };
}
