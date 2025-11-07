using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.TestingHost;

public class ClusterFixture : IDisposable
{
    public static TestCluster CreateCluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        builder.AddClientBuilderConfigurator<ActorBusConfigurator>();

        // Cloud actor (instantiation) is only needed on the silo-side.
        // The UseCloudActors already configures the actor bus, so we don't need 
        // that on the silo side.
        builder.AddSiloBuilderConfigurator<AddCloudActorsConfigurator>();

        var cluster = builder.Build();
        cluster.Deploy();
        return cluster;
    }

    public ClusterFixture() => Cluster = CreateCluster();

    public void Dispose() => Cluster.StopAllSilos();

    public TestCluster Cluster { get; }

    class AddCloudActorsConfigurator : IHostConfigurator
    {
        public void Configure(IHostBuilder builder) => builder.ConfigureServices(services =>
        {
            services.AddCloudActors();
        });
    }

    class ActorBusConfigurator : IHostConfigurator
    {
        // Adds IActorBus default implementation
        public void Configure(IHostBuilder builder) => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IActorBus>(sp => new OrleansActorBus(sp.GetRequiredService<IGrainFactory>()));
        });
    }

    class TestSiloConfigurations : ISiloConfigurator
    {
        public void Configure(ISiloBuilder builder)
        {
            builder.Services.AddSingleton(CloudStorageAccount.DevelopmentStorageAccount);
            //builder.Services.AddSingleton<IGrainStorage>(sp => sp.GetServiceByName<IGrainStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME));
            //builder.Services.AddSingletonNamedService<IGrainStorage, StreamstoneStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
            builder.AddStreamstoneActorStorage();
        }
    }

    // Alternative to StreamstoneStorage
    class MemoryStorage : IGrainStorage
    {
        ConcurrentDictionary<GrainId, object> memory = new();

        public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
            => memory.TryRemove(grainId, out _) ? Task.CompletedTask : Task.CompletedTask;

        public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            grainState.State = (T)memory.GetOrAdd(grainId, id => Activator.CreateInstance(typeof(T), new object[] { grainId.Key.ToString()! })!);

            if (grainState.State is IEventSourced sourced)
                sourced.LoadEvents(Array.Empty<object>());

            return Task.CompletedTask;
        }

        public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            memory[grainId] = grainState.State!;

            if (grainState.State is IEventSourced sourced)
                sourced.AcceptEvents();

            return Task.CompletedTask;
        }
    }
}

[CollectionDefinition(ClusterCollection.Name)]
public class ClusterCollection : ICollectionFixture<ClusterFixture>
{
    public const string Name = "ClusterCollection";
}