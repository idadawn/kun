using FluentAssertions;
using Kun.Core.Security;
using Xunit;

namespace Kun.Core.Tests.Security;

public sealed class CurrentUserPrincipalKindTests
{
    [Fact]
    public void PrincipalKind_LegacyImplementation_DefaultsToUser()
    {
        ICurrentUser legacy = new LegacyUser();
        legacy.PrincipalKind.Should().Be("user");
        AnonymousCurrentUser.Instance.PrincipalKind.Should().BeEmpty();
    }
    private sealed class LegacyUser : ICurrentUser
    {
        public string? UserId => "legacy";
        public IReadOnlyList<string> Roles => [];
        public bool IsAuthenticated => true;
    }
}
