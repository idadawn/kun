namespace Kun.Audit;

/// <summary>
/// Well-known values for <see cref="AuditLog.ActorType"/>. These are
/// free-form strings by schema — the constants here are the vocabulary
/// the framework emits, but consumers may introduce additional actor
/// types (e.g. <c>"agent"</c>) without coordinating with Kun.
/// </summary>
public static class AuditActorType
{
    /// <summary>Action was attributed to a human end-user (resolved from <c>ICurrentUser.UserId</c>).</summary>
    public const string User = "user";

    /// <summary>Action originated inside the application process with no caller identity (boot-time migrations, scheduled jobs, …).</summary>
    public const string System = "system";

    /// <summary>Action was attributed to an inter-service call authenticated via the internal S2S JWT but not tied to a human user.</summary>
    public const string Service = "service";

    /// <summary>Attribution could not be determined — used when <c>ICurrentUser</c> reports unauthenticated and no system marker is available.</summary>
    public const string Unknown = "unknown";
}
