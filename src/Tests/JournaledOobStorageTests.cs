using System;
using System.Threading.Tasks;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.TestingHost;
using TestDomain;

namespace Tests;

/// <summary>
/// Tests for [Journaled] actors using Orleans out-of-the-box log consistency providers
/// (StateStorage and LogStorage) backed by Microsoft.Orleans.Persistence.Memory.
/// </summary>
[Collection(nameof(MemoryClusterCollection))]
public class JournaledOobStorageTests(MemoryClusterFixture fixture)
{
    IActorBus Bus => fixture.Bus;
    TestCluster Cluster => fixture.Cluster;

    [Fact]
    public async Task StateStorage_RoundTrip()
    {
        await Bus.ExecuteAsync(StateStorageJournaledAccount.NewId("ss1"), new Deposit(100));
        await Bus.ExecuteAsync(StateStorageJournaledAccount.NewId("ss1"), new Withdraw(40));

        Assert.Equal(60, await Bus.QueryAsync(StateStorageJournaledAccount.NewId("ss1"), GetBalance.Default));
    }

    [Fact]
    public async Task StateStorage_PersistsAcrossReactivation()
    {
        await Bus.ExecuteAsync(StateStorageJournaledAccount.NewId("ss2"), new Deposit(200));
        await Bus.ExecuteAsync(StateStorageJournaledAccount.NewId("ss2"), new Withdraw(75));
        Assert.Equal(125, await Bus.QueryAsync(StateStorageJournaledAccount.NewId("ss2"), GetBalance.Default));

        // Force all grains to deactivate so the next call re-activates from persisted state.
        await Cluster.Client.GetGrain<IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);

        // StateStorage re-activation loads the view snapshot directly.
        Assert.Equal(125, await Bus.QueryAsync(StateStorageJournaledAccount.NewId("ss2"), GetBalance.Default));
    }

    [Fact]
    public async Task StateStorage_InsufficientFunds_Throws()
    {
        await Bus.ExecuteAsync(StateStorageJournaledAccount.NewId("ss3"), new Deposit(50));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Bus.ExecuteAsync(StateStorageJournaledAccount.NewId("ss3"), new Withdraw(100)));
    }

    [Fact]
    public async Task LogStorage_RoundTrip()
    {
        await Bus.ExecuteAsync(LogStorageJournaledAccount.NewId("ls1"), new Deposit(100));
        await Bus.ExecuteAsync(LogStorageJournaledAccount.NewId("ls1"), new Withdraw(40));

        Assert.Equal(60, await Bus.QueryAsync(LogStorageJournaledAccount.NewId("ls1"), GetBalance.Default));
    }

    [Fact]
    public async Task LogStorage_PersistsAcrossReactivation()
    {
        // LogStorage stores the full event log and replays it via TransitionState on re-activation.
        await Bus.ExecuteAsync(LogStorageJournaledAccount.NewId("ls2"), new Deposit(300));
        await Bus.ExecuteAsync(LogStorageJournaledAccount.NewId("ls2"), new Deposit(50));
        await Bus.ExecuteAsync(LogStorageJournaledAccount.NewId("ls2"), new Withdraw(100));
        Assert.Equal(250, await Bus.QueryAsync(LogStorageJournaledAccount.NewId("ls2"), GetBalance.Default));

        // Force all grains to deactivate so the next call re-activates from the persisted event log.
        await Cluster.Client.GetGrain<IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);

        Assert.Equal(250, await Bus.QueryAsync(LogStorageJournaledAccount.NewId("ls2"), GetBalance.Default));
    }

    [Fact]
    public async Task LogStorage_InsufficientFunds_Throws()
    {
        await Bus.ExecuteAsync(LogStorageJournaledAccount.NewId("ls3"), new Deposit(50));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Bus.ExecuteAsync(LogStorageJournaledAccount.NewId("ls3"), new Withdraw(100)));
    }
}

[CollectionDefinition(nameof(MemoryClusterCollection))]
public class MemoryClusterCollection : ICollectionFixture<MemoryClusterFixture> { }

public class MemoryClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }
    public IActorBus Bus { get; }

    public MemoryClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<MemorySiloConfigurator>();
        builder.AddSiloBuilderConfigurator<AddCloudActorsConfigurator>();
        builder.AddClientBuilderConfigurator<ActorBusConfigurator>();

        Cluster = builder.Build();
        Cluster.Deploy();
        Bus = Cluster.ServiceProvider.GetRequiredService<IActorBus>();
    }

    public void Dispose() => Cluster.StopAllSilos();

    class MemorySiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder builder) => builder
            .AddMemoryGrainStorageAsDefault()
            .AddStateStorageBasedLogConsistencyProvider("StateStorage")
            .AddLogStorageBasedLogConsistencyProvider("LogStorage");
    }

    class AddCloudActorsConfigurator : IHostConfigurator
    {
        public void Configure(IHostBuilder builder) => builder.ConfigureServices(services =>
            services.AddCloudActors());
    }

    class ActorBusConfigurator : IHostConfigurator
    {
        public void Configure(IHostBuilder builder) => builder.ConfigureServices(services =>
            services.AddSingleton<IActorBus>(sp => new OrleansActorBus(sp.GetRequiredService<IGrainFactory>())));
    }
}
