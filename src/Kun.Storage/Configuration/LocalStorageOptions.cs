namespace Kun.Storage.Configuration;

/// <summary>Options for <c>LocalFileSystemStorage</c> (development backend).</summary>
public sealed class LocalStorageOptions
{
    /// <summary>
    /// Root directory under which objects are stored. Relative paths are
    /// resolved against the host's current directory; the full path is
    /// created on first use.
    /// </summary>
    public string RootPath { get; set; } = "./storage";
}
