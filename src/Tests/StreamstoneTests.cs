using System.Collections.Generic;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Orleans;
using Orleans.Runtime;
using TestDomain;

namespace Tests;

public class StreamstoneTests
{
    [Fact]
    public async Task ReadWrite()
    {
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .DeleteTableAsync(nameof(Account));

        var account = new Account("1");
        account.Deposit(new Deposit(100));
        account.Withdraw(new Withdraw(50));
        account.Close(new Close(CloseReason.Customer));

        var storage = new StreamstoneStorage(CloudStorageAccount.DevelopmentStorageAccount);
        var actor = (IActor<Account.ActorState>)account;
        await storage.WriteStateAsync(nameof(Account), GrainId.Parse("account/1"), GrainState.Create(actor.GetState()));

        var state = GrainState.Create<Account.ActorState>(((IActor<Account.ActorState>)new Account("1")).GetState());
        await storage.ReadStateAsync(nameof(Account), GrainId.Parse("account/1"), state);
        actor.SetState(state.State);

        Assert.Equal(account.Balance, state.State.Balance);
        Assert.Equal(account.IsClosed, state.State.IsClosed);
        Assert.Equal(account.Reason, state.State.Reason);
    }

    [Fact]
    public async Task ReadWriteComplexObject()
    {
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .DeleteTableAsync("CloudActorWallet");

        var wallet = new Wallet("1");
        wallet.AddFunds("USD", 100);
        wallet.AddFunds("EUR", 50);
        wallet.AddFunds("EUR", 25);

        var actor = (IActor<Wallet.ActorState>)wallet;

        var storage = new StreamstoneStorage(CloudStorageAccount.DevelopmentStorageAccount);
        await storage.WriteStateAsync("CloudActorWallet", GrainId.Parse("wallet/1"), GrainState.Create(actor.GetState()));

        var state = GrainState.Create<Wallet.ActorState>(((IActor<Wallet.ActorState>)new Wallet("1")).GetState());
        await storage.ReadStateAsync("CloudActorWallet", GrainId.Parse("wallet/1"), state);
        actor.SetState(state.State);

        Assert.Equal(wallet.Funds, state.State.Funds);
    }
}


[Actor("CloudActorWallet")]
public partial class Wallet(string id)
{
    public string Id => id;

    public Dictionary<string, decimal> Funds { get; private set; } = new();

    public void AddFunds(string currency, decimal amount)
    {
        if (Funds.TryGetValue(currency, out var balance))
            Funds[currency] = balance + amount;
        else
            Funds.Add(currency, amount);
    }
}

static class GrainState
{
    public static IGrainState<T> Create<T>(T state) => new GrainState<T>(state);
}

class GrainState<T>(T state) : IGrainState<T>
{
    public T State
    {
        get => state;
        set => state = value;
    }

    public string ETag { get; set; } = "";

    public bool RecordExists { get; set; }
}

