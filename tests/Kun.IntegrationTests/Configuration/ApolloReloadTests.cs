using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kun.Configuration.Apollo;
using Kun.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.IntegrationTests.Configuration;

[RequiresDynamicCode("集成验证标准 Options 反射绑定。")]
[RequiresUnreferencedCode("集成验证标准 Options DataAnnotations。")]
public sealed class ApolloReloadTests
{
    [Fact]
    public async Task SignedApolloRefresh_InvalidOptionsRetainLastGood_AndCacheCanStartOffline()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kun-apollo-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var handler = new ApolloHandler();
            using var client = new HttpClient(handler);
            var options = Options(directory);
            using var root = (ConfigurationRoot)new ConfigurationBuilder().AddApollo(options, client).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(root);
            services.AddKunOptions<Settings>("Service");
            using var provider = services.BuildServiceProvider();
            var monitor = provider.GetRequiredService<IOptionsMonitor<Settings>>();
            Assert.Equal(3, monitor.CurrentValue.Retries);
            handler.Value = "0";
            await Assert.IsType<ApolloConfigurationProvider>(root.Providers.Single()).RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(3, monitor.CurrentValue.Retries);
            handler.Value = "5";
            await Assert.IsType<ApolloConfigurationProvider>(root.Providers.Single()).RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(5, monitor.CurrentValue.Retries);
            Assert.True(handler.SignedRequests >= 3);
            handler.Offline = true;
            using var cached = (ConfigurationRoot)new ConfigurationBuilder().AddApollo(options, client).Build();
            Assert.Equal("5", cached["Service:Retries"]);
            Assert.DoesNotContain("test-access-key", await File.ReadAllTextAsync(Directory.GetFiles(directory).Single(), TestContext.Current.CancellationToken));
            handler.Offline = false;
            handler.Denied = true;
            Assert.Throws<UnauthorizedAccessException>(() => new ConfigurationBuilder().AddApollo(options, client).Build());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void NoNetworkAndNoCache_FailsInsteadOfReturningEmptyConfiguration()
    {
        using var handler = new ApolloHandler { Offline = true };
        using var client = new HttpClient(handler);
        Assert.Throws<InvalidOperationException>(() => new ConfigurationBuilder().AddApollo(Options(null), client).Build());
    }

    [Fact]
    public async Task LongPoll_UsesNotificationRevision_ThenReloadsConfiguration()
    {
        using var handler = new ApolloHandler { LongPoll = true };
        using var client = new HttpClient(handler);
        using var root = (ConfigurationRoot)new ConfigurationBuilder().AddApollo(new ApolloOptions
        {
            AppId = "sample",
            ConfigServers = [new Uri("https://apollo.test")],
            ResolveAccessKey = () => "test-access-key",
            ReloadOnChange = true
        }, client).Build();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (root["Service:Retries"] != "5" && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("5", root["Service:Retries"]);
        Assert.True(handler.NotificationRevisionForwarded);
    }

    [Fact]
    public async Task SnapshotValidator_RejectsBeforeReplacingConfigurationOrCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kun-apollo-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var handler = new ApolloHandler();
            using var client = new HttpClient(handler);
            var options = new ApolloOptions
            {
                AppId = "sample",
                ConfigServers = [new Uri("https://apollo.test")],
                ResolveAccessKey = () => "test-access-key",
                ReloadOnChange = false,
                CacheDirectory = directory,
                Validate = data => int.TryParse(data["Service:Retries"], out var count) && count > 0
            };
            using var root = (ConfigurationRoot)new ConfigurationBuilder().AddApollo(options, client).Build();
            handler.Value = "0";
            await Assert.ThrowsAsync<InvalidDataException>(() => ((ApolloConfigurationProvider)root.Providers.Single()).RefreshAsync(TestContext.Current.CancellationToken));
            Assert.Equal("3", root["Service:Retries"]);
            handler.Offline = true;
            using var cached = (ConfigurationRoot)new ConfigurationBuilder().AddApollo(options, client).Build();
            Assert.Equal("3", cached["Service:Retries"]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static ApolloOptions Options(string? cache) => new()
    {
        AppId = "sample",
        ConfigServers = [new Uri("https://apollo.test")],
        CacheDirectory = cache,
        ReloadOnChange = false,
        ResolveAccessKey = () => "test-access-key"
    };
    public sealed class Settings { [Range(1, 10)] public int Retries { get; set; } }

    private sealed class ApolloHandler : HttpMessageHandler
    {
        public string Value = "3";
        public bool Offline;
        public bool Denied;
        public int SignedRequests;
        public bool LongPoll;
        public bool NotificationRevisionForwarded;
        private int _notifications;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline) throw new HttpRequestException("offline");
            if (Denied) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            var timestamp = request.Headers.GetValues("Timestamp").Single();
#pragma warning disable CA5350 // 固定测试 Apollo 协议签名。
            var signature = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes("test-access-key"), Encoding.UTF8.GetBytes(timestamp + "\n" + request.RequestUri!.PathAndQuery)));
#pragma warning restore CA5350
            Assert.Equal("Apollo sample:" + signature, request.Headers.GetValues("Authorization").Single());
            SignedRequests++;
            if (LongPoll && request.RequestUri!.AbsolutePath.EndsWith("/notifications/v2", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _notifications) == 1)
                {
                    Value = "5";
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("""[{"namespaceName":"application","notificationId":1}]""") };
                }
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            if (LongPoll && Value == "5")
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
                using var messages = JsonDocument.Parse(query["messages"].ToString());
                Assert.Equal(1, messages.RootElement.GetProperty("details").GetProperty("sample+default+application").GetInt64());
                NotificationRevisionForwarded = true;
            }
            var payload = JsonSerializer.Serialize(new
            {
                appId = "sample",
                cluster = "default",
                namespaceName = "application",
                releaseKey = "revision-" + Value,
                configurations = new Dictionary<string, string> { ["Service:Retries"] = Value }
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }
}
