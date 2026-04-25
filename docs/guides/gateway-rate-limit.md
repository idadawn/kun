# Gateway 限流策略指南

> `Kun.Gateway.RateLimiting` 把 ASP.NET Core 内置的 RateLimiter 包装成**策略 × 分区**的两维组合：
> 三种分区解析（partition resolver）× 三种限流策略（limiter policy）= 九种可选配置，配合 Redis 后端做跨 Pod 计数共享。
>
> 参考：`docs/architecture/why-cqrs.md` · 白皮书 §12.2

## 分区解析 (IRateLimitPartitionResolver)

决定"同一个限流桶由谁共享"。从 HttpContext 里取一个字符串 key。

| 实现 | 分区依据 | 适用 |
|---|---|---|
| `IpRateLimitPartitionResolver` | `HttpContext.Connection.RemoteIpAddress` | 匿名 API、防 abuse |
| `TenantRateLimitPartitionResolver` | S2S JWT 的 `tenant_id` claim | 多租户共享库场景（默认） |
| `UserPrincipalRateLimitPartitionResolver` | S2S JWT 的 `sub` claim | Free-tier API，per-account 限额 |

**注册**：任意一种用 `services.AddSingleton<IRateLimitPartitionResolver, XxxResolver>()`。Kun 的 `AddKunXxxRateLimiter()` 系列扩展会用 `TryAddSingleton<...,TenantRateLimitPartitionResolver>()` 做默认，你预先注册就覆盖它。

## 限流策略 (IRateLimiterPolicy)

决定"桶里怎么发放许可"。

| 策略 | 行为 | 特点 | 默认值 |
|---|---|---|---|
| **FixedWindow**（`AddKunRateLimiter`） | 每 N 秒重置计数 | 最简单、内存最低，边界突刺 | 100 permits / 1 min |
| **SlidingWindow**（`AddKunSlidingWindowRateLimiter`） | 分段滑动 | 平滑，无突刺；内存与 `SegmentsPerWindow` 线性 | 100 / 1 min / 6 segments |
| **TokenBucket**（`AddKunTokenBucketRateLimiter`） | 令牌累积 + 匀速补充 | 吸收突发流量；适合 webhook fanout、批量导入 | 100 tokens burst，20 / 10s 补充 |

三者均调用 `AddRateLimiter()` 注册一个具名策略：`"kun-default"`、`"kun-sliding-window"`、`"kun-token-bucket"`。端点开启时写：

```csharp
app.MapGet("/orders", handler).RequireRateLimiting(KunRateLimiterExtensions.SlidingWindowPolicyName);
```

## 典型组合

### 1. 多租户 SaaS，边界精度要求一般
```csharp
builder.Services.AddKunRateLimiter();   // Tenant partition + FixedWindow
```

### 2. 按用户限流，平滑曲线
```csharp
builder.Services.AddSingleton<IRateLimitPartitionResolver, UserPrincipalRateLimitPartitionResolver>();
builder.Services.AddKunSlidingWindowRateLimiter(o => { o.PermitLimit = 60; o.Window = TimeSpan.FromMinutes(1); });
```

### 3. 防 DDoS 按 IP，允许突发
```csharp
builder.Services.AddSingleton<IRateLimitPartitionResolver, IpRateLimitPartitionResolver>();
builder.Services.AddKunTokenBucketRateLimiter(o =>
{
    o.TokenLimit = 500;
    o.TokensPerPeriod = 50;
    o.ReplenishmentPeriod = TimeSpan.FromSeconds(1);   // 50 req/s 稳态
});
```

## Redis 后端：跨 Pod 分布式计数

内置的 FixedWindow / SlidingWindow / TokenBucket 策略都是**进程内**（每个 Gateway pod 各自独立计数）。在多 pod 部署时 100 permits 会变成 `100 × podCount` 的实际吞吐。

`Kun.Gateway.RateLimiting.IRedisRateLimitStore` 提供基础 primitive：

```csharp
services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("redis:6379"));
services.AddKunRedisRateLimitStore(o => o.KeyPrefix = "kun:prod:rl");
```

调用：
```csharp
var result = await store.HitAsync("tenant-a", TimeSpan.FromMinutes(1));
// result.Count 现在有多少个命中、result.ResetsIn 距离重置还有多久
if (result.Count > 100) { /* reject */ }
```

**实现要点**：
- 原子 `INCR`，首次命中额外 `EXPIRE` 设 TTL，后续命中不触发 `EXPIRE`（保持滑动边界）
- 计数器自动到期自清理，无需后台 GC 任务
- 多 Pod 共享计数，PgBouncer / Redis Cluster 环境里行为一致

**限制**：当前 `RedisFixedWindowRateLimitStore` 是**低层 primitive**，不是 `PartitionedRateLimiter<HttpContext>` 实现。即你不能直接用 `RequireRateLimiting("redis-policy")` 让框架路由到它；需要手写一个 middleware 调 `HitAsync` 并在命中超限时返回 429。框架内置 `PartitionedRateLimiter` 适配留待未来（tracked as Part 3b）。

## 落地 checklist

- [ ] 选一个 `IRateLimitPartitionResolver`（或默认 Tenant）
- [ ] 选一个或多个限流策略（`AddKunXxxRateLimiter`）
- [ ] 在需要的端点调 `.RequireRateLimiting(KunRateLimiterExtensions.XxxPolicyName)`
- [ ] 多 Pod 部署：额外注册 `AddKunRedisRateLimitStore` + 写 middleware 替代/补强
- [ ] 压测：推荐 `Testcontainers.Redis` + 100 并发 request，校验精度 < 2% drift

## 相关代码

- `src/Kun.Gateway/RateLimiting/IRateLimitPartitionResolver.cs` + `Resolvers/` 三种实现
- `src/Kun.Gateway/RateLimiting/KunRateLimiterExtensions.cs` 三种 `AddKunXxxRateLimiter` + `AddKunRedisRateLimitStore`
- `src/Kun.Gateway/RateLimiting/Kun{Sliding,TokenBucket}Options.cs`
- `src/Kun.Gateway/RateLimiting/RedisFixedWindowRateLimitStore.cs`
