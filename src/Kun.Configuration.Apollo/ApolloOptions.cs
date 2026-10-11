namespace Kun.Configuration.Apollo;

/// <summary>平台／服务级 Apollo 引导配置；不承载租户级配置或明文业务密钥。</summary>
public sealed class ApolloOptions
{
    /// <summary>已允许访问的 ConfigService 端点，按顺序故障转移。</summary>
    public required IReadOnlyList<Uri> ConfigServers { get; init; }
    /// <summary>应用标识。</summary>
    public required string AppId { get; init; }
    /// <summary>集群标识。</summary>
    public string Cluster { get; init; } = "default";
    /// <summary>按公共、插件、application 顺序叠加的 properties namespace。</summary>
    public IReadOnlyList<string> Namespaces { get; init; } = ["application"];
    /// <summary>可选实例灰度标签。</summary>
    public string? Label { get; init; }
    /// <summary>缓存目录；null 禁用磁盘缓存。</summary>
    public string? CacheDirectory { get; init; }
    /// <summary>在每次签名前由宿主解析 AccessKey；不写入配置快照、缓存或日志。</summary>
    public Func<string?>? ResolveAccessKey { get; init; }
    /// <summary>启动后台长轮询；测试或显式刷新宿主可关闭。</summary>
    public bool ReloadOnChange { get; init; } = true;
    /// <summary>连接失败后的重试间隔。</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>可选的整份快照校验，失败时不替换数据与磁盘缓存。</summary>
    public Func<IReadOnlyDictionary<string, string?>, bool>? Validate { get; init; }
    /// <summary>每份原始 namespace 在合并与缓存前校验；离线缓存读取时同样执行。</summary>
    public Func<string, IReadOnlyDictionary<string, string?>, bool>? ValidateNamespace { get; init; }
}
