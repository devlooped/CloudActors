using System;
using System.Collections.Generic;

namespace Devlooped.CloudActors;

public abstract class EventSourced : IEventSourced
{
    List<object>? events;
    List<object>? history;
    bool isReadOnly = true;
    int version;

    protected EventSourced() { }

    protected EventSourced(IEnumerable<object> history) => ((IEventSourced)this).LoadEvents(history);

    IReadOnlyList<object> IEventSourced.Events => events ??= new List<object>();

    IReadOnlyList<object> IEventSourced.History => history ??= new List<object>();

    bool IEventSourced.Empty => !(events?.Count > 0 || history?.Count > 0);

    bool IEventSourced.ReadOnly => isReadOnly;

    int IEventSourced.Version => version;

    void IEventSourced.AcceptEvents()
    {
        if (events is null)
            return;

        history ??= new List<object>();
        history.AddRange(events);
        version += events.Count;
        events.Clear();
    }

    void IEventSourced.LoadEvents(IEnumerable<object> history)
    {
        isReadOnly = false;
        foreach (var @event in history)
        {
            Apply(@event);
            (this.history ??= new List<object>()).Add(@event);
        }
        version = this.history?.Count ?? 0;
    }

    /// <summary>
    /// Applies an event to the current instance, typically changing its state.
    /// </summary>
    protected virtual void Apply(object @event)
    {
        // By default, we do dynamic dispatch to the generic Apply<T> method
        // but codegen will generate a direct call to the specific Apply method 
        // in both cases to avoid it.
        dynamic e = @event;
        Apply(e);
    }

    /// <summary>
    /// Raises and applies a new event of the specified type.
    /// See <see cref="Raise{T}(T)"/>.
    /// </summary>
    protected void Raise<T>() where T : notnull, new() => Raise(new T());

    /// <summary>
    /// Raises and applies an event.
    /// </summary>
    protected void Raise<T>(T @event) where T : notnull
    {
        if (isReadOnly)
            throw new InvalidOperationException();

        Apply(@event);

        (events ??= new List<object>()).Add(@event);
    }
}
