namespace Kun.Core.Security;

/// <summary>
/// <see cref="ICurrentUser"/> implementation used when no authentication context is available
/// (for instance, before S2S JWT validation or in background workers with no caller identity).
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    /// <summary>Singleton instance — <see cref="AnonymousCurrentUser"/> has no state.</summary>
    public static AnonymousCurrentUser Instance { get; } = new();

    private AnonymousCurrentUser() { }

    /// <inheritdoc />
    public string? UserId => null;

    /// <inheritdoc />
    public string PrincipalKind => string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<string> Roles { get; } = Array.Empty<string>();

    /// <inheritdoc />
    public bool IsAuthenticated => false;
}
