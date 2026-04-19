using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.Jwks;

/// <summary>
/// Loads a JSON Web Key Set from a local file and caches it in memory.
/// The cache is invalidated when the file's last-write time changes, enabling
/// key rotation without service restart.
/// </summary>
public sealed class FileKunJwksProvider : IKunJwksProvider
{
    private static readonly Action<ILogger, string, Exception?> s_reloadLog =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, "JwksReload"),
            "Reloading JWKS from {FilePath} (mtime changed)");

    private readonly string _filePath;
    private readonly ILogger<FileKunJwksProvider> _logger;

    private JsonWebKeySet? _cached;
    private DateTime _cachedMtime;
    private readonly object _lock = new();

    /// <summary>Initializes the provider with the path to the JWKS JSON file.</summary>
    /// <param name="filePath">Absolute path to the JWKS JSON file.</param>
    /// <param name="logger">Logger for cache reload diagnostics.</param>
    public FileKunJwksProvider(string filePath, ILogger<FileKunJwksProvider> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<JsonWebKeySet> GetAsync(CancellationToken ct = default)
    {
        var mtime = File.GetLastWriteTimeUtc(_filePath);

        lock (_lock)
        {
            if (_cached is not null && mtime == _cachedMtime)
                return Task.FromResult(_cached);

            s_reloadLog(_logger, _filePath, null);
            var json = File.ReadAllText(_filePath);
            var keySet = new JsonWebKeySet(json);
            _cached = keySet;
            _cachedMtime = mtime;
            return Task.FromResult(keySet);
        }
    }
}
