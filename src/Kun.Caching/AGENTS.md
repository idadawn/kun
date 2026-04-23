<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 -->

# Kun.Caching

## Purpose

Unified cache abstraction for Kun Framework consumers. Single entry
point — `AddKunCaching(...).UseMemory()` or `.UseRedis(connection)` —
backed by two implementations:

- `MemoryKunCache` wrapping `IMemoryCache` (process-local, fastest, no
  serialization hop).
- `RedisKunCache` wrapping `IDistributedCache` (cross-process, backed
  by `Microsoft.Extensions.Caching.StackExchangeRedis`, values serialized
  through a pluggable `IKunCacheSerializer`).

Both implementations share the single public surface:

```csharp
public interface IKunCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default);
}
```

## Layer and AOT

- Layer: **Infrastructure** — references `Microsoft.Extensions.Caching.*`
  and `Microsoft.Extensions.Caching.StackExchangeRedis`.
- `<IsAotCandidate>true</IsAotCandidate>`. The generic `GetAsync<T>` /
  `SetAsync<T>` / `GetOrSetAsync<T>` methods are annotated with
  `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`; the
  reflection-based default JSON serializer is the reason. AOT-strict
  consumers supply a custom `IKunCacheSerializer` backed by a
  source-generated `JsonSerializerContext`.
- If the AOT analyzer ever flags the module after an
  `Microsoft.Extensions.Caching.*` upgrade, **stop-gate the upgrade
  and file an ADR**; do not silently flip `IsAotCandidate=false`.

## Key Design Decisions

- **Single-flight `GetOrSetAsync`**: per-key `SemaphoreSlim` pool
  guarantees the factory runs at most once under concurrent access
  on the same process. Single-flight is local-only; cross-process
  single-flight would require a distributed lock (deferred work).
- **Key prefix**: `KunCachingOptions.KeyPrefix` is prepended to every
  key. Ideal for namespacing per-service caches on a shared Redis.
- **Default TTL**: when a caller passes `ttl=null`, the registered
  `DefaultTtl` (if any) applies. Missing both ⇒ cache entry never
  expires.
- **Memory uniformity**: the Memory backend stores `T` directly (no
  serialization round-trip). Callers that rely on mutate-after-cache
  semantics should treat the cache as immutable.
- **Redis fail-open**: when
  `KunCachingOptions.FallbackToMemoryOnRedisFailure = true` the Redis
  backend routes reads and writes to a sidecar `MemoryKunCache` on
  `RedisConnectionException` / `SocketException`; the exception is
  logged and swallowed. Default is `false` — exceptions propagate.

## Wiring

```csharp
// Zero-config in-memory cache.
services.AddKunCaching().UseMemory();

// Redis with a key prefix + 10-minute default TTL.
services.AddKunCaching(o =>
{
    o.KeyPrefix = "platform:";
    o.DefaultTtl = TimeSpan.FromMinutes(10);
}).UseRedis("localhost:6379");

// Production: Redis with memory fallback on outage.
services.AddKunCaching(o =>
{
    o.KeyPrefix = "edge:";
    o.FallbackToMemoryOnRedisFailure = true;
}).UseRedis(Environment.GetEnvironmentVariable("REDIS_CONNECTION")!);
```

## For AI Agents

- Adding a new operation (`IncrementAsync`, `GetManyAsync`, etc.)?
  Put it on `IKunCache` and implement it on **both** backends.
  Memory-backed code must stay behaviour-equivalent with Redis so the
  same test suite can run against either.
- Avoid introducing runtime reflection in a non-annotated path. The
  `[RequiresUnreferencedCode]` contract only covers the generic
  methods — new non-generic members must be reflection-free.
- `IDistributedCache` exposes `GetAsync(string)` which allocates
  on each miss. This module uses it as-is; do not replace with a
  custom Redis client without an ADR.
- The per-key `SemaphoreSlim` pool is bounded only by the working
  set of keys referenced by `GetOrSetAsync`. If a caller uses
  unbounded ephemeral keys, they will leak. Document the caller
  convention (stable keys) and consider adding a TTL-based eviction
  pool if it becomes a problem.
