using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Data.Tables;
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
    public async Task GeneratedJsonContextIsUsedForSerialization()
    {
        // Custom options clearly differ from the generated ActorJsonContext defaults:
        // - camelCase naming  (generated ActorJsonContext uses PascalCase, the STJ default)
        // - no string enum converter  (generated uses UseStringEnumConverter = true)
        // If StreamstoneStorage uses the state's IActorState.JsonOptions (i.e. the generated
        // ActorJsonContext), the raw JSON in the table must still be PascalCase with string enums.
        var customOptions = new StreamstoneOptions
        {
            AutoSnapshot = false,
            JsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                // No JsonStringEnumConverter → enums serialized as numbers
            }
        };
        var storage = new StreamstoneStorage(CloudStorageAccount.DevelopmentStorageAccount, customOptions);

        // --- Non-event-sourced actor: Wallet ---
        const string walletTable = "CloudActorWallet";
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .DeleteTableAsync(walletTable);

        var wallet = new Wallet("json-ctx-test");
        wallet.AddFunds("USD", 100m);
        wallet.AddFunds("EUR", 50m);

        var walletActor = (IActor<Wallet.ActorState>)wallet;
        await storage.WriteStateAsync(walletTable, GrainId.Parse("wallet/json-ctx-test"),
            GrainState.Create(walletActor.GetState()));

        // Use QueryAsync (not GetEntityAsync) – '/' in row keys is disallowed in path-based
        // Azure Table REST calls, but OData filter expressions handle them fine.
        var walletJson = default(string);
        await foreach (var entity in CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .GetTableClient(walletTable)
            .QueryAsync<TableEntity>(e => e.PartitionKey == walletTable))
        {
            walletJson = entity.GetString("Data");
            if (walletJson is not null) break;
        }

        Assert.NotNull(walletJson);
        // Generated context: PascalCase → "Funds", not "funds"
        Assert.Contains("\"Funds\"", walletJson);

        // --- Event-sourced actor: Account (Closed event has Balance + CloseReason enum) ---
        const string accountTable = nameof(Account);
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .DeleteTableAsync(accountTable);

        var account = new Account("json-ctx-test");
        account.Deposit(new Deposit(100));
        account.Close(new Close(CloseReason.Fraud));

        var accountActor = (IActor<Account.ActorState>)account;
        await storage.WriteStateAsync(accountTable, GrainId.Parse("account/json-ctx-test"),
            GrainState.Create(accountActor.GetState()));

        // Read all event entities in the stream partition (PK = grain key, without type prefix)
        var accountEvents = CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .GetTableClient(accountTable)
            .QueryAsync<TableEntity>(filter: string.Empty);

        var dataValues = new List<string>();
        await foreach (var entity in accountEvents)
        {
            if (entity.TryGetValue("Data", out var raw) && raw is string json)
                dataValues.Add(json);
        }

        Assert.NotEmpty(dataValues);

        // Generated context: PascalCase → "Amount"/"Balance"/"Reason", not camelCase
        // Generated context: string enum → "Fraud", not 1 (numeric value of CloseReason.Fraud)
        Assert.Contains(dataValues, j => j.Contains("\"Amount\""));      // Deposited event
        Assert.Contains(dataValues, j => j.Contains("\"Reason\""));      // Closed event: PascalCase
        Assert.Contains(dataValues, j => j.Contains("\"Fraud\""));       // Closed event: string enum
        Assert.DoesNotContain(dataValues, j => j.Contains("\"reason\"")); // Not camelCase
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

