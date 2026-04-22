# Design: CGF Scenario Serialization Correctness (cgf-scn-2)

## Overview

This workstream addresses five tightly related gaps discovered during scenario-authoring testing
in the Hrot distributed CGF cluster. The root cause of all bugs is the same: the
`FdpAutoSerializer` silently truncates data it cannot handle (fixed buffers, InlineArrays,
managed classes), and several execution-tier components lack the `[DataPolicy(DataPolicy.NoSave)]`
guard that would keep them out of scenario JSON in the first place.

Custom `IEntityScenarioTranslator` implementations (`TargetMemoryTranslator`,
`PassengerBufferTranslator`) exist for components with cross-entity references. Both are
upgraded to the Intent pattern introduced in Phase 4; the design extends that pattern to all
remaining components and fixes the underlying serializer and checkpoint infrastructure.

### Distributed Preview (Already Implemented)

`ReferencePreviewHandler` is correctly wired into every node via `HrotNodeBuilder`
(line 182: `clusterSlave.RegisterHandler(new ReferencePreviewHandler(world))`).  The
2PC orchestration through `LoadingPreview` (state 20) and `UnloadingPreview` (state 22)
is already operational.  No new tasks are needed for distributed preview.

---

## Phase 1: DataPolicy Cleanup and Execution-State Exclusion

**Goal:** Prevent runtime execution buffers from polluting the scenario JSON DOM.  Fix
a misleading XML comment that conflates scenario saving with checkpointing.

### Background

Scenarios and Checkpoints use two entirely separate persistence paths governed by
different `DataPolicy` bitmasks:

- **Scenarios** — processed by `ScenarioSerializer` / `FdpAutoSerializer`, which respects
  `ComponentTypeRegistry.GetSaveableTypeIds()`.  Components carrying `DataPolicy.NoSave`
  are excluded from the JSON DOM.

- **Checkpoints (Flight Recorder)** — processed by `CheckpointIOWorker` /
  `RecorderSystem.RecordKeyframe`, which respects `ComponentTypeRegistry.GetRecordableTypeIds()`.
  Components carrying `DataPolicy.NoRecord` are excluded from the binary `.fdp` file.

The current `DataPolicy.NoSave` XML comment incorrectly reads  
*"Exclude from Save Game / Checkpoints"*.  It should read  
*"Exclude from Scenario JSON serialization"*.

### Components to Mark `[DataPolicy(DataPolicy.NoSave)]`

| File | Component | Reason |
|---|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs` | `LocomotionChannel` | Transient execution buffer (`fixed byte Params/State`) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs` | `WeaponChannel` | Same; also has `Entity` refs inside Params buffer |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs` | `InteractionChannel` | Same |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainComponents.cs` | `BrainBTreeState` | Execution pointer (BTree node index stack) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainComponents.cs` | `BrainHsm64` | HSM execution stack |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainComponents.cs` | `BrainHsm128` | HSM execution stack |
| `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` | `SensorContactList` | Transient; raw `fixed long EntityIds`; re-acquired organically |
| `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` | `ActiveSensorTracks` | Same; Brain-side cognitive buffer |

### WeaponChannelTranslator Deletion

`Hrot/Subsystems/Hrot.SimHost/Serializers/WeaponChannelTranslator.cs` must be deleted.
It was introduced to partially preserve `WeaponChannel` across scenario round-trips.  Now that
`WeaponChannel` carries `[DataPolicy(DataPolicy.NoSave)]`, the serializer never visits it.
The active doctrine re-initializes the channel organically on the first simulation tick after
load through `DoctrineIngressSystem`.

Its registration must also be removed from `SimHostApp.cs`.

---

## Phase 2: MissionPlan Scenario Serialization

**Goal:** Persist an entity's active mission (e.g., `FireAtTarget`) into the scenario JSON
so that it is faithfully reconstructed after a round-trip.

### Root Cause of the Bug

When clicking "Save As" in `Hrot.Editor`, the mission state is missing from the output JSON.
Mission state lives in two components:

- **`ActiveMissionPlan`** — a managed Tier-2 class containing `DomainMissionPlan`
  (human-readable `BehaviorId`, `BehaviorParams` strings).  The `FdpAutoSerializer` is
  hardcoded to skip managed classes.
- **`MissionPlanQueue`** — an unmanaged struct containing a `MissionPhaseBuffer`
  (`[InlineArray]`).  The `FdpAutoSerializer` sees only the single private backing field and
  serializes one element; all other phases are lost.

Both components are currently serialized as empty / truncated JSON.

### Solution: `MissionPlanTranslator`

A single combined `IEntityScenarioTranslator` handles both components atomically.  It must:

1. **Extract**: serialize `ActiveMissionPlan.Plan` as a JSON-embedded string (using
   `HrotSerializerOptions.HrotJsonOptions`); save `MissionPlanQueue.CurrentPhase` and
   `PhaseElapsedSeconds` alongside it.
2. **Inject**: deserialize the plan JSON back to `DomainMissionPlan`; use `DoctrineRegistry`
   to map `BehaviorId` strings back to integer doctrine IDs; rebuild `MissionPlanQueue`
   (including trigger chain) atomically.

The translator resides in `Hrot/Subsystems/Hrot.SimHost/Serializers/MissionPlanTranslator.cs`.
It must take a `DoctrineRegistry` constructor parameter.

### Registration Sites

The translator must be registered at every `ScenarioSerializerBuilder` call site:

| File | Action |
|---|---|
| `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` | Add `.RegisterTranslator(new MissionPlanTranslator(doctrineRegistry))` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Add `MissionPlanTranslator`; remove `WeaponChannelTranslator` |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Add `MissionPlanTranslator` |

`DoctrineRegistry` is already constructed in each of these sites (via `CgfDoctrineSetup`);
it must be passed to the translator constructor.

### Preview-to-Scenario Extraction and "Doctrine Amnesia"

When saving a new scenario from a paused preview, execution buffers (`WeaponChannel`,
`BrainBTreeState`, etc.) are intentionally excluded.  This "amnesia" is architecturally sound
because:

- **B-Trees** are environmentally reactive: they tick from the root on load, evaluate the
  preserved `TargetMemory` (serialized via `TargetMemoryTranslator`), and branch back into the
  correct action within one tick.
- **HSMs** fast-forward: if a unit was in `Disabled` state, the preserved `Health` and
  `ActorCapabilityState` cause `HsmDamageBridgeSystem` to inject `MobilityLost` on the first
  tick, snapping the HSM back to `Disabled`.
- **Mission plans** resume: the current phase is persisted via `MissionPlanTranslator`.

The `ActiveMissionPlan` is the single source of truth for operator-commanded behavior.
Mid-preview operator overrides (issued via `ISimHostMissionSender`) are already stored there
and will be correctly serialized.

---

## Phase 3: FdpAutoSerializer Upgrade for Unmanaged Memory Layouts

**Goal:** Teach `FdpAutoSerializer` to correctly iterate `fixed` buffers and `[InlineArray]`
types for **pure scalar** payloads, so components like `BrainBlackboard` are serialized
without truncation.

### Root Cause

The C# compiler lowers `fixed` buffers to a nested struct with a single 1-byte field
(`FixedElementField`) and struct padding, and lowers `[InlineArray]` to a struct with a single
private backing element.  Reflection only sees the compiler-generated field, truncating all
data after the first element.

### Approach

In `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs`, extend the expression tree
compilation in `GetSerializableFields` and `BuildExtract` / `BuildInject`:

1. **`fixed` buffers:** Detect `FixedBufferAttribute` on a field (which exposes `ElementType`
   and `Length`).  Emit a compiled loop using `System.Runtime.CompilerServices.Unsafe.Add` to
   iterate the elements as a `JsonArray`.
2. **`[InlineArray]`:** Detect `InlineArrayAttribute` on the field's declared type (which
   exposes `Length`).  Emit a loop that casts to `Span<T>` and serializes each element.

**Critical constraint:** If `ElementType` or the inline element type is `Entity` (or any struct
embedding an `Entity`), `Build()` must throw `InvalidOperationException` naming the offending
component type and field.  The auto-serializer must never silently serialize entity handles as
raw integers, nor attempt GUID resolution itself — doing so would bypass the Intent safety
net and cause silent `Entity.Null` corruption during distributed loading.  Any component whose
unmanaged buffer contains entity references must be intercepted by a custom
`IEntityScenarioTranslator` that generates an Intent DTO (see Phase 4).

**Scope:** Only public fields of value-type components registered as `SaveableTypeIds` are
affected.  Managed classes (like `ActiveMissionPlan`) remain outside the auto-serializer scope.

### Impact on BrainBlackboard

`BrainBlackboard` has `fixed byte Memory[...]`.  After this upgrade, the auto-serializer will
emit the full byte array as a JSON array.  Doctrines that cache entity handles as packed `long`
values inside this buffer MUST NOT do so — `Build()` will throw `InvalidOperationException` if
it detects an `Entity`-typed fixed buffer; and any raw-`long` entity handles packed inside a
`byte` buffer are invisible to the constraint check and will silently become stale after a
scenario round-trip.  Cross-entity AI state must be stored in dedicated components
(e.g., `TargetMemory`) with a custom Intent-pattern translator, not packed inside `BrainBlackboard`.

---

## Phase 4: Intent Components for Cross-Entity Reference Safety in Distributed Loading

**Goal:** Prevent dangling-pointer bugs when the distributed genesis pipeline transmits entity
cross-references to cluster nodes that have not yet spawned the referenced entities.

### Architecture

An ECS `Entity` handle (Index + Generation) is a local memory pointer valid only within the
`EntityRepository` that issued it.  Even though the existing translators serialize entity
handles as stable GUID strings, the *receiving node's resolver* may return `Entity.Null`
during `Inject` if the referenced entity has not been spawned yet (genesis order is not
deterministic across the network).

The solution introduces **Intent DTOs** — managed `class` components that carry `long`
Network IDs rather than `Entity` handles.  These cross the scenario genesis boundary safely
and are late-bound to live handles by `GenesisMaterializationSystem` on the destination node.

### Intent DTO Components

Defined in `Hrot/Engine/Hrot.Common/` (accessible from both CGF and SimHost):

```
[DataPolicy(DataPolicy.Transient)]   // never saved in checkpoints
public class InitialPassengersIntent { public List<long> PassengerNetworkIds = new(); }

[DataPolicy(DataPolicy.Transient)]
public class InitialVehicleIntent { public long VehicleNetworkId; }

[DataPolicy(DataPolicy.Transient)]
public class InitialHierarchyIntent
{
    public long ParentNetworkId;
    public long FirstChildNetworkId;
    public long NextSiblingNetworkId;
}

[DataPolicy(DataPolicy.Transient)]
public class InitialRouteIntent { public long RouteNetworkId; }

[DataPolicy(DataPolicy.Transient)]
public class InitialTargetsIntent
{
    public struct TargetEntry
    {
        public long  NetworkId;
        public float PosX;
        public float PosY;
        public float Score;
        public uint  LastSeenTick;
        public byte  Modality;
    }
    public List<TargetEntry> Entries = new();
}
```

`InitialTargetsIntent` preserves the full target-memory state (positions, threat scores,
modality, last-seen tick) so that B-Trees can evaluate their threat-assessment logic
correctly on the first tick after distributed genesis without suffering amnesia.

### New Translators

| File | Translator | Consumed Component | Emits |
|---|---|---|---|
| `Hrot/Subsystems/Hrot.SimHost/Serializers/IsEmbarkedTagTranslator.cs` | `IsEmbarkedTagTranslator` | `IsEmbarkedTag` | `InitialVehicleIntent` |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/VisHierarchyNodeTranslator.cs` | `VisHierarchyNodeTranslator` | `VisHierarchyNode` | `InitialHierarchyIntent` |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/PersonalRouteRefTranslator.cs` | `PersonalRouteRefTranslator` | `PersonalRouteRef` | `InitialRouteIntent` |

Two existing translators are **refactored** to emit Intent DTOs:

| File | Translator | Consumed Component | Currently Emits | Refactored to Emit |
|---|---|---|---|---|
| `Hrot/Subsystems/Hrot.SimHost/Serializers/PassengerBufferTranslator.cs` | `PassengerBufferTranslator` | `PassengerBuffer` | `PassengerBuffer` directly | `InitialPassengersIntent` |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/TargetMemoryTranslator.cs` | `TargetMemoryTranslator` | `TargetMemory` | `TargetMemory` directly | `InitialTargetsIntent` |
The `Inject` path writes the `InitialPassengersIntent` managed component onto the entity and leaves
`PassengerBuffer` unset; `GenesisMaterializationSystem` applies the buffer once all passengers have
spawned.

### StagingEntityExtractor Patch

`Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` must intercept Intent
components in its extraction loop and remap their `long` NetworkIds using the `oldToNewMap`
generated during Pass 1, exactly as it already does for `ActiveMissionPlan` JSON strings via
`ScenarioBehaviorRemapper`.

### GenesisMaterializationSystem

A new `ComponentSystem` registered in `InitializationSystemGroup` on the SimHost node.
It polls every entity carrying an Intent component, waits for the referenced Network IDs to
appear in `NetworkEntityMap`, materializes the unmanaged components (`PassengerBuffer`,
`IsEmbarkedTag`, `VisHierarchyNode`, `PersonalRouteRef`, `TargetMemory`), and removes the
Intent component.  Entities whose referenced peers have not yet spawned are skipped and retried
on the next tick.

For `InitialTargetsIntent`, each `TargetEntry.NetworkId` is resolved independently; entries
whose Network ID cannot be resolved are dropped rather than blocking the remaining valid
targets.  Once all resolvable entries have been materialized into `TargetMemory`, the
`InitialTargetsIntent` is removed.

---

## Phase 5: Checkpoint Event Preservation

**Goal:** Persist in-flight FDP events (e.g., `WeaponFireIntent` published just before a
checkpoint snapshot) into the binary `.fdp` file so that checkpoints can be restored to a
perfectly consistent simulation state.

### Root Cause

`CheckpointIOWorker.WriteCheckpointFile` calls:

```csharp
_recorderSystem.RecordKeyframe(snapshot, bw, DateTimeOffset.UtcNow.Ticks);
// no eventBus passed -> recorder writes stream count = 0
```

Two additional gaps compound this:

1. `ReferenceCheckpointHandler.Commit` calls `snap.SyncFrom(source)` but does not copy the
   `FdpEventBus` state into the snapshot.
2. `RecorderSystem.WriteEvents` is hardcoded to read from the **Pending (Write) buffer** via
   `eventBus.PopulatePendingStreams()`.  But at checkpoint time (`ClusterSlave.Tick()`), the
   bus has already been swapped — in-flight events now reside in the **Current (Read) buffer**.

### Required Changes

#### 1. `FdpEventBus` — expose Read-buffer streams

Add `PopulateCurrentStreams(List<INativeEventStream>)` and
`PopulateCurrentManagedStreams(List<IManagedEventStreamInfo>)` methods that return streams whose
Read buffer is non-empty.  Zero-allocation (list is pre-allocated by caller).

File: `FDP/Engine/Fdp.Core/FdpEventBus.cs`

#### 2. `RecorderSystem.WriteEvents` — buffer-selection parameter

Add `bool serializeReadBuffer` parameter (default: `false`).  When `true`, the method calls
`PopulateCurrentStreams` / `PopulateCurrentManagedStreams` instead of `PopulatePendingStreams`.
The public `RecordKeyframe` and `RecordDeltaFrame` methods accept the same optional parameter
for backward compatibility.

File: `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`

#### 3. `ReferenceCheckpointHandler` — inject `EventAccumulator`

Add `EventAccumulator` to the constructor.  In `Commit()`, after `snap.SyncFrom(source)`,
call `_eventAccumulator.FlushToReplica(snap.Bus, source.GlobalVersion - 1)` to copy in-flight
events into the snapshot's bus.

File: `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceCheckpointHandler.cs`

#### 4. `CheckpointIOWorker` — pass event bus to recorder

Pass `snapshot.Bus` and `serializeReadBuffer: true` to `RecordKeyframe`:

```csharp
_recorderSystem.RecordKeyframe(snapshot, bw, DateTimeOffset.UtcNow.Ticks,
    snapshot.Bus, serializeReadBuffer: true);
```

File: `FDP/Engine/Fdp.Core/Orchestration/CheckpointIOWorker.cs`

### Why Not Save Events to Scenario JSON

Scenarios are declarative authoring templates, not state snapshots.  Events are transient bus
payloads — saving them to a scenario file would violate the State/Message boundary and introduce
race conditions on deserialization.  Event persistence applies **only** to binary checkpoints.

---

## Architectural Invariants

1. **State vs. Message boundary.** Events live exclusively in the `FdpEventBus`; they must never
   be written to scenario JSON.

2. **DataPolicy.NoSave governs scenario exclusion.**  Any component with volatile mid-tick runtime
   state (execution channels, brain pointers, sensor contacts) must carry this flag.
   `DataPolicy.NoRecord` governs checkpoint exclusion and is independent.

3. **Entity handles never cross scenario boundaries.**  All translators must convert `Entity`
   handles to stable GUID strings (or Network IDs for distributed loading) before writing to DOM.
   The `FdpAutoSerializer` must throw `InvalidOperationException` when it encounters an `Entity`
   (or struct embedding `Entity`) in a `fixed` buffer or `[InlineArray]`; silent raw-integer
   serialization of entity handles is forbidden.

4. **`ActiveMissionPlan` is the single source of truth.**  Operator commands, menu-driven
   overrides, and editor-assigned missions all flow through `ISimHostMissionSender` into
   `ActiveMissionPlan`.  This component is the only place where scenario serialization needs to
   capture behavioral intent.

5. **Checkpoint binary clone is complete.**  `DataPolicy.NoRecord` is the guard for checkpoint
   exclusion.  Items marked `DataPolicy.NoSave` only are still written to binary checkpoints.
