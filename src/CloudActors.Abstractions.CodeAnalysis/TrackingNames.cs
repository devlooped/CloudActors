namespace Devlooped.CloudActors;

/// <summary>
/// Constants for pipeline step tracking names used by all generators.
/// Enables incrementality testing and debugging.
/// </summary>
static class TrackingNames
{
    // Shared discovery steps
    public const string Actors = nameof(Actors);
    public const string ActorMessages = nameof(ActorMessages);

    // ActorBusOverloadGenerator
    public const string BusOverloads = nameof(BusOverloads);
    public const string BusInterfaces = nameof(BusInterfaces);

    // ActorIdBusOverloadGenerator
    public const string ActorIdOverloads = nameof(ActorIdOverloads);
    public const string ParsableType = nameof(ParsableType);

    // ActorMessageGenerator
    public const string MessageModels = nameof(MessageModels);
    public const string AdditionalTypes = nameof(AdditionalTypes);

    // ActorPrimitiveIdGenerator
    public const string PrimitiveIdModels = nameof(PrimitiveIdModels);

    // ActorStateGenerator
    public const string StateModels = nameof(StateModels);

    // EventSourcedGenerator
    public const string EventSourcedModels = nameof(EventSourcedModels);

    // ActorGrainGenerator
    public const string GrainModels = nameof(GrainModels);

    // ActorIdFactoryGenerator
    public const string ActorIdEntries = nameof(ActorIdEntries);

    // ActorsAssemblyGenerator
    public const string CloudActorAssemblies = nameof(CloudActorAssemblies);

    // CloudActorsAttributeGenerator
    public const string CloudActorsAttribute = nameof(CloudActorsAttribute);

    // OrleansConfig
    public const string OrleansConfig = nameof(OrleansConfig);
}
