using System.Text.Json;
using System.Threading.Tasks;
using Devlooped.CloudActors;
using Microsoft.Azure.Cosmos.Table;
using Moq;
using Newtonsoft.Json;
using Orleans;
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

        var storage = new StreamstoneStorage(CloudStorageAccount.DevelopmentStorageAccount);
        await storage.WriteStateAsync(nameof(Account), GrainId.Parse("account/1"), GrainState.Create(account));

        var state = GrainState.Create<Account>(new Account("1"));
        await storage.ReadStateAsync(nameof(Account), GrainId.Parse("account/1"), state);
    }

    [Fact]
    public void Deserialize()
    {
        var account = new Account("1");

        JsonConvert.PopulateObject("{\"Balance\":100,\"IsClosed\":true,\"Id\":\"1\"}", account, new JsonSerializerSettings
        {
        });

        Assert.NotNull(account);
        Assert.Equal("1", account.Id);
        Assert.Equal(100, account.Balance);
        Assert.True(account.IsClosed);
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

