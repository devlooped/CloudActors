# Plan: always-generated `ConfirmEvents()` for `IEventSourced`

## Problem

Today, `[Journaled]` actors always get end-of-command durability from the generated grain:

```csharp
RaiseEvents(events);
await ConfirmEvents();
```

That is convenient, but it prevents users from opting into Orleans-style manual confirmation semantics where the actor controls when to wait for persistence. It also leaves the actor-side `await ConfirmEvents();` surface tied to opt-in generation instead of making it a consistent capability of `IEventSourced`.

The revised goal is:

1. Always emit actor-side `ConfirmEvents()` for `IEventSourced` actors.
2. Document `ConfirmEvents()` as a safe no-op unless the chosen storage/runtime path actually supports delayed or background persistence.
3. Move the opt-in switch from `[Actor(backgroundSave: true)]` to `[Journaled(backgroundSave: true)]`.
4. A hidden public interface (`IConfirmableEvents`) is acceptable for the generated grain/runtime to wire the actor’s wait callback.
5. For journaled actors:
   - `backgroundSave: false` (default): keep the current generated `await ConfirmEvents()` behavior at command end.
   - `backgroundSave: true`: stop auto-awaiting at the end of the command and rely on actor code calling `await ConfirmEvents()` explicitly when it wants to wait.
6. For standard event-sourced actors, the Streamstone path can always wire the callback. If `StreamstoneOptions.BackgroundSave` is not enabled, `ConfirmEvents()` remains a no-op.

## Proposed approach

### 1. Extend `[Journaled]` with `backgroundSave`

Add support for a `backgroundSave` flag on `JournaledAttribute` while preserving the existing API shape.

This preserves the existing constructor overloads while enabling:

```csharp
[Journaled(backgroundSave: true)]
[Journaled(ProviderName = "Streamstone", backgroundSave: true)]
```

The source-generator models then need to capture this flag:

- `ActorModel.JournaledBackgroundSave` or equivalent journaled-specific metadata
- `JournaledModel.BackgroundSave` if the pipeline already splits journaled details

`ModelExtractors` should read the new `[Journaled]` `backgroundSave` setting from attribute metadata, not just the existing constructor arguments.

No new actor-level analyzer is needed here because `[Journaled]` already requires `[Actor]` + `IEventSourced` via `DCA006`.

### 2. Add a hidden actor-side confirmation bridge

Add a public but non-browsable interface in `CloudActors.Abstractions`, for example:

```csharp
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IConfirmableEvents
{
    Func<Task>? ConfirmEventsCallback { get; set; }
}
```

Then, in the generated event-sourced partial (`EventSourced.sbntxt`), emit explicit interface implementation for **all** `IEventSourced` actors:

```csharp
Func<Task>? confirmEvents;

Func<Task>? IConfirmableEvents.ConfirmEventsCallback
{
    get => confirmEvents;
    set => confirmEvents = value;
}

protected Task ConfirmEvents() => confirmEvents?.Invoke() ?? Task.CompletedTask;
```

This keeps the actor surface consistent:

- every event-sourced actor gets the same `ConfirmEvents()` affordance
- the method remains a no-op until the runtime wires a meaningful callback
- there is still no browsable public setter on the actor itself
- generated grains/runtime code can wire the callback via `IConfirmableEvents`

### 3. Journaled grain behavior

The generated `JournaledGrain` should branch on `JournaledAttribute.BackgroundSave`.

#### `backgroundSave: false`

Preserve today’s behavior exactly:

```csharp
if (((IEventSourced)actor).Events is { Count: > 0 } events)
{
    RaiseEvents(events);
    await ConfirmEvents();
}
```

#### `backgroundSave: true`

Wire the actor callback when the grain creates the actor:

```csharp
if (actor is IConfirmableEvents confirmable)
    confirmable.ConfirmEventsCallback = ConfirmEvents;
```

At the end of the command, the grain should still always publish raised events with `RaiseEvents(events)`. The only behavior controlled by `backgroundSave: true` is whether the grain then auto-awaits `ConfirmEvents()`:

- `backgroundSave: false`: `RaiseEvents(events); await ConfirmEvents();`
- `backgroundSave: true`: `RaiseEvents(events);` with no implicit await

That keeps event publication in the grain, avoids making actor-level `ConfirmEvents()` responsible for flushing pending events, and matches the Orleans-style manual confirmation mode.

This means:

- actor code can do `await ConfirmEvents()` at chosen checkpoints
- if actor code never calls it, the command can still complete with raised-but-not-explicitly-confirmed events
- default behavior remains unchanged unless `backgroundSave: true` is opted in

### 4. Standard grain + Streamstone background-save behavior

For standard grains, the generated callback should still exist for event-sourced actors, but Streamstone can own the hookup instead of adding a new seam to `IActorPersistentState`. The intended shape is:

1. every `IEventSourced` actor implements `IConfirmableEvents`
2. standard grains keep using the existing persistent-state path
3. provider code can wire the callback when it materializes the actor
4. callback target:
   - waits on a provider-specific flush/confirmation task when available
   - otherwise completes immediately

For Streamstone, the preferred path is to do this inside `StreamstoneStorage.ReadStateAsync(...)`:

1. after detecting an `IEventSourced` actor, also check whether the actor implements `IConfirmableEvents`
2. if so, assign `ConfirmEventsCallback` to a function that checks for an active background writer for the current grain/state
3. when a background writer exists, return the task from `BackgroundWriter.FlushAsync()`
4. otherwise return `Task.CompletedTask`

This keeps `IActorPersistentState` untouched and makes the no-op behavior naturally provider-specific:

- Streamstone with `BackgroundSave = true` returns a real flush task
- Streamstone with `BackgroundSave = false` returns a completed task
- other providers can ignore the hook unless they later add delayed/background-save support

The plan assumption is:

- **standard grains keep their existing end-of-command `WriteStateAsync()`**
- `ConfirmEvents()` is an extra checkpoint for all event-sourced actors
- Streamstone wires that checkpoint in `ReadStateAsync(...)` without changing the standard grain contract
- only delayed/background-save-capable providers do meaningful waiting there; unsupported or disabled paths can safely complete immediately

### 5. Files expected to change

**Abstractions**

- `src/CloudActors.Abstractions/JournaledAttribute.cs`
- `src/CloudActors.Abstractions/IEventSourced.cs` or new `IConfirmableEvents.cs`

**Code analysis / generation**

- `src/CloudActors.Abstractions.CodeAnalysis/CacheableModels.cs`
- `src/CloudActors.Abstractions.CodeAnalysis/EventSourcedGenerator.cs`
- `src/CloudActors.Abstractions.CodeAnalysis/EventSourced.sbntxt`

**Hosting / grain generation**

- `src/CloudActors.Hosting.CodeAnalysis/JournaledGrain.sbntxt`
- `src/CloudActors.Streamstone.CodeAnalysis/StreamstoneCustomStorageGenerator.cs` or related Streamstone generator/template files so the callback hookup is always emitted
- `src/CloudActors.Streamstone/StreamstoneStorage.cs`

**Tests**

- `src/Tests.CodeAnalysis/GrainsGeneration.cs`
- runtime coverage in `src/Tests/JournaledOobStorageTests.cs`
- runtime coverage in `src/Tests/StreamstoneTests.cs`

## Verification plan

1. **Generation coverage**
   - any `IEventSourced` actor emits actor-side `ConfirmEvents()` and explicit `IConfirmableEvents`
   - non-event-sourced actors do not get `ConfirmEvents()`
   - `[Journaled(backgroundSave: true)]` changes generated journaled grain behavior without affecting standard actors

2. **Journaled runtime coverage**
    - default journaled actor still auto-confirms at end of command
    - journaled actor with `[Journaled(backgroundSave: true)]` can `await ConfirmEvents()` mid-command
    - `RaiseEvents(events)` still happens at command end in both modes
    - remaining events are raised without implicit wait when `backgroundSave: true`

3. **Standard Streamstone runtime coverage**
   - any event-sourced actor using Streamstone can call `await ConfirmEvents()`
   - with `StreamstoneOptions.BackgroundSave = true`, `ConfirmEvents()` forces `FlushAsync(...)`
   - with `StreamstoneOptions.BackgroundSave = false`, `ConfirmEvents()` is a no-op
   - normal end-of-command write behavior remains intact

4. **Repo validation**
   - `dotnet build`
   - `dnx --yes retest`
   - `dotnet format whitespace -v:diag --exclude ~/.nuget`
   - `dotnet format style -v:diag --exclude ~/.nuget`

## Notes / considerations

- `ConfirmEvents()` becomes a stable part of the generated `IEventSourced` actor surface, but it is explicitly documented as a no-op unless a storage/runtime path wires delayed/background persistence support.
- `backgroundSave: true` remains an opt-in semantic change only for journaled grains, so the default journaled behavior stays fully backward compatible.
- The plan assumes standard-grain waiting is implemented through a host/provider bridge rather than hard-coding Streamstone lookups in generated code.
- If implementation shows the standard path needs a narrower hook than `Func<Task>?`, the internal callback contract can be refined without changing the user-facing design.

## Todo set

### Current items

| ID | Title | Status | Description |
| --- | --- | --- | --- |
| `journaled-backgroundsave-attribute` | Add backgroundSave to JournaledAttribute and models | pending | Add a named `BackgroundSave` property to `JournaledAttribute`, then thread the flag through the journaled-related generator models so hosting generation can branch on journaled manual-confirm opt-in. |
| `confirmable-events-bridge` | Generate IConfirmableEvents bridge and actor ConfirmEvents() | pending | Add a public non-browsable `IConfirmableEvents` abstraction and update `EventSourced.sbntxt`/`EventSourcedGenerator` so all `IEventSourced` actors get `protected ConfirmEvents()` plus explicit interface implementation for the runtime callback. |
| `journaled-manual-confirmation` | Update JournaledGrain for opt-in manual confirmation | pending | Always wire `IConfirmableEvents.ConfirmEventsCallback` to the grain's `ConfirmEvents` task when creating the actor. Preserve current auto-await behavior when `JournaledAttribute.BackgroundSave` is false; when true, still call `RaiseEvents(events)` at command end but stop auto-awaiting confirmation. |
| `standard-streamstone-confirmation` | Wire Streamstone confirmation in ReadStateAsync | pending | Update `StreamstoneStorage.ReadStateAsync(...)` so when it materializes an event-sourced actor that also implements `IConfirmableEvents`, it assigns a callback that returns `FlushAsync(stateName, grainId)` when a background writer exists and `Task.CompletedTask` otherwise, leaving `IActorPersistentState` and standard grains untouched. |
| `runtime-and-generator-tests` | Cover generation and runtime behaviors | pending | Add tests for generated `ConfirmEvents`/`IConfirmableEvents` emission on event-sourced actors, default journaled auto-confirm, journaled manual confirm with `[Journaled(backgroundSave: true)]`, and Streamstone standard actor confirmation checkpoints with both enabled and disabled background save. |
| `validate-repo` | Run build, tests, and formatting | pending | Run `dotnet build`, `dnx --yes retest`, `dotnet format whitespace -v:diag --exclude ~/.nuget`, and `dotnet format style -v:diag --exclude ~/.nuget` after implementation. |

### Dependencies

| Todo | Depends on |
| --- | --- |
| `confirmable-events-bridge` | `journaled-backgroundsave-attribute` |
| `journaled-manual-confirmation` | `confirmable-events-bridge` |
| `standard-streamstone-confirmation` | `confirmable-events-bridge` |
| `runtime-and-generator-tests` | `journaled-manual-confirmation`, `standard-streamstone-confirmation` |
| `validate-repo` | `runtime-and-generator-tests` |

### Suggested execution order

1. `journaled-backgroundsave-attribute`
2. `confirmable-events-bridge`
3. `journaled-manual-confirmation`
4. `standard-streamstone-confirmation`
5. `runtime-and-generator-tests`
6. `validate-repo`
