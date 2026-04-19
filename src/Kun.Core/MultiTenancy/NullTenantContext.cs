namespace Kun.Core.MultiTenancy;

/// <summary>
/// <see cref="ITenantContext"/> implementation for non-multi-tenant hosts or background jobs
/// that operate without a tenant scope.
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    /// <summary>Singleton instance — <see cref="NullTenantContext"/> has no state.</summary>
    public static NullTenantContext Instance { get; } = new();

    private NullTenantContext() { }

    /// <inheritdoc />
    public string? TenantId => null;

    /// <inheritdoc />
    public bool IsMultiTenant => false;
}
