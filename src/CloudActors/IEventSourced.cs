using System.Collections.Generic;

namespace Devlooped.CloudActors;

/// <summary>
/// Interface implemented by actors that are event sourced.
/// </summary>
public interface IEventSourced
{
    /// <summary>
    /// Events produced by this actor so far.
    /// </summary>
    IReadOnlyList<object> Events { get; }
    /// <summary>
    /// Clears the pending events produced by this actor so far, typically done 
    /// by the storage provider after successufly persisting them.
    /// </summary>
    void AcceptEvents();
    /// <summary>
    /// Loads events from the specified history, typically done by the storage 
    /// provider on actor activation.
    /// </summary>
    void LoadEvents(IEnumerable<object> history);
}