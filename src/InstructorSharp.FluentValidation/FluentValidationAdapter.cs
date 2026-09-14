using System.Text;
using FluentValidation;
using FluentValidation.Results;
using InstructorSharp.Validation;

namespace InstructorSharp.FluentValidation;

/// <summary>
/// Adapts a FluentValidation <see cref="IValidator{T}"/> into an
/// <see cref="IInstructorValidator{T}"/>, so rules you already maintain become the repair
/// instructions sent back to the model.
/// </summary>
/// <typeparam name="T">The validated type.</typeparam>
/// <remarks>
/// Most teams doing extraction already own validators for these shapes, written for the API
/// boundary. Rewriting them as attributes to satisfy a library is wasted work and a second
/// place for the rules to drift out of sync. The only translation needed is the property path:
/// FluentValidation reports <c>Lines[2].Total</c> in CLR terms, and the model has to be told
/// about <c>$.lines[2].total</c>, because that is what it wrote.
/// </remarks>
public sealed class FluentValidationAdapter<T> : IInstructorValidator<T>
{
    private readonly IValidator<T> _validator;
    private readonly Func<string, string> _propertyNamePolicy;

    /// <summary>Wraps a FluentValidation validator.</summary>
    /// <param name="validator">The validator.</param>
    /// <param name="propertyNamePolicy">
    /// Converts a single CLR property name to its JSON name. Defaults to camelCase, matching
    /// the library's own default serializer settings.
    /// </param>
    public FluentValidationAdapter(IValidator<T> validator, Func<string, string>? propertyNamePolicy = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _propertyNamePolicy = propertyNamePolicy ?? ToCamelCase;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        T value,
        CancellationToken cancellationToken = default)
    {
        ValidationResult result = await _validator.ValidateAsync(value, cancellationToken).ConfigureAwait(false);

        if (result.IsValid)
        {
            return Array.Empty<ValidationFailure>();
        }

        var failures = new List<ValidationFailure>(result.Errors.Count);
        foreach (global::FluentValidation.Results.ValidationFailure error in result.Errors)
        {
            failures.Add(new ValidationFailure(ToJsonPath(error.PropertyName), error.ErrorMessage));
        }

        return failures;
    }

    /// <summary>
    /// Rewrites a FluentValidation property path such as <c>Lines[2].Total</c> into the JSON
    /// path <c>$.lines[2].total</c>, leaving the collection indexers intact.
    /// </summary>
    private string ToJsonPath(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return "$";
        }

        var builder = new StringBuilder("$");

        foreach (string segment in propertyName.Split('.'))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            int bracket = segment.IndexOf('[');
            if (bracket < 0)
            {
                builder.Append('.').Append(_propertyNamePolicy(segment));
            }
            else
            {
                builder.Append('.')
                       .Append(_propertyNamePolicy(segment.Substring(0, bracket)))
                       .Append(segment.Substring(bracket));
            }
        }

        return builder.ToString();
    }

    private static string ToCamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }

        char[] chars = name.ToCharArray();
        chars[0] = char.ToLowerInvariant(chars[0]);
        return new string(chars);
    }
}

/// <summary>Registration helpers for the FluentValidation adapter.</summary>
public static class FluentValidationInstructorBuilderExtensions
{
    /// <summary>
    /// Adds a FluentValidation validator as a repair rule.
    /// </summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="builder">The instructor builder.</param>
    /// <param name="validator">The FluentValidation validator.</param>
    /// <param name="propertyNamePolicy">Optional CLR-to-JSON property name conversion.</param>
    /// <returns>The builder, for chaining.</returns>
    public static InstructorBuilder AddFluentValidator<T>(
        this InstructorBuilder builder,
        IValidator<T> validator,
        Func<string, string>? propertyNamePolicy = null)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.AddValidator(new FluentValidationAdapter<T>(validator, propertyNamePolicy));
    }
}
