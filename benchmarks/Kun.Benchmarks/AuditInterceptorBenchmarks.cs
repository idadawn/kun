using BenchmarkDotNet.Attributes;
using Kun.Audit;
using Kun.Audit.DependencyInjection;
using Kun.Audit.EntityFrameworkCore;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.Core.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kun.Benchmarks;

/// <summary>
/// Measures the overhead the <see cref="AuditSaveChangesInterceptor"/>
/// adds to <c>DbContext.SaveChangesAsync</c>. The interceptor walks
/// <c>ChangeTracker</c> entries, serialises original/current values via
/// <see cref="System.Text.Json.Utf8JsonWriter"/>, and dispatches to an
/// <c>IAuditWriter</c>. The baseline here uses
/// <see cref="NullAuditWriter"/> so we isolate the interceptor path
/// without layering in a real writer's cost.
/// </summary>
[MemoryDiagnoser]
public class AuditInterceptorBenchmarks : IDisposable
{
    private BenchDbContext _withInterceptor = null!;
    private BenchDbContext _noInterceptor = null!;

    [GlobalSetup]
    public void Setup()
    {
        _noInterceptor = new BenchDbContext(
            new DbContextOptionsBuilder<BenchDbContext>()
                .UseInMemoryDatabase($"kun-bench-audit-none-{Guid.NewGuid():N}")
                .Options);

        var interceptor = new AuditSaveChangesInterceptor(
            writer: new NullAuditWriter(NullLogger<NullAuditWriter>.Instance),
            tenantContext: NullTenantContext.Instance,
            currentUser: AnonymousCurrentUser.Instance,
            clock: SystemClock.Instance,
            options: Options.Create(new KunAuditOptions()),
            logger: NullLogger<AuditSaveChangesInterceptor>.Instance);

        _withInterceptor = new BenchDbContext(
            new DbContextOptionsBuilder<BenchDbContext>()
                .UseInMemoryDatabase($"kun-bench-audit-{Guid.NewGuid():N}")
                .AddInterceptors(interceptor)
                .Options);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Disposes both DbContexts.</summary>
    public void Dispose()
    {
        _noInterceptor?.Dispose();
        _withInterceptor?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true, Description = "SaveChanges · no interceptor")]
    public async Task<int> SaveChanges_NoInterceptor()
    {
        var entity = BenchEntity.Create("bench", "t-0");
        _noInterceptor.Entities.Add(entity);
        return await _noInterceptor.SaveChangesAsync().ConfigureAwait(false);
    }

    [Benchmark(Description = "SaveChanges · with AuditSaveChangesInterceptor")]
    public async Task<int> SaveChanges_WithInterceptor()
    {
        var entity = BenchEntity.Create("bench", "t-0");
        _withInterceptor.Entities.Add(entity);
        return await _withInterceptor.SaveChangesAsync().ConfigureAwait(false);
    }
}

internal sealed class BenchDbContext : DbContext
{
    public BenchDbContext(DbContextOptions<BenchDbContext> options) : base(options) { }

    public DbSet<BenchEntity> Entities => Set<BenchEntity>();
}

/// <summary>
/// Benchmark-only aggregate root implementing both <c>IAggregateRoot</c>
/// (via <see cref="AggregateRoot{TKey}"/>) and <see cref="IMultiTenant"/>
/// so the audit interceptor picks it up. <see cref="Entity{TKey}.Id"/>
/// has <c>protected set</c>, so we seed the identifier via a private
/// constructor called from the factory method <see cref="Create(string, string?)"/>.
/// </summary>
internal sealed class BenchEntity : AggregateRoot<Guid>, IMultiTenant
{
    // EF Core's InMemory provider uses the parameterless constructor + reflection
    // to materialise rows, but the factory method is what benchmark code uses.
    private BenchEntity() { }

    private BenchEntity(Guid id, string name, string? tenantId)
    {
        Id = id;
        Name = name;
        TenantId = tenantId;
    }

    public string Name { get; set; } = string.Empty;

    public string? TenantId { get; set; }

    public static BenchEntity Create(string name, string? tenantId)
        => new(Guid.NewGuid(), name, tenantId);
}
