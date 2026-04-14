using System;
using System.ComponentModel;

namespace Devlooped.CloudActors;

/// <summary>
/// Creates actor ID values from a string grain key.
/// </summary>
public interface IActorIdFactory
{
    /// <summary>
    /// Creates an actor ID value for the given actor type from a string key.
    /// </summary>
    object Create(Type actorType, string key);
}

/// <summary>
/// Provides access to the compile-time generated <see cref="IActorIdFactory"/> 
/// implementation. Set automatically via <c>[ModuleInitializer]</c> by the 
/// source generator; consumers should not set this directly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ActorIdFactory
{
    /// <summary>
    /// Gets or sets the generated actor ID factory. When a source-generated 
    /// factory exists, it is set here via <c>[ModuleInitializer]</c> before 
    /// DI registration runs.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IActorIdFactory? Generated { get; set; }

    /// <summary>
    /// Returns the generated factory if available, otherwise the default 
    /// string pass-through factory.
    /// </summary>
    public static IActorIdFactory Default => Generated ?? DefaultActorIdFactory.Instance;

    /// <summary>
    /// Default factory that returns the string key as-is. Suitable for 
    /// string-keyed actors. For typed-ID actors, a source-generated 
    /// implementation is registered automatically via <see cref="ActorIdFactory"/>.
    /// </summary>
    sealed class DefaultActorIdFactory : IActorIdFactory
    {
        public static readonly IActorIdFactory Instance = new DefaultActorIdFactory();

        public object Create(Type actorType, string key) => key;
    }
}
