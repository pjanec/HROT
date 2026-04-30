# BTree + HSM Unification Design

## Executive Summary

`Hrot.AI.Doctrines` currently supports only Behavior Tree (BTree) doctrines. FastHSM
exists as a parallel paradigm but the two are not interchangeable: hot reload ignores
HSM, the HSM kernel never sets the `Terminated` flag, there is no unified interrupt path,
and there is no way to share condition/action logic between the two paradigms.

This design unifies BTree and HSM as first-class doctrine paradigms so that a CGF unit's
tactical brain can be implemented in either technology without the mission director,
reload pipeline, or interrupt system needing to know which one is running.

**Five phases, each independently deliverable:**

| Phase | Theme                            | Key files changed |
|-------|----------------------------------|-------------------|
| 1     | Unified Hot Reload Coordinator   | `Hrot.Editor`, `Fhsm.Kernel`, `Hrot.AI.Doctrines.csproj` |
| 2     | HSM Terminal State Routing       | `Fhsm.Compiler`, `Fhsm.Kernel`, `Fdp.Toolkits` |
| 3     | Cognitive Interrupt Decoupling   | `Fdp.Toolkits` |
| 4     | Shared AI Node Attributes        | `Fbt.Kernel`, `Fbt.SourceGen`, `Fhsm.SourceGen` |
| 5     | Actuator Channel Safety          | `Fbt.SourceGen`, `Fhsm.SourceGen` |

---

## Current State

### FbtAssemblyHotReloader

`FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/FbtAssemblyHotReloader.cs`

- Watches a directory for DLL changes, loads each new DLL into a collectible
  `AssemblyLoadContext`, finds the `[FbtRegistrar]`-annotated type, calls
  `_handler(registrarType, newAssembly)`, then immediately unloads the old ALC.
- `_handler` returns `IEnumerable<(string treeName, BehaviorTreeBlob blob)>` only.
- The old ALC is unloaded on the **background thread** right after the handler returns,
  before `DrainPendingCallbacks` fires on the main thread.
- No HSM awareness whatsoever.

### HotReloadManager (FastHSM)

`FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HotReloadManager.cs`

- Evaluates incoming `HsmDefinitionBlob` updates and calls either `SoftReload` or
  `HardReset` depending on structural compatibility.
- Has no `FileSystemWatcher`, no `AssemblyLoadContext` management, and is never called
  from the editor reload path.

### HsmActionDispatcher (Fhsm.Kernel — generated)

`Fhsm.SourceGen` generates `HsmActionDispatcher.g.cs` into `Fhsm.Kernel` on first build.
The generated class lives in namespace `Fhsm.Kernel` and contains:

```csharp
private static readonly Dictionary<ushort, IntPtr> ActionTable = new() { ... };
private static readonly Dictionary<ushort, IntPtr> GuardTable  = new() { ... };
public static void ExecuteAction(ushort actionId, ...)
public static bool EvaluateGuard(ushort guardId, ...)
public static void RegisterAction(ushort id, IntPtr action) => ActionTable[id] = action;
public static void RegisterGuard(ushort id, IntPtr guard)  => GuardTable[id]  = guard;
```

For user assemblies (e.g. `Hrot.AI.Doctrines`), `Fhsm.SourceGen` generates
`HsmActionRegistrar.g.cs` in namespace `{assemblyName}.Generated`:

```csharp
public static void RegisterAll()
{
    HsmActionDispatcher.RegisterAction(id, (IntPtr)(delegate* <...>)&Method);
    HsmActionDispatcher.RegisterGuard(id, (IntPtr)(delegate* <...>)&Guard);
}
```

`ActionTable` and `GuardTable` are `private static readonly Dictionary`, which is NOT
thread-safe for concurrent mutation. Updates must happen on the main thread at a frame
boundary.

### HsmTickSystem

`FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs`

- Steps `BrainHsm64`/`BrainHsm128` ECS components each frame.
- `BrainHsm256` ECS component does NOT exist (`HsmInstance256` exists only in
  `Fhsm.Kernel`; there is no corresponding ECS component).
- Does NOT detect `InstanceFlags.Terminated` after `HsmKernel.Update()`.
- Does NOT publish `DoctrineFinishedEvent`.

### StateFlags.IsFinal / InstanceFlags.Terminated

Both flags exist in `Fhsm.Kernel/Data/Enums.cs`:

```csharp
// StateFlags
IsFinal = 1 << 8,       // Final state (terminates)

// InstanceFlags
Terminated = 1 << 4,    // Reached final state
```

Neither flag is checked or set anywhere in `HsmKernelCore.cs`. `StateNode` in
`Fhsm.Compiler` has no `IsFinal` property. `HsmFlattener.BuildStateFlags()` does not
emit `StateFlags.IsFinal`. The `IsFinal` / `Terminated` pair is a pre-allocated but
unimplemented stub.

### HsmDamageBridgeSystem

`FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmDamageBridgeSystem.cs`

- Translates `ActorCapabilityState` changes into `HsmEvent` injections for
  `BrainHsm128` and `BrainHsm64` via separate ECS queries.
- Completely ignores BTree entities.
- `CognitiveRuntimeModule` registers it before `HsmTickSystem<T>`.

### Hrot.AI.Doctrines

`Hrot/Subsystems/Hrot.AI.Doctrines/Hrot.AI.Doctrines.csproj`

References only BTree libraries:
- `Fdp.Toolkits` (BrainBlackboard, channels, etc.)
- `Fdp.Core` (Entity type)
- `Fbt.Compiler` (BTreeBuilder)
- `Fbt.SourceGen` (analyzer, no output assembly ref)

Does NOT reference `Fhsm.Kernel`, `Fhsm.Compiler`, or `Fhsm.SourceGen`.

Contains `Idle_HSM` in `AiDoctrineFactory` as a stub with `HsmDefinition = null`.

---

## Phase 1 — Unified Hot Reload Coordinator

### Problem

The hot reload callback returns only BTree blobs. HSM action pointer tables
(`HsmActionDispatcher`) are never cleared or refreshed on reload. There is no mechanism
to call `HotReloadManager.TryReload()` for live HSM ECS instances. The old ALC is
released on the background thread before the main thread applies the reload, which
prevents safe HSM pointer refresh.

### Design

#### 1.1 — Add Fhsm references to `Hrot.AI.Doctrines.csproj`

```xml
<ProjectReference Include="..\..\..\FDP\ExtDeps\FastHSM\src\Fhsm.Kernel\Fhsm.Kernel.csproj" />
<ProjectReference Include="..\..\..\FDP\ExtDeps\FastHSM\src\Fhsm.Compiler\Fhsm.Compiler.csproj" />
<ProjectReference Include="..\..\..\FDP\ExtDeps\FastHSM\src\Fhsm.SourceGen\Fhsm.SourceGen.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

This enables:
- Writing HSM doctrines with `[HsmAction]`/`[HsmGuard]` methods in `CgfNodes.cs` or a
  new `CgfHsmNodes.cs`.
- Source generator emitting `Hrot.AI.Doctrines.Generated.HsmActionRegistrar.g.cs` with
  `RegisterAll()`.

#### 1.2 — Add `HsmActionDispatcher.ClearAll()` to `Fhsm.Kernel`

The generated `HsmActionDispatcher` class needs a method to purge stale pointers from the
previous ALC before re-registration:

```csharp
// Added to HsmActionDispatcher (generated for Fhsm.Kernel assembly)
public static void ClearAll()
{
    ActionTable.Clear();
    GuardTable.Clear();
}
```

Because `HsmActionGenerator` generates `HsmActionDispatcher`, the `ClearAll()` body must
be emitted by `GenerateKernelDispatcher()` in `Fhsm.SourceGen/HsmActionGenerator.cs`.

#### 1.3 — `AiHotReloadCoordinator` (new class, in `Hrot.Editor`)

Replaces the direct use of `FbtAssemblyHotReloader` in `EditorSubsystem`. Owns its own
`FileSystemWatcher` and `AssemblyLoadContext` lifetime so the old ALC is NOT released
until the main thread has processed the reload.

```
AiHotReloadCoordinator
  - FileSystemWatcher + debounce timer (identical mechanics to FbtAssemblyHotReloader)
  - Background thread: LoadAndReload(path)
      1. Load new ALC, load assembly.
      2. Reflect AiDoctrineFactory.BuildRegistrationAction() ->
             Action<DoctrineRegistry> applyAction.
         This is the SAME reflection pattern already in EditorSubsystem lines 367-410.
      3. Build local ActionRegistry, call FbtActionRegistrar.RegisterAll(actionRegistry).
      4. Invoke applyAction(stagingRegistry) to collect BTree AND HSM blobs into a
         staging DoctrineRegistry.
      5. Enqueue main-thread callback with:
           (stagingRegistry, newAlc, old applyAction reference)
  - Main-thread drain (DrainPendingCallbacks()):
      6. HsmActionDispatcher.ClearAll()
      7. Reflect Hrot.AI.Doctrines.Generated.HsmActionRegistrar.RegisterAll()
         on the new assembly.
      8. Apply staging registry to live DoctrineRegistry.
      9. For each machine ID in staging registry's HSM doctrines: iterate over all
        matching chunks in `world.Query<BrainHsmNN>()` and call
        `HotReloadManager.TryReload()` with each chunk's component span. The FDP ECS
        stores components in 64KB `NativeChunk` blocks; there is no single contiguous
        `Span<T>` across the whole world (see Q6 in Open Questions). `TryReload()` must
        also force any instance currently in `InstancePhase.RTC` or
        `InstancePhase.Activity` back to `InstancePhase.Idle` before applying the
        HardReset, preventing mid-evaluation topology corruption.
     10. Release old ALC ref (set field to null; GC handles unload).
  - OnReloadCompleted / OnReloadFailed events (same as FbtAssemblyHotReloader)
  - Expose PreviousAlcRef for test verification.
```

Steps 6-10 must execute between simulation ticks (i.e., inside `DrainPendingCallbacks`,
which is called at the start of each frame before any tick system runs).

#### 1.4 — Update `AiDoctrineFactory.BuildRegistrationAction`

The existing `BuildRegistrationAction(ActionRegistry)` method returns
`Action<DoctrineRegistry>`. It must be extended to:

1. Still call `FbtActionRegistrar.RegisterAll(actionRegistry)` for BTree nodes.
2. Also call `HsmActionRegistrar.RegisterAll()` via the generated class — but NOTE: since
   `RegisterAll()` touches `HsmActionDispatcher`'s non-thread-safe Dictionary, this call
   is moved OUT of `BuildRegistrationAction` and into the main-thread callback (step 7
   above). `BuildRegistrationAction` only needs to build the `HsmDefinitionBlob` objects
   and register them into the staging `DoctrineRegistry`.
3. Build `HsmDefinitionBlob` objects for all HSM doctrines (using `HsmCompiler.Compile()`
   on the `StateMachineGraph` returned by `HsmBuilder.Build()`).
4. Register HSM doctrines via `stagingRegistry.RegisterHsmDoctrine(name, blob)` (a new
   overload or a new method to be added to `DoctrineRegistry`).

#### 1.5 — Update `EditorSubsystem`

Remove the `FbtAssemblyHotReloader _aiHotReloader` field. Replace with
`AiHotReloadCoordinator _aiCoordinator`. The coordinator accepts a reference to `_world`
(EntityRepository) so step 9 can query live ECS instances.

The `ClusterRunner/Program.cs` also creates a `FbtAssemblyHotReloader`. It must be
updated analogously or extracted into a shared helper.

### Dependency Graph (Phase 1)

```
Hrot.Editor
  -> AiHotReloadCoordinator
       -> Fbt.Kernel (FbtActionRegistrar, BehaviorTreeBlob)
       -> Fhsm.Kernel (HsmActionDispatcher, HotReloadManager, HsmDefinitionBlob)
       -> Fdp.Core (EntityRepository)
       -> Fdp.Toolkits (BrainHsm64, BrainHsm128)
Hrot.AI.Doctrines
  -> Fhsm.Kernel (new)
  -> Fhsm.Compiler (new)
  -> Fhsm.SourceGen (analyzer, new)
```

No circular references are introduced. `Hrot.Editor` already references `Fdp.Toolkits`.

---

## Phase 2 — HSM Terminal State Routing

### Problem

The entire `IsFinal` → `Terminated` chain is unimplemented:

1. `StateNode` (compiler IR) has no `IsFinal` property.
2. `HsmFlattener.BuildStateFlags()` does not emit `StateFlags.IsFinal`.
3. `HsmBuilder.StateBuilder` has no `Final()` method.
4. `HsmKernelCore` never checks `StateFlags.IsFinal` and never sets
   `InstanceFlags.Terminated`.
5. `HsmTickSystem<T>` never reads `InstanceFlags.Terminated`.
6. No `DoctrineFinishedEvent` is published for HSM doctrines.

### Design

#### 2.1 — `StateNode.IsFinal` + `StateBuilder.Final()`

Add to `Fhsm.Compiler/Graph/StateNode.cs`:

```csharp
public bool IsFinal { get; set; }
```

Add to `Fhsm.Compiler/HsmBuilder.cs` `StateBuilder` class:

```csharp
public StateBuilder Final()
{
    _state.IsFinal = true;
    return this;
}
```

#### 2.2 — `HsmFlattener.BuildStateFlags()`

Add to the existing method body (line ~191 in `HsmFlattener.cs`):

```csharp
if (node.IsFinal) flags |= StateFlags.IsFinal;
```

#### 2.3 — `HsmKernelCore` — set `InstanceFlags.Terminated` on final state entry

After every state entry (the point where `OnEntryActionId` is executed), check if the
newly-active state has `StateFlags.IsFinal`. If so, set `InstanceFlags.Terminated` in
the instance header:

```csharp
// After executing OnEntry for a newly-entered state:
if ((state.Flags & StateFlags.IsFinal) != 0)
{
    ref InstanceHeader hdr = ref Unsafe.As<TInstance, InstanceHeader>(ref instance);
    hdr.Flags |= InstanceFlags.Terminated;
}
```

The `InstanceHeader` is the first 16 bytes of every HSM instance tier
(`BrainHsm64`, `BrainHsm128`). The cast via `Unsafe.As` is safe because
`InstanceHeader` is explicitly layout-compatible by design (see
`Fhsm.Kernel/Data/InstanceHeader.cs`, offset 0).

Final states must also not be re-entered or transitioned out of. The existing kernel
guard logic already prevents transitions from states with no registered transitions,
which is the normal case for final states. No additional kernel change is needed for
that constraint.

#### 2.4 — `HsmTickSystem<T>` — publish `DoctrineFinishedEvent`

Mirror the deduplication pattern from `BTreeTickSystem`:

```csharp
// Fields (initialized in module registration):
private readonly Dictionary<int, uint> _publishedTerminalForInstanceId = new();
private readonly HashSet<int>          _seenThisFrame                  = new();
private readonly List<int>             _staleKeys                      = new();

// At the top of the entity loop:
_seenThisFrame.Clear();

// In tick loop, after HsmKernel.Update():
_seenThisFrame.Add(entity.Index);
ref var hdr = ref Unsafe.As<T, InstanceHeader>(ref component);
if ((hdr.Flags & InstanceFlags.Terminated) != 0)
{
    uint instanceId = doctrine.InstanceId; // matches BTreeTickSystem's dedup contract
    int  entityIdx  = entity.Index;
    if (!_publishedTerminalForInstanceId.TryGetValue(entityIdx, out uint prev)
        || prev != instanceId)
    {
        _publishedTerminalForInstanceId[entityIdx] = instanceId;
        _eventBus.Publish(new DoctrineFinishedEvent { Entity = entity });
    }
}

// After the entity loop — prune stale keys to prevent unbounded growth:
_staleKeys.Clear();
foreach (var key in _publishedTerminalForInstanceId.Keys)
    if (!_seenThisFrame.Contains(key)) _staleKeys.Add(key);
foreach (var key in _staleKeys) _publishedTerminalForInstanceId.Remove(key);
```

`DoctrineFinishedEvent` and `_eventBus` are already present in the toolkit. The
deduplication value is `doctrine.InstanceId` (consistent with `BTreeTickSystem`). The
stale-key pruning loop mirrors `BTreeTickSystem`'s pattern and prevents unbounded
dictionary growth when entities are destroyed.

**Terminal state latch fix**: After publishing the event, clear `InstanceFlags.Terminated`
and reset `header.Phase` to `InstancePhase.Idle`:

```csharp
// Immediately after _eventBus.Publish(...):
hdr.Flags &= ~InstanceFlags.Terminated;
hdr.Phase  = InstancePhase.Idle;
```

Without this, if the mission director assigns a new doctrine (bumping `doctrine.InstanceId`),
the sticky `Terminated` flag from the previous run causes the very first tick of the new
doctrine to instantly publish another `DoctrineFinishedEvent`, skipping the new phase
entirely. `DoctrineIngressSystem` (see the DoctrineIngressSystem section below) provides
defense-in-depth by also resetting HSM state on every doctrine assignment.

---

## Phase 3 — Cognitive Interrupt Decoupling

### Problem

`HsmDamageBridgeSystem` has two hard-coded ECS queries for `BrainHsm64` and `BrainHsm128`
and knows nothing about BTree entities. Damage events for BTree units are handled via
Observer nodes that read shared ECS components — there is no corresponding single-writer
for the signal that both paradigms can read uniformly.

The design talk identifies this as the "separate bridge → shared blackboard" migration.

### Design

#### 3.1 — Reserved Interrupt Registers in `BrainBlackboard`

`BrainBlackboard.Memory` is a 128-byte fixed buffer. The last two bytes are reserved for
interrupt signals:

| Byte index | Signal            | Written by                  | Read by |
|------------|-------------------|-----------------------------|---------|
| 126        | MobilityLost (1=set, 0=clear) | `CognitiveInterruptSystem` | `HsmTickSystem<T>`, BTree Observer nodes |
| 127        | (reserved for future interrupts) | — | — |

The reserved layout must be documented in `BrainBlackboard`'s source file comment.

Interrupt bytes behave as **single-frame pulses**: they are written to `1` by
`CognitiveInterruptSystem` on the frame an edge is detected (capability transitions from
capable to incapable), read by all subscribers during that same frame (HSM tick injects
the event; BTree Observer reads the flag), and then unconditionally zeroed by
`CognitiveCleanupSystem` at the very end of the frame.

This prevents a permanent soft-lock that would otherwise arise if a BTree-brained
entity's interrupt byte were set but never consumed: `HsmTickSystem<T>` skips all
non-HSM entities, so without the cleanup system no other code would ever clear the byte.

#### 3.2 — `CognitiveInterruptSystem` (new class)

Replaces `HsmDamageBridgeSystem`. Lives in `Fdp.Toolkits/Behavior/Systems/`.

The system uses **edge-triggered detection** with a `PreviousCapabilities` component to
set the interrupt byte only on the frame a capability is lost — not every frame while the
unit remains incapacitated:

```csharp
// Single query: all entities with BrainBlackboard + ActorCapabilityState + PreviousCapabilities
// (covers BOTH BTree and HSM units)
foreach (var entity in world.Query<BrainBlackboard, ActorCapabilityState, PreviousCapabilities>())
{
    ref var bb   = ref entity.Get<BrainBlackboard>();
    ref var curr = ref entity.Get<ActorCapabilityState>();
    ref var prev = ref entity.Get<PreviousCapabilities>();

    bool wasAbleToMove = (prev.Capabilities & ActorCapabilities.CanMove) != 0;
    bool canMoveNow    = (curr.Capabilities & ActorCapabilities.CanMove) != 0;

    if (wasAbleToMove && !canMoveNow)
        bb.Memory[InterruptRegister_MobilityLost] = 1;

    prev.Capabilities = curr.Capabilities;
}
```

The byte index must be a named constant: `internal const int InterruptRegister_MobilityLost = 126;`.

The system runs in the same slot `HsmDamageBridgeSystem` occupied in
`CognitiveRuntimeModule` — before `HsmTickSystem<T>` and `BTreeTickSystem`.

#### 3.3 — `HsmTickSystem<T>` — inject interrupt events

Before calling `HsmKernel.Update()`, read the interrupt byte and inject the HSM event if
set. Do NOT clear the byte here — clearing is handled by `CognitiveCleanupSystem` after
all tick systems have run (§ 3.5):

```csharp
ref var bb = ref entity.Get<BrainBlackboard>();
if (bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] == 1)
    HsmEventQueue.TryEnqueue(ref component, EventId_MobilityLost);
// Byte 126 is zeroed by CognitiveCleanupSystem at end of frame.
```

`EventId_MobilityLost` is the same constant already used by `HsmDamageBridgeSystem`.

BTree Observer nodes read byte 126 as a pulse during the same frame and do not need to
consume it; `CognitiveCleanupSystem` clears it uniformly for all entity types.

#### 3.4 — `CognitiveRuntimeModule` update

Remove `HsmDamageBridgeSystem` registration. Add `CognitiveInterruptSystem` and
`CognitiveCleanupSystem`:

```
Before: ChannelArbitrationSystem, HsmDamageBridgeSystem, BTreeTickSystem, HsmTickSystem<128>, HsmTickSystem<64>
After:  ChannelArbitrationSystem, CognitiveInterruptSystem, BTreeTickSystem, HsmTickSystem<128>, HsmTickSystem<64>, CognitiveCleanupSystem
```

#### 3.5 — `CognitiveCleanupSystem` (new class)

Runs last in `CognitiveRuntimeModule`, after both `BTreeTickSystem` and all
`HsmTickSystem<T>` registrations. Unconditionally zeros all interrupt register bytes
for every entity with a `BrainBlackboard`, making them single-frame pulses regardless of
brain tier:

```csharp
internal sealed class CognitiveCleanupSystem : ISystem
{
    public void Update(EntityRepository world, float deltaTime)
    {
        foreach (var entity in world.Query<BrainBlackboard>())
        {
            ref var bb = ref entity.Get<BrainBlackboard>();
            bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] = 0;
            bb.Memory[127] = 0; // reserved; cleared proactively
        }
    }
}
```

The system does not check brain tier. Its sole responsibility is to ensure interrupt
bytes are never read as stale on the following frame.

---

## Phase 4 — Shared AI Node Attributes

### Problem

Conditions and actions that are logically the same across BTree and HSM doctrines must
currently be duplicated: once as a `NodeLogicDelegate` with `[BTreeCondition]` for BTree,
and once as an unmanaged static with `[HsmGuard]` for HSM. There is no way to annotate a
single method and have both source generators pick it up.

### Design

#### 4.1 — New Attributes in `Fbt.Kernel`

Place the shared attributes in `Fbt.Kernel` (namespace `Fbt.Kernel`), which is already
referenced by both `Hrot.AI.Doctrines` (for BTree use) and can be referenced by
`Fhsm.SourceGen` (for HSM use, since source generators reference assemblies by metadata
only, not by runtime linking):

```csharp
/// <summary>
/// Marks a static method as a shared AI condition usable from both BTree and HSM doctrines.
/// Signature: static bool MethodName(ref TValue dto, Entity self, EntityRepository repo)
/// TValue must be the type of the field <paramref name="fieldName"/> on <paramref name="dtoType"/>.
/// The source generator computes the byte offset of that field within the parent DTO via
/// Roslyn's semantic model and emits adapters keyed as "{MethodName}@{computedOffset}".
/// Apply multiple times on the same method to share it across different parent DTOs.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SharedAiConditionAttribute : Attribute
{
    /// <summary>The parent DTO struct that contains the projected field.</summary>
    public Type DtoType { get; }
    /// <summary>Name of the field within <see cref="DtoType"/> that TValue is projected from.</summary>
    public string FieldName { get; }
    public SharedAiConditionAttribute(Type dtoType, string fieldName)
    {
        DtoType   = dtoType;
        FieldName = fieldName;
    }
}

/// <summary>
/// Marks a static method as a shared AI action usable from both BTree and HSM doctrines.
/// Signature: static NodeStatus MethodName(ref TValue dto, Entity self, EntityRepository repo)
/// HSM adapter discards the NodeStatus return (HSM is event-driven, not polling).
/// Apply multiple times on the same method to share it across different parent DTOs.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SharedAiActionAttribute : Attribute
{
    public Type   DtoType   { get; }
    public string FieldName { get; }
    public SharedAiActionAttribute(Type dtoType, string fieldName)
    {
        DtoType   = dtoType;
        FieldName = fieldName;
    }
}
```

`Fhsm.SourceGen` scans for these attributes **by fully qualified name** (string comparison
on `INamedTypeSymbol.ToDisplayString()`), so it does NOT need a compile-time reference to
`Fbt.Kernel`. Only `Hrot.AI.Doctrines` needs a runtime reference to `Fbt.Kernel` to use
the attributes — which it already has via `Fdp.Toolkits` → `Fbt.Kernel`.

#### 4.2 — BTree Adapter in `Fbt.SourceGen`

`BTreeActionGenerator` is extended to scan for `[SharedAiCondition]` and
`[SharedAiAction]` in addition to `[BTreeCondition]` and `[BTreeAction]`.

For a `[SharedAiCondition(typeof(CombatParams), nameof(CombatParams.Weapon))]` method,
the generator resolves `offset = offsetOf(CombatParams, "Weapon")` by analyzing the
struct layout via Roslyn's semantic model, then emits:

```csharp
// Emitted into FbtActionRegistrar.g.cs alongside existing entries
// Offset resolved at generation time from CombatParams.Weapon field layout
actionRegistry.RegisterCondition(
    "ConditionName@16",      // compound key: "{MethodName}@{computedOffset}"
    static (ref BrainBlackboard bb, BTreeContext ctx) =>
    {
        ref WeaponParams dto = ref Unsafe.As<byte, WeaponParams>(
            ref Unsafe.AddByteOffset(ref bb.Memory[0], (nint)16));
        return ConditionName(ref dto, ctx.Self, ctx.Repo);
    });
```

The compound key `"MethodName@offset"` is the registered node name in the tree JSON/DSL.
Because the offset is baked into the key at generation time rather than supplied as a
magic number by the caller, the same condition method can carry multiple
`[SharedAiCondition]` attributes for different parent DTOs and the generator emits a
separate adapter per attribute.

Existing `[BTreeCondition]`-marked methods continue to work unchanged.

#### 4.3 — HSM Adapter in `Fhsm.SourceGen`

`HsmActionGenerator` is extended to scan for `[SharedAiCondition]` and `[SharedAiAction]`.

For a `[SharedAiCondition(typeof(CombatParams), nameof(CombatParams.Weapon))]` guard
method, the generator resolves the offset by analyzing `CombatParams`'s struct layout
via Roslyn's semantic model and emits an unmanaged thunk:

```csharp
// Emitted into HsmActionRegistrar.g.cs
// Hash computed over "ConditionName@16" — same compound key as BTree adapter
HsmActionDispatcher.RegisterGuard(
    ComputeHash("ConditionName@16"),
    (IntPtr)(delegate* <void*, void*, ushort, bool>)&Guard_ConditionName_At16);

// Thunk (file-scope static, also emitted):
private static unsafe bool Guard_ConditionName_At16(
    void* instancePtr, void* contextPtr, ushort eventId)
{
    var bridge = (HsmKernelBridge*)contextPtr;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    var entity = bridge->Entity;
    ref var bb = ref entity.Get<BrainBlackboard>();
    ref WeaponParams dto = ref Unsafe.As<byte, WeaponParams>(
        ref Unsafe.AddByteOffset(ref bb.Memory[0], (nint)16));
    return ConditionName(ref dto, entity, repo);
}
```

The offset is baked into the thunk name and key at code-generation time. The same
condition method may carry multiple `[SharedAiCondition]` attributes; one thunk is
emitted per attribute.

For `[SharedAiAction]`, the thunk signature matches
`delegate* <void*, void*, HsmCommandWriter*, void>` and discards the `NodeStatus` return:

```csharp
private static unsafe void Action_MethodName_At16(
    void* instancePtr, void* contextPtr, HsmCommandWriter* writer)
{
    // ... same dto projection ...
    MethodName(ref dto, entity, repo); // NodeStatus return discarded
}
```

**ECS mutation constraint**: Shared action thunks write directly to the `EntityRepository`,
bypassing FastHSM's deferred `HsmCommandWriter` architecture. This is acceptable in
FDP's current single-threaded Simulation phase but carries a hard restriction: shared
actions must **never** make structural ECS changes (adding or removing components).
Direct structural changes during active chunk iteration corrupt the ECS chunk arrays.
Only reads and writes of fields on already-existing components are permitted.

#### 4.4 — Dependency Note: `Fhsm.SourceGen` and `Fbt.Kernel`

`Fhsm.SourceGen` must recognize `SharedAiConditionAttribute` and `SharedAiActionAttribute`
by their fully qualified names (`"Fbt.Kernel.SharedAiConditionAttribute"` etc.). Since
Roslyn source generators analyze source symbols at compile time, no runtime assembly
reference to `Fbt.Kernel` is needed inside `Fhsm.SourceGen.csproj`. This avoids a
circular-looking dependency:

```
Fhsm.SourceGen → [reads attribute metadata from user assembly] → Fbt.Kernel (no project ref needed)
```

The user assembly (`Hrot.AI.Doctrines`) references `Fbt.Kernel` at compile time (via
`Fdp.Toolkits`), making the attribute symbols visible to both generators.

---

## Phase 5 — Actuator Channel Safety

### Problem

BTree nodes and HSM states that write to `LocomotionChannel`, `WeaponChannel`, or
`InteractionChannel` must clear those channels when the node fails or the state exits.
Currently this cleanup is manual and error-prone. Missing cleanup leads to stale channel
states that persist across doctrine transitions.

`ChannelArbitrationSystem` handles doctrine-level preemption (full channel reset on
doctrine switch), but does not handle sub-doctrine-level node/state exit.

### Design

#### 5.1 — New Generator Attribute: `[WritesChannel]`

Added to `Fbt.Kernel` (alongside `SharedAiConditionAttribute`):

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class WritesChannelAttribute : Attribute
{
    public ChannelKind Channel { get; }
    public WritesChannelAttribute(ChannelKind channel) { Channel = channel; }
}

public enum ChannelKind { Locomotion, Weapon, Interaction }
```

#### 5.2 — BTree SourceGen Cleanup Wrapper

For a BTree `[BTreeAction]` method annotated with `[WritesChannel(ChannelKind.Locomotion)]`,
`BTreeActionGenerator` wraps the registered delegate:

```csharp
actionRegistry.RegisterAction(
    "MoveTo",
    static (ref BrainBlackboard bb, BTreeContext ctx) =>
    {
        var status = MoveTo(ref bb, ctx);
        if (status == NodeStatus.Failure)
        {
            ref var loco = ref ctx.Entity.Get<LocomotionChannel>();
            loco.ActiveAction     = 0;
            loco.ActionInstanceId = (ushort)(loco.ActionInstanceId + 1);
        }
        return status;
    });
```

#### 5.3 — HSM SourceGen Exit Cleanup

For an HSM `[HsmAction]` method annotated with `[WritesChannel(ChannelKind.Locomotion)]`,
`HsmActionGenerator` emits a paired `OnExit` cleanup action registered under the naming
convention `"ExitCleanup_MoveTo"`:

```csharp
private static unsafe void ExitCleanup_MoveTo(
    void* instancePtr, void* contextPtr, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)contextPtr;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    ref var loco = ref bridge->Entity.Get<LocomotionChannel>();
    loco.ActiveAction     = 0;
    loco.ActionInstanceId = (ushort)(loco.ActionInstanceId + 1);
}
```

State machine authors must wire the cleanup as the `OnExit` action for states that use
the channel-writing action as `OnEntry` or `Activity`. Source generators cannot silently
modify the user's builder chain. To enforce this at build time, the generator also emits
a **channel-safety registry** and `HsmCompiler.Compile()` invokes an extended
`HsmGraphValidator` pass:

```csharp
// Emitted by Fhsm.SourceGen into HsmActionRegistrar.g.cs
public static readonly IReadOnlyDictionary<string, string> RequiredExitCleanups =
    new Dictionary<string, string>
    {
        ["MoveTo"]       = "ExitCleanup_MoveTo",
        ["FireAtTarget"] = "ExitCleanup_FireAtTarget",
        // ... one entry per [WritesChannel]-annotated action ...
    };
```

`HsmGraphValidator` is extended with a channel-safety check: for every state in the
graph that references an action key present in `RequiredExitCleanups` as `OnEntry` or
`Activity`, the validator throws a descriptive build-time error if the state does not
also register the corresponding cleanup string as `OnExit`. The error message names the
offending state and the missing cleanup key, making the omission impossible to miss.

---

## DoctrineIngressSystem — HSM State Reset on Doctrine Transition

### Gap

`DoctrineIngressSystem` correctly resets `BrainBTreeState.State = default` when a BTree
doctrine is assigned. It does **not** touch `BrainHsm64` or `BrainHsm128`.

If the mission director transitions an entity to a different HSM doctrine (e.g., from
`Idle_HSM` to `Combat_HSM`), the new state machine ticks against the stale execution
state from the previous run: active leaf IDs, lifecycle phase, event queues, and history
slots all carry over. The result is garbage evaluations and potential out-of-bounds
state-slot accesses in the new doctrine's topology.

### Fix

Extend `DoctrineIngressSystem` so that when an HSM doctrine is assigned (detected by
`BrainTier == BrainTierHsm64` or `BrainTierHsm128`), it resets the corresponding
`BrainHsm64`/`BrainHsm128` component. The reset must:

1. Scrub active-leaf IDs, event queues, and history slots. Use the same reset helpers
   that `HotReloadManager.HardReset` uses (`ClearInstance64State` /
   `ClearInstance128State`) rather than duplicating the logic.
2. Clear `InstanceFlags.Terminated` in `InstanceHeader.Flags` — defense-in-depth against
   the Terminal State Latch bug described in Phase 2.
3. Reset `InstanceHeader.Phase` to `InstancePhase.Idle`.
4. Set `InstanceHeader.MachineId` to the new doctrine's machine ID.

The existing BTree reset (`BrainBTreeState.State = default`) must remain unchanged.
Verify the location of `DoctrineIngressSystem` at task start (likely `Hrot.CGF` or
`Fdp.Toolkits`).

---

## Data Flow Summary

```
Hot Reload (Phase 1)
  File change detected
    -> background: load ALC, build staging registry (BTree + HSM blobs)
    -> enqueue main-thread callback
  Main thread (DrainPendingCallbacks):
    -> HsmActionDispatcher.ClearAll()
    -> HsmActionRegistrar.RegisterAll()    [new ALC pointers]
    -> apply staging registry to live DoctrineRegistry
    -> HotReloadManager.TryReload() for each HSM doctrine
    -> release old ALC ref

Frame tick (Phase 2 + 3)
  CognitiveInterruptSystem:
    -> read ActorCapabilityState (edge-triggered), write interrupt bytes to BrainBlackboard
  BTreeTickSystem:
    -> tick BTree, publish DoctrineFinishedEvent on Success/Failure
    -> BTree Observer nodes poll blackboard interrupt bytes natively
  HsmTickSystem<T>:
    -> read blackboard interrupt bytes -> inject HsmEvents (Phase 3)
    -> HsmKernel.Update()
    -> check InstanceFlags.Terminated -> publish DoctrineFinishedEvent + clear flag (Phase 2)
  CognitiveCleanupSystem:
    -> zero all interrupt register bytes (single-frame pulse enforcement)

Shared node authoring (Phase 4)
  [SharedAiCondition(typeof(TParentDto), nameof(TParentDto.Field))]
    static bool Condition(ref TField dto, Entity e, EntityRepository repo)
    -> generator resolves offset from TParentDto.Field struct layout
    -> BTree: registered as NodeLogicDelegate "Condition@{offset}"
    -> HSM:   registered as unmanaged guard thunk "Condition@{offset}"
```

---

## Affected Project List

| Project | Change type |
|---------|-------------|
| `Hrot.AI.Doctrines` | Add Fhsm project refs; add HSM doctrine methods |
| `Hrot.Editor` | Replace `FbtAssemblyHotReloader` with `AiHotReloadCoordinator` |
| `Hrot.CGF` | Update if it also creates a hot reloader |
| `Fhsm.Kernel` | Add `ClearAll()` to generated `HsmActionDispatcher` (via SourceGen change) |
| `Fhsm.Compiler` | Add `StateNode.IsFinal`, `StateBuilder.Final()`, `HsmFlattener` update |
| `Fhsm.Kernel` (core) | Implement `StateFlags.IsFinal` → `InstanceFlags.Terminated` in `HsmKernelCore` |
| `Fdp.Toolkits` | `HsmTickSystem<T>` terminal detection + interrupt ingestion (no consume); `CognitiveInterruptSystem` (new, edge-triggered); `CognitiveCleanupSystem` (new); `CognitiveRuntimeModule` registration order; `HsmDamageBridgeSystem` removal |
| `Hrot.CGF` (or `Fdp.Toolkits`) | `DoctrineIngressSystem`: reset `BrainHsm64`/`BrainHsm128` on HSM doctrine assignment |
| `Fbt.Kernel` | New `SharedAiConditionAttribute`, `SharedAiActionAttribute`, `WritesChannelAttribute` |
| `Fbt.SourceGen` | Extend `BTreeActionGenerator` for shared attributes and channel cleanup |
| `Fhsm.SourceGen` | Extend `HsmActionGenerator` for shared attributes (guard/action thunks) and channel cleanup |

---

## Open Questions and Risks

### Q1: HsmInstance256 / BrainHsm256

`HsmInstance256` exists in `Fhsm.Kernel` but no `BrainHsm256` ECS component exists.
`HsmTickSystem<T>` is generic, so it could technically support 256-byte instances.
A future task should add `BrainHsm256` if any doctrine's HSM state exceeds 128 bytes.
This design does not add it; `HsmTickSystem<BrainHsm256>` is out of scope.

### Q2: BTreeTickSystem dedup key vs HsmTickSystem dedup key

`BTreeTickSystem` uses `DoctrineState.InstanceId` as the deduplication value.
`HsmTickSystem<T>` uses `InstanceHeader.Generation` as the equivalent. These are
semantically the same concept (per-incarnation unique counter) but have different field
names. This is acceptable; no unification of field names is needed.

### Q3: HsmActionDispatcher thread safety during reload

The `ActionTable`/`GuardTable` dictionaries are not thread-safe. `ClearAll()` and
`RegisterAll()` MUST execute on the main thread via `DrainPendingCallbacks()`, not on
the background reload thread. The coordinator design enforces this.

### Q4: Stale pointer window

If a main-thread frame tick runs between `ClearAll()` and `RegisterAll()`, an HSM action
lookup will find nothing (returning early without error per `ExecuteAction`'s guard).
This is a one-frame glitch acceptable for a developer hot-reload scenario.
To eliminate it: move `ClearAll()` + `RegisterAll()` into a single atomic callback
ahead of all tick systems in the same `DrainPendingCallbacks()` invocation.

### Q5: ClusterRunner hot reload path

`Hrot/Runner/Hrot.ClusterRunner/Program.cs` also uses `FbtAssemblyHotReloader`. The
same coordinator pattern applies. This is noted in the task list but may be a separate
batch depending on scope.

### Q6: `HotReloadManager.TryReload()` API — chunk-aware refactoring required

The current `HotReloadManager.TryReload<TInstance>(uint machineId, HsmDefinitionBlob newBlob,
Span<TInstance> instances)` assumes a single contiguous span of all component instances.
In the FDP ECS, components reside in 64KB `NativeChunk` blocks; there is no world-wide
contiguous span. `TryReload` must be refactored to accept an `EntityRepository` plus a
pre-built `EntityQuery`, or to accept `IEnumerable<Span<TInstance>>` (one span per
chunk), and apply `HardReset` across chunk boundaries. This is a `Fhsm.Kernel` API
change coordinated with BHU-003.
