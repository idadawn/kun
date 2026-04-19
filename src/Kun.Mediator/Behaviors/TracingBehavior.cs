using System.Diagnostics;

namespace Kun.Mediator.Behaviors;

/// <summary>
/// Pipeline behavior that starts an <see cref="Activity"/> for each dispatched request
/// using the <c>"Kun.Mediator"</c> <see cref="ActivitySource"/>. Compatible with
/// OpenTelemetry — attach a listener via <c>AddSource("Kun.Mediator")</c> in the
/// OpenTelemetry builder.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
{
    /// <summary>The <see cref="ActivitySource"/> name used by all Kun.Mediator activities.</summary>
    public const string ActivitySourceName = "Kun.Mediator";

    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    /// <inheritdoc/>
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        using var activity = s_activitySource.StartActivity(
            $"Mediator.Send {typeof(TRequest).Name}",
            ActivityKind.Internal);

        return await next().ConfigureAwait(false);
    }
}
