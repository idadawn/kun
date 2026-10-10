using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kun.Sandbox.Abstractions;
using Kun.Sandbox.Internal;

namespace Kun.Sandbox.Implementations;

/// <summary>
/// Production <see cref="ISandboxProvider"/> that talks to the
/// configured supervisor's <c>/v2/sandboxes/*</c> endpoints.
/// The HTTP client is supplied by
/// the caller — typically via <see cref="System.Net.Http.IHttpClientFactory"/>
/// — so delegating handlers can attach S2S JWT, mTLS client cert, and
/// OpenTelemetry instrumentation without this class knowing about them.
/// </summary>
public sealed class SupervisorSandboxProvider : ISandboxProvider
{
    /// <summary>Logical name used when resolving the <see cref="HttpClient"/> from <see cref="System.Net.Http.IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "Kun.Sandbox.Supervisor";

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        TypeInfoResolver = SandboxJsonContext.Default,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> CoreFields = new(StringComparer.OrdinalIgnoreCase)
    { "templateId", "cpuCount", "memoryMB", "diskMB", "timeoutMs", "envs", "metadata" };
    private readonly HttpClient _httpClient;

    /// <summary>Creates the provider with a caller-configured <see cref="HttpClient"/>. The <see cref="HttpClient.BaseAddress"/> must point at the Supervisor (e.g. <c>https://sandbox-supervisor.internal:8443/</c>).</summary>
    public SupervisorSandboxProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException(
                "HttpClient.BaseAddress must be set to the Supervisor URL before constructing SupervisorSandboxProvider.",
                nameof(httpClient));
        }

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<ISandboxHandle> CreateAsync(SandboxCreateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ProviderExtensions is not null)
        {
#pragma warning disable CS0618 // An explicitly used legacy field must not collide with provider-owned extension data.
            if (options.ProviderExtensions.Keys.Any(key => CoreFields.Contains(key) || options.NeuroagentExtension is not null && key.Equals("neuroagent", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Provider extensions cannot override core or existing legacy fields.", nameof(options));
#pragma warning restore CS0618
        }

        using var response = await _httpClient.PostAsJsonAsync(
            "v2/sandboxes",
            options,
            SandboxJsonContext.Default.SandboxCreateOptions,
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, "create sandbox", cancellationToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadFromJsonAsync(SandboxJsonContext.Default.SandboxCreateResponse, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            throw new HttpRequestException("Supervisor returned an empty body for POST /v2/sandboxes.");
        }

        return new SupervisorSandboxHandle(_httpClient, body);
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string detail = string.Empty;
        try
        {
            detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Ignore — we are already building an error; best-effort body capture only.
        }

        throw new HttpRequestException(
            $"Supervisor {operation} failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {detail}",
            inner: null,
            statusCode: response.StatusCode);
    }

    private sealed class SupervisorSandboxHandle : ISandboxHandle
    {
        private readonly HttpClient _httpClient;
        private readonly SupervisorProcesses _processes;
        private readonly SupervisorFiles _files;
        private int _killed;

        public SupervisorSandboxHandle(HttpClient httpClient, SandboxCreateResponse body)
        {
            _httpClient = httpClient;
            SandboxId = body.SandboxId;
            TemplateId = body.TemplateId;
            StartedAt = body.StartedAt;
            EndAt = body.EndAt;
            State = ParseState(body.State);
            _processes = new SupervisorProcesses(httpClient, SandboxId);
            _files = new SupervisorFiles(httpClient, SandboxId);
        }

        public string SandboxId { get; }

        public string TemplateId { get; }

        public SandboxState State { get; private set; }

        public DateTimeOffset StartedAt { get; }

        public DateTimeOffset EndAt { get; }

        public IProcesses Processes => _processes;

        public IFiles Files => _files;

        public async Task KillAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _killed, 1) != 0)
            {
                return;
            }

            using var response = await _httpClient.DeleteAsync(
                $"v2/sandboxes/{Uri.EscapeDataString(SandboxId)}",
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                State = SandboxState.Killed;
                return;
            }

            await EnsureSuccessAsync(response, "kill sandbox", cancellationToken).ConfigureAwait(false);
            State = SandboxState.Killed;
        }

        private static SandboxState ParseState(string value) => value switch
        {
            "creating" => SandboxState.Creating,
            "running" => SandboxState.Running,
            "killed" => SandboxState.Killed,
            "failed" => SandboxState.Failed,
            "timedOut" or "timed_out" => SandboxState.TimedOut,
            _ => SandboxState.Unknown,
        };
    }

    private sealed class SupervisorProcesses : IProcesses
    {
        private readonly HttpClient _httpClient;
        private readonly string _sandboxId;

        public SupervisorProcesses(HttpClient httpClient, string sandboxId)
        {
            _httpClient = httpClient;
            _sandboxId = sandboxId;
        }

        public async Task<ProcessResult> RunAsync(
            string[] command,
            ProcessRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (command.Length == 0)
            {
                throw new ArgumentException("command must contain at least one argv element.", nameof(command));
            }

            var body = new ProcessRunRequestBody
            {
                Cmd = command,
                Cwd = options?.Cwd,
                Envs = options?.Envs,
                TimeoutMs = options?.TimeoutMs,
                Stdin = options?.Stdin,
                Background = options?.Background ?? false,
            };

            using var response = await _httpClient.PostAsJsonAsync(
                $"v2/sandboxes/{Uri.EscapeDataString(_sandboxId)}/processes",
                body,
                SandboxJsonContext.Default.ProcessRunRequestBody,
                cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, "run process", cancellationToken).ConfigureAwait(false);

            var result = await response.Content
                .ReadFromJsonAsync(SandboxJsonContext.Default.ProcessResult, cancellationToken)
                .ConfigureAwait(false);

            return result ?? throw new HttpRequestException("Supervisor returned an empty body for POST processes.");
        }

        public Task<ProcessResult> RunAsync(
            string command,
            ProcessRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(command);
            return RunAsync(["/bin/sh", "-c", command], options, cancellationToken);
        }
    }

    private sealed class SupervisorFiles : IFiles
    {
        private readonly HttpClient _httpClient;
        private readonly string _sandboxId;

        public SupervisorFiles(HttpClient httpClient, string sandboxId)
        {
            _httpClient = httpClient;
            _sandboxId = sandboxId;
        }

        public async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            using var response = await _httpClient.GetAsync(
                BuildFileUri(path),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, "read file", cancellationToken).ConfigureAwait(false);

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(content);

            using var body = new ByteArrayContent(content);
            body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            using var response = await _httpClient.PostAsync(
                BuildFileUri(path),
                body,
                cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, "write file", cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<FileEntry>> ListAsync(
            string path,
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            var uri =
                $"v2/sandboxes/{Uri.EscapeDataString(_sandboxId)}/files/list" +
                $"?path={Uri.EscapeDataString(path)}" +
                $"&recursive={(recursive ? "true" : "false")}";

            using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "list files", cancellationToken).ConfigureAwait(false);

            var body = await response.Content
                .ReadFromJsonAsync(SandboxJsonContext.Default.FileListResponse, cancellationToken)
                .ConfigureAwait(false);

            return body?.Entries ?? [];
        }

        public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            using var response = await _httpClient.DeleteAsync(BuildFileUri(path), cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            await EnsureSuccessAsync(response, "delete file", cancellationToken).ConfigureAwait(false);
        }

        private string BuildFileUri(string path) =>
            $"v2/sandboxes/{Uri.EscapeDataString(_sandboxId)}/files?path={Uri.EscapeDataString(path)}";
    }
}
