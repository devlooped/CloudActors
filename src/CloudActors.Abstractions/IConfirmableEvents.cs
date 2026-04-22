using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Devlooped.CloudActors;

/// <summary>Provides an internal bridge for generated actors to await provider-specific event confirmation.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IConfirmableEvents
{
    /// <summary>Gets or sets the callback used by generated runtime code to await persistence confirmation.</summary>
    Func<Task>? ConfirmEventsCallback { get; set; }
}
