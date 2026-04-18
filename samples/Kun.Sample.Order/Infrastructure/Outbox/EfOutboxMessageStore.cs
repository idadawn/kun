using Kun.EventBus.Background;
using Kun.EventBus.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Kun.Sample.Order.Infrastructure.Outbox;

/// <summary>
/// EF Core-backed <see cref="IOutboxMessageStore"/> that reads and updates
/// <see cref="OutboxMessage"/> rows from <see cref="OrderDbContext"/>.
/// Required by <see cref="Kun.EventBus.Background.OutboxPublisherBackgroundService"/>.
/// </summary>
public sealed class EfOutboxMessageStore : IOutboxMessageStore
{
    private readonly OrderDbContext _db;

    /// <summary>Creates a new <see cref="EfOutboxMessageStore"/>.</summary>
    public EfOutboxMessageStore(OrderDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(
        int batchSize, CancellationToken cancellationToken = default)
    {
        return await _db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending)
            .OrderBy(m => m.OccurredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        _db.OutboxMessages.Update(message);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
