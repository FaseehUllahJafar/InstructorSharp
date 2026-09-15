using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InstructorSharp.Validation;

/// <summary>
/// Walks a deserialized object graph applying <c>System.ComponentModel.DataAnnotations</c>
/// attributes, reporting each failure against the JSON path the model actually wrote.
/// </summary>
/// <remarks>
/// <para>
/// The framework's own <c>Validator.TryValidateObject</c> is deliberately shallow: it checks
/// the top-level object and stops, so a bad <c>total</c> on the third invoice line passes
/// straight through. This walks nested objects, collections and dictionaries.
/// </para>
/// <para>
/// Paths are reported in JSON terms (<c>$.lines[2].total</c>) rather than CLR terms
/// (<c>Lines[2].Total</c>) because the model is being asked to fix the document it wrote,
/// and it has never seen your property names.
/// </para>
/// <para>
/// Reflection is intrinsic to what this does: the attributes are discovered at runtime and
/// the graph is walked by property. That is not trimmable, and pretending otherwise would be
/// worse than saying so. Callers publishing Native AOT should set
/// <see cref="InstructorOptions.ValidateDataAnnotations"/> to false and express their rules as
/// <see cref="IInstructorValidator{T}"/> instead, which is fully AOT-safe.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(
    "DataAnnotations validation reflects over the object graph. Set " +
    "InstructorOptions.ValidateDataAnnotations to false and use IInstructorValidator<T> when trimming.")]
[RequiresDynamicCode(
    "DataAnnotations validation reflects over the object graph. Set " +
    "InstructorOptions.ValidateDataAnnotations to false and use IInstructorValidator<T> when publishing Native AOT.")]
internal sealed class DataAnnotationsValidator
{
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly int _maxDepth;

    internal DataAnnotationsValidator(JsonSerializerOptions serializerOptions, int maxDepth)
    {
        _serializerOptions = serializerOptions;
        _maxDepth = maxDepth;
    }

    internal IReadOnlyList<ValidationFailure> Validate(object? value)
    {
        if (value is null)
        {
            return Array.Empty<ValidationFailure>();
        }

        var failures = new List<ValidationFailure>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(value, "$", failures, visited, 0);
        return failures;
    }

    private void Walk(object? value, string path, List<ValidationFailure> failures, HashSet<object> visited, int depth)
    {
        if (value is null || depth > _maxDepth)
        {
            return;
        }

        Type type = value.GetType();

        if (IsScalar(type))
        {
            return;
        }

        // Two problems, one guard. A reference cycle would recurse until the stack gave out, and
        // a shared node reachable by several paths would be re-walked once per path, which is
        // exponential on a wide graph. Membership is therefore permanent for the whole walk:
        // each object is validated exactly once, under the first path that reaches it.
        if (!visited.Add(value))
        {
            return;
        }

        {
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    Walk(entry.Value, $"{path}.{entry.Key}", failures, visited, depth + 1);
                }

                return;
            }

            if (value is IEnumerable enumerable and not string)
            {
                int index = 0;
                foreach (object? item in enumerable)
                {
                    Walk(item, $"{path}[{index}]", failures, visited, depth + 1);
                    index++;
                }

                return;
            }

            ValidateMembers(value, type, path, failures);

            foreach (PropertyInfo property in GetReadableProperties(type))
            {
                object? child = TryGetValue(property, value);
                if (child is null || IsScalar(child.GetType()))
                {
                    continue;
                }

                Walk(child, $"{path}.{JsonNameOf(property)}", failures, visited, depth + 1);
            }
        }
    }

    private void ValidateMembers(object value, Type type, string path, List<ValidationFailure> failures)
    {
        foreach (PropertyInfo property in GetReadableProperties(type))
        {
            ValidationAttribute[] attributes = GetValidationAttributes(property);
            if (attributes.Length == 0)
            {
                continue;
            }

            object? propertyValue = TryGetValue(property, value);
            string propertyPath = $"{path}.{JsonNameOf(property)}";

            var context = new ValidationContext(value)
            {
                MemberName = property.Name,
                DisplayName = JsonNameOf(property),
            };

            foreach (ValidationAttribute attribute in attributes)
            {
                ValidationResult? result;
                try
                {
                    result = attribute.GetValidationResult(propertyValue, context);
                }
                catch (Exception ex)
                {
                    // A validation attribute that throws is a bug in the attribute, but it
                    // must not take down the extraction.
                    failures.Add(new ValidationFailure(propertyPath, $"validation rule threw: {ex.Message}"));
                    continue;
                }

                if (result is not null && result != ValidationResult.Success)
                {
                    failures.Add(new ValidationFailure(
                        propertyPath,
                        result.ErrorMessage ?? $"failed {attribute.GetType().Name}"));
                }
            }
        }
    }

    private static ValidationAttribute[] GetValidationAttributes(PropertyInfo property)
    {
        object[] attributes = property.GetCustomAttributes(typeof(ValidationAttribute), inherit: true);
        if (attributes.Length == 0)
        {
            return Array.Empty<ValidationAttribute>();
        }

        var result = new ValidationAttribute[attributes.Length];
        for (int i = 0; i < attributes.Length; i++)
        {
            result[i] = (ValidationAttribute)attributes[i];
        }

        return result;
    }

    private static IEnumerable<PropertyInfo> GetReadableProperties(Type type)
    {
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
            {
                yield return property;
            }
        }
    }

    private static object? TryGetValue(PropertyInfo property, object instance)
    {
        try
        {
            return property.GetValue(instance);
        }
        catch (TargetInvocationException)
        {
            // A computed property that throws on partially-populated data is not the model's fault.
            return null;
        }
    }

    private string JsonNameOf(PropertyInfo property)
    {
        var attribute = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (attribute is not null)
        {
            return attribute.Name;
        }

        return _serializerOptions.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan) ||
        type == typeof(Guid) ||
        type == typeof(Uri) ||
        Nullable.GetUnderlyingType(type) is not null;

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
