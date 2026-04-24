using System.Diagnostics.CodeAnalysis;
using Kun.Core.Cqrs;
using Kun.Core.Results;
using Kun.Mediator;
using Kun.Validation.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.Validation.DependencyInjection;

/// <summary>
/// Extension methods to register Kun.Validation services into an <see cref="IServiceCollection"/>
/// or a <see cref="KunMediatorBuilder"/>.
/// </summary>
public static class KunValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TValidator"/> as a transient
    /// <see cref="IValidator{TRequest}"/> for <typeparamref name="TRequest"/>.
    /// </summary>
    /// <typeparam name="TRequest">The request type to validate.</typeparam>
    /// <typeparam name="TValidator">The validator implementation type.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddValidator<TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator>(
        this IServiceCollection services)
        where TValidator : class, IValidator<TRequest>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<IValidator<TRequest>, TValidator>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ValidationPipelineBehavior{TRequest,TResponse}"/> for the given
    /// request+response pair on a <see cref="KunMediatorBuilder"/>.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="builder">The mediator builder to register the behavior on.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static KunMediatorBuilder AddValidationBehavior<TRequest, TResponse>(
        this KunMediatorBuilder builder)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddPipelineBehavior<TRequest, TResponse, ValidationPipelineBehavior<TRequest, TResponse>>();
        return builder;
    }

    /// <summary>
    /// Registers <see cref="ValidationResultPipelineBehavior{TRequest, TValue}"/>
    /// for the given request whose response is <c>Result&lt;TValue&gt;</c>. On
    /// validator failure the behaviour returns
    /// <c>Result&lt;TValue&gt;.Fail(Error.Validation("request.invalid", ...))</c>
    /// instead of throwing <see cref="ValidationException"/>. Do <b>not</b>
    /// also call <see cref="AddValidationBehavior{TRequest, TResponse}"/> for
    /// the same request type — pick one. See
    /// <c>docs/architecture/ADR-014-Validation-Result-behavior.md</c>.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TValue">The success value carried by the handler's <see cref="Result{TValue}"/>.</typeparam>
    /// <param name="builder">The mediator builder to register the behavior on.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static KunMediatorBuilder AddValidationResultBehavior<TRequest, TValue>(
        this KunMediatorBuilder builder)
        where TRequest : IRequest<Result<TValue>>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddPipelineBehavior<
            TRequest,
            Result<TValue>,
            ValidationResultPipelineBehavior<TRequest, TValue>>();
        return builder;
    }
}
