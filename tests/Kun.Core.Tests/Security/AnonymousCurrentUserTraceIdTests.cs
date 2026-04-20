using FluentAssertions;
using Kun.Core.Security;
using Xunit;

namespace Kun.Core.Tests.Security;

/// <summary>
/// Proves the deliberate design choice from P0-2 (commit 4564278): the
/// anonymous singleton does NOT implement <see cref="ITracedCurrentUser"/>.
/// Only authenticated, request-scoped users carry a trace context; background
/// flows that resolve the anonymous singleton should see a null cast.
/// </summary>
public sealed class AnonymousCurrentUserTraceIdTests
{
    [Fact]
    public void AnonymousCurrentUser_DoesNotImplement_ITracedCurrentUser()
    {
        ICurrentUser user = AnonymousCurrentUser.Instance;

        (user is ITracedCurrentUser).Should().BeFalse();
    }

    [Fact]
    public void AnonymousCurrentUser_CastToTracedInterface_ReturnsNull()
    {
        ICurrentUser user = AnonymousCurrentUser.Instance;

        var traced = user as ITracedCurrentUser;
        traced.Should().BeNull();
    }
}
