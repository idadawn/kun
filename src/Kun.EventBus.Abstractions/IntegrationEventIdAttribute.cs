namespace Kun.EventBus;

/// <summary>
/// Declares the stable event-type identifier (token) for an integration event CLR type.
/// Use with <see cref="IEventTypeRegistrar.Register{TEvent}()"/> so the token lives next
/// to the type definition rather than being repeated in DI wiring.
/// </summary>
/// <remarks>
/// The token shape follows the framework convention
/// <c>{bounded-context}.{event-name}.v{version}</c>, e.g. <c>order.placed.v1</c>.
/// Applying this attribute does <em>not</em> auto-register the type — registration
/// is still explicit via <see cref="IEventTypeRegistrar"/>. AOT-strict consumers that
/// must avoid reflection at startup can keep using
/// <see cref="IEventTypeRegistrar.Register{TEvent}(string)"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class IntegrationEventIdAttribute : Attribute
{
    /// <summary>Initializes a new attribute with the given stable token.</summary>
    /// <param name="eventType">The stable event-type token, e.g. <c>order.placed.v1</c>.</param>
    public IntegrationEventIdAttribute(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        EventType = eventType;
    }

    /// <summary>The stable event-type token declared by this attribute.</summary>
    public string EventType { get; }
}
