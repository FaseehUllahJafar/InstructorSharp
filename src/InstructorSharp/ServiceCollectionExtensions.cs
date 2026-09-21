using System.Diagnostics.CodeAnalysis;
using InstructorSharp.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace InstructorSharp;

/// <summary>
/// Registration helpers for applications that already resolve an <see cref="IChatClient"/> from
/// the container, which is the normal shape of an ASP.NET Core service.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IInstructor"/> as a singleton over the <see cref="IChatClient"/>
    /// already in the container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Singleton is the right lifetime: an instructor holds no per-request state, and the schema
    /// cache it shares is the thing you want reused across requests rather than rebuilt per call.
    /// </remarks>
    public static IServiceCollection AddInstructor(
        this IServiceCollection services,
        Action<InstructorBuilder>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton<IInstructor>(provider =>
        {
            IChatClient client = provider.GetRequiredService<IChatClient>();
            var builder = new InstructorBuilder(client);

            // Any validators registered in the container are picked up automatically, so an
            // application can drop in a rule class the same way it drops in any other service.
            foreach (IInstructorValidatorMarker registration in provider.GetServices<IInstructorValidatorMarker>())
            {
                builder.AddValidatorObject(registration.Validator);
            }

            configure?.Invoke(builder);
            return builder.Build();
        });

        return services;
    }

    /// <summary>
    /// Registers a custom validation rule so that <see cref="AddInstructor"/> picks it up.
    /// </summary>
    /// <typeparam name="TValidator">The rule implementation.</typeparam>
    /// <typeparam name="TModel">The type the rule validates.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddInstructorValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator,
        TModel>(this IServiceCollection services)
        where TValidator : class, IInstructorValidator<TModel>
    {
        services.AddSingleton<TValidator>();
        services.AddSingleton<IInstructorValidatorMarker>(
            provider => new ValidatorRegistration(provider.GetRequiredService<TValidator>()));

        return services;
    }

    /// <summary>
    /// Registers an already-constructed validation rule.
    /// </summary>
    /// <typeparam name="TModel">The type the rule validates.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="validator">The rule.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddInstructorValidator<TModel>(
        this IServiceCollection services,
        IInstructorValidator<TModel> validator)
    {
        services.AddSingleton<IInstructorValidatorMarker>(new ValidatorRegistration(validator));
        return services;
    }

    private sealed class ValidatorRegistration : IInstructorValidatorMarker
    {
        internal ValidatorRegistration(object validator) => Validator = validator;

        public object Validator { get; }
    }
}

/// <summary>
/// Lets validators of differing generic arguments share one container registration. Internal on
/// purpose: it is DI plumbing, not something a consumer should implement.
/// </summary>
/// <remarks>
/// <see cref="IInstructorValidator{T}"/> is generic and contravariant, so there is no single
/// closed type the container can resolve them all by. This non-generic marker is the seam that
/// makes <c>GetServices</c> able to return the whole set.
/// </remarks>
internal interface IInstructorValidatorMarker
{
    /// <summary>The wrapped <see cref="IInstructorValidator{T}"/> instance.</summary>
    object Validator { get; }
}
