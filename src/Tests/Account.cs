using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Devlooped.CloudActors;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Orleans;
using Orleans.Configuration;
using Orleans.Core;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Runtime.Development;
using Orleans.Storage;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Tests;

public class TestAccounts : IAsyncDisposable
{
    public TestAccounts() => CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .GetTableReference(nameof(Account))
        .DeleteIfExistsAsync()
        .Wait();

    public async ValueTask DisposeAsync() => await CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .GetTableReference(nameof(Account))
        .DeleteIfExistsAsync();

    [Fact]
    public async Task HostedGrain()
    {
        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();

            await bus.ExecuteAsync("account/1", new Deposit(100));
            await bus.ExecuteAsync("account/1", new Withdraw(50));

            var balance = await bus.QueryAsync("account/1", new GetBalance());
            Assert.Equal(50, balance);

            Assert.Equal(50, await bus.ExecuteAsync("account/1", new Close()));
            Assert.Equal(0, await bus.QueryAsync("account/1", new GetBalance()));
        }

        // Force re-activation of grain.
        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            Assert.Equal(0, await bus.QueryAsync("account/1", new GetBalance()));
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
                .AddCloudActors()
            )
            .ConfigureServices(services =>
            {
                services.AddSingleton(CloudStorageAccount.DevelopmentStorageAccount);
                services.AddSingleton<IGrainStorage>(sp => sp.GetServiceByName<IGrainStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME));
                services.AddSingletonNamedService<IGrainStorage, StreamstoneStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
                
                services.UseCloudActors();
            }).Build();

        //builder.Host.UseOrleans(silo =>
        //{
        //    silo.UseDevelopmentClustering(new IPEndPoint(IPAddress.Loopback, 1234))
        //    .ConfigureEndpoints(IPAddress.Loopback, 1234, 30000);
        //    silo.AddCloudActors();
        //});

        host.Start();

        //var app = builder.Build();
        //var task = Task.Run(app.Run);

        var bus = host.Services.GetRequiredService<IActorBus>();

        await bus.ExecuteAsync("account/1", new Deposit(100));
        await bus.ExecuteAsync("account/1", new Withdraw(50));

        var balance = await bus.QueryAsync("account/1", new GetBalance());
        Assert.Equal(50, balance);

        Assert.Equal(50, await bus.ExecuteAsync("account/1", new Close()));
        Assert.Equal(0, await bus.QueryAsync("account/1", new GetBalance()));
        
        await host.StopAsync();
    }
}

[GenerateSerializer]
public partial record Deposit(decimal Amount) : IActorCommand;

public partial record Deposited(decimal Amount);

[GenerateSerializer]
public partial record Withdraw(decimal Amount) : IActorCommand;

public partial record Withdrawn(decimal Amount);

[GenerateSerializer]
public partial record Close() : IActorCommand<decimal>;

public partial record Closed(decimal Balance);

[GenerateSerializer]
public partial record GetBalance() : IActorQuery<decimal>;

partial class Account
{
    protected Account(string id, decimal balance, bool isClosed)
        => (Id, Balance, IsClosed) = (id, balance, isClosed);
}

[Actor]
public partial class Account : IEventSourced
{
    public Account(string id) => Id = id;

    public string Id { get; }

    [JsonInclude]
    [JsonProperty]
    public decimal Balance { get; private set; }

    [JsonInclude]
    [JsonProperty]
    public bool IsClosed { get; private set; }

    // Showcases that operation can also be just Execute overloads 
    //public void Execute(Deposit command)
    //{
    //    // validate command
    //    Raise(new Deposited(command.Amount));
    //}
    //public void Execute(Withdraw command)
    //{
    //    // validate command
    //    Raise(new Withdraw(command.Amount));
    //}

    // Showcases that operations can have a name that's not Execute
    public void Deposit(Deposit command)
    {
        if (IsClosed)
            throw new InvalidOperationException("Account is closed.");

        // validate command
        Raise(new Deposited(command.Amount));
    }

    // Showcases that operations don't have to be async
    public void Withdraw(Withdraw command)
    {
        if (IsClosed)
            throw new InvalidOperationException("Account is closed.");

        if (command.Amount > Balance)
            throw new InvalidOperationException("Insufficient funds.");

        Raise(new Withdrawn(command.Amount));
    }

    // Showcases value-returning async operation with custom name.
    public decimal Close(Close _)
    {
        var final = Balance;
        Raise(new Closed(Balance));
        IsClosed = true;
        return final;
    }

    // Showcases a query that doesn't change state, which becomes a [ReadOnly] grain operation.
    public decimal Query(GetBalance _) => Balance;

    void Apply(Deposited @event) => Balance += @event.Amount;
    void Apply(Withdrawn @event) => Balance -= @event.Amount;
    void Apply(Closed @event) => Balance = 0;
}