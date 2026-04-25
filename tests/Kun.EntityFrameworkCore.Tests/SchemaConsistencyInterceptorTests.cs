using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kun.EntityFrameworkCore.Tests;

public sealed class SchemaConsistencyInterceptorTests
{
    // Host intentionally unreachable: if the interceptor does NOT throw (matching/null cases),
    // EF Core proceeds to open a real Npgsql connection and fails with a socket / connection
    // error — distinct from our interceptor's InvalidOperationException. That distinction is
    // the signal the tests assert on.
    private const string UnreachableConnectionString =
        "Host=kun-interceptor-noop.invalid;Database=kun_test;Username=kun;Password=kun;Timeout=2;Command Timeout=2";

    private static DbContextOptionsBuilder ConfigureBase(DbContextOptionsBuilder builder) =>
        builder
            .UseNpgsql(UnreachableConnectionString)
            .ReplaceService<IModelCacheKeyFactory, UncachedModelCacheKeyFactory>();

    [Fact]
    public async Task Mismatched_Schemas_Throw_InvalidOperationException_With_Remediation_Message()
    {
        var options = ConfigureBase(new DbContextOptionsBuilder())
            .UseKunSchema("migrations_schema")
            .Options;

        await using var ctx = new TestDbContext(options, NullTenantContext.Instance, schema: "catalog");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ctx.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(false));

        Assert.Contains("DefaultSchema='catalog'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("migrations_schema", ex.Message, StringComparison.Ordinal);
        Assert.Contains("UseKunSchema", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_UseKunSchema_With_DefaultSchema_Set_Throws_Pointing_To_UseKunSchema()
    {
        var options = ConfigureBase(new DbContextOptionsBuilder()).Options;

        await using var ctx = new TestDbContext(options, NullTenantContext.Instance, schema: "catalog");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ctx.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(false));

        Assert.Contains("DefaultSchema='catalog'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("UseKunSchema", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Matching_Schemas_Do_Not_Throw_From_Interceptor()
    {
        var options = ConfigureBase(new DbContextOptionsBuilder())
            .UseKunSchema("catalog")
            .Options;

        await using var ctx = new TestDbContext(options, NullTenantContext.Instance, schema: "catalog");

        // Save fails at the actual connection (unreachable host); crucially it must NOT be
        // our InvalidOperationException from the SchemaConsistencyInterceptor.
        var ex = await Record.ExceptionAsync(
            async () => await ctx.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(false));

        if (ex is InvalidOperationException iox && iox.Message.Contains("UseKunSchema", StringComparison.Ordinal))
        {
            Assert.Fail($"Interceptor threw despite matching schemas: {ex.Message}");
        }
    }

    [Fact]
    public async Task Null_DefaultSchema_Skips_Check_Even_Without_UseKunSchema()
    {
        var options = ConfigureBase(new DbContextOptionsBuilder()).Options;

        await using var ctx = new TestDbContext(options, NullTenantContext.Instance, schema: null);

        var ex = await Record.ExceptionAsync(
            async () => await ctx.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(false));

        if (ex is InvalidOperationException iox && iox.Message.Contains("UseKunSchema", StringComparison.Ordinal))
        {
            Assert.Fail($"Interceptor threw on null DefaultSchema: {ex.Message}");
        }
    }
}
