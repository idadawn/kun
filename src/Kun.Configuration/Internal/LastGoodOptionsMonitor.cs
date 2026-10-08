using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Kun.Configuration.Internal;

internal static class ConfigurationTelemetry
{
    private static readonly Meter Meter = new("Kun.Configuration");
    internal static readonly Counter<long> Rejected = Meter.CreateCounter<long>("kun.configuration.reload.rejected");
}

internal sealed partial class LastGoodOptionsMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(IOptionsFactory<T> factory,
    IEnumerable<IOptionsChangeTokenSource<T>> sources, ILogger<LastGoodOptionsMonitor<T>> logger)
    : IOptionsMonitor<T>, IDisposable where T : class
{
    private readonly object _gate = new();
    private readonly Dictionary<string, T> _values = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _subscriptions = [];
    private Action<T, string?>? _listeners;
    private bool _disposed;

    // DI 工厂显式调用一次，避免主构造器初始化期间捕获尚未就绪的实例。
    internal LastGoodOptionsMonitor<T> Start()
    {
        foreach (var source in sources)
            _subscriptions.Add(ChangeToken.OnChange(source.GetChangeToken, () => Reload(source.Name ?? Options.DefaultName)));
        return this;
    }

    public T CurrentValue => Get(Options.DefaultName);

    public T Get(string? name)
    {
        name ??= Options.DefaultName;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_values.TryGetValue(name, out var value)) _values[name] = value = factory.Create(name);
            return value;
        }
    }

    private void Reload(string name)
    {
        T candidate;
        Action<T, string?>? notify;
        lock (_gate)
        {
            if (_disposed) return;
            try { candidate = factory.Create(name); }
            catch (Exception error) when (error is OptionsValidationException or InvalidOperationException or FormatException or OverflowException)
            {
                ConfigurationTelemetry.Rejected.Add(1, new KeyValuePair<string, object?>("options.type", typeof(T).FullName),
                    new KeyValuePair<string, object?>("options.name", name));
                LogRejected(logger, typeof(T).Name, name);
                return;
            }
            _values[name] = candidate;
            notify = _listeners;
        }
        notify?.Invoke(candidate, name);
    }

    public IDisposable OnChange(Action<T, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_gate) _listeners += listener;
        return new Subscription(() => { lock (_gate) _listeners -= listener; });
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _listeners = null; }
        foreach (var subscription in _subscriptions) subscription.Dispose();
    }

    private sealed class Subscription(Action remove) : IDisposable
    {
        private Action? _remove = remove;
        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "配置 {OptionsType}/{OptionsName} 重载被拒绝，保留上一份有效值。")]
    private static partial void LogRejected(ILogger logger, string optionsType, string optionsName);
}
