using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Identity.Tests;

/// <summary>Helpers for generating in-memory RSA signing keys shared between the issuer and the validator.</summary>
internal static class TestKeys
{
    public static RsaSecurityKey CreateRsaKey(string? keyId = null)
    {
        var rsa = RSA.Create(2048);
        return new RsaSecurityKey(rsa) { KeyId = keyId ?? Guid.NewGuid().ToString("N") };
    }
}
