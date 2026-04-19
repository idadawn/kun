namespace Kun.EntityFrameworkCore;

/// <summary>Atomic unit-of-work boundary for command handlers.</summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits aggregate changes, dispatches domain events, and flushes buffered integration
    /// events to the Outbox in a single transaction.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
