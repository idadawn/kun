using System.Text.Json.Serialization.Metadata;
using Kun.Core.Cqrs;
using Kun.Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.AspNetCore.Endpoints;

/// <summary>
/// Extension methods on <see cref="IEndpointRouteBuilder"/> for mapping Kun CQRS
/// requests to Minimal API endpoints in an AOT-safe manner.
/// </summary>
public static class KunEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a POST endpoint at <paramref name="pattern"/> that deserializes the request body
    /// into <typeparamref name="TCommand"/> and dispatches it via <see cref="ISender"/>.
    /// </summary>
    /// <typeparam name="TCommand">The command type (must implement <see cref="IRequest{TResponse}"/>).</typeparam>
    /// <typeparam name="TResponse">The response type returned by the handler.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern for the endpoint.</param>
    /// <param name="commandTypeInfo">AOT-safe <see cref="JsonTypeInfo{T}"/> for <typeparamref name="TCommand"/>.</param>
    /// <param name="responseTypeInfo">AOT-safe <see cref="JsonTypeInfo{T}"/> for <typeparamref name="TResponse"/>.</param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> for further configuration.</returns>
    public static RouteHandlerBuilder MapCommand<TCommand, TResponse>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        JsonTypeInfo<TCommand> commandTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo)
        where TCommand : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(commandTypeInfo);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);

        return endpoints.MapPost(pattern, async (HttpContext ctx, CancellationToken ct) =>
        {
            var command = await ctx.Request.ReadFromJsonAsync(commandTypeInfo, ct);
            if (command is null)
                return Results.BadRequest();

            var sender = ctx.RequestServices.GetRequiredService<ISender>();
            var response = await sender.Send(command, ct);
            return Results.Ok(response);
        });
    }

    /// <summary>
    /// Maps a GET endpoint at <paramref name="pattern"/> that binds query string / route parameters
    /// into <typeparamref name="TQuery"/> via <c>[AsParameters]</c> and dispatches it via <see cref="ISender"/>.
    /// </summary>
    /// <typeparam name="TQuery">The query type (must implement <see cref="IRequest{TResponse}"/>).</typeparam>
    /// <typeparam name="TResponse">The response type returned by the handler.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern for the endpoint.</param>
    /// <param name="responseTypeInfo">AOT-safe <see cref="JsonTypeInfo{T}"/> for <typeparamref name="TResponse"/>.</param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> for further configuration.</returns>
    public static RouteHandlerBuilder MapQuery<TQuery, TResponse>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        JsonTypeInfo<TResponse> responseTypeInfo)
        where TQuery : IRequest<TResponse>, new()
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);

        return endpoints.MapGet(pattern, async ([AsParameters] TQuery query, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(query, ct);
            return Results.Ok(response);
        });
    }
}
