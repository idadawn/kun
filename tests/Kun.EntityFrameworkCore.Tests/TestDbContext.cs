using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kun.EntityFrameworkCore.Tests;

/// <summary>
/// Minimal <see cref="KunDbContext"/> subclass whose <c>DefaultSchema</c> is injected at
/// construction time so each test can exercise a different schema override without defining
/// a per-test DbContext class.
/// </summary>
internal sealed class TestDbContext : KunDbContext
{
    private readonly string? _schema;

    public TestDbContext(DbContextOptions options, ITenantContext tenantContext, string? schema)
        : base(options, tenantContext)
    {
        _schema = schema;
    }

    protected override string? DefaultSchema => _schema;
}

/// <summary>
/// Returns a unique cache key per call, defeating EF Core's model cache so each
/// <see cref="TestDbContext"/> instance re-runs <c>OnModelCreating</c> and picks up the
/// current <c>DefaultSchema</c> value. Production code does not need this — each bounded
/// context has its own DbContext subclass which gets its own cache bucket.
/// </summary>
internal sealed class UncachedModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => new object();
}
