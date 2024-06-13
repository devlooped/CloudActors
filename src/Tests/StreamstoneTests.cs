using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Devlooped.CloudActors;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Newtonsoft.Json;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Tests;

namespace Tests;

public class StreamstoneTests
{
    [Fact]
    public async Task ReadWrite()
    {
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .GetTableReference(nameof(Account))
            .DeleteIfExistsAsync();

        var account = new Account("1");
        account.Deposit(new Deposit(100));
        account.Withdraw(new Withdraw(50));
        account.Close(new Close(CloseReason.Customer));

        var storage = new StreamstoneStorage(CloudStorageAccount.DevelopmentStorageAccount);
        await storage.WriteStateAsync(nameof(Account), GrainId.Parse("account/1"), GrainState.Create(account));

        var state = GrainState.Create<Account>(new Account("1"));
        await storage.ReadStateAsync(nameof(Account), GrainId.Parse("account/1"), state);

        Assert.Equal(account.Balance, state.State.Balance);
        Assert.Equal(account.IsClosed, state.State.IsClosed);
        Assert.Equal(account.Reason, state.State.Reason);
    }

    [Fact]
    public async Task ReadWriteComplexObject()
    {
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .GetTableReference("CloudActorWallet")
            .DeleteIfExistsAsync();

        var wallet = new Wallet("1");
        wallet.AddFunds("USD", 100);
        wallet.AddFunds("EUR", 50);
        wallet.AddFunds("EUR", 25);

        var storage = new StreamstoneStorage(CloudStorageAccount.DevelopmentStorageAccount);
        await storage.WriteStateAsync("CloudActorWallet", GrainId.Parse("wallet/1"), GrainState.Create(wallet));

        var state = GrainState.Create<Wallet>(new Wallet("1"));
        await storage.ReadStateAsync("CloudActorWallet", GrainId.Parse("wallet/1"), state);

        Assert.Equal(wallet.Funds, state.State.Funds);
    }

    [Fact]
    public void ConfigureWebAppWithGeneratedSerializers()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Host.UseOrleans(silo => silo
            .AddCloudActors()
            // See https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
            .AddStreamstoneActorStorage(options => options.JsonOptions.TypeInfoResolver = StreamstoneContext.Default));
    }
}

// Read more about this at https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation 
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true)]
[JsonSerializable(typeof(Wallet))]
partial class StreamstoneContext : JsonSerializerContext { }

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

