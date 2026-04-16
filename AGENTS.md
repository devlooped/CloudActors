# CloudActors – Implementation Reference

This document captures design decisions, project structure, and implementation details for contributors and AI agents working on the CloudActors codebase.

## Project Overview

CloudActors is an opinionated, simplified actor library for .NET built on Microsoft Orleans. It replaces Orleans' RPC-style grain API with a uniform message-passing style. All actor interactions go through a single `IActorBus` with just two operations: **Execute** and **Query**.

The library makes Orleans grains completely transparent to the developer: actors are plain C# classes annotated with `[Actor]`, and all Orleans plumbing (grain definition, serialization, activation, state persistence) is handled by Roslyn source generators.

Orleans version is centralized in `src/Directory.props` as `$(OrleansVersion)`. Currently targeting **Orleans 10.1.0** and **net10.0**.

---

## Repository Layout

```
src/
├── CloudActors.Abstractions/           # Core public abstractions (IActorBus, IActorCommand, etc.)
├── CloudActors.Abstractions.CodeAnalysis/  # Source generators + analyzers that run in actor domain projects
├── CloudActors.Abstractions.CodeFix/   # Roslyn code fixes for diagnostics
├── CloudActors.Abstractions.Package/   # NuGet packaging for Abstractions
├── CloudActors/                        # Runtime: OrleansActorBus, state factory, DI extensions
├── CloudActors.CodeAnalysis/           # Source generators that run in Orleans silo projects
├── CloudActors.Streamstone/            # Optional Streamstone/Azure Table Storage grain storage provider
├── TestApp/                            # End-to-end ASP.NET + Orleans host sample
├── TestDomain/                         # Sample actor domain library (used by tests)
├── Tests/                              # Integration tests (Orleans in-process)
├── Tests.CodeAnalysis/                 # Incremental source-generator tests
└── Tests.CodeFix/                      # Roslyn code-fix tests
```

---

## Core Abstractions (`CloudActors.Abstractions`)

### IActorBus

The single entry point for all actor interactions, injected via DI:

```csharp
public interface IActorBus
{
    Task ExecuteAsync(string id, IActorCommand command);
    Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command);
    Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query);
}
```

- **ExecuteAsync** — state-changing operation (maps to a regular Orleans grain call)
- **QueryAsync** — read-only operation (maps to `[ReadOnly]` Orleans grain method)
- `id` is a string like `"account/42"`; typed-ID overloads are generated per actor

The three public methods are **default interface methods** that delegate to hidden `[OverloadResolutionPriority(1)]` overloads with `[CallerMemberName]`, `[CallerFilePath]`, and `[CallerLineNumber]` parameters used for automatic OpenTelemetry telemetry capture (see `Telemetry.cs`).

### Message interfaces

```csharp
IActorMessage            // base marker interface for all messages
IActorCommand            // void command (state-changing, no return value) — extends IActorMessage
IActorCommand<TResult>   // state-changing command with return value — extends IActorCommand
IActorQuery<TResult>     // read-only query — extends IActorMessage
```

Actor messages should be `partial record` types (required for source generators to add `[GenerateSerializer]`).

### [Actor] attribute

`ActorAttribute` flags a class as an actor. Optional parameters:
- `stateName` — overrides the default state name (defaults to the class name)
- `storageProvider` — Orleans storage provider name (defaults to the default provider)

### IEventSourced

Interface implemented by actors that use event sourcing. The implementation is **code-generated**; actors only need to list it in the base type list without implementing it. The generator provides:
- `Raise<T>(event)` — records an event and calls the matching `partial void Apply(TEvent)` method
- `Events`, `AcceptEvents()`, `LoadEvents()` — managed by the storage provider

### IActorIdFactory

Converts string Orleans grain keys to typed actor ID values at runtime. A source-generated implementation registers itself via `[ModuleInitializer]` through `ActorIdFactory.Generated` before DI runs. `CloudActorsExtensions.AddCloudActors()` uses `ActorIdFactory.Default`, which returns the generated factory when present, otherwise a string passthrough.

---

## Runtime (`CloudActors`)

### OrleansActorBus

Implements `IActorBus` by resolving the actor grain from the Orleans `IGrainFactory` and routing commands/queries through it. Wraps every call with OpenTelemetry activity spans via `Telemetry`.

### ActorStateFactory

Wraps the built-in Orleans `IPersistentStateFactory` so that actor grains can be instantiated with a typed ID argument (not a parameterless constructor). This allows actors to receive their `id` in the constructor.

### CloudActorsExtensions.AddCloudActors()

Registers:
1. `IActorBus` → `OrleansActorBus`
2. `IActorIdFactory` → generated factory (or string passthrough)
3. Replaces `IPersistentStateFactory` with `ActorStateFactory`

Usage inside an Orleans silo configuration:

```csharp
builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
});

// registers IActorBus, IActorIdFactory, and wraps IPersistentStateFactory
builder.Services.AddCloudActors();
```

### IActorGrain

Internal interface implemented by all generated grain classes (`IGrainWithStringKey`):

```csharp
Task ExecuteAsync(IActorCommand command);
Task<TResult> ExecuteAsync<TResult>(IActorCommand<TResult> command);
Task<TResult> QueryAsync<TResult>(IActorQuery<TResult> query);
```

`OrleansActorBus` resolves grains via `IGrainFactory.GetGrain<IActorGrain>(GrainId.Parse(id))`.

### GrainStorageExtensions

Public extension methods on `IGrainStorage` for reading actor instances directly from storage (bypassing the bus):

```csharp
// Infers TState from IActor<TState> via reflection
Task<TActor> ReadActorAsync<TActor>(this IGrainStorage storage, string id, IActorIdFactory? idFactory = null);

// Explicit state type for direct invocation
Task<TActor> ReadActorAsync<TActor, TState>(this IGrainStorage storage, string id, IActorIdFactory? idFactory = null)
    where TActor : IActor<TState>
    where TState : IActorState<TActor>, new();
```

Reads state from `IGrainStorage`, instantiates the actor with its typed ID (via `IActorIdFactory`), and restores state via `SetState()`. Useful for testing or server-side code that needs to inspect actor state without going through the bus.

### Telemetry

Emits OpenTelemetry spans (using `ActivitySource`) and metrics (using `Meter`) for every command and query:
- `Processing` histogram — duration of commands
- `Sending` histogram — duration of queries
- `Commands` counter / `Queries` counter
- Span tags follow OpenTelemetry messaging semantic conventions

---

## Source Generators

### Generator packages

| Package | Generators | Runs in |
|---------|-----------|---------|
| `CloudActors.Abstractions.CodeAnalysis` | `ActorStateGenerator`, `ActorMessageGenerator`, `ActorIdBusOverloadGenerator`, `ActorBusOverloadGenerator`, `ActorPrimitiveIdGenerator`, `EventSourcedGenerator`, `CloudActorsAttributeGenerator` | Actor domain class library |
| `CloudActors.CodeAnalysis` | `ActorGrainGenerator`, `ActorIdFactoryGenerator`, `ActorsAssemblyGenerator` | Orleans silo / host project |

### Incremental pipeline conventions

- All pipeline models are **string-only record structs** (e.g. `ActorModel`, `ActorMessageModel`) defined in `CacheableModels.cs`. This ensures proper incremental caching — no Roslyn symbols cross pipeline step boundaries.
- `EquatableArray<T>` (in `EquatableArray.cs`) wraps `ImmutableArray<T>` for value equality in pipeline models.
- `TrackingNames` provides string constants used with `.WithTrackingName()` for testable incrementality steps.
- `ModelExtractors` (in `CacheableModels.cs`) converts Roslyn symbols into string-only models.

### ActorStateGenerator (`CloudActors.Abstractions.CodeAnalysis`)

Uses `SyntaxProvider.CreateSyntaxProvider` to find `[Actor]`-annotated classes **in the current project only**.

Generates, for each actor:
- A nested `ActorState` record with all **writable instance properties** and **mutable instance fields** (static, const, readonly, and get-only are excluded)
- Explicit `IActor<ActorState>` implementation: `GetState()` and `SetState(ActorState)` methods

Template: `ActorState.sbntxt`

### ActorGrainGenerator (`CloudActors.CodeAnalysis`)

Uses `CompilationProvider.Select` (not `CreateSyntaxProvider`) to discover actors across **all referenced assemblies** (not just the current project). This is essential because the silo project references the actor domain library.

For each actor, generates:
- The Orleans grain class (partial) that wraps the actor
- Routes `ExecuteAsync`/`QueryAsync` calls to the matching actor method by parameter type
- Injects `IPersistentState<ActorState>` and calls `ReadStateAsync()` on activation
- After each command, calls `GetState()` and writes via `WriteStateAsync()`
- Also runs the Orleans code generator (`OrleansGenerator.GenerateCode`) to produce the `.orleans.cs` file

Template: `ActorGrain.sbntxt`

### ActorsAssemblyGenerator (`CloudActors.CodeAnalysis`)

Uses `CompilationProvider.Select` to scan referenced assemblies for those tagged with `[CloudActorsAttribute]` (generated by `CloudActorsAttributeGenerator`). For each such assembly emits:
- `[assembly: ApplicationPartAttribute("...")]`
- `[assembly: GenerateCodeForDeclaringAssembly(typeof(SomeTypeFromAssembly))]`

Filters out generic types (`!x.IsGenericType`) and internal/special types to avoid picking up template types.

### ActorIdFactoryGenerator (`CloudActors.CodeAnalysis`)

Uses `CompilationProvider.Select` to discover all actors with typed IDs and generates an `IActorIdFactory` implementation registered via `[ModuleInitializer]` through `ActorIdFactory.Generated`.

### ActorIdBusOverloadGenerator (`CloudActors.Abstractions.CodeAnalysis`)

Generates typed `IActorBus` extension methods for actors with non-string IDs. Two cases:
- **Primitive ID** (e.g. `long`, `Guid`): uses the generated `{Actor}.{Actor}Id` wrapper type
- **Typed ID** (e.g. `ProductId`): uses the actual ID type directly

To avoid `CS0111`, overloads are **only generated for ID types used by exactly one actor**. If two actors share the same underlying primitive type, no typed overloads are emitted.

### ActorPrimitiveIdGenerator (`CloudActors.Abstractions.CodeAnalysis`)

For actors whose primary constructor takes a primitive BCL value type (e.g. `long`, `Guid`), generates:
- A nested `readonly record struct {Actor}Id(T Id)` wrapper
- A static `NewId(T id)` factory method
- For `Guid` IDs: an additional parameterless `NewId()` that calls `Guid.CreateVersion7()` (if available on the runtime) or `Guid.NewGuid()`

### EventSourcedGenerator (`CloudActors.Abstractions.CodeAnalysis`)

For `[Actor]` classes that list `IEventSourced` in their base type list **without implementing it**:
- Generates `Raise<T>(event)`, `Events`, `AcceptEvents()`, `LoadEvents()` implementation
- Discovers `Raise<T>()` call sites syntactically via `EventLocator` to enumerate event types
- Emits `[GenerateSerializer]` for each event type (and the Orleans `.orleans.cs` file when in server mode)

Template: `EventSourced.sbntxt`

### ActorMessageGenerator

- Validates messages are `partial` (for `[GenerateSerializer]` generation)
- Emits `[GenerateSerializer]` on message types and any referenced partial types
- Uses the `SerializableGenerator` helper (static class, not a generator itself) to produce the `[GenerateSerializer]` output and Orleans `.orleans.cs` file when running in server mode

### OrleansGenerator

Helper that wraps the Orleans Roslyn source generator (`Microsoft.Orleans.CodeGenerator`) to produce the `.orleans.cs` file from generated code. Uses `CodeGeneratorOptions` with `GenerateFieldIds` and `GenerateCompatibilityInvokers` sourced from MSBuild properties.

---

## Typed Actor IDs

Actors can use non-string IDs in three ways:

1. **Primitive ID** (`long`, `Guid`, etc.): A nested `{Actor}Id` wrapper struct is generated; `NewId()` helper is available.
2. **Typed ID** via `IParsable<T>` / `IFormattable`: The ID type (e.g. `ProductId`) is used directly. Typed `IActorBus` extension overloads are generated.
3. **StructId**: Types implementing `IStructId` (from the [StructId](https://github.com/devlooped/StructId) package) are detected automatically.

ID format stored in Orleans: `"{actortype}/{id}"` (e.g. `"account/42"`, `"product/p1"`).

---

## Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| `DCA001` | Error | Actor/message type must be `partial` |
| `DCA002` | Error | Do not manually add `[GenerateSerializer]` — it is added automatically |
| `DCA003` | Error | Message types may only implement one of `IActorCommand`, `IActorCommand<T>`, `IActorQuery<T>` |
| `DCA004` | Error | `<ProduceReferenceAssembly>true</ProduceReferenceAssembly>` is not supported in actor projects |
| `DCA005` | Error | Actor state types must be serializable — make `partial` or add `[GenerateSerializer]` |

---

## Streamstone Storage (`CloudActors.Streamstone`)

An optional event-store-aware `IGrainStorage` implementation backed by [Streamstone](https://github.com/yevhen/Streamstone) on Azure Table Storage.

- For `IEventSourced` actors: events are stored as individual event rows in a stream partition; state is reconstituted by replaying events via `LoadEvents()`
- For regular actors: state is stored as a single JSON entity
- **Auto-snapshot**: when `StreamstoneOptions.AutoSnapshot = true`, each write also atomically upserts a `"State"` row with the serialized current state. On load, if the snapshot is version-compatible with the assembly, it is used directly (no event replay needed)
- **Version compatibility**: controlled by `SnapshotVersionCompatibility` (Major or Minor)
- Register via extension methods on `ISiloBuilder`:
  - `AddStreamstoneActorStorageAsDefault()` — registers as the default grain storage provider
  - `AddStreamstoneActorStorageAsDefault(Action<StreamstoneOptions> configure)` — with configuration
  - `AddStreamstoneActorStorage(string name)` — registers a named provider
  - `AddStreamstoneActorStorage(string name, Action<StreamstoneOptions> configure)` — named with configuration

---

## Key Conventions

- Actor classes must always be `partial` (required by the state generator; DCA001 enforces this)
- Actor message types must be `partial record` (or `partial class`)
- Do not add `[GenerateSerializer]` manually — generated automatically
- Do not set `<ProduceReferenceAssembly>true</ProduceReferenceAssembly>` in actor projects
- Actor domain libraries reference `Devlooped.CloudActors.Abstractions`; the Orleans silo references both the domain library and `Devlooped.CloudActors`
- Business logic inside actors should be independent of Orleans; only plain C# is required

---

## Build, Test, and Formatting

```sh
# Restore
dotnet restore

# Build
dotnet build

# Run all tests (with auto-retry on transient failures)
dnx --yes retest

# Fix formatting
dotnet format whitespace -v:diag --exclude ~/.nuget
dotnet format style -v:diag --exclude ~/.nuget

# Verify formatting (CI check)
dotnet format whitespace --verify-no-changes -v:diag --exclude ~/.nuget
```

Tests that require Azure Storage (Streamstone integration) need Azurite running:

```sh
npm install azurite
npx azurite &
```

Test projects:
- `Tests/` — integration tests (Orleans in-process silo)
- `Tests.CodeAnalysis/` — incremental source-generator incrementality tests
- `Tests.CodeFix/` — Roslyn code-fix tests
