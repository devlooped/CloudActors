using Devlooped;
using Devlooped.CloudActors;
using Microsoft.AspNetCore.Mvc;
using TestDomain;

var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(silo =>
{
    if (builder.Environment.IsProduction())
    {
        silo.UseAzureStorageClustering(options =>
        {
            options.TableServiceClient = new Azure.Data.Tables.TableServiceClient(
                builder.Configuration["AzureStorage"] ??
                builder.Configuration["AzureWebJobsStorage"]);
        });
    }
    else
    {
        silo.UseLocalhostClustering();
    }

    silo.AddStreamstoneActorStorageAsDefault(opt => opt.AutoSnapshot = true);
});

builder.Services.AddSingleton(
    builder.Environment.IsDevelopment() ?
    CloudStorageAccount.DevelopmentStorageAccount :
    CloudStorageAccount.Parse(builder.Configuration.GetConnectionString("AzureStorage") ??
    throw new ArgumentException("Missing required AzureStorage connection string.")));

builder.Services.AddCloudActors();

var app = builder.Build();

app.MapPost("/account", async (IActorBus bus) =>
{
    var id = Guid.CreateVersion7().ToString("N");
    return Results.Created($"/account/{id}", new { id });
});

app.MapGet("/account/{id}", async (string id, IActorBus bus) =>
{
    var balance = await bus.QueryAsync(Account.NewId(id.Trim('"')), GetBalance.Default);
    return Results.Ok(balance);
});

app.MapMethods("/account/{id}", ["PUT", "POST"], async (string id, [FromBody] decimal amount, IActorBus bus) =>
{
    var accountId = Account.NewId(id.Trim('"'));
    if (amount > 0)
        await bus.ExecuteAsync(accountId, new Deposit(amount));
    else if (amount < 0)
        await bus.ExecuteAsync(accountId, new Withdraw(-amount));

    var balance = await bus.QueryAsync(accountId, GetBalance.Default);
    return Results.Ok(balance);
});

app.MapDelete("/account/{id}", async (string id, IActorBus bus) =>
{
    var balance = await bus.ExecuteAsync(Account.NewId(id.Trim('"')), new Close(CloseReason.Customer));
    return Results.Ok(balance);
});

app.Run();
