using Kun.Core.Domain;
using Kun.EventBus.Outbox;

namespace Kun.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IUnitOfWork"/> that atomically persists aggregate changes,
/// dispatches domain events in-process, and writes integration events to the Outbox.
/// </summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly KunDbContext _dbContext;
    private readonly IDomainEventDispatcher _domainEvents;
    private readonly IntegrationEventBuffer _integrationBuffer;

    /// <summary>Creates a new <see cref="EfUnitOfWork"/>.</summary>
    public EfUnitOfWork(
        KunDbContext dbContext,
        IDomainEventDispatcher domainEvents,
        IntegrationEventBuffer integrationBuffer)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(domainEvents);
        ArgumentNullException.ThrowIfNull(integrationBuffer);
        _dbContext = dbContext;
        _domainEvents = domainEvents;
        _integrationBuffer = integrationBuffer;
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Collect domain events from all tracked aggregates, then clear them.
        var aggregates = _dbContext.ChangeTracker
            .Entries<IAggregateRoot>()
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = aggregates
            .SelectMany(a => a.DomainEvents)
            .ToList();

        foreach (var agg in aggregates)
        {
            agg.ClearDomainEvents();
        }

        // 2. Drain integration event buffer → OutboxMessages (appended atomically).
        var pending = _integrationBuffer.GetPendingMessages();
        if (pending.Count > 0)
        {
            _dbContext.OutboxMessages.AddRange(pending);
            _integrationBuffer.Clear();
        }

        // 3. Commit aggregate changes + outbox rows in ONE transaction.
        var affected = await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 4. Dispatch domain events in-process AFTER commit (local, non-transactional).
        if (domainEvents.Count > 0)
        {
            await _domainEvents.DispatchAsync(domainEvents, cancellationToken).ConfigureAwait(false);
        }

        return affected;
    }
}
