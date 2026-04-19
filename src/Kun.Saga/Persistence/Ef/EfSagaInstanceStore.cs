using Kun.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kun.Saga.Persistence.Ef;

/// <summary>
/// EF Core-backed implementation of <see cref="ISagaInstanceStore"/>.
/// Operates against <c>saga_instances</c> and <c>saga_step_queue</c> tables
/// via <c>DbContext.Set&lt;T&gt;</c>.
/// </summary>
public sealed class EfSagaInstanceStore : ISagaInstanceStore
{
    private readonly KunDbContext _dbContext;

    /// <summary>Creates a new <see cref="EfSagaInstanceStore"/>.</summary>
    /// <param name="dbContext">The <see cref="KunDbContext"/> to use for persistence.</param>
    public EfSagaInstanceStore(KunDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public Task<SagaInstance?> FindAsync(Guid sagaId, CancellationToken ct = default)
        => _dbContext.Set<SagaInstance>().FirstOrDefaultAsync(s => s.Id == sagaId, ct);

    /// <inheritdoc/>
    public async Task AddAsync(SagaInstance instance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        await _dbContext.Set<SagaInstance>().AddAsync(instance, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(SagaInstance instance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _dbContext.Set<SagaInstance>().Update(instance);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SagaInstance>> FindTimedOutAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken ct = default)
    {
        return await _dbContext.Set<SagaInstance>()
            .Where(s => s.State == SagaState.Running && s.TimeoutAt != null && s.TimeoutAt < now)
            .OrderBy(s => s.TimeoutAt)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SagaStepQueueEntry>> FetchPendingStepsAsync(
        int batchSize,
        CancellationToken ct = default)
    {
        return await _dbContext.Set<SagaStepQueueEntry>()
            .Where(e => e.ProcessedAt == null)
            .OrderBy(e => e.EnqueuedAt)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task EnqueueStepAsync(SagaStepQueueEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _dbContext.Set<SagaStepQueueEntry>().AddAsync(entry, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task UpdateStepAsync(SagaStepQueueEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _dbContext.Set<SagaStepQueueEntry>().Update(entry);
        return Task.CompletedTask;
    }
}
