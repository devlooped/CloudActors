using System;
using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Devlooped.CloudActors;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans;
using Orleans.Concurrency;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.TestingHost;
using Xunit.Abstractions;

namespace Tests;

public class TestAccounts(ITestOutputHelper output)
{
    [Fact]
    public async Task HostedGrain()
    {
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .GetTableReference("account")
            .DeleteIfExistsAsync();

        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = new OrleansActorBus(cluster.GrainFactory);

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
            var bus = new OrleansActorBus(cluster.GrainFactory);
            Assert.Equal(0, await bus.QueryAsync("account/1", new GetBalance()));
        }
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

[Actor]
public partial class Account : IEventSourced
{
    public Account() : this("") { }

    public Account(string id) => Id = id;

    public string Id { get; }

    public decimal Balance { get; private set; }

    // Showcases that operation can also be just Execute overloads 
    //public void Execute(Deposit command)
    //{
    //    // validate command
    //    Raise(new Deposited(command.Amount));
    //}

    // Showcases that operations can have a name that's not Execute
    public Task DepositAsync(Deposit command)
    {
        // validate command
        Raise(new Deposited(command.Amount));
        return Task.CompletedTask;
    }

    // Showcases that operations don't have to be async
    public void Execute(Withdraw command)
    {
        // validate command
        Raise(new Withdrawn(command.Amount));
    }

    // Showcases value-returning async operation with custom name.
    public Task<decimal> CloseAsync(Close _)
    {
        var balance = Balance;
        Raise(new Closed(Balance));
        return Task.FromResult(balance);
    }

    // Showcases a query that doesn't change state, which becomes a [ReadOnly] grain operation.
    public decimal Query(GetBalance _) => Balance;

    void Apply(Deposited @event) => Balance += @event.Amount;

    void Apply(Withdrawn @event) => Balance -= @event.Amount;

    void Apply(Closed @event) => Balance = 0;
}