using Kun.Core.Domain;
using Kun.EventBus.Outbox;

namespace Kun.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IUnitOfWork"/> that atomically persists aggregate changes,
/// dispatches domain events in-process, and writes integration events to the Outbox.
/// </summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private bool _dispatchFailed;
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
        if (_dispatchFailed) throw new InvalidOperationException("通知处理失败后必须释放当前工作单元并从新作用域重试。");
        // 提交前派发并 drain 级联领域事件；通知产生的集成事件必须进入本次事务。
        for (var round = 0; ; round++)
        {
            var aggregates = _dbContext.ChangeTracker.Entries<IAggregateRoot>().Select(e => e.Entity).ToArray();
            var events = aggregates.SelectMany(a => a.DomainEvents).ToArray();
            if (events.Length == 0) break;
            if (round >= 100) throw new InvalidOperationException("领域事件级联超过 100 轮，拒绝提交循环事件。");
            foreach (var aggregate in aggregates) aggregate.ClearDomainEvents();
            try
            {
                await _domainEvents.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _dispatchFailed = true;
                throw;
            }
        }
        var pending = _integrationBuffer.GetPendingMessages();
        if (pending.Count > 0) _dbContext.OutboxMessages.AddRange(pending);
        var affected = await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _integrationBuffer.Clear();
        return affected;
    }
}
