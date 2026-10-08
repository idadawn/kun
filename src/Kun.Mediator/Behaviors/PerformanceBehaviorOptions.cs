namespace Kun.Mediator.Behaviors;

/// <summary>慢命令告警的可配置阈值。</summary>
public sealed class PerformanceBehaviorOptions
{
    /// <summary>告警阈值毫秒数，默认 500；0 表示记录任何耗时为正的调用。</summary>
    public long ThresholdMs { get; set; } = 500;
}
