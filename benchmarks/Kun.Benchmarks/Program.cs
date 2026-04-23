using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Kun.Benchmarks;

/// <summary>
/// BenchmarkDotNet entry point.
///
/// <para>
/// Default config: <c>Job.ShortRun</c> + JSON + GitHub-flavoured Markdown
/// exporters wired programmatically (no need to pass <c>--exporters</c>
/// on the CLI). <c>ShortRunJob</c>'s warmup/iteration counts keep the
/// Phase 1 baselines runnable in &lt; 5 minutes end-to-end on a developer
/// laptop; switch to <c>Job.Default</c> for publishable numbers.
/// </para>
///
/// <para>
/// Usage:
/// <code>
///     dotnet run -c Release --project benchmarks/Kun.Benchmarks -- --filter '*'
///     dotnet run -c Release --project benchmarks/Kun.Benchmarks -- --filter '*CachingBenchmarks*'
///     dotnet run -c Release --project benchmarks/Kun.Benchmarks -- --list flat
/// </code>
/// Artifacts land under <c>BenchmarkDotNet.Artifacts/results/*.(json|md)</c>.
/// </para>
/// </summary>
public static class Program
{
    /// <summary>
    /// BenchmarkDotNet entry point. Parses CLI <paramref name="args"/> and
    /// dispatches to the selected benchmarks. Returns <c>0</c> on success,
    /// <c>1</c> when at least one summary has critical validation errors.
    /// </summary>
    public static int Main(string[] args)
    {
        var config = ManualConfig
            .CreateMinimumViable()
            .AddJob(Job.ShortRun)
            .AddExporter(JsonExporter.Default)
            .AddExporter(MarkdownExporter.GitHub)
            .WithOptions(ConfigOptions.JoinSummary);

        var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);
        var summaries = switcher.Run(args, config);

        // Non-zero exit if any benchmark failed to run so CI / scripts can detect
        // authoring errors (missing baseline classes, benchmark method throwing, …).
        foreach (var summary in summaries)
        {
            if (summary.HasCriticalValidationErrors)
            {
                return 1;
            }
        }
        return 0;
    }
}
