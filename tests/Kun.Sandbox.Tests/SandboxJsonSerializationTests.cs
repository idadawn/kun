#pragma warning disable CS0618 // Explicit legacy wire compatibility; default and provider-owned shapes have separate tests.
using System.Text.Json;
using FluentAssertions;
using Kun.Sandbox.Abstractions;
using Kun.Sandbox.Implementations;
using Xunit;

namespace Kun.Sandbox.Tests;

public sealed class SandboxJsonSerializationTests
{
    [Fact]
    public void Defaults_AreProductNeutral_AndExtensionKeysAreCallerOwned()
    {
        var defaults = JsonSerializer.Serialize(new SandboxCreateOptions(), SupervisorSandboxProvider.SerializerOptions);
        defaults.Should().Contain("kun-default").And.NotContain("neuroagent").And.NotContain("iclaw");
        using var extension = JsonDocument.Parse("""{"networkPolicy":"deny-all"}""");
        var configured = new SandboxCreateOptions { ProviderExtensions = new Dictionary<string, JsonElement> { ["team-runtime"] = extension.RootElement.Clone() } };
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(configured, SupervisorSandboxProvider.SerializerOptions));
        payload.RootElement.GetProperty("team-runtime").GetProperty("networkPolicy").GetString().Should().Be("deny-all");
    }

    [Fact]
    public void SandboxCreateOptions_serialises_with_camelCase_property_names()
    {
        var options = new SandboxCreateOptions
        {
            TemplateId = "neuroagent-default",
            CpuCount = 4,
            MemoryMB = 4096,
            DiskMB = 1024,
            TimeoutMs = 60_000,
            Envs = new Dictionary<string, string> { ["X"] = "1" },
            Metadata = new Dictionary<string, string> { ["tenantId"] = "t" },
            NeuroagentExtension = new NeuroagentSandboxExtension { MaxCommands = 50 },
        };

        var json = JsonSerializer.Serialize(options, SupervisorSandboxProvider.SerializerOptions);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("templateId", out _).Should().BeTrue();
        root.TryGetProperty("cpuCount", out _).Should().BeTrue();
        root.TryGetProperty("memoryMB", out _).Should().BeTrue();
        root.TryGetProperty("diskMB", out _).Should().BeTrue();
        root.TryGetProperty("timeoutMs", out _).Should().BeTrue();
        root.TryGetProperty("envs", out _).Should().BeTrue();
        root.TryGetProperty("metadata", out _).Should().BeTrue();
        root.TryGetProperty("neuroagent", out _).Should().BeTrue();

        // Ensure PascalCase variants are NOT present.
        root.TryGetProperty("TemplateId", out _).Should().BeFalse();
        root.TryGetProperty("CpuCount", out _).Should().BeFalse();
    }

    [Fact]
    public void SandboxCreateOptions_omits_null_optional_fields()
    {
        var options = new SandboxCreateOptions
        {
            TemplateId = "neuroagent-default",
        };

        var json = JsonSerializer.Serialize(options, SupervisorSandboxProvider.SerializerOptions);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("diskMB", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("envs", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("metadata", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("neuroagent", out _).Should().BeFalse();
    }
}
