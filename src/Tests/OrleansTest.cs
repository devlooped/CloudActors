using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Devlooped.CloudActors;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.TestingHost;

public class ClusterFixture : IDisposable
{
    public ClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public void Dispose() => Cluster.StopAllSilos();

    public TestCluster Cluster { get; }

    class TestSiloConfigurations : ISiloConfigurator
    {
        public void Configure(ISiloBuilder builder)
        {
            builder.Services.AddSingleton(CloudStorageAccount.DevelopmentStorageAccount);
            builder.Services.AddSingleton<IGrainStorage>(sp => sp.GetServiceByName<IGrainStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME));
            builder.Services.AddSingletonNamedService<IGrainStorage, StreamstoneStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
            builder.AddCloudActors();
        }
    }

    class MemoryStorage : IGrainStorage
    {
        ConcurrentDictionary<GrainId, object> memory = new();

        public MemoryStorage()
        {
                
        }

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