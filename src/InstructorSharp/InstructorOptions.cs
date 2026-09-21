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
    private int _maxValidationDepth = 32;
    private int _maxStreamBytes = 8 * 1024 * 1024;

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
    /// Stops further attempts once total tokens (prompt plus completion) across this extraction
    /// have passed the given figure. Null means no limit. This is the guard against a repair loop
    /// quietly costing ten times what the first call did.
    /// <para>
    /// It is a gate, not a truncation: the check runs before each attempt, so a single very long
    /// reply can overshoot. It applies to <see cref="IInstructor.ExtractAsync{T}"/> and
    /// <see cref="IInstructor.TryExtractAsync{T}"/> only -- a streamed call reports no usage until
    /// it ends, so streaming is bounded by <see cref="MaxStreamBytes"/> instead.
    /// </para>
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
    public int MaxValidationDepth
    {
        get => _maxValidationDepth;
        set
        {
            // A zero or negative depth would silently switch validation off, which is the one
            // failure this library exists to prevent. It must be an error, not a quiet no-op.
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxValidationDepth must be at least 1.");
            }

            _maxValidationDepth = value;
        }
    }

    /// <summary>
    /// Ceiling on the bytes a single streamed response may accumulate before it is abandoned.
    /// Defaults to 8 MiB. A model that falls into a repetition loop streams without end, and the
    /// caller's cancellation token cannot help because the growth happens between yields.
    /// </summary>
    public int MaxStreamBytes
    {
        get => _maxStreamBytes;
        set
        {
            if (value < 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxStreamBytes must be at least 1024.");
            }

            _maxStreamBytes = value;
        }
    }

    /// <summary>
    /// Extra rules applied to this call only, on top of any registered on the builder. Each entry
    /// must implement <see cref="Validation.IInstructorValidator{T}"/> for the type being extracted;
    /// entries for other types are ignored.
    /// </summary>
    public IList<object> Validators { get; } = [];

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
    /// Creates a copy, so that options handed to an instructor are insulated from later mutation
    /// of the caller's instance.
    /// </summary>
    /// <returns>A copy of these options.</returns>
    internal InstructorOptions Clone()
    {
        var copy = new InstructorOptions
        {
            MaxAttempts = MaxAttempts,
            Mode = Mode,
            SerializerOptions = SerializerOptions,
            TokenBudget = TokenBudget,
            UseStrictSchema = UseStrictSchema,
            ValidateDataAnnotations = ValidateDataAnnotations,
            MaxValidationDepth = MaxValidationDepth,
            MaxStreamBytes = MaxStreamBytes,
            SchemaName = SchemaName,
            ChatOptions = ChatOptions?.Clone(),
        };

        foreach (object validator in Validators)
        {
            copy.Validators.Add(validator);
        }

        return copy;
    }
}
