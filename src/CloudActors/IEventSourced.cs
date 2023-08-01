using System.Collections.Generic;

namespace Devlooped.CloudActors;

public interface IEventSourced
{
    IReadOnlyList<object> Events { get; }
    IReadOnlyList<object> History { get; }
    bool Empty { get; }
    bool ReadOnly { get; }
    int Version { get; }
    void AcceptEvents();
    void LoadEvents(IEnumerable<object> history);
}