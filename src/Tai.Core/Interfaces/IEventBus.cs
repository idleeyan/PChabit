namespace Tai.Core.Interfaces;

public interface IEventBus
{
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent;
    void Publish<TEvent>(TEvent @event) where TEvent : IEvent;
}

public interface IEvent
{
    DateTime Timestamp { get; }
}

public abstract class EventBase : IEvent
{
    public DateTime Timestamp { get; } = DateTime.Now;
}
