using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Kun.Integration.AotTests;

public sealed class GatewayPackageAotPublishTests
{
    [Fact]
    public async Task OrdinaryPackage_LinuxNativeConsumer_PreservesJwksGatesAndRotation()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("此回归必须在 Linux 上发布并执行 ELF；CI 的 sample-aot-integration 必须运行它。");
            return;
        }

        var repository = LocateRepository();
        var directory = Path.Combine(Path.GetTempPath(), $"kun-gateway-package-aot-{Guid.NewGuid():N}");
        var version = $"0.0.0-aot.{Guid.NewGuid():N}";
        var feed = Path.Combine(directory, "feed");
        var consumer = Path.Combine(directory, "consumer");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(consumer);
        try
        {
            // 普通 NuGet 制品是被测对象。库不启用 PublishAot/RDG，且使用隔离的 obj/bin，
            // 避免库自己的宿主发布结果或并行 Sample 测试污染这次构建。
            foreach (var package in new[] { "Kun.Core", "Kun.MultiTenancy", "Kun.Security", "Kun.Observability", "Kun.Gateway" })
            {
                var result = await RunAsync(repository, "dotnet", TimeSpan.FromMinutes(5),
                    "pack", Path.Combine(repository, "src", package, package + ".csproj"),
                    "-c", "Release", "--artifacts-path", Path.Combine(directory, "artifacts"), "-o", feed,
                    "-p:Version=" + version, "-p:PublishAot=false", "-p:EnableRequestDelegateGenerator=false",
                    "-p:TreatWarningsAsErrors=true", "-p:IncludeSymbols=false", "-p:UseSharedCompilation=false", "-m:1");
                AssertClean(result, "普通打包 " + package);
                File.Exists(Path.Combine(feed, package + "." + version + ".nupkg")).Should().BeTrue();
            }

            var fixture = Path.Combine(repository, "tests", "Kun.Integration.AotTests", "Fixtures", "GatewayPackageConsumer");
            foreach (var file in Directory.EnumerateFiles(fixture))
                File.Copy(file, Path.Combine(consumer, Path.GetFileName(file)));
            // 从仓库外解析，Kun.* 只能来自刚生成的本地包，其他依赖来自官方源。
            var config = Path.Combine(directory, "NuGet.Config");
            var configuration = new System.Xml.Linq.XDocument(
                new System.Xml.Linq.XElement("configuration",
                    new System.Xml.Linq.XElement("packageSources",
                        new System.Xml.Linq.XElement("clear"),
                        new System.Xml.Linq.XElement("add", new System.Xml.Linq.XAttribute("key", "probe"), new System.Xml.Linq.XAttribute("value", feed)),
                        new System.Xml.Linq.XElement("add", new System.Xml.Linq.XAttribute("key", "nuget.org"), new System.Xml.Linq.XAttribute("value", "https://api.nuget.org/v3/index.json"))),
                    new System.Xml.Linq.XElement("packageSourceMapping",
                        new System.Xml.Linq.XElement("packageSource", new System.Xml.Linq.XAttribute("key", "probe"), new System.Xml.Linq.XElement("package", new System.Xml.Linq.XAttribute("pattern", "Kun.*"))),
                        new System.Xml.Linq.XElement("packageSource", new System.Xml.Linq.XAttribute("key", "nuget.org"), new System.Xml.Linq.XElement("package", new System.Xml.Linq.XAttribute("pattern", "*"))))));
            configuration.Save(config);

            var publish = Path.Combine(directory, "publish");
            var published = await RunAsync(consumer, "dotnet", TimeSpan.FromMinutes(10),
                "publish", "GatewayPackageConsumer.csproj", "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier,
                "--configfile", config, "-p:KunPackageVersion=" + version,
                "-p:TreatWarningsAsErrors=true", "-p:UseSharedCompilation=false", "-m:1", "-o", publish);
            AssertClean(published, "普通包的独立 Linux NativeAOT 消费者发布");

            using var assets = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(consumer, "obj", "project.assets.json"), TestContext.Current.CancellationToken));
            var libraries = assets.RootElement.GetProperty("libraries");
            libraries.GetProperty("Kun.Gateway/" + version).GetProperty("type").GetString().Should().Be("package");
            libraries.EnumerateObject().Should().NotContain(library => library.Value.GetProperty("type").GetString() == "project",
                "宿主必须消费普通包，不能通过 ProjectReference 重新生成库端点");

            var binary = Path.Combine(publish, "GatewayPackageConsumer");
            var bytes = await File.ReadAllBytesAsync(binary, TestContext.Current.CancellationToken);
            bytes.Take(4).Should().Equal(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' });
            File.Exists(binary + ".dll").Should().BeFalse("执行的是原生宿主而不是托管启动器");
            var execution = await RunAsync(consumer, binary, TimeSpan.FromSeconds(30));
            execution.ExitCode.Should().Be(0, "原生端点行为必须通过。输出：\n{0}", execution.Output);
            execution.Output.Should().Contain("Kun.Gateway package NativeAOT: anonymous JWKS, gates, no-store and key rotation passed.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertClean((int ExitCode, string Output) result, string step)
    {
        Regex.Matches(result.Output, @"\b(?:warning|error) IL\d{4}\b", RegexOptions.IgnoreCase)
            .Should().BeEmpty("{0} 不允许任何 IL 诊断。输出：\n{1}", step, result.Output);
        result.ExitCode.Should().Be(0, "{0} 必须成功。输出：\n{1}", step, result.Output);
    }

    private static string LocateRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Kun.sln"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate Kun.sln");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string directory, string executable, TimeSpan timeout, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException($"{executable} 未在 {timeout} 内完成或测试已取消。\n{await stdout}\n{await stderr}");
        }
        return (process.ExitCode, await stdout + Environment.NewLine + await stderr);
    }
}
