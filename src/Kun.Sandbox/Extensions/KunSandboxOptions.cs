using System.Security.Cryptography.X509Certificates;
using Kun.Sandbox.Abstractions;
using Kun.Sandbox.Implementations;

namespace Kun.Sandbox.Extensions;

/// <summary>
/// Builder passed to <see cref="KunSandboxServiceCollectionExtensions.AddKunSandbox"/>.
/// Exactly one of <see cref="UseSupervisor"/>, <see cref="UseE2BCloud"/>,
/// or <see cref="UseInMemory"/> must be called; calling a second one
/// replaces the previous choice.
/// </summary>
public sealed class KunSandboxOptions
{
    internal SandboxBackendKind Backend { get; private set; } = SandboxBackendKind.Unset;

    // Supervisor fields
    internal Uri? SupervisorBaseUrl { get; private set; }
    internal X509Certificate2? SupervisorClientCertificate { get; private set; }
    internal Action<HttpClient>? SupervisorHttpClientConfigurator { get; private set; }

    // E2B Cloud fields
    internal string? E2BApiKey { get; private set; }
    internal string? E2BTeamId { get; private set; }

    // InMemory fields
    internal Func<IReadOnlyList<string>, ProcessRunOptions?, ProcessResult>? InMemoryRunScript { get; private set; }

    /// <summary>
    /// Wire the Supervisor backend: <see cref="SupervisorSandboxProvider"/>
    /// talking to <c>/v2/sandboxes/*</c> at <paramref name="baseUrl"/>.
    /// </summary>
    /// <param name="baseUrl">Supervisor base URL, e.g. <c>https://sandbox-supervisor.internal:8443/</c>.</param>
    /// <param name="clientCertificate">Optional mTLS client certificate — attached to the primary handler when supplied.</param>
    /// <param name="configureClient">Optional hook to configure the <see cref="HttpClient"/> (e.g. add S2S JWT delegating handler, set a <c>User-Agent</c>).</param>
    public KunSandboxOptions UseSupervisor(
        Uri baseUrl,
        X509Certificate2? clientCertificate = null,
        Action<HttpClient>? configureClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        if (!baseUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Supervisor base URL must be absolute.", nameof(baseUrl));
        }

        Backend = SandboxBackendKind.Supervisor;
        SupervisorBaseUrl = baseUrl;
        SupervisorClientCertificate = clientCertificate;
        SupervisorHttpClientConfigurator = configureClient;
        E2BApiKey = null;
        E2BTeamId = null;
        InMemoryRunScript = null;
        return this;
    }

    /// <summary>
    /// Wire the E2B Cloud backend (<see cref="E2BCloudSandboxProvider"/>,
    /// Phase 2+ placeholder). Registration succeeds but
    /// <see cref="ISandboxProvider.CreateAsync"/> throws
    /// <see cref="NotImplementedException"/> at call time until Mode D
    /// (Sandbox Spec v1.1 §14.4) is implemented.
    /// </summary>
    public KunSandboxOptions UseE2BCloud(string apiKey, string teamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        Backend = SandboxBackendKind.E2BCloud;
        E2BApiKey = apiKey;
        E2BTeamId = teamId;
        SupervisorBaseUrl = null;
        SupervisorClientCertificate = null;
        SupervisorHttpClientConfigurator = null;
        InMemoryRunScript = null;
        return this;
    }

    /// <summary>
    /// Wire the in-memory fake backend (<see cref="InMemorySandboxProvider"/>).
    /// Intended for unit tests and developer-loop prototyping — there is
    /// no isolation and no network egress.
    /// </summary>
    /// <param name="runScript">Optional scripted response for <c>processes.run</c>. See <see cref="InMemorySandboxProvider"/>.</param>
    public KunSandboxOptions UseInMemory(
        Func<IReadOnlyList<string>, ProcessRunOptions?, ProcessResult>? runScript = null)
    {
        Backend = SandboxBackendKind.InMemory;
        InMemoryRunScript = runScript;
        SupervisorBaseUrl = null;
        SupervisorClientCertificate = null;
        SupervisorHttpClientConfigurator = null;
        E2BApiKey = null;
        E2BTeamId = null;
        return this;
    }
}

internal enum SandboxBackendKind
{
    Unset,
    Supervisor,
    E2BCloud,
    InMemory,
}
