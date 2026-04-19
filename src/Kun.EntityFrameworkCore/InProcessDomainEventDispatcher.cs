using System.Reflection;
using Kun.Core.Domain;
using Kun.Mediator;

namespace Kun.EntityFrameworkCore;

/// <summary>
/// Dispatches domain events to in-process Mediator notification handlers.
/// Domain events that also implement <see cref="INotification"/> are routed to all
/// registered <see cref="INotificationHandler{T}"/>s via <see cref="IPublisher"/>.
/// Events that do not implement <see cref="INotification"/> are silently skipped.
/// </summary>
/// <remarks>
/// <see cref="IPublisher.Publish{TNotification}"/> is a generic method keyed on the
/// concrete notification type at runtime. Because <c>Kun.EntityFrameworkCore</c> is not
/// an AOT candidate (<c>IsAotCandidate=false</c>), reflection is permitted here to
/// invoke <c>Publish</c> with the exact concrete type so the Mediator dispatcher can
/// resolve the correct <see cref="INotificationHandler{T}"/> registrations.
/// </remarks>
public sealed class InProcessDomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly MethodInfo s_publishMethod =
        typeof(IPublisher).GetMethod(nameof(IPublisher.Publish))!;

    private readonly IPublisher _publisher;

    /// <summary>Creates a new <see cref="InProcessDomainEventDispatcher"/>.</summary>
    /// <param name="publisher">The Mediator publisher used to broadcast notifications.</param>
    public InProcessDomainEventDispatcher(IPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        _publisher = publisher;
    }

    /// <inheritdoc/>
    public async ValueTask DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            if (domainEvent is INotification)
            {
                var concreteType = domainEvent.GetType();
                var publishMethod = s_publishMethod.MakeGenericMethod(concreteType);
                var task = (ValueTask)publishMethod.Invoke(_publisher, [domainEvent, cancellationToken])!;
                await task.ConfigureAwait(false);
            }
        }
    }
}
