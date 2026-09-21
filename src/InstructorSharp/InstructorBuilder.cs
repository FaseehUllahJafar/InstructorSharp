using InstructorSharp.Modes;
using InstructorSharp.Validation;
using Microsoft.Extensions.AI;

namespace InstructorSharp;

/// <summary>
/// Fluent configuration for an <see cref="IInstructor"/>.
/// </summary>
public sealed class InstructorBuilder
{
    private readonly IChatClient _client;
    private readonly InstructorOptions _options = new();
    private readonly List<object> _validators = [];
    private List<ExtractionStrategy>? _strategies;

    /// <summary>Starts building an instructor over the given client.</summary>
    /// <param name="client">The underlying chat client.</param>
    public InstructorBuilder(IChatClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>Adjusts the options.</summary>
    /// <param name="configure">Callback that mutates the options.</param>
    /// <returns>This builder.</returns>
    public InstructorBuilder Configure(Action<InstructorOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(_options);
        return this;
    }

    /// <summary>Sets how many times the model may be asked, including the first attempt.</summary>
    /// <param name="maxAttempts">Total attempts. Must be at least 1.</param>
    /// <returns>This builder.</returns>
    public InstructorBuilder WithMaxAttempts(int maxAttempts)
    {
        _options.MaxAttempts = maxAttempts;
        return this;
    }

    /// <summary>Forces a specific extraction mode instead of detecting one.</summary>
    /// <param name="mode">The mode to use.</param>
    /// <returns>This builder.</returns>
    public InstructorBuilder WithMode(ExtractionMode mode)
    {
        _options.Mode = mode;
        return this;
    }

    /// <summary>Caps the total tokens a single extraction may spend across all attempts.</summary>
    /// <param name="tokens">The ceiling.</param>
    /// <returns>This builder.</returns>
    public InstructorBuilder WithTokenBudget(long tokens)
    {
        _options.TokenBudget = tokens;
        return this;
    }

    /// <summary>
    /// Adds a custom rule. Its failures are fed back to the model on the next attempt exactly
    /// as written.
    /// </summary>
    /// <typeparam name="T">The type the rule applies to.</typeparam>
    /// <param name="validator">The rule.</param>
    /// <returns>This builder.</returns>
    public InstructorBuilder AddValidator<T>(IInstructorValidator<T> validator)
    {
        _validators.Add(validator ?? throw new ArgumentNullException(nameof(validator)));
        return this;
    }

    /// <summary>
    /// Adds a custom rule expressed as a delegate.
    /// </summary>
    /// <typeparam name="T">The type the rule applies to.</typeparam>
    /// <param name="rule">
    /// Returns the reasons the value is unacceptable, or an empty sequence when it is fine.
    /// </param>
    /// <returns>This builder.</returns>
    public InstructorBuilder AddValidator<T>(Func<T, IReadOnlyList<ValidationFailure>> rule)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        _validators.Add(new DelegateValidator<T>(rule));
        return this;
    }

    /// <summary>
    /// Registers a validator whose generic argument is not known statically, which is how
    /// container-resolved rules arrive.
    /// </summary>
    /// <param name="validator">An object implementing <see cref="IInstructorValidator{T}"/>.</param>
    /// <returns>This builder.</returns>
    public InstructorBuilder AddValidatorObject(object validator)
    {
        _validators.Add(validator ?? throw new ArgumentNullException(nameof(validator)));
        return this;
    }

    /// <summary>
    /// Registers a strategy, which takes precedence over the built-in ones of the same mode.
    /// </summary>
    /// <param name="strategy">The strategy.</param>
    /// <returns>This builder.</returns>
    public InstructorBuilder AddStrategy(ExtractionStrategy strategy)
    {
        if (strategy is null)
        {
            throw new ArgumentNullException(nameof(strategy));
        }

        _strategies ??= [.. Instructor.DefaultStrategies];
        _strategies.Insert(0, strategy);
        return this;
    }

    /// <summary>Builds the instructor.</summary>
    /// <returns>A configured <see cref="IInstructor"/>.</returns>
    /// <remarks>
    /// Copies are taken, so a builder that is kept and mutated afterwards cannot alter an
    /// instructor that has already been built -- which would otherwise throw
    /// "collection was modified" from inside an unrelated in-flight request.
    /// </remarks>
    public IInstructor Build() =>
        new Instructor(_client, _options, _strategies?.ToArray(), _validators.ToArray());

    private sealed class DelegateValidator<T> : IInstructorValidator<T>
    {
        private readonly Func<T, IReadOnlyList<ValidationFailure>> _rule;

        internal DelegateValidator(Func<T, IReadOnlyList<ValidationFailure>> rule) => _rule = rule;

        public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
            T value,
            CancellationToken cancellationToken = default) =>
            new(_rule(value));
    }
}
