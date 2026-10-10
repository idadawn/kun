using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kun.Gateway.Tests;

/// <summary>Explicit canonical identity fields for successful issuance fixtures; negative contract cases remove individual claims.</summary>
internal static class S2sPrincipalFixture
{
    internal static ClaimsPrincipal Complete(ClaimsPrincipal principal)
    {
        var identity = principal.Identities.Single();
        if (!identity.HasClaim(claim => claim.Type == "principal_kind")) identity.AddClaim(new Claim("principal_kind", "user"));
        if (!identity.HasClaim(claim => claim.Type == "tenant_id")) identity.AddClaim(new Claim("tenant_id", "", JsonClaimValueTypes.JsonNull));
        if (!identity.HasClaim(claim => claim.Type == "session_id")) identity.AddClaim(new Claim("session_id", "00000000-0000-4000-8000-000000000100"));
        if (!identity.HasClaim(claim => claim.Type == "membership_id")) identity.AddClaim(new Claim("membership_id", "00000000-0000-4000-8000-000000000200"));
        if (!identity.HasClaim(claim => claim.Type == "identity_revision")) identity.AddClaim(new Claim("identity_revision", "0", ClaimValueTypes.Integer64));
        return principal;
    }
}
