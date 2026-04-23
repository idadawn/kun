using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Kun.Integration.AotTests;

/// <summary>
/// End-to-end AOT validation for <c>Kun.Sample.Minimal</c> — the
/// framework's canonical AOT canary. Two layered tests:
///
///   1. <see cref="Build_WithPublishAot_IsFree_OfTrimAndAotWarnings"/> —
///      runs a fast <c>dotnet build -p:PublishAot=true</c> and asserts
///      zero <c>IL####</c> analyzer diagnostics. Works offline, no
///      platform linker needed, so it's the unconditional gate.
///
///   2. <see cref="PublishedBinary_RunsAndEmitsExpectedOutput"/> — runs
///      a full native publish and executes the binary. Only runs when
///      <c>clang</c> (or <c>gcc</c>) is available, otherwise skipped.
///      CI's <c>sample-aot-integration</c> job (ubuntu-latest) always
///      runs this path.
/// </summary>
public sealed class SampleMinimalAotPublishTests
{
    // Matches "warning IL####" from the trim / AOT / single-file analyzers.
    private static readonly Regex IlWarningRegex = new(
        @"warning IL\d{4}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches "error IL####" — analyzer diagnostics promoted to errors
    // by TreatWarningsAsErrors=true.
    private static readonly Regex IlErrorRegex = new(
        @"error IL\d{4}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string ClangMissingMarker =
        "Platform linker ('clang' or 'gcc') not found in PATH";

    [Fact]
    public async Task Build_WithPublishAot_IsFree_OfTrimAndAotWarnings()
    {
        // `dotnet build` (not publish) runs the trim / AOT / single-file
        // analyzers but skips the ILC → clang native pipeline, so it
        // completes in seconds even on dev laptops without clang.
        // Directory.Build.props already enables EnableTrimAnalyzer +
        // EnableAotAnalyzer for IsAotCandidate=true projects.
        var sampleProj = LocateSampleProject();

        var (exitCode, stdout, stderr) = await RunDotnetAsync(
            "build",
            sampleProj,
            "-c", "Release",
            "-p:TreatWarningsAsErrors=true",
            "-p:PublishAot=true",
            "-v:minimal",
            "-nologo");

        var combined = stdout + Environment.NewLine + stderr;
        IlWarningRegex.Matches(combined).Should().BeEmpty(
            "Kun.Sample.Minimal must produce zero trim / AOT analyzer warnings.\nOutput:\n{0}",
            combined);
        IlErrorRegex.Matches(combined).Should().BeEmpty(
            "Kun.Sample.Minimal must produce zero trim / AOT analyzer errors.\nOutput:\n{0}",
            combined);
        exitCode.Should().Be(0,
            "Kun.Sample.Minimal must build cleanly with PublishAot=true and warnings-as-errors.\nOutput:\n{0}",
            combined);
    }

    [Fact]
    public async Task PublishedBinary_RunsAndEmitsExpectedOutput()
    {
        if (!IsPlatformLinkerAvailable())
        {
            // Local dev machines without clang/gcc cannot produce a native
            // binary. The build-time test above already asserted that the
            // AOT analyzer is clean; CI picks up the native execution.
            Assert.Skip("clang / gcc not on PATH — native AOT publish requires a platform linker.");
            return;
        }

        var sampleProj = LocateSampleProject();
        var outputDir = Path.Combine(
            Path.GetTempPath(), $"kun-aot-run-{Guid.NewGuid():N}");

        try
        {
            var (publishExit, publishOut, publishErr) = await RunDotnetAsync(
                "publish",
                sampleProj,
                "-c", "Release",
                "-r", "linux-x64",
                "-p:PublishAot=true",
                "-p:TreatWarningsAsErrors=true",
                "-o", outputDir);

            publishExit.Should().Be(0,
                "native AOT publish must succeed when clang is available. stdout+stderr:\n{0}\n{1}",
                publishOut, publishErr);

            var binary = Path.Combine(outputDir, "Kun.Sample.Minimal");
            File.Exists(binary).Should().BeTrue(
                "native AOT binary must be produced at {0}", binary);

            var (runExit, runOut, runErr) = await RunRawAsync(binary);
            runExit.Should().Be(0,
                "Kun.Sample.Minimal must exit cleanly. stdout:\n{0}\nstderr:\n{1}",
                runOut, runErr);
            runOut.Should().Contain("Kun Framework sample — Mediator dispatch demo");
            runOut.Should().Contain("Done.");
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                try { Directory.Delete(outputDir, recursive: true); }
                catch { /* tmp cleanup — best effort */ }
            }
        }
    }

    private static string LocateSampleProject()
    {
        var repoRoot = LocateRepoRoot();
        var sampleProj = Path.Combine(
            repoRoot, "samples", "Kun.Sample.Minimal", "Kun.Sample.Minimal.csproj");
        File.Exists(sampleProj).Should().BeTrue(
            "Kun.Sample.Minimal is the expected AOT canary at {0}", sampleProj);
        return sampleProj;
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Kun.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Kun.sln starting from {AppContext.BaseDirectory}");
    }

    private static bool IsPlatformLinkerAvailable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(entry))
            {
                continue;
            }

            if (File.Exists(Path.Combine(entry, "clang")) ||
                File.Exists(Path.Combine(entry, "gcc")))
            {
                return true;
            }
        }

        return false;
    }

    private static Task<(int ExitCode, string StdOut, string StdErr)> RunDotnetAsync(
        params string[] arguments)
        => RunProcessAsync("dotnet", arguments);

    private static Task<(int ExitCode, string StdOut, string StdErr)> RunRawAsync(
        string executable,
        params string[] arguments)
        => RunProcessAsync(executable, arguments);

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName,
        string[] arguments)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Disable MSBuild node-reuse and the persistent MSBuild server so
        // nested `dotnet build` / `dotnet publish` invocations do not
        // deadlock against the MSBuild node pool owned by the parent
        // `dotnet test` host. Without these, the child publish can block
        // indefinitely waiting for a node that the parent keeps pinned.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Failed to start process: {fileName}");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }
}
