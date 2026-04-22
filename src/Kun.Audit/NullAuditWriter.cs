using Kun.Audit.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kun.Audit;

/// <summary>
/// <see cref="IAuditWriter"/> that silently drops every entry. Registered as
/// the fallback when no concrete writer has been configured so that
/// interceptor / pipeline wiring stays safe in tests and samples.
/// </summary>
/// <remarks>
/// Emits a <see cref="LogLevel.Warning"/> on the first call so operators
/// notice missing configuration in production. Subsequent calls are silent
/// — at-most-once warning avoids log-flooding.
/// </remarks>
public sealed partial class NullAuditWriter : IAuditWriter
{
    private readonly ILogger<NullAuditWriter> _logger;
    private int _warnedOnce;

    /// <summary>Creates a new <see cref="NullAuditWriter"/>.</summary>
    public NullAuditWriter(ILogger<NullAuditWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditLog entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
        {
            Log.DroppingAuditEntries(_logger);
        }
        return Task.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Kun.Audit is wired but no concrete IAuditWriter is registered — audit entries are being dropped. Call services.AddKunAudit().UseEventBus() or register Kun.Audit.EntityFrameworkCore.")]
        public static partial void DroppingAuditEntries(ILogger logger);
    }
}
