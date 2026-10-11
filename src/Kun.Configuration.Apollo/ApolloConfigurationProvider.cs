using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kun.Configuration.Apollo;

/// <summary>通过公开协议获取服务级配置；无网络且无缓存时启动失败，不以空配置继续。</summary>
public sealed partial class ApolloConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly ApolloOptions _options;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly Dictionary<string, ApolloResponse> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _notifications = new(StringComparer.Ordinal);
    private Task? _poll;
    private bool _disposed;

    /// <summary>客户端超时须大于长轮询的 60 秒；默认客户端禁止重定向以避免签名泄漏。</summary>
    public ApolloConfigurationProvider(ApolloOptions options, HttpClient? client = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Cluster);
        if (options.ConfigServers.Count == 0 || options.ConfigServers.Any(uri => !uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new ArgumentException("ConfigServers 必须是无内嵌凭据的 HTTP(S) 地址。", nameof(options));
        if (options.Namespaces.Count == 0 || options.Namespaces.Any(string.IsNullOrWhiteSpace) || options.Namespaces.Distinct(StringComparer.Ordinal).Count() != options.Namespaces.Count)
            throw new ArgumentException("namespace 不得为空或重复。", nameof(options));
        if (client is not null && client.Timeout != Timeout.InfiniteTimeSpan && client.Timeout <= TimeSpan.FromSeconds(60))
            throw new ArgumentException("Apollo 长轮询客户端超时必须大于 60 秒。", nameof(client));
        if (options.RetryDelay <= TimeSpan.Zero) throw new ArgumentException("重试间隔必须为正。", nameof(options));
        _options = options;
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(70) };
        _logger = logger ?? NullLogger.Instance;
        foreach (var name in options.Namespaces) _notifications[name] = -1;
    }

    /// <inheritdoc />
    public override void Load()
    {
        try { RefreshAsync(_stop.Token).GetAwaiter().GetResult(); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            if (!TryReadCache()) throw new InvalidOperationException("Apollo 不可达且无有效本地缓存，拒绝以空配置启动。", error);
            LogCacheFallback(_logger);
        }
        if (_options.ReloadOnChange) _poll = Task.Run(() => PollAsync(_stop.Token));
    }

    /// <summary>显式刷新全部 namespace；校验失败时原配置与缓存保持不变。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await RefreshCoreAsync(_notifications, cancellationToken).ConfigureAwait(false); }
        finally { _refresh.Release(); }
    }

    private async Task RefreshCoreAsync(Dictionary<string, long> notifications, CancellationToken ct)
    {
        var next = new Dictionary<string, ApolloResponse>(_snapshots, StringComparer.Ordinal);
        foreach (var name in _options.Namespaces)
        {
            var path = $"configs/{Escape(_options.AppId)}/{Escape(_options.Cluster)}/{Escape(name)}";
            var query = new List<string>();
            if (next.TryGetValue(name, out var old)) query.Add("releaseKey=" + Escape(old.ReleaseKey));
            if (_options.Label is not null) query.Add("label=" + Escape(_options.Label));
            if (notifications[name] >= 0)
            {
                var messages = new ApolloMessages(new Dictionary<string, long> { [$"{_options.AppId}+{_options.Cluster}+{name}"] = notifications[name] });
                query.Add("messages=" + Escape(JsonSerializer.Serialize(messages, ApolloJsonContext.Default.ApolloMessages)));
            }
            if (query.Count > 0) path += "?" + string.Join('&', query);
            using var response = await SendAsync(path, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (old is null) throw new InvalidDataException("首次拉取不能只返回 304。");
                continue;
            }
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize(text, ApolloJsonContext.Default.ApolloResponse)
                ?? throw new InvalidDataException("Apollo 返回空快照。");
            ValidateResponse(name, snapshot);
            next[name] = snapshot;
        }
        var data = Merge(next);
        if (_options.Validate?.Invoke(data) == false) throw new InvalidDataException("Apollo 快照校验未通过。");
        WriteCache(next);
        var changed = Data.Count != data.Count || Data.Any(pair => !data.TryGetValue(pair.Key, out var value) || value != pair.Value);
        _snapshots.Clear();
        foreach (var item in next) _snapshots.Add(item.Key, item.Value);
        Data = data;
        if (changed) OnReload();
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var notifications = _options.Namespaces.Select(name => new ApolloNotification(name, _notifications[name])).ToArray();
                var payload = JsonSerializer.Serialize(notifications, ApolloJsonContext.Default.ApolloNotificationArray);
                using var response = await SendAsync($"notifications/v2?appId={Escape(_options.AppId)}&cluster={Escape(_options.Cluster)}&notifications={Escape(payload)}", ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotModified) continue;
                var updates = JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), ApolloJsonContext.Default.ApolloNotificationArray)
                    ?? throw new InvalidDataException("通知响应为空。");
                var candidate = new Dictionary<string, long>(_notifications, StringComparer.Ordinal);
                foreach (var update in updates)
                {
                    if (update is null || string.IsNullOrWhiteSpace(update.NamespaceName) || update.NotificationId < 0)
                        throw new InvalidDataException("通知响应的 namespace 或修订无效。");
                    if (candidate.ContainsKey(update.NamespaceName)) candidate[update.NamespaceName] = update.NotificationId;
                }
                await _refresh.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await RefreshCoreAsync(candidate, ct).ConfigureAwait(false);
                    foreach (var update in candidate) _notifications[update.Key] = update.Value;
                }
                finally { _refresh.Release(); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException or UnauthorizedAccessException)
            {
                LogPollFailed(_logger);
                try { await Task.Delay(_options.RetryDelay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var server in _options.ConfigServers)
        {
            var uri = new Uri(new Uri(server.AbsoluteUri.TrimEnd('/') + '/'), path);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var secret = _options.ResolveAccessKey?.Invoke();
            if (!string.IsNullOrEmpty(secret))
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
#pragma warning disable CA5350 // Apollo 公共 AccessKey 协议指定 HMAC-SHA1；不用于新密码学方案。
                var signature = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp + "\n" + uri.PathAndQuery)));
#pragma warning restore CA5350
                request.Headers.Add("Timestamp", timestamp);
                request.Headers.Add("Authorization", $"Apollo {_options.AppId}:{signature}");
            }
            try
            {
                var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotModified || response.IsSuccessStatusCode) return response;
                var status = response.StatusCode;
                response.Dispose();
                if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new UnauthorizedAccessException("Apollo 拒绝访问；不回退缓存掩盖鉴权问题。");
                last = new HttpRequestException("Apollo 配置服务返回非成功状态。", null, status);
            }
            catch (HttpRequestException error) { last = error; }
            catch (TaskCanceledException error) when (!ct.IsCancellationRequested) { last = error; }
        }
        throw new HttpRequestException("所有 Apollo 配置服务均不可用。", last);
    }

    private Dictionary<string, string?> Merge(Dictionary<string, ApolloResponse> snapshots)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _options.Namespaces)
            foreach (var pair in snapshots[name].Configurations) data[pair.Key] = pair.Value;
        return data;
    }

    private void ValidateResponse(string name, ApolloResponse value)
    {
        if (value.AppId != _options.AppId || value.Cluster != _options.Cluster || value.NamespaceName != name ||
            string.IsNullOrEmpty(value.ReleaseKey) || value.Configurations is null)
            throw new InvalidDataException("Apollo 快照的应用、集群、namespace 或修订无效。");
        if (_options.ValidateNamespace?.Invoke(name, value.Configurations) == false)
            throw new InvalidDataException("Apollo namespace 快照校验未通过。");
    }

    private string? CachePath => _options.CacheDirectory is null ? null : Path.Combine(_options.CacheDirectory,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', _options.ConfigServers) + "|" + _options.AppId + "|" + _options.Cluster + "|" + string.Join('|', _options.Namespaces) + "|" + _options.Label))) + ".json");

    private void WriteCache(Dictionary<string, ApolloResponse> snapshots)
    {
        if (CachePath is not { } path) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshots.Values.ToArray(), ApolloJsonContext.Default.ApolloResponseArray));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private bool TryReadCache()
    {
        if (CachePath is not { } path || !File.Exists(path)) return false;
        try
        {
            var values = JsonSerializer.Deserialize(File.ReadAllText(path), ApolloJsonContext.Default.ApolloResponseArray);
            if (values is null) return false;
            var snapshots = values.ToDictionary(value => value.NamespaceName, StringComparer.Ordinal);
            if (_options.Namespaces.Any(name => !snapshots.ContainsKey(name))) return false;
            foreach (var name in _options.Namespaces) ValidateResponse(name, snapshots[name]);
            var data = Merge(snapshots);
            if (_options.Validate?.Invoke(data) == false) return false;
            foreach (var item in snapshots) _snapshots[item.Key] = item.Value;
            Data = data;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return false; }
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        if (_poll is not null) _poll.GetAwaiter().GetResult();
        if (_ownsClient) _client.Dispose();
        _refresh.Dispose();
        _stop.Dispose();
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Apollo 暂不可达，使用已缓存的服务级配置。")]
    private static partial void LogCacheFallback(ILogger logger);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Apollo 长轮询或快照校验失败，保留当前配置并重试。")]
    private static partial void LogPollFailed(ILogger logger);
}

internal sealed record ApolloResponse(string AppId, string Cluster, string NamespaceName, Dictionary<string, string?> Configurations, string ReleaseKey);
internal sealed record ApolloNotification(string NamespaceName, long NotificationId);
internal sealed record ApolloMessages(Dictionary<string, long> Details);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ApolloResponse))]
[JsonSerializable(typeof(ApolloResponse[]))]
[JsonSerializable(typeof(ApolloNotification[]))]
[JsonSerializable(typeof(ApolloMessages))]
internal sealed partial class ApolloJsonContext : JsonSerializerContext;
