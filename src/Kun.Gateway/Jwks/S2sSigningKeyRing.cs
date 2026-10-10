using System.Security.Cryptography;
using Kun.Gateway.Configuration;
using Kun.Gateway.Endpoints;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Gateway.Jwks;

/// <summary>签发和 JWKS 共用的热轮换状态；旧项只保留公钥，不保留旧私钥。</summary>
internal sealed class S2sSigningKeyRing : IDisposable
{
    private static readonly Action<ILogger, Exception?> s_rejected = LoggerMessage.Define(LogLevel.Warning, new EventId(110, "SigningKeyReloadRejected"), "Signing key reload rejected; retaining the last valid signing key.");
    private readonly object _gate = new();
    private readonly KunGatewayOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<S2sSigningKeyRing> _logger;
    private readonly List<(JwkDto Key, DateTimeOffset Until)> _retired = [];
    private Key? _current;
    private string? _fingerprint;
    private DateTimeOffset _nextCheck;
    private bool _disposed;

    public S2sSigningKeyRing(IOptions<KunGatewayOptions> options, IHostEnvironment environment, TimeProvider clock, ILogger<S2sSigningKeyRing> logger)
    {
        _options = options.Value; _clock = clock; _logger = logger;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SigningKeyPath);
        if (_options.KeyOverlapPeriod < TimeSpan.Zero || _options.KeyReloadInterval < TimeSpan.Zero)
            throw new InvalidOperationException("Key rotation intervals cannot be negative.");
        if (!File.Exists(_options.SigningKeyPath) && environment.IsDevelopment())
        {
            using var key = RSA.Create(2048);
            Write(_options.SigningKeyPath, key.ExportRSAPrivateKeyPem());
            Write(_options.PublicKeyPath, key.ExportRSAPublicKeyPem());
        }
        if (!File.Exists(_options.SigningKeyPath))
            throw new InvalidOperationException("S2sTokenIssuer: signing key not found. Production deployments must pre-provision the RSA key pair; Development can generate it.");
        Reload();
    }

    internal string CreateToken(SecurityTokenDescriptor descriptor)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Reload();
            descriptor.SigningCredentials = _current!.Credentials;
            return new JsonWebTokenHandler().CreateToken(descriptor);
        }
    }

    internal JwksResponse PublicKeys()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Reload();
            return new JwksResponse { Keys = _retired.Select(k => k.Key).Append(_current!.Public).ToArray() };
        }
    }

    private void Reload()
    {
        var now = _clock.GetUtcNow();
        _retired.RemoveAll(key => key.Until <= now);
        if (_current is not null && now < _nextCheck) return;
        _nextCheck = now + _options.KeyReloadInterval;
        RSA? candidate = null;
        try
        {
            var info = new FileInfo(_options.SigningKeyPath);
            if (!info.Exists || info.Length > 65_536) throw new IOException("Signing key is missing or too large.");
            var bytes = File.ReadAllBytes(_options.SigningKeyPath);
            try
            {
                var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
                if (fingerprint == _fingerprint) return;
                candidate = RSA.Create();
                var characters = System.Text.Encoding.UTF8.GetChars(bytes);
                try { candidate.ImportFromPem(characters); }
                finally { Array.Clear(characters); }
                if (candidate.KeySize < 2048) throw new CryptographicException("RSA signing key is too small.");
                // ImportFromPem 也接受公钥；必须实际具备私钥才能替换当前签发状态。
                var privateParameters = candidate.ExportParameters(true);
                Clear(privateParameters);
                var key = new RsaSecurityKey(candidate)
                {
                    KeyId = JwksResponseBuilder.GetKeyId(candidate),
                    CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
                };
                var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(candidate.ExportParameters(false)));
                jwk.KeyId = key.KeyId; jwk.Use = "sig"; jwk.Alg = SecurityAlgorithms.RsaSha256;
                if (_current?.Public.Kid == key.KeyId) { _fingerprint = fingerprint; return; }
                _retired.RemoveAll(old => old.Key.Kid == key.KeyId);
                var previous = _current;
                if (previous is not null && _options.KeyOverlapPeriod > TimeSpan.Zero) _retired.Add((previous.Public, now + _options.KeyOverlapPeriod));
                _current = new Key(candidate, new SigningCredentials(key, SecurityAlgorithms.RsaSha256), new JwkDto(jwk));
                candidate = null; _fingerprint = fingerprint;
                previous?.Rsa.Dispose();
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception error) when (_current is not null && error is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            // 不记录异常文本，避免密钥解析器把输入内容带入日志。
            s_rejected(_logger, null);
        }
        finally { candidate?.Dispose(); }
    }

    private static void Clear(RSAParameters parameters)
    {
        foreach (var value in new[] { parameters.D, parameters.DP, parameters.DQ, parameters.InverseQ, parameters.P, parameters.Q })
            if (value is not null) CryptographicOperations.ZeroMemory(value);
    }

    private static void Write(string path, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, value);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _current?.Rsa.Dispose(); _retired.Clear(); }
    }

    private sealed record Key(RSA Rsa, SigningCredentials Credentials, JwkDto Public);
}

internal sealed class GatewaySigningHealthCheck(S2sSigningKeyRing keys) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(keys.PublicKeys().Keys.Count > 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("No signing key is available."));
}
