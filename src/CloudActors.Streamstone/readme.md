Streamstone storage provider for Orleans grain persistence, 
supporting Cloud Native Actors on Azure Table Storage.

[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](osmfeula.txt)
[![OSS](https://img.shields.io/github/license/devlooped/oss.svg?color=blue)](license.txt) 
[![GitHub](https://img.shields.io/badge/-source-181717.svg?logo=GitHub)](https://github.com/devlooped/CloudActors)

## Features

* Supports plain CLR objects as grain state
* Supports event sourced actors
* Supports journaled CloudActors actors via Orleans `CustomStorage`
* Supports automatic snapshots for faster state reading 

See [Streamstone](https://github.com/yevhen/Streamstone) for more details.

## Usage

Register the provider via the Orleans silo builder:

and provide a `CloudStorageAccount` singleton:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(silo =>
{
    // 👇 register Streamstone as the default storage provider for all grains/actors
    silo.AddStreamstoneActorStorageAsDefault(opt => opt.AutoSnapshot = true);
});

builder.Services.AddCloudActors(); // 👈 registers grains, serializers, etc.
```

For `[Actor]` + `[Journaled]` actors using the default `CustomStorage` backend, the same registration also activates the Orleans EventSourcing custom log-consistency provider through generated host-side registration code.

Alternatively, you can register the provider under a specific name instead of the default:

```csharp
silo.AddStreamstoneActorStorage("streamstone", opt => opt.AutoSnapshot = true);
```

And actors can then opt in to the named provider explicitly:

```csharp
[Actor(nameof(Account), "streamstone")]
public partial class Account : IEventSourced
```

The same named provider also applies to `[Journaled(ProviderName = "streamstone")]` actors. When the host references both `Devlooped.CloudActors.Streamstone` and Orleans EventSourcing, Streamstone generates a registrar that wires `AddCustomStorageBasedLogConsistencyProvider("streamstone")` automatically when `AddStreamstoneActorStorage("streamstone")` runs.

### Storage

The Streamstone storage provider depends on [Devlooped.CloudStorage](https://nuget.org/packages/Devlooped.CloudStorage) 
(`CloudStorageAccount`) which must be registered in the DI container too:

```csharp
builder.Services.AddSingleton(
    builder.Environment.IsDevelopment() ?
    // Use the development emulator locally:
    CloudStorageAccount.DevelopmentStorageAccount :
    // Or provide a real account for production:
    CloudStorageAccount.Parse(builder.Configuration.GetConnectionString("AzureStorage") ??
    throw new ArgumentException("Missing required AzureStorage connection string.")));
```

### Clustering

It's quite common to use localhost clustering during development, and azure storage in production, 
for example:

```csharp
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

    // 👇 register Streamstone as the default storage provider for all grains/actors
    silo.AddStreamstoneActorStorageAsDefault(opt => opt.AutoSnapshot = true);
});
```


<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
<!-- exclude -->
