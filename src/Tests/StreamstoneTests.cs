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

    [Fact]
    public async Task ReadAsyncEventSourcedActor()
    {
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .DeleteTableAsync(nameof(Account));

        // Create and persist an event-sourced actor
        var originalAccount = new Account("test-123");
        originalAccount.Deposit(new Deposit(100));
        originalAccount.Withdraw(new Withdraw(30));

        var storage = new StreamstoneStorage(CloudStorageAccount.DevelopmentStorageAccount);
        var actor = (IActor<Account.ActorState>)originalAccount;
        
        // Get the state and set the __actor reference for event-sourced actors
        var state = actor.GetState();
        var actorField = typeof(Account.ActorState).GetField("__actor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (actorField != null)
        {
            actorField.SetValue(state, originalAccount);
        }
        
        // Verify the actor has events before writing
        var eventSourced = originalAccount as IEventSourced;
        Assert.NotNull(eventSourced);
        Assert.Equal(2, eventSourced.Events.Count); // Deposit and Withdraw
        
        await storage.WriteStateAsync(nameof(Account), GrainId.Parse("account/test-123"), GrainState.Create(state));

        // First verify we can read using Orleans interface directly
        var directAccount = new Account("test-123");
        var directState = ((IActor<Account.ActorState>)directAccount).GetState();
        var directActorField = typeof(Account.ActorState).GetField("__actor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (directActorField != null)
        {
            directActorField.SetValue(directState, directAccount);
        }
        await storage.ReadStateAsync(nameof(Account), GrainId.Parse("account/test-123"), GrainState.Create(directState));
        Assert.Equal(70, directAccount.Balance); // Should be 70 after replaying events

        // Use the extension method to read the actor back
        var loadedAccount = await storage.ReadAsync<Account>("test-123");

        // Verify the actor was loaded correctly
        Assert.NotNull(loadedAccount);
        Assert.Equal(70, loadedAccount.Balance);
        Assert.False(loadedAccount.IsClosed);
    }

    [Fact]
    public async Task ReadAsyncRegularActor()
    {
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .DeleteTableAsync("CloudActorWallet");

        // Create and persist a regular actor
        var originalWallet = new Wallet("test-456");
        originalWallet.AddFunds("USD", 100);
        originalWallet.AddFunds("EUR", 50);

        var storage = new StreamstoneStorage(CloudStorageAccount.DevelopmentStorageAccount);
        var actor = (IActor<Wallet.ActorState>)originalWallet;
        await storage.WriteStateAsync("CloudActorWallet", GrainId.Parse("wallet/test-456"), GrainState.Create(actor.GetState()));

        // Use the extension method to read the actor back
        var loadedWallet = await storage.ReadAsync<Wallet>("test-456", "CloudActorWallet");

        // Verify the actor was loaded correctly
        Assert.NotNull(loadedWallet);
        Assert.Equal(2, loadedWallet.Funds.Count);
        Assert.Equal(100, loadedWallet.Funds["USD"]);
        Assert.Equal(50, loadedWallet.Funds["EUR"]);
    }

    [Fact]
    public async Task ReadAsyncNonExistentActor()
    {
        var storage = new StreamstoneStorage(CloudStorageAccount.DevelopmentStorageAccount);

        // Try to read a non-existent actor
        var loadedAccount = await storage.ReadAsync<Account>("non-existent-999");

        // Should return null for non-existent actors
        Assert.Null(loadedAccount);
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

