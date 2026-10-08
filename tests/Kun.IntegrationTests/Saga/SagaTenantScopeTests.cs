using System.Collections.Concurrent;
using Kun.Core.MultiTenancy;
using Kun.Core.Time;
using Kun.EntityFrameworkCore;
using Kun.IntegrationTests.Fixtures;
using Kun.MultiTenancy.DependencyInjection;
using Kun.Saga;
using Kun.Saga.Orchestration;
using Kun.Saga.Persistence;
using Kun.Saga.Persistence.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kun.IntegrationTests.Saga;

public sealed class SagaTenantScopeTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;
    public SagaTenantScopeTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Executor_TwoTenants_RestoresTenantForExecutionAndCompensation()
    {
        var observed = new ConcurrentQueue<(string? Tenant, StepDirection Direction, Guid DependencyScope)>();
        using var host = Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddKunMultiTenancy(options => options.Production = true);
            services.AddDbContext<SagaTestDbContext>(options => options.UseNpgsql(_postgres.ConnectionString));
            services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<SagaTestDbContext>());
            services.AddScoped<ISagaInstanceStore, EfSagaInstanceStore>();
            services.AddScoped<TenantStepDependency>();
            services.AddSingleton<ISystemClock>(SystemClock.Instance);
            services.AddSingleton(new SagaStepExecutorOptions { PollInterval = TimeSpan.FromMilliseconds(20) });
            services.AddSingleton(new SagaRegistry([new SagaRegistration("tenant-probe",
                (_, _, _, _, _) => throw new NotSupportedException(),
                async (_, direction, _, sp, _) =>
                {
                    var tenant = sp.GetRequiredService<ITenantContext>();
                    var dependency = sp.GetRequiredService<TenantStepDependency>();
                    await Task.Yield();
                    observed.Enqueue((tenant.TenantId, direction, dependency.Id));
                    Assert.Equal(tenant.TenantId, dependency.ConstructorTenant);
                    if (tenant.TenantId == "tenant-b" && direction == StepDirection.Execute)
                        throw new InvalidOperationException("触发补偿以验证租户回灌");
                }, stepCount: 1)]));
            services.AddHostedService<SagaStepExecutor>();
        }).Build();
        var ids = new List<Guid>();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SagaTestDbContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            foreach (var tenant in new[] { "tenant-a", "tenant-b" })
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                db.Add(new SagaInstance(id, "tenant-probe", tenant, "{}", DateTimeOffset.UtcNow, null, null));
                db.Add(new SagaStepQueueEntry(Guid.NewGuid(), id, 0, StepDirection.Execute, DateTimeOffset.UtcNow));
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<SagaTestDbContext>();
                var finished = await db.Set<SagaInstance>().CountAsync(x => ids.Contains(x.Id) &&
                    (x.State == SagaState.Completed || x.State == SagaState.Compensated), timeout.Token);
                if (finished == 2) break;
                await Task.Delay(50, timeout.Token);
            }
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
        Assert.Equal(3, observed.Count);
        Assert.Contains(observed, x => x.Tenant == "tenant-a" && x.Direction == StepDirection.Execute);
        Assert.Contains(observed, x => x.Tenant == "tenant-b" && x.Direction == StepDirection.Execute);
        Assert.Contains(observed, x => x.Tenant == "tenant-b" && x.Direction == StepDirection.Compensate);
        Assert.Equal(3, observed.Select(x => x.DependencyScope).Distinct().Count());
        Assert.Null(host.Services.GetRequiredService<ITenantContext>().TenantId);
    }

    private sealed class TenantStepDependency(ITenantContext tenant)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string? ConstructorTenant { get; } = tenant.TenantId;
    }
}
