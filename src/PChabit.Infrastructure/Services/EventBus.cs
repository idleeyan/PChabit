using System.Collections.Concurrent;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, object> _handlers = new();
    
    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
    {
        var handlers = GetOrCreateHandlers<TEvent>();
        lock (handlers)
        {
            handlers.Add(handler);
        }
    }
    
    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var handlersObj))
        {
            var handlers = (List<Action<TEvent>>)handlersObj;
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        }
    }
    
    public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var handlersObj))
        {
            var handlers = (List<Action<TEvent>>)handlersObj;
            Action<TEvent>[] handlersCopy;
            lock (handlers)
            {
                handlersCopy = handlers.ToArray();
            }
            
            foreach (var handler in handlersCopy)
            {
                try
                {
                    handler(@event);
                }
                catch
                {
                }
            }
        }
    }
    
    private List<Action<TEvent>> GetOrCreateHandlers<TEvent>() where TEvent : IEvent
    {
        return (List<Action<TEvent>>)_handlers.GetOrAdd(typeof(TEvent), _ => new List<Action<TEvent>>());
    }
}
