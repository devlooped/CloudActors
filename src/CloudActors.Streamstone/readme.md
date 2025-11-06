Streamstone storage provider for Orleans grain persistence, 
supporting Cloud Actors on Azure Table Storage.

[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](osmfeula.txt)
[![OSS](https://img.shields.io/github/license/devlooped/oss.svg?color=blue)](license.txt) 
[![GitHub](https://img.shields.io/badge/-source-181717.svg?logo=GitHub)](https://github.com/devlooped/CloudActors)

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

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
<!-- exclude -->