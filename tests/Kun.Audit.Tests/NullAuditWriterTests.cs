using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kun.Audit.Tests;

public sealed class NullAuditWriterTests
{
    [Fact]
    public async Task WriteAsync_CompletesWithoutThrowing()
    {
        var writer = new NullAuditWriter(Substitute.For<ILogger<NullAuditWriter>>());
        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = "test.performed",
            ResourceType = "Test",
            Timestamp = DateTimeOffset.UtcNow,
        };

        await writer.WriteAsync(entry, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WriteAsync_NullEntry_Throws()
    {
        var writer = new NullAuditWriter(Substitute.For<ILogger<NullAuditWriter>>());

        Func<Task> act = async () =>
            await writer.WriteAsync(null!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
