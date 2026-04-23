using Kun.Core.MultiTenancy;

namespace Kun.EntityFrameworkCore.MultiTenancy;

/// <summary>
/// Configuration for <c>AddKunMultiTenancy</c>. Selects the isolation strategy and tunes
/// the few knobs the framework exposes across all three strategies.
/// </summary>
public sealed class KunMultiTenancyOptions
{
    /// <summary>The active isolation strategy. Defaults to <see cref="TenantIsolationStrategy.Shared"/>.</summary>
    public TenantIsolationStrategy Strategy { get; set; } = TenantIsolationStrategy.Shared;

    /// <summary>
    /// When <see langword="true"/>, <c>SchemaPerTenantProvisioner.DeprovisionAsync</c> is
    /// permitted to run <c>DROP SCHEMA IF EXISTS "{tenantId}" CASCADE</c>. Default is
    /// <see langword="false"/> — deprovisioning is a data-loss operation and must be an
    /// explicit opt-in. When left at the default, <c>DeprovisionAsync</c> throws
    /// <see cref="NotSupportedException"/> with a remediation message.
    /// </summary>
    public bool AllowDestructiveDeprovision { get; set; }
}
