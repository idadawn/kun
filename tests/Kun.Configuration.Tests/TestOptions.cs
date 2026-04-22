using Kun.Configuration.Attributes;

namespace Kun.Configuration.Tests;

public sealed class AuthentikOptions
{
    [RequiredConfig]
    [UrlConfig]
    public string Authority { get; set; } = string.Empty;

    [RequiredConfig]
    [NonEmptyStringConfig]
    public string ClientId { get; set; } = string.Empty;

    [RequiredConfig]
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class NestedDatabaseOptions
{
    [RequiredConfig]
    [NonEmptyStringConfig]
    public string ConnectionString { get; set; } = string.Empty;

    [UrlConfig]
    public string? DashboardUrl { get; set; }
}

public sealed class AggregateOptions
{
    [RequiredConfig]
    public string First { get; set; } = string.Empty;

    [RequiredConfig]
    public string Second { get; set; } = string.Empty;

    [NonEmptyStringConfig]
    public string Third { get; set; } = string.Empty;
}
