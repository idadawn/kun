using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;
using Kun.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Configuration.Tests;

public sealed class ReloadOptions
{
    [Range(1, 10)]
    public int Retries { get; set; }
    public required string Endpoint { get; set; }
}
[JsonSerializable(typeof(ReloadOptions))]
internal sealed partial class ReloadJsonContext : JsonSerializerContext;

[RequiresDynamicCode("验证既有的反射绑定入口。")]
[RequiresUnreferencedCode("验证既有的 DataAnnotations 入口。")]
public sealed class LastGoodOptionsTests
{
    [Fact]
    public void InvalidReload_KeepsOldValue_ReportsMetric_ThenRecovers()
    {
        var rejected = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Name == "kun.configuration.reload.rejected") meter.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref rejected, value));
        listener.Start();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Service:Retries"] = "3",
            ["Service:Endpoint"] = "https://config.test"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddKunOptions<ReloadOptions>("Service", ReloadJsonContext.Default.ReloadOptions);
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<ReloadOptions>>();
        var original = monitor.CurrentValue;
        var notifications = 0;
        using var subscription = monitor.OnChange((_, _) => notifications++);
        config["Service:Retries"] = "0";
        config.Reload();
        Assert.Same(original, monitor.CurrentValue);
        config["Service:Retries"] = "not-an-integer";
        config.Reload();
        Assert.Same(original, monitor.CurrentValue);
        Assert.Equal(2, rejected);
        Assert.Equal(0, notifications);
        config["Service:Retries"] = "5";
        config.Reload();
        Assert.Equal(5, monitor.CurrentValue.Retries);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task OptionalUnusedConfiguration_DoesNotPreventStartup_ButStillValidatesWhenRead()
    {
        using var host = Host.CreateDefaultBuilder().ConfigureServices(services =>
            services.AddKunOptions<ReloadOptions>("OptionalMissing", critical: false)).Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        Assert.Throws<OptionsValidationException>(() => host.Services.GetRequiredService<IOptionsMonitor<ReloadOptions>>().CurrentValue);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AddKunOptions_Exports202012Schema_FromSourceGeneratedMetadata()
    {
        var services = new ServiceCollection();
        services.AddKunOptions<ReloadOptions>("Service", ReloadJsonContext.Default.ReloadOptions);
        using var provider = services.BuildServiceProvider();
        var entry = provider.GetRequiredService<OptionsSchema<ReloadOptions>>();
        var schema = entry.Export();
        Assert.Equal("Service", entry.Section);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)schema["$schema"]);
        var validator = JsonSchema.FromText(schema.ToJsonString());
        Assert.True(validator.Evaluate(JsonNode.Parse("""{"Retries":3,"Endpoint":"https://test"}""")).IsValid);
        Assert.False(validator.Evaluate(JsonNode.Parse("""{"Retries":"bad","Endpoint":"https://test"}""")).IsValid);
        schema["title"] = "mutated";
        Assert.Null(entry.Export()["title"]);
    }
}
