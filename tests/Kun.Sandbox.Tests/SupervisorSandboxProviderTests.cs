#pragma warning disable CS0618 // Explicit legacy wire compatibility; default and provider-owned shapes have separate tests.
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Kun.Sandbox.Abstractions;
using Kun.Sandbox.Implementations;
using Kun.Sandbox.Tests.Infrastructure;
using Xunit;

namespace Kun.Sandbox.Tests;

public sealed class SupervisorSandboxProviderTests
{
    private static readonly Uri BaseUri = new("https://sandbox-supervisor.internal:8443/");

    private const string CreateResponseJson = """
        {"sandboxId":"sb_7f3a","templateId":"neuroagent-default","state":"running","startedAt":"2026-04-23T12:00:05Z","endAt":"2026-04-23T12:05:05Z"}
        """;

    private const string ProcessResponseJson = """
        {"processId":"proc-uuid","exitCode":0,"stdout":"hello\n","stderr":"","durationMs":1823,"truncated":false}
        """;

    [Fact]
    public async Task CreateAsync_RejectsExtensionsThatOverrideCoreFieldsBeforeSending()
    {
        var stub = new StubHttpMessageHandler();
        using var client = new HttpClient(stub) { BaseAddress = BaseUri };
        var provider = new SupervisorSandboxProvider(client);
        using var value = JsonDocument.Parse("999999");
        var create = () => provider.CreateAsync(new SandboxCreateOptions { ProviderExtensions = new Dictionary<string, JsonElement> { ["CPUCOUNT"] = value.RootElement.Clone() } }, TestContext.Current.CancellationToken);
        await create.Should().ThrowAsync<ArgumentException>();
        stub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_sends_camelCase_body_with_neuroagent_extension()
    {
        var ct = TestContext.Current.CancellationToken;
        var stub = new StubHttpMessageHandler()
            .MapJson(HttpMethod.Post, "/v2/sandboxes", CreateResponseJson, HttpStatusCode.Created);

        using var client = new HttpClient(stub) { BaseAddress = BaseUri };
        var provider = new SupervisorSandboxProvider(client);

        var options = new SandboxCreateOptions
        {
            TemplateId = "neuroagent-default",
            CpuCount = 2,
            MemoryMB = 2048,
            TimeoutMs = 300_000,
            Envs = new Dictionary<string, string> { ["PYTHONUNBUFFERED"] = "1" },
            Metadata = new Dictionary<string, string> { ["tenantId"] = "t-123" },
            NeuroagentExtension = new NeuroagentSandboxExtension
            {
                NetworkPolicy = "tenant-default",
                SecretScope = new SecretScopeConfig { Scopes = ["github_read"] },
                Browser = new BrowserSessionConfig { Enabled = true },
                MaxCommands = 200,
            },
        };

        var handle = await provider.CreateAsync(options, ct);

        handle.SandboxId.Should().Be("sb_7f3a");
        handle.State.Should().Be(SandboxState.Running);

        var recorded = stub.Requests.Single();
        using var doc = JsonDocument.Parse(recorded.Body);
        var root = doc.RootElement;
        root.GetProperty("templateId").GetString().Should().Be("neuroagent-default");
        root.GetProperty("cpuCount").GetInt32().Should().Be(2);
        root.GetProperty("memoryMB").GetInt32().Should().Be(2048);
        root.GetProperty("timeoutMs").GetInt32().Should().Be(300_000);
        root.GetProperty("envs").GetProperty("PYTHONUNBUFFERED").GetString().Should().Be("1");
        root.GetProperty("metadata").GetProperty("tenantId").GetString().Should().Be("t-123");
        var neuro = root.GetProperty("neuroagent");
        neuro.GetProperty("networkPolicy").GetString().Should().Be("tenant-default");
        neuro.GetProperty("secretScope").GetProperty("scopes").EnumerateArray()
            .Select(e => e.GetString()).Should().BeEquivalentTo(["github_read"]);
        neuro.GetProperty("browser").GetProperty("enabled").GetBoolean().Should().BeTrue();
        neuro.GetProperty("maxCommands").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task CreateAsync_throws_HttpRequestException_on_non_success()
    {
        var ct = TestContext.Current.CancellationToken;
        var stub = new StubHttpMessageHandler()
            .MapJson(HttpMethod.Post, "/v2/sandboxes", """{"code":"conflict"}""", HttpStatusCode.Conflict);
        using var client = new HttpClient(stub) { BaseAddress = BaseUri };
        var provider = new SupervisorSandboxProvider(client);

        var act = () => provider.CreateAsync(new SandboxCreateOptions(), ct);

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Processes_RunAsync_targets_correct_url_and_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var stub = new StubHttpMessageHandler()
            .MapJson(HttpMethod.Post, "/v2/sandboxes", CreateResponseJson, HttpStatusCode.Created)
            .MapJson(HttpMethod.Post, "/v2/sandboxes/sb_7f3a/processes", ProcessResponseJson);
        using var client = new HttpClient(stub) { BaseAddress = BaseUri };
        var provider = new SupervisorSandboxProvider(client);
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);

        var result = await handle.Processes.RunAsync(
            ["python", "analyze.py"],
            new ProcessRunOptions { Cwd = "/mnt/user-data/workspace", TimeoutMs = 60_000 },
            ct);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Be("hello\n");

        var runReq = stub.Requests.Last();
        runReq.Uri.AbsolutePath.Should().Be("/v2/sandboxes/sb_7f3a/processes");
        using var doc = JsonDocument.Parse(runReq.Body);
        doc.RootElement.GetProperty("cmd").EnumerateArray()
            .Select(e => e.GetString()).Should().BeEquivalentTo(["python", "analyze.py"]);
        doc.RootElement.GetProperty("cwd").GetString().Should().Be("/mnt/user-data/workspace");
        doc.RootElement.GetProperty("timeoutMs").GetInt32().Should().Be(60_000);
    }

    [Fact]
    public async Task Files_Write_targets_query_path_and_sends_octet_stream()
    {
        var ct = TestContext.Current.CancellationToken;
        var stub = new StubHttpMessageHandler()
            .MapJson(HttpMethod.Post, "/v2/sandboxes", CreateResponseJson, HttpStatusCode.Created)
            .MapStatus(HttpMethod.Post, "/v2/sandboxes/sb_7f3a/files", HttpStatusCode.OK);
        using var client = new HttpClient(stub) { BaseAddress = BaseUri };
        var provider = new SupervisorSandboxProvider(client);
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);

        await handle.Files.WriteAsync("/mnt/user-data/workspace/a.txt", Encoding.UTF8.GetBytes("hi"), ct);

        var write = stub.Requests.Last();
        write.Method.Should().Be(HttpMethod.Post);
        write.Uri.AbsolutePath.Should().Be("/v2/sandboxes/sb_7f3a/files");
        write.Uri.Query.Should().Contain("path=");
    }

    [Fact]
    public async Task Files_List_parses_entries_from_response()
    {
        var ct = TestContext.Current.CancellationToken;
        const string listJson = """
            {"entries":[{"path":"/a","type":"file","sizeBytes":10,"modifiedAt":"2026-04-23T12:00:00Z"},
                        {"path":"/sub","type":"directory","sizeBytes":0}], "truncated":false}
            """;
        var stub = new StubHttpMessageHandler()
            .MapJson(HttpMethod.Post, "/v2/sandboxes", CreateResponseJson, HttpStatusCode.Created)
            .Map(HttpMethod.Get, "/v2/sandboxes/sb_7f3a/files/list",
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listJson, Encoding.UTF8, "application/json") });
        using var client = new HttpClient(stub) { BaseAddress = BaseUri };
        var provider = new SupervisorSandboxProvider(client);
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);

        var entries = await handle.Files.ListAsync("/", recursive: false, ct);

        entries.Should().HaveCount(2);
        entries[0].Path.Should().Be("/a");
        entries[0].SizeBytes.Should().Be(10);
    }

    [Fact]
    public async Task KillAsync_swallows_404_already_gone()
    {
        var ct = TestContext.Current.CancellationToken;
        var stub = new StubHttpMessageHandler()
            .MapJson(HttpMethod.Post, "/v2/sandboxes", CreateResponseJson, HttpStatusCode.Created)
            .MapStatus(HttpMethod.Delete, "/v2/sandboxes/sb_7f3a", HttpStatusCode.NotFound);
        using var client = new HttpClient(stub) { BaseAddress = BaseUri };
        var provider = new SupervisorSandboxProvider(client);
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);

        await handle.KillAsync(ct);

        handle.State.Should().Be(SandboxState.Killed);
    }

    [Fact]
    public void Constructor_rejects_HttpClient_without_BaseAddress()
    {
        using var client = new HttpClient(new StubHttpMessageHandler());

        var act = () => new SupervisorSandboxProvider(client);

        act.Should().Throw<ArgumentException>().WithMessage("*BaseAddress*");
    }
}
