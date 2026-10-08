using Kun.Core.MultiTenancy;
using Kun.EventBus.Background;
using Kun.EventBus.Outbox;
using Kun.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.EventBus.Tests.Background;

public sealed class OutboxPublisherBackgroundServiceTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeOutboxMessageStore : IOutboxMessageStore
    {
        private readonly List<OutboxMessage> _messages;
        public List<OutboxMessage> Updated { get; } = [];

        public FakeOutboxMessageStore(IEnumerable<OutboxMessage> messages)
            => _messages = [.. messages];

        public Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboxMessage>>(_messages.Take(batchSize).ToList());

        public Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Updated.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<OutboxMessage> Published { get; } = [];
        public bool ShouldThrow { get; set; }

        public ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("broker unavailable");
            }

            Published.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OutboxMessage MakePendingMessage() =>
        new(
            Guid.NewGuid(),
            "order.placed.v1",
            "{}",
            tenantId: null,
            traceParent: null,
            traceState: null,
            DateTimeOffset.UtcNow);

    private static OutboxPublisherBackgroundService BuildService(
        FakeOutboxMessageStore store,
        FakeIntegrationEventPublisher publisher,
        OutboxPublisherOptions? options = null,
        IOutboxLockProvider? lockProvider = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxMessageStore>(store);

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new OutboxPublisherBackgroundService(
            scopeFactory,
            publisher,
            lockProvider ?? NullOutboxLockProvider.Instance,
            Options.Create(options ?? new OutboxPublisherOptions()),
            NullLogger<OutboxPublisherBackgroundService>.Instance,
            TimeProvider.System);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessBatch_SuccessfulPublish_MarksPublished()
    {
        var message = MakePendingMessage();
        var store = new FakeOutboxMessageStore([message]);
        var publisher = new FakeIntegrationEventPublisher();

        var svc = BuildService(store, publisher);
        await svc.ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Single(publisher.Published);
        Assert.Equal(OutboxStatus.Published, message.Status);
        Assert.NotNull(message.PublishedAt);
        Assert.Single(store.Updated);
    }

    [Fact]
    public async Task ProcessBatch_FailingPublish_IncrementsAttempt_StatusRemainsDefault()
    {
        var message = MakePendingMessage();
        var store = new FakeOutboxMessageStore([message]);
        var publisher = new FakeIntegrationEventPublisher { ShouldThrow = true };

        var svc = BuildService(store, publisher, new OutboxPublisherOptions { MaxAttempts = 5 });
        await svc.ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Empty(publisher.Published);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Single(store.Updated);
    }

    [Fact]
    public async Task ProcessBatch_FailingPublish_AfterMaxAttempts_MarksFailedAndRecordsError()
    {
        var message = MakePendingMessage();
        // Simulate 4 prior failed attempts so the next one hits the limit (MaxAttempts = 5).
        for (var i = 0; i < 4; i++) message.IncrementAttempt();

        var store = new FakeOutboxMessageStore([message]);
        var publisher = new FakeIntegrationEventPublisher { ShouldThrow = true };

        var svc = BuildService(store, publisher, new OutboxPublisherOptions { MaxAttempts = 5 });
        await svc.ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Equal("broker unavailable", message.LastError);
        Assert.Equal(5, message.AttemptCount);
        Assert.Single(store.Updated);
    }

    [Fact]
    public async Task ProcessBatch_NoPendingMessages_DoesNothing()
    {
        var store = new FakeOutboxMessageStore([]);
        var publisher = new FakeIntegrationEventPublisher();

        var svc = BuildService(store, publisher);
        await svc.ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Empty(publisher.Published);
        Assert.Empty(store.Updated);
    }

    [Fact]
    public async Task ProcessBatch_MultiplePendingMessages_PublishesAll()
    {
        var messages = new[] { MakePendingMessage(), MakePendingMessage(), MakePendingMessage() };
        var store = new FakeOutboxMessageStore(messages);
        var publisher = new FakeIntegrationEventPublisher();

        var svc = BuildService(store, publisher);
        await svc.ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, publisher.Published.Count);
        Assert.All(messages, m => Assert.Equal(OutboxStatus.Published, m.Status));
        Assert.Equal(3, store.Updated.Count);
    }

    [Fact]
    public async Task Service_StartsAndStops_WithoutError()
    {
        var store = new FakeOutboxMessageStore([]);
        var publisher = new FakeIntegrationEventPublisher();

        var svc = BuildService(store, publisher, new OutboxPublisherOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(50)
        });

        await svc.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await svc.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessBatch_WhenLockUnavailable_NoMessagesArePublished()
    {
        // Lock provider that always returns null — simulates another pod holding the lock.
        var message = MakePendingMessage();
        var store = new FakeOutboxMessageStore([message]);
        var publisher = new FakeIntegrationEventPublisher();

        var svc = BuildService(store, publisher, lockProvider: new AlwaysLockedLockProvider());
        await svc.ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Empty(publisher.Published);
        Assert.Empty(store.Updated);
        Assert.Equal(OutboxStatus.Pending, message.Status);
    }

    [Fact]
    public async Task ProcessBatch_TenantsAndFailure_RestoresScopePerMessage()
    {
        var tenant = new AsyncLocalTenantContext();
        string[] tenantIds = ["tenant-a", "tenant-b", "tenant-c"];
        var messages = tenantIds.Select(id =>
            new OutboxMessage(Guid.NewGuid(), "test.v1", "{}", id, null, null, DateTimeOffset.UtcNow)).ToArray();
        var store = new FakeOutboxMessageStore(messages);
        using var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenant)
            .AddSingleton<IOutboxMessageStore>(store)
            .BuildServiceProvider();
        var publisher = new TenantObservingPublisher(tenant);
        using var background = new OutboxPublisherBackgroundService(
            services.GetRequiredService<IServiceScopeFactory>(), publisher, NullOutboxLockProvider.Instance,
            Options.Create(new OutboxPublisherOptions()), NullLogger<OutboxPublisherBackgroundService>.Instance,
            TimeProvider.System);
        using (tenant.BeginScope("outer"))
        {
            await background.ProcessBatchAsync(TestContext.Current.CancellationToken);
            Assert.Equal("outer", tenant.TenantId);
        }
        Assert.Null(tenant.TenantId);
        Assert.Equal(tenantIds, publisher.Observed);
        Assert.Equal(OutboxStatus.Published, messages[0].Status);
        Assert.Equal(1, messages[1].AttemptCount);
        Assert.Equal(OutboxStatus.Published, messages[2].Status);
    }

    private sealed class TenantObservingPublisher(ITenantContext tenant) : IIntegrationEventPublisher
    {
        public List<string> Observed { get; } = [];
        public async ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Assert.Equal(message.TenantId, tenant.TenantId);
            Observed.Add(tenant.TenantId!);
            if (message.TenantId == "tenant-b") throw new InvalidOperationException("broker unavailable");
        }
    }

    private sealed class AlwaysLockedLockProvider : IOutboxLockProvider
    {
        public ValueTask<IAsyncDisposable?> TryAcquireAsync(string lockName, TimeSpan ttl, CancellationToken ct = default)
            => ValueTask.FromResult<IAsyncDisposable?>(null);
    }
}
