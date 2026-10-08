using Kun.Core.Domain;
using Kun.Core.Security;
using Kun.EntityFrameworkCore.Conventions;
using Kun.EntityFrameworkCore.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kun.EntityFrameworkCore.Tests;

public sealed class AuditFieldConventionTests
{
    [Fact]
    public async Task Stamp_StringAndNumericSubjects_AreConvertedWithoutDefaultValues()
    {
        await Verify<StringRow, string>("oidc-sub", "oidc-sub");
        await Verify<LongRow, long?>("42", 42L);
        await Verify<GuidRow, Guid?>("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"));
        await Verify<GuidRow, Guid?>(null, null);
    }

    private static async Task Verify<TRow, TActor>(string? subject, TActor expected)
        where TRow : AuditRow<TActor>, new()
    {
        await using var db = CreateContext(new User(subject));
        var row = new TRow { Id = Guid.NewGuid() };
        db.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, row.CreatedBy);
        Assert.NotEqual(default, row.CreatedAt);
    }

    [Fact]
    public async Task Stamp_InvalidNumericSubject_RejectsInsteadOfUsingZero()
    {
        await using var db = CreateContext(new User("not-a-number"));
        db.Add(new LongRow { Id = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Model_NonNullableValueTypeActor_RequiresExplicitMigration()
    {
        using var db = new LegacyContext(new DbContextOptionsBuilder<LegacyContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Assert.Throws<InvalidOperationException>(() => db.Model);
    }

    private static FieldsContext CreateContext(ICurrentUser user) => new(new DbContextOptionsBuilder<FieldsContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .AddInterceptors(new AuditFieldsSaveChangesInterceptor(user, TimeProvider.System)).Options);

    private sealed class User(string? id) : ICurrentUser
    {
        public string? UserId => id;
        public bool IsAuthenticated => id is not null;
        public IReadOnlyList<string> Roles => [];
    }

    private sealed class FieldsContext(DbContextOptions<FieldsContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GuidRow>();
            modelBuilder.Entity<StringRow>();
            modelBuilder.Entity<LongRow>();
        }
    }

    private sealed class LegacyContext(DbContextOptions<LegacyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LegacyRow>();
            modelBuilder.ApplyKunDataConventions();
        }
    }

    public abstract class AuditRow<TActor> : IAuditedObject<TActor>
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public TActor? CreatedBy { get; set; }
        public TActor? UpdatedBy { get; set; }
    }
    public sealed class GuidRow : AuditRow<Guid?>;
    public sealed class LongRow : AuditRow<long?>;
    public sealed class StringRow : AuditRow<string>;
    public sealed class LegacyRow : AuditRow<Guid>;
}
