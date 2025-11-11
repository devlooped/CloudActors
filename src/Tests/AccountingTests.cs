using System;
using System.Net;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Storage;
using TestDomain;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Tests;

public class TestAccounts : IAsyncDisposable
{
    public TestAccounts() => CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTable(nameof(Account));

    public async ValueTask DisposeAsync() => await CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTableAsync(nameof(Account));

    [Fact]
    public void ExtendOnRaised()
    {
        var account = new Account("account/1");
        account.Deposit(new(100));
        Assert.Single(account.Raised);
        Assert.Contains(typeof(Deposited), account.Raised);
    }

    [Fact]
    public async Task HostedGrain()
    {
        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();

            await bus.ExecuteAsync("account/1", new Deposit(100));
            await bus.ExecuteAsync("account/1", new Withdraw(50));

            var balance = await bus.QueryAsync("account/1", GetBalance.Default);
            Assert.Equal(50, balance);

            Assert.Equal(50, await bus.ExecuteAsync("account/1", new Close(CloseReason.Customer)));

            Assert.Equal(0, await bus.QueryAsync("account/1", GetBalance.Default));
        }

        // Force re-activation of grain.
        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            Assert.Equal(0, await bus.QueryAsync("account/1", GetBalance.Default));
        }
    }

    [Fact]
    public async Task WebAppHosting()
    {
        var siloPort = 11111;
        int gatewayPort = 30000;
        var siloAddress = IPAddress.Loopback;

        var host = Host
            .CreateDefaultBuilder()
            .UseOrleans((ctx, builder) => builder
                .Configure<ClusterOptions>(options => options.ClusterId = "TEST")
                .UseDevelopmentClustering(options => options.PrimarySiloEndpoint = new IPEndPoint(siloAddress, siloPort))
                .ConfigureEndpoints(siloAddress, siloPort, gatewayPort)
                .AddStreamstoneActorStorageAsDefault(opt => opt.AutoSnapshot = true)
            )
            .ConfigureServices(services =>
            {
                services.AddSingleton(CloudStorageAccount.DevelopmentStorageAccount);
                services.AddCloudActors();
            }).Build();

        host.Start();

        var bus = host.Services.GetRequiredService<IActorBus>();
        var storage = host.Services.GetRequiredKeyedService<IGrainStorage>("Default");

        await bus.ExecuteAsync("account/1", new Deposit(100));
        var actor = await storage.ReadActorAsync<Account>("account/1");
        Assert.Equal(100, actor.Balance);

        await bus.ExecuteAsync("account/1", new Withdraw(50));

        var balance = await bus.QueryAsync("account/1", GetBalance.Default);
        Assert.Equal(50, balance);

        actor = await storage.ReadActorAsync<Account, Account.ActorState>("account/1");
        Assert.Equal(50, actor.Balance);

        Assert.Equal(50, await bus.ExecuteAsync("account/1", new Close()));
        Assert.Equal(0, await bus.QueryAsync("account/1", GetBalance.Default));

        actor = await storage.ReadActorAsync<Account>("account/1");
        Assert.Equal(0, actor.Balance);

        await host.StopAsync();
    }
}