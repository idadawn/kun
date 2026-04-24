using FluentAssertions;
using Kun.Audit;
using Xunit;

namespace Kun.Audit.Tests;

public sealed class AuditContentHasherTests
{
    private static readonly Guid SampleId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset SampleTimestamp = new(2026, 4, 24, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_Is_Deterministic_For_Same_Inputs()
    {
        var a = AuditContentHasher.Compute(
            SampleId, "tenant-a", "user-1", "CreateOrderCommand",
            SampleTimestamp, 42, AuditOutcome.Success, null, prevHash: null);
        var b = AuditContentHasher.Compute(
            SampleId, "tenant-a", "user-1", "CreateOrderCommand",
            SampleTimestamp, 42, AuditOutcome.Success, null, prevHash: null);

        a.Should().Be(b);
    }

    [Fact]
    public void Compute_Changes_With_Any_Field_Mutation()
    {
        var baseline = AuditContentHasher.Compute(
            SampleId, "tenant-a", "user-1", "CreateOrderCommand",
            SampleTimestamp, 42, AuditOutcome.Success, null, prevHash: "abc");

        AuditContentHasher.Compute(SampleId, "tenant-b", "user-1", "CreateOrderCommand",
            SampleTimestamp, 42, AuditOutcome.Success, null, prevHash: "abc")
            .Should().NotBe(baseline, "tenant change must alter hash");

        AuditContentHasher.Compute(SampleId, "tenant-a", "user-2", "CreateOrderCommand",
            SampleTimestamp, 42, AuditOutcome.Success, null, prevHash: "abc")
            .Should().NotBe(baseline, "actor change must alter hash");

        AuditContentHasher.Compute(SampleId, "tenant-a", "user-1", "CancelOrderCommand",
            SampleTimestamp, 42, AuditOutcome.Success, null, prevHash: "abc")
            .Should().NotBe(baseline, "command-name change must alter hash");

        AuditContentHasher.Compute(SampleId, "tenant-a", "user-1", "CreateOrderCommand",
            SampleTimestamp, 43, AuditOutcome.Success, null, prevHash: "abc")
            .Should().NotBe(baseline, "latency change must alter hash");

        AuditContentHasher.Compute(SampleId, "tenant-a", "user-1", "CreateOrderCommand",
            SampleTimestamp, 42, AuditOutcome.Exception, null, prevHash: "abc")
            .Should().NotBe(baseline, "outcome change must alter hash");

        AuditContentHasher.Compute(SampleId, "tenant-a", "user-1", "CreateOrderCommand",
            SampleTimestamp, 42, AuditOutcome.Success, null, prevHash: "different-prev")
            .Should().NotBe(baseline, "prev-hash change must alter hash (chain tamper-evidence)");
    }

    [Fact]
    public void Compute_Format_Is_64_Char_Lowercase_Hex()
    {
        var hash = AuditContentHasher.Compute(
            SampleId, "t", "u", "C", SampleTimestamp, 1, AuditOutcome.Success, null, null);

        hash.Length.Should().Be(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_Null_Prev_And_Empty_Prev_Are_Equivalent()
    {
        var withNull = AuditContentHasher.Compute(
            SampleId, "t", "u", "C", SampleTimestamp, 1, AuditOutcome.Success, null, prevHash: null);
        var withEmpty = AuditContentHasher.Compute(
            SampleId, "t", "u", "C", SampleTimestamp, 1, AuditOutcome.Success, null, prevHash: "");

        withNull.Should().Be(withEmpty,
            "null prev-hash (first entry) is canonicalised as empty string");
    }

    [Fact]
    public void Compute_Throws_For_Empty_CommandName()
    {
        Action act = () => AuditContentHasher.Compute(
            SampleId, "t", "u", "", SampleTimestamp, 1, AuditOutcome.Success, null, null);
        act.Should().Throw<ArgumentException>();
    }
}
