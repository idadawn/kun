using System.Security.Claims;
using Kun.RateLimit.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Kun.RateLimit.Internal;

/// <summary>Maps a <see cref="RateLimitPartitionKey"/> + <see cref="HttpContext"/> to the partition string the limiter buckets on.</summary>
internal static class PartitionKeyResolver
{
    public const string Anonymous = "anonymous";
    public const string Unknown = "unknown";

    public static string Resolve(RateLimitPartitionKey key, HttpContext context)
    {
        return key switch
        {
            RateLimitPartitionKey.Tenant => ResolveTenant(context),
            RateLimitPartitionKey.User => ResolveUser(context),
            RateLimitPartitionKey.Ip => ResolveIp(context),
            _ => Anonymous,
        };
    }

    private static string ResolveTenant(HttpContext context)
    {
        var claim = context.User.FindFirst("tenant_id")?.Value;
        return string.IsNullOrWhiteSpace(claim) ? Anonymous : claim;
    }

    private static string ResolveUser(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(sub) ? Anonymous : sub;
    }

    private static string ResolveIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? Unknown;
    }
}
