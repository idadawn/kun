# Kun.Configuration.Apollo

面向平台／服务级非敏感配置的标准 `IConfiguration` provider，不承载租户配置，也不解析业务 secretRef。

```csharp
builder.Configuration.AddApollo(new ApolloOptions
{
    AppId = "service-a",
    ConfigServers = [new Uri("http://apollo-configservice:8080")],
    Namespaces = ["qian.common.flags", "qian.plugin.example", "application"],
    CacheDirectory = "/var/cache/service-a/apollo",
    ResolveAccessKey = () => accessKeyFromSecretStore
});
```

properties namespace 按列表顺序覆盖；.NET 层次键写为 `Section:Property`。源顺序由宿主负责：appsettings → Apollo → Secret 文件 → 环境变量 → 命令行。后续版本的 JSON/YAML namespace 内容展开不属于此基础 provider。

默认客户端超时 70 秒，满足长轮询的 60 秒 hold；禁止自动重定向，避免将 AccessKey 签名发送到另一个端点。自定义 HttpClient 由调用方管理，必须保持对应超时和可信重定向策略。AccessKey 每次请求从回调解析，不进入缓存或日志。

启动时网络不可达可回退本地快照，无缓存则失败；401/403 直接失败。`Validate` 可对整份候选快照校验，拒绝时不替换当前值或磁盘缓存。另用 `Kun.Configuration.AddKunOptions<T>` 保持运行期强类型配置的最后有效值；若仅配置 Options 校验而未配置快照 Validate，缓存保存的是协议合法的原始配置，重启时仍会执行关键 Options 校验。

敏感数据检查应设置 `ValidateNamespace(namespaceName, values)`，它在每份原始 namespace 合并前及读取离线缓存时运行。只检查合并结果会漏掉被高优先级 namespace 覆盖的值，而这些值仍属于原始缓存内容。任一原始快照被拒绝时，整次刷新保持上一份内存与缓存；框架不定义具体的敏感数据规则，由宿主传入。

后台长轮询使用 `/notifications/v2`，携带 notificationId，随后 `/configs` 请求附带 messages 修订避免多副本缓存滞后。配置及通知 JSON 均采用源生成元数据；缓存文件按端点、应用、集群、namespace 顺序和标签区分，并以临时文件替换。

公开协议依据：[Apollo 多语言客户端协议](https://github.com/apolloconfig/apollo/blob/master/docs/en/client/other-language-client-user-guide.md)。示例访问键由 Secret 管理器提供，切勿把真实值写入配置或版本库。
