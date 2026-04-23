using FluentAssertions;
using Kun.Sandbox.Abstractions;
using Kun.Sandbox.Implementations;
using Xunit;

namespace Kun.Sandbox.Tests;

public sealed class InMemorySandboxProviderTests
{
    [Fact]
    public async Task CreateAsync_returns_handle_with_running_state_and_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new InMemorySandboxProvider();

        var handle = await provider.CreateAsync(new SandboxCreateOptions { TemplateId = "neuroagent-default" }, ct);

        handle.SandboxId.Should().StartWith("sb_mem_");
        handle.TemplateId.Should().Be("neuroagent-default");
        handle.State.Should().Be(SandboxState.Running);
        handle.EndAt.Should().BeAfter(handle.StartedAt);
    }

    [Fact]
    public async Task RunAsync_uses_scripted_response_when_provided()
    {
        var ct = TestContext.Current.CancellationToken;
        ProcessResult scripted = new() { ProcessId = "p1", ExitCode = 7, Stdout = "hi", Stderr = "err", DurationMs = 42 };
        var provider = new InMemorySandboxProvider((_, _) => scripted);
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);

        var result = await handle.Processes.RunAsync(["echo", "hi"], null, ct);

        result.Should().BeSameAs(scripted);
    }

    [Fact]
    public async Task RunAsync_default_returns_exit_zero_and_measured_duration()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new InMemorySandboxProvider();
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);

        var result = await handle.Processes.RunAsync("true", null, ct);

        result.ExitCode.Should().Be(0);
        result.ProcessId.Should().StartWith("proc_mem_");
        result.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Files_Write_then_Read_roundtrips_bytes()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new InMemorySandboxProvider();
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);
        var payload = System.Text.Encoding.UTF8.GetBytes("hello, kun");

        await handle.Files.WriteAsync("/mnt/user-data/workspace/a.txt", payload, ct);
        var read = await handle.Files.ReadAsync("/mnt/user-data/workspace/a.txt", ct);

        read.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public async Task Files_ReadAsync_throws_on_missing_path()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new InMemorySandboxProvider();
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);

        var act = () => handle.Files.ReadAsync("/mnt/user-data/workspace/nope", ct);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Files_ListAsync_returns_only_direct_children_when_non_recursive()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new InMemorySandboxProvider();
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);
        await handle.Files.WriteAsync("/root/a.txt", [1], ct);
        await handle.Files.WriteAsync("/root/b.txt", [2], ct);
        await handle.Files.WriteAsync("/root/sub/c.txt", [3], ct);

        var topLevel = await handle.Files.ListAsync("/root", recursive: false, ct);
        var deep = await handle.Files.ListAsync("/root", recursive: true, ct);

        topLevel.Select(e => e.Path).Should().BeEquivalentTo(["/root/a.txt", "/root/b.txt"]);
        deep.Select(e => e.Path).Should().Contain("/root/sub/c.txt");
    }

    [Fact]
    public async Task Files_DeleteAsync_removes_stored_bytes()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new InMemorySandboxProvider();
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);
        await handle.Files.WriteAsync("/x.txt", [1], ct);

        await handle.Files.DeleteAsync("/x.txt", ct);

        await FluentActions.Invoking(() => handle.Files.ReadAsync("/x.txt", ct))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task KillAsync_transitions_state_and_blocks_further_work()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new InMemorySandboxProvider();
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);

        await handle.KillAsync(ct);
        handle.State.Should().Be(SandboxState.Killed);

        await FluentActions.Invoking(() => handle.Processes.RunAsync("true", null, ct))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Invoking(() => handle.Files.WriteAsync("/p", [1], ct))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task KillAsync_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new InMemorySandboxProvider();
        var handle = await provider.CreateAsync(new SandboxCreateOptions(), ct);

        await handle.KillAsync(ct);
        await handle.KillAsync(ct);

        handle.State.Should().Be(SandboxState.Killed);
    }

    [Fact]
    public async Task Parallel_create_yields_independent_sandboxes_with_unique_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new InMemorySandboxProvider();

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => provider.CreateAsync(new SandboxCreateOptions(), ct))
            .ToArray();
        var handles = await Task.WhenAll(tasks);

        handles.Select(h => h.SandboxId).Distinct().Should().HaveCount(16);
        foreach (var h in handles)
        {
            await h.Files.WriteAsync("/marker", System.Text.Encoding.UTF8.GetBytes(h.SandboxId), ct);
        }
        foreach (var h in handles)
        {
            var bytes = await h.Files.ReadAsync("/marker", ct);
            System.Text.Encoding.UTF8.GetString(bytes).Should().Be(h.SandboxId);
        }
    }
}
