namespace Kun.EventBus.Outbox;

/// <summary>Appends outbox messages to the current unit-of-work boundary.</summary>
public interface IIntegrationEventOutbox
{
    /// <summary>Stages <paramref name="message"/> for atomic persistence with the business transaction.</summary>
    void Add(OutboxMessage message);

    /// <summary>Returns all staged messages pending flush.</summary>
    IReadOnlyList<OutboxMessage> GetPendingMessages();
}
