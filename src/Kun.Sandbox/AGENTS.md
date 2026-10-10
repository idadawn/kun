<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-23 -->

# Kun.Sandbox

## Purpose

A .NET sandbox abstraction for Kun consumers (C# business services in
any Kun-based application). Models the E2B v2
REST API shape in idiomatic C#:

```csharp
var sandbox = await provider.CreateAsync(new SandboxCreateOptions
{
    TemplateId = "kun-default",
    CpuCount = 2,
    MemoryMB = 2048,
    TimeoutMs = 300_000,
    Envs = new Dictionary<string, string> { ["DEBUG"] = "1" },
    Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId },
});

var result = await sandbox.Processes.RunAsync(
    ["python", "preprocess.py", "--input", "data.csv"],
    new ProcessRunOptions { TimeoutMs = 60_000 },
    ct);

await sandbox.Files.WriteAsync("/mnt/user-data/workspace/out.json", bytes, ct);
await sandbox.KillAsync(ct);
```

Backend is chosen at DI time:

```csharp
services.AddKunSandbox(opts => opts.UseSupervisor(
    baseUrl: new Uri("https://sandbox-supervisor.internal:8443"),
    clientCertificate: cert));

// or in unit tests:
services.AddKunSandbox(opts => opts.UseInMemory());

// or future Mode D:
services.AddKunSandbox(opts => opts.UseE2BCloud(apiKey, teamId));
```

## Scope and non-scope

- **In scope**: abstractions (`ISandboxProvider`, `ISandboxHandle`,
  `IProcesses`, `IFiles`), records (`SandboxCreateOptions`,
  `ProcessRunOptions`, `ProcessResult`, `FileEntry`), three
  implementations, DI.
- **Out of scope**: *running a sandbox*. No Docker, no gVisor, no
  process supervision. The runtime lives in
  the configured external supervisor. Spec v1.1 §14 pins the
  Supervisor to Python — this module is only the .NET client.

## Layer and AOT

- Layer: **Abstraction**. Only depends on
  `Microsoft.Extensions.DependencyInjection.Abstractions`,
  `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options`.
- `<IsAotCandidate>true</IsAotCandidate>`. All JSON serialisation
  goes through a source-generated `SandboxJsonContext` so the module
  stays reflection-free. `HttpClient` is resolved through
  `IHttpClientFactory` so the AOT-strict caller can configure its own
  delegating handlers (S2S JWT, mTLS, retries) without
  `Kun.Sandbox` taking a hard dependency on `Kun.Security`.

## Backend choice

| Provider | When to use | Requires |
|----------|-------------|----------|
| `InMemorySandboxProvider` | Unit tests, dev-loop prototyping. No isolation, no network. | — |
| `SupervisorSandboxProvider` | Phase 1 production path. Calls `/v2/sandboxes/*` on a configured supervisor. | Supervisor URL, mTLS client cert, S2S JWT issuer (via delegating handler). |
| `E2BCloudSandboxProvider` | **Phase 2+ only.** Currently throws `NotImplementedException` — waiting on Mode D scoping in Spec v1.1 §14.4. | E2B API key + team ID. |

## Shape contract — E2B v2

Record fields are `PascalCase` in C# but serialise to `camelCase` to
match the E2B wire shape verbatim. Fields cover:

| C# property | JSON name | Source |
|-------------|-----------|--------|
| `TemplateId` | `templateId` | E2B v2 |
| `CpuCount` | `cpuCount` | E2B v2 |
| `MemoryMB` | `memoryMB` | E2B v2 |
| `DiskMB` | `diskMB` | E2B v2 |
| `TimeoutMs` | `timeoutMs` | E2B v2 |
| `Envs` | `envs` | E2B v2 |
| `Metadata` | `metadata` | E2B v2 |
| `NeuroagentExtension` | `neuroagent` | Spec v1.1 §4.3 NeuroAgent sub-object |

The `neuroagent` sub-object carries NeuroAgent-only concerns
(`secretScope`, `networkPolicy`, `browser`, `maxCommands`). E2B SDKs
will ignore it; our Supervisor reads it.

## For AI Agents

- **Do not** port Supervisor runtime code into this module. The
  runtime is Python-owned (Spec v1.1 §14). If a scenario seems to
  require in-process code execution, you probably want a different
  abstraction, not `Kun.Sandbox`.
- **Do not** add runtime reflection (`Activator.CreateInstance`,
  `Type.GetMethod`). JSON goes through `SandboxJsonContext`; types
  are registered explicitly.
- **Do not** shortcut past `IHttpClientFactory`. The Supervisor
  provider must get its `HttpClient` from the factory so consumers can
  attach S2S JWT / mTLS handlers. Taking a `HttpClient` constructor
  arg directly is acceptable for unit tests but not for the DI path.
- **Do not** drift record field names away from the E2B v2 shape. If
  E2B renames a field in a future API version, update here **and** in
  the Supervisor OpenAPI spec in lockstep, never in one side only.
- **Do** mark `E2BCloudSandboxProvider` methods
  `throw new NotImplementedException("...Phase 2+...")` until Mode D
  lands. The skeleton exists so DI wiring can be exercised in tests;
  it is not a functional backend.
- **Do** use `InMemorySandboxProvider` for business-service unit
  tests. Integration tests that want a real Supervisor belong under
  `Kun.IntegrationTests`, not here.

S1-KUN-004: ProviderExtensions carries explicitly named provider-owned JSON members; core fields cannot be overridden. The old named extension/property remains obsolete compatibility only and is omitted by default. Do not silently discard a caller's explicitly configured legacy policy. Use a set accessor for JsonExtensionData: source-generated constructor binding does not support an init-only extension property.
