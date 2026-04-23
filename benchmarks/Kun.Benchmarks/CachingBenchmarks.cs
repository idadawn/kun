using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Kun.Caching.Configuration;
using Kun.Caching.Implementations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kun.Benchmarks;

/// <summary>
/// Caching hot-path baselines for <see cref="MemoryKunCache"/> vs
/// <see cref="RedisKunCache"/>. The "Redis" side is backed by
/// <see cref="MemoryDistributedCache"/> so the measurement captures the
/// extra <c>IDistributedCache</c> + serialization cost without the
/// network round-trip — useful for proving serializer / key-prefix /
/// allocation regressions; NOT a substitute for running against a real
/// Redis deployment under load.
/// </summary>
[MemoryDiagnoser]
public class CachingBenchmarks
{
    private MemoryKunCache _memory = null!;
    private RedisKunCache _distributed = null!;
    private string _key = null!;
    private string _value = null!;

    /// <summary>Key length in bytes — stresses <c>KeyPrefixer</c> string building + dictionary lookup.</summary>
    [Params(10, 100, 1000)]
    public int KeyLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var cachingOptions = Options.Create(new KunCachingOptions());
        var distributedOptions = Options.Create(new MemoryDistributedCacheOptions());

        _memory = new MemoryKunCache(
            new MemoryCache(new MemoryCacheOptions()),
            cachingOptions);

        _distributed = new RedisKunCache(
            new MemoryDistributedCache(distributedOptions),
            new SystemTextJsonCacheSerializer(),
            cachingOptions,
            NullLogger<RedisKunCache>.Instance);

        _key = new string('k', KeyLength);
        _value = "v";

        // Prime both caches so Get benchmarks don't measure a cold miss.
        _ = SeedAsync();
    }

    [SuppressMessage("Performance", "CA1849",
        Justification = "Seeding at setup, not in the benchmark hot path.")]
    private async Task SeedAsync()
    {
        await _memory.SetAsync(_key, _value).ConfigureAwait(false);
        await _distributed.SetAsync(_key, _value).ConfigureAwait(false);
    }

    // ─── MemoryKunCache (baseline) ──────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "Memory · Set")]
    public async Task Memory_Set() => await _memory.SetAsync(_key, _value).ConfigureAwait(false);

    [Benchmark(Description = "Memory · Get")]
    public async Task<string?> Memory_Get() => await _memory.GetAsync<string>(_key).ConfigureAwait(false);

    [Benchmark(Description = "Memory · Remove")]
    public async Task Memory_Remove() => await _memory.RemoveAsync(_key).ConfigureAwait(false);

    // ─── RedisKunCache (IDistributedCache-backed; in-process fake) ─────────

    [Benchmark(Description = "Distributed · Set")]
    public async Task Distributed_Set() => await _distributed.SetAsync(_key, _value).ConfigureAwait(false);

    [Benchmark(Description = "Distributed · Get")]
    public async Task<string?> Distributed_Get() => await _distributed.GetAsync<string>(_key).ConfigureAwait(false);

    [Benchmark(Description = "Distributed · Remove")]
    public async Task Distributed_Remove() => await _distributed.RemoveAsync(_key).ConfigureAwait(false);
}
