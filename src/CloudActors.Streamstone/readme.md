Streamstone storage provider for Orleans grain persistence, 
supporting Cloud Actors on Azure Table Storage.

## Features

* Supports plain CLR objects as grain state
* Supports event sourced actors
* Supports automatic snapshots for faster state reading 

See [Streamstone](https://github.com/yevhen/Streamstone) for more details.

## Usage

```csharp
var builder = WebApplication.Create(args);

// other config, specially Orleans and Cloud Actors

// 👇 register provider as default for all grains/actors
builder.Services.AddSingletonNamedService<IGrainStorage, StreamstoneStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);

// 👇 register provider with a specific name, alternatively
builder.Services.AddSingletonNamedService<IGrainStorage, StreamstoneStorage>("streamstone");
```

If the storage provider is registered with a specific name, actors can then 
specify the name of the provider to use:

```csharp
[Actor(nameof(Account), "streamstone")]
public partial class Account : IEventSourced
```

<!-- include ../../readme.md#sponsors -->

<!-- Exclude from auto-expansion by devlooped/actions-include GH action -->
<!-- exclude -->