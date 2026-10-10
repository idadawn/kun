using Kun.Sandbox.Abstractions;

namespace Kun.Sandbox.Implementations;

/// <summary>
/// Placeholder <see cref="ISandboxProvider"/> targeting E2B Cloud
/// (<c>https://api.e2b.dev</c>). The constructor signature is fixed so
/// DI wiring can be exercised in tests and reviewed in PRs, but every
/// method throws <see cref="NotImplementedException"/>.
/// </summary>
/// <remarks>
/// This provider corresponds to the future cloud backend mode — "delegate sandbox execution to E2B Cloud, Supervisor acts as
/// an auth + extension-layer proxy". Mode D is explicitly **Phase 2+**
/// and is not scoped for the current milestone. Do not fill in the
/// implementation here as a drive-by; it requires workspace-sync design,
/// secret-injection policy, and a separate ADR update.
/// </remarks>
public sealed class E2BCloudSandboxProvider : ISandboxProvider
{
    private const string NotImplementedMessage =
        "E2BCloudSandboxProvider is a Phase 2+ placeholder. Mode D (E2B Cloud backend) " +
        "is not yet scoped; see the sandbox provider ADR for scope and risks.";

    /// <summary>Creates the provider with the credentials needed to eventually call the E2B Cloud API.</summary>
    /// <param name="apiKey">E2B API key for the tenant.</param>
    /// <param name="teamId">E2B team id the sandboxes will bill against.</param>
    public E2BCloudSandboxProvider(string apiKey, string teamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        ApiKey = apiKey;
        TeamId = teamId;
    }

    /// <summary>The E2B API key held for the future Mode D implementation.</summary>
    public string ApiKey { get; }

    /// <summary>The E2B team id held for the future Mode D implementation.</summary>
    public string TeamId { get; }

    /// <inheritdoc />
    public Task<ISandboxHandle> CreateAsync(SandboxCreateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        throw new NotImplementedException(NotImplementedMessage);
    }
}
