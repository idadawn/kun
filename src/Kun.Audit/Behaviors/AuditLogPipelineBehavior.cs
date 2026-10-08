using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Kun.Audit.DependencyInjection;
using Kun.Core.Cqrs;
using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Audit.Behaviors;

/// <summary>
/// Mediator pipeline behaviour that records one <see cref="AuditEntry"/> per handler
/// invocation and appends it to the per-tenant hash chain. Chain-state mutation and
/// the <see cref="IAuditSink"/> write are serialised inside
/// <see cref="IAuditChainState.AppendAsync"/> so concurrent commands on the same
/// tenant cannot fork the chain.
/// </summary>
/// <remarks>
/// <para>
/// Failure handling: if the handler throws, the original exception is preserved and
/// rethrown *after* the audit write so the audit row exists and records
/// <see cref="AuditOutcome.Exception"/>. If the audit write itself fails (sink error,
/// chain-state lock abandoned), the failure is logged at warning level and swallowed
/// — audit must never convert a successful business outcome into an error nor replace
/// the handler's own exception with an audit-pipeline one.
/// </para>
/// <para>
/// Registered explicitly per command type via
/// <see cref="DependencyInjection.KunAuditServiceCollectionExtensions.AddAuditBehavior{TRequest, TResponse}"/>
/// so queries are not audited by default.
/// </para>
/// </remarks>
public sealed partial class AuditLogPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly KunAuditOptions _options = new();
    private readonly IAuditSink _sink;
    private readonly IAuditChainState _chain;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuditLogPipelineBehavior<TRequest, TResponse>> _logger;

    /// <summary>Initialises a new <see cref="AuditLogPipelineBehavior{TRequest, TResponse}"/>.</summary>
    public AuditLogPipelineBehavior(
        IAuditSink sink,
        IAuditChainState chain,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        TimeProvider clock,
        ILogger<AuditLogPipelineBehavior<TRequest, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _sink = sink;
        _chain = chain;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>从宿主审计选项选择 user、service、agent 或 system 主体类型。</summary>
    public AuditLogPipelineBehavior(IAuditSink sink, IAuditChainState chain, ITenantContext tenantContext,
        ICurrentUser currentUser, TimeProvider clock, ILogger<AuditLogPipelineBehavior<TRequest, TResponse>> logger,
        IOptions<KunAuditOptions> options) : this(sink, chain, tenantContext, currentUser, clock, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var id = Guid.NewGuid();
        var occurredAt = _clock.GetUtcNow();
        var commandName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        TResponse? response = default;
        var outcome = AuditOutcome.Success;
        string? errorCode = null;
        Exception? captured = null;

        try
        {
            response = await next().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            captured = ex;
            outcome = AuditOutcome.Exception;
            errorCode = ex.GetType().Name;
        }

        sw.Stop();
        var latencyMs = sw.ElapsedMilliseconds;

        try
        {
            await _chain.AppendAsync(
                _tenantContext.TenantId,
                async prev =>
                {
                    var entry = new AuditEntry(
                        id,
                        _tenantContext.TenantId,
                        _currentUser.UserId,
                        commandName,
                        occurredAt,
                        latencyMs,
                        outcome,
                        errorCode,
                        prev,
                        string.Empty)
                    {
                        ActorType = _currentUser.IsAuthenticated ? _options.DefaultActorType : _options.AnonymousActorType
                    };
                    var hash = AuditContentHasher.Compute(entry);
                    entry = entry with { ContentHash = hash };

                    await _sink.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
                    return hash;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception auditEx)
        {
            // Audit-side failure must never replace a business exception or convert
            // a successful handler into a failure. Log and continue.
            LogAuditWriteFailed(_logger, commandName, auditEx);
        }

        if (captured is not null)
        {
            ExceptionDispatchInfo.Capture(captured).Throw();
        }

        return response!;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Kun.Audit failed to record audit entry for {CommandName}; business outcome preserved.")]
    private static partial void LogAuditWriteFailed(ILogger logger, string commandName, Exception ex);
}
