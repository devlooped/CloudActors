using System;
using System.ComponentModel;

namespace Devlooped.CloudActors;

[AttributeUsage(AttributeTargets.Class)]
public class ActorCommandAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class)]
public class ActorCommandAttribute<TResult> : ActorCommandAttribute
{
}

/// <summary>
/// Marker interface for void actor commands.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorCommand { }

/// <summary>
/// Marker interface for value-returning actor commands.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorCommand<out TResult> { }