# Task Detail: CGF Scenario Serialization Correctness (cgf-scn-2)

**Reference:** See [DESIGN.md](./DESIGN.md) for architectural context.

---

## Phase 1: DataPolicy Cleanup and Execution-State Exclusion

### TASK-S101: Fix DataPolicy.NoSave XML Comment

**Design Reference:** DESIGN.md § Phase 1 — Background

**Scope:**
Update the XML `<summary>` on `DataPolicy.NoSave` and (for symmetry) `DataPolicy.NoRecord`
in `FDP/Engine/Fdp.Core/DataPolicyAttribute.cs`.

Not in scope: changing any enum values, flag bits, or behavior.

**Constraints:**
- Do not change the enum values or bit assignments.
- Do not alter `DataPolicy.Transient`, `NoSnapshot`, or `SnapshotViaClone`.

**Success Conditions:**

1. `DataPolicy.NoSave` XML comment reads: "Exclude from Scenario JSON serialization. Use for
   runtime execution state (e.g., BTree pointers, active weapon channels) that should be
   preserved in binary checkpoints but omitted from declarative authoring templates."
2. `DataPolicy.NoRecord` XML comment reads: "Exclude from Flight Recorder and Binary
   Checkpoints. Use for debug-only data or metrics that should not pollute binary state
   snapshots."
3. No other lines in the file are changed.
4. Project builds without errors.

---

### TASK-S102: Add DataPolicy.NoSave to Execution Channel Components

**Design Reference:** DESIGN.md § Phase 1 — Components to Mark

**Scope:**
Add `[DataPolicy(DataPolicy.NoSave)]` to `LocomotionChannel`, `WeaponChannel`, and
`InteractionChannel` in `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs`.

Not in scope: changing field definitions, sizes, or any other attribute.

**Constraints:**
- Apply only to the three named structs; do not touch other types in the file.
- Preserve the existing `[StructLayout(LayoutKind.Sequential)]` and
  `[ComponentId(...)]` attributes.

**Success Conditions:**

1. Each of the three structs carries `[DataPolicy(DataPolicy.NoSave)]`.
2. A unit test creates a fresh `ScenarioSerializerBuilder`, registers component types,
   calls `Build()`, and asserts that `autoSerializer.GetComponentName(typeIdOf(WeaponChannel))`
   returns `null` (i.e., WeaponChannel is absent from the saveable set).
3. Same assertion holds for `LocomotionChannel` and `InteractionChannel`.
4. No regression in existing tests.

---

### TASK-S103: Add DataPolicy.NoSave to Brain Execution Components

**Design Reference:** DESIGN.md § Phase 1 — Components to Mark

**Scope:**
Add `[DataPolicy(DataPolicy.NoSave)]` to `BrainBTreeState`, `BrainHsm64`, and `BrainHsm128`
in `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainComponents.cs`.

Not in scope: changing struct field definitions.

**Constraints:**
- Preserve `[StructLayout(LayoutKind.Sequential)]` and `[ComponentId(...)]`.
- Do NOT add `NoRecord` — brain execution state must still appear in binary checkpoints.

**Success Conditions:**

1. Each of the three structs carries `[DataPolicy(DataPolicy.NoSave)]`.
2. A unit test asserts `FdpAutoSerializer` (after `Build()`) has no entry for
   `BrainBTreeState`, `BrainHsm64`, or `BrainHsm128` (i.e., `GetComponentName(typeId)` == null).
3. The types are still present in `ComponentTypeRegistry.GetRecordableTypeIds()` (not NoRecord).
4. Existing tests pass.

---

### TASK-S104: Add DataPolicy.NoSave to Transient Perception Components

**Design Reference:** DESIGN.md § Phase 1 — Components to Mark

**Scope:**
Add `[DataPolicy(DataPolicy.NoSave)]` to `SensorContactList` and `ActiveSensorTracks`
in `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs`.

Not in scope: changing field definitions.

**Constraints:**
- Same rules as TASK-S103: preserve existing attributes, do not add `NoRecord`.
- `SensorContactList` and `ActiveSensorTracks` are re-acquired organically from DDS
  SensorTrackState updates on scenario start; they must not be seeded from stale JSON.

**Success Conditions:**

1. Both structs carry `[DataPolicy(DataPolicy.NoSave)]`.
2. A unit test asserts they do not appear in `ComponentTypeRegistry.GetSaveableTypeIds()`.
3. They still appear in `GetRecordableTypeIds()`.
4. Existing perception tests pass (they do not rely on scenario serialization of these types).

---

### TASK-S105: Delete WeaponChannelTranslator and Unregister It

**Design Reference:** DESIGN.md § Phase 1 — WeaponChannelTranslator Deletion

**Scope:**
Delete `Hrot/Subsystems/Hrot.SimHost/Serializers/WeaponChannelTranslator.cs`.
Remove the `.RegisterTranslator(new Hrot.SimHost.Serializers.WeaponChannelTranslator())`
call from `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`.

Not in scope: changing any other translator registration, any other files.

**Constraints:**
- TASK-S102 must be completed first (WeaponChannel must have `[DataPolicy(DataPolicy.NoSave)]`
  before this translator is deleted).
- Do not remove `TargetMemoryTranslator` or `PassengerBufferTranslator` registrations.

**Success Conditions:**

1. The file `WeaponChannelTranslator.cs` no longer exists in the repository.
2. `SimHostApp.cs` contains no reference to `WeaponChannelTranslator`.
3. Solution builds without errors (no dangling `using` or `new WeaponChannelTranslator()`).
4. Integration test: save a scenario containing an entity with a `WeaponChannel` component;
   reload it; assert the entity exists and does NOT have a `WeaponChannel` component
   (it was stripped by `DataPolicy.NoSave`).

---

## Phase 2: MissionPlan Scenario Serialization

### TASK-S201: Implement MissionPlanTranslator

**Design Reference:** DESIGN.md § Phase 2 — Solution: MissionPlanTranslator

**Scope:**
Create `Hrot/Subsystems/Hrot.SimHost/Serializers/MissionPlanTranslator.cs`.

The class is `sealed`, implements `IEntityScenarioTranslator`, and lives in
namespace `Hrot.SimHost.Serializers`.

**Constraints:**
- Constructor must accept `DoctrineRegistry registry` (not optional).
- `GetConsumedComponentsMask()` must set bits for both `ActiveMissionPlan` (type ID from
  `BehaviorApplicationComponentIds.ActiveMissionPlan`) and `MissionPlanQueue`
  (type ID from `GlobalComponentIds.MissionPlanQueue`).
- `CanTranslate` returns `true` iff the entity has `ActiveMissionPlan` (managed component).
- `Extract` must serialize `ActiveMissionPlan.Plan` using `HrotSerializerOptions.HrotJsonOptions`
  (same options used throughout the Hrot scenario pipeline).
- `Inject` must deserialize `DomainMissionPlan`, call
  `_registry.TryGetId(task.BehaviorId, out int doctrineId)` for each task, use
  `MissionTriggerHelper.ResolveTrigger` for triggers (matches `EntityMissionIngressTranslator`),
  and atomically `SetManagedComponent<ActiveMissionPlan>` and `SetComponent<MissionPlanQueue>`.
- Do NOT use `DoctrineRegistry` static methods; only the injected instance.
- The output DOM key is `"MissionPlan"` (via `GetOutputDomKeys()`).

**Success Conditions:**

1. Unit test: create an entity with `ActiveMissionPlan` (FireAtTarget, 1 task) + corresponding
   `MissionPlanQueue`; call `Extract`; assert DOM contains `"MissionPlan"` key with
   `"PlanData"`, `"CurrentPhase"`, `"PhaseElapsedSeconds"` fields.
2. Unit test: call `Inject` with the DOM from above; assert entity has `ActiveMissionPlan`
   with `Plan.Tasks[0].BehaviorId == "FireAtTarget"` and a matching `MissionPlanQueue` with
   `Phases[0].DoctrineId` equal to the registry-resolved ID.
3. Unit test: entity with no `ActiveMissionPlan`; `CanTranslate` returns `false`.
4. Round-trip test: save a scenario with a mission entity via `ScenarioSerializer`; reload;
   assert `ActiveMissionPlan.Plan.Tasks.Count == original count` and all `BehaviorId` strings
   match.

---

### TASK-S202: Register MissionPlanTranslator at All Serializer Sites

**Design Reference:** DESIGN.md § Phase 2 — Registration Sites

**Scope:**
Register `MissionPlanTranslator` at three call sites:

1. `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` — `CreateFileService()`
2. `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` — `ScenarioSerializerBuilder` block
3. `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` — `ScenarioSerializerBuilder` block

Each site must obtain a `DoctrineRegistry` instance and pass it to the translator constructor.
`DoctrineRegistry` is already constructed in SimHost and CGF contexts via
`CgfDoctrineSetup.CreateDoctrineRegistry()` (or equivalent); for `EditorBootstrap`, the
same factory must be called.

Not in scope: changing any other translator registration.

**Constraints:**
- TASK-S201 must be completed first.
- TASK-S105 must be completed first (WeaponChannelTranslator already removed from SimHostApp).
- The `MissionPlanTranslator` must be registered BEFORE `.Build()` is called.
- Do not change the subsystem type string passed to `ScenarioSerializerBuilder`.

**Success Conditions:**

1. Integration test (`EditorFileOpsIntegrationTests` or new test): save a scenario from
   `EditorBootstrap.CreateFileService()` containing an entity with `ActiveMissionPlan`;
   assert the resulting JSON contains `"MissionPlan"` as a DOM key under the entity.
2. Solution builds without errors on all three affected projects.
3. Existing scenario round-trip tests in `Hrot.SimHost.Tests` still pass.

---

## Phase 3: FdpAutoSerializer Upgrade for Unmanaged Memory Layouts

### TASK-S301: FdpAutoSerializer — fixed Buffer Expression Trees

**Design Reference:** DESIGN.md § Phase 3 — Approach

**Scope:**
Update `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs`.

Extend `GetSerializableFields` to detect fields decorated with
`System.Runtime.CompilerServices.FixedBufferAttribute`.  Extend `BuildExtract` and `BuildInject`
to emit loops over the buffer elements instead of treating the backing struct as a single value.

**Constraints:**
- Only unmanaged scalar element types are in scope: `byte`, `sbyte`, `short`, `ushort`, `int`,
  `uint`, `long`, `ulong`, `float`, `double`.
- If `FixedBufferAttribute.ElementType` is `Entity` (or any struct that contains an `Entity`
  field), `Build()` must throw `InvalidOperationException` with a message that names the
  offending component type and field name.  The auto-serializer must never silently emit raw
  integer values for entity handles, nor attempt GUID resolution internally.
- If the element type is unsupported for any other reason, the field is skipped and a warning
  is logged (consistent with the existing scalar-skip behaviour).
- The loop must be compiled via `Expression.Loop` / `Expression.Block` using
  `System.Runtime.CompilerServices.Unsafe.Add` (zero-allocation, no `Marshal.AllocHGlobal`).
- The generated JSON value for a `fixed byte` buffer is a `JsonArray` of integers.
- This change must not break any existing auto-serializer tests.

**Success Conditions:**

1. Unit test: define a component with `fixed byte Data[4]` values `{1,2,3,4}`; auto-serialize it;
   assert JSON contains `"Data": [1,2,3,4]`.
2. Unit test: inject from `"Data": [5,6,7,8]`; assert the component field contains those values.
3. Unit test: define a component with `fixed long EntityIds[2]`; call `Build()`; assert
   `InvalidOperationException` is thrown containing the field name in the message.
4. `BrainBlackboard` round-trip: create a component with non-zero `Memory` bytes; auto-serialize
   and inject; assert byte-for-byte identity.

---

### TASK-S302: FdpAutoSerializer — InlineArray Expression Trees

**Design Reference:** DESIGN.md § Phase 3 — Approach

**Scope:**
Extend `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` to handle
`[InlineArray]` types.

A field whose declared type carries `System.Runtime.CompilerServices.InlineArrayAttribute`
must be serialized as a `JsonArray` of length `InlineArrayAttribute.Length`, using
`MemoryMarshal.CreateSpan` or equivalent to read elements.

**Constraints:**
- Same element-type constraints as TASK-S301.
- `MissionPlanQueue.Phases` is a `MissionPhaseBuffer` (`[InlineArray(8)]`); its element type
  `MissionPhase` is a struct with multiple fields.  Nested struct serialization must use the
  existing field-traversal logic recursively (or serialize each `MissionPhase` as a `JsonObject`).
- If the element type is `Entity` or a struct embedding `Entity`, `Build()` must throw
  `InvalidOperationException` — same rule as TASK-S301.  No silent GUID resolution.
- Must not affect the hot path for components without fixed buffers or inline arrays.

**Success Conditions:**

1. Unit test: define a struct with a `[InlineArray(3)]` of `float`; serialize; assert JSON
   array length is 3 with correct values.
2. Unit test: inject from JSON array; assert all three values are restored.
3. `MissionPlanQueue` auto-serialization round-trip (if `DataPolicy.NoSave` is NOT applied):
   a queue with 2 phases survives a JSON round-trip with all `DoctrineId` and `Trigger`
   values intact. (Note: in production, `MissionPlanTranslator` handles `MissionPlanQueue`
   and consumes it from the auto-serializer mask; this test verifies the underlying
   infrastructure works.)

---

## Phase 4: Intent Components for Cross-Entity Reference Safety

### TASK-S401: Define Intent DTO Components

**Design Reference:** DESIGN.md § Phase 4 — Intent DTO Components

**Scope:**
Create `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs` containing:
`InitialPassengersIntent`, `InitialVehicleIntent`, `InitialHierarchyIntent`,
`InitialRouteIntent`, and `InitialTargetsIntent`.

`InitialTargetsIntent` carries a `List<TargetEntry>` where each entry holds the Network ID
plus all associated scoring/position/modality data from `TargetMemory`:
```csharp
public struct TargetEntry
{
    public long  NetworkId;
    public float PosX;
    public float PosY;
    public float Score;
    public uint  LastSeenTick;
    public byte  Modality;
}
```

Each class is a managed `class` decorated with `[DataPolicy(DataPolicy.Transient)]`.
Each class must have `[ComponentId(...)]` with a new ID allocated in the appropriate
Hrot component ID file.

**Constraints:**
- These are **managed** components (class, not struct) so they can hold `List<long>`.
- All must use `DataPolicy.Transient` (excluded from both snapshots and checkpoints).
- The `long` values are Network IDs, not raw `Entity.PackedValue`.
- Project `Hrot.Common` must already reference `Fdp.Core` for `DataPolicy`.

**Success Conditions:**

1. Each class (including `InitialTargetsIntent`) is registered via `repo.RegisterManagedComponent<T>()`
   in a unit test without error.
2. After a component type is registered, `ComponentTypeRegistry.GetType(id)` returns the correct
   CLR type.
3. `DataPolicy.Transient` is verified for each type via `DataPolicyHelper` or equivalent.

---

### TASK-S402: Translators for VisHierarchyNode, IsEmbarkedTag, PersonalRouteRef

**Design Reference:** DESIGN.md § Phase 4 — New Translators

**Scope:**
Create three new `IEntityScenarioTranslator` implementations in `Hrot/Subsystems/Hrot.SimHost/Serializers/`:

- `VisHierarchyNodeTranslator` — consumes `VisHierarchyNode`; extracts 3 entity GUIDs
  (`Parent`, `FirstChild`, `NextSibling`) → `InitialHierarchyIntent` Network IDs.
- `IsEmbarkedTagTranslator` — consumes `IsEmbarkedTag`; extracts `VehicleEntity` → `InitialVehicleIntent`.
- `PersonalRouteRefTranslator` — consumes `PersonalRouteRef`; extracts `RouteEntity` → `InitialRouteIntent`.

Each translator's `Inject` writes the corresponding Intent component onto the entity (NOT the
original structural component) and returns.  `GenesisMaterializationSystem` (TASK-S404) does
the final binding.

**Constraints:**
- TASK-S401 must be completed first.
- `IGuidResolver.Resolve(Entity)` is called during `Extract` to get Network IDs (not raw packed
  values).  On the extract side, Network IDs are obtained by calling
  `IGuidResolver.Resolve(Entity) → string (GUID)` and then looking up the entity's
  `NetworkIdentity.NetworkId` directly from the ECS world, whichever is accessible.
  
  Clarification: use the GUID resolver to get a stable reference; the `InitialHierarchyIntent`
  stores `long` NetworkIds which map 1:1 with GUIDs for same-session round-trips.  The
  translators must store GUID strings in the DOM (as with all other translators) and resolve
  them back via `IGuidResolver.Resolve(string)` during `Inject` — but only to obtain the
  Network ID (from `repo.GetComponent<NetworkIdentity>(entity).NetworkId`), NOT to obtain a
  live `Entity` handle.
- `CanTranslate` returns false if the entity does not carry the consumed component.
- `GetOutputDomKeys()` must return the DOM key name to prevent the auto-serializer from
  attempting to process it.

**Success Conditions:**

1. Unit test: entity with `VisHierarchyNode {Parent=e1, FirstChild=e2, NextSibling=e3}`;
   call `Extract`; assert DOM contains `"VisHierarchyNode"` with 3 GUID strings.
2. Unit test: call `Inject` with that DOM; assert entity now carries `InitialHierarchyIntent`
   with correct Network IDs (resolved from the GUIDs via the repository's network identity map).
3. Same for `IsEmbarkedTag` and `PersonalRouteRef`.
4. Entities with no such component: `CanTranslate` returns `false`; no DOM entry written.

---

### TASK-S403: Update PassengerBufferTranslator to Emit Intent

**Design Reference:** DESIGN.md § Phase 4 — New Translators (PassengerBufferTranslator update)

**Scope:**
Modify the `Inject` method of
`Hrot/Subsystems/Hrot.SimHost/Serializers/PassengerBufferTranslator.cs`.

Currently, `Inject` constructs a `PassengerBuffer` directly and calls `repo.SetComponent`.
Change it to write an `InitialPassengersIntent` managed component instead.  The `Extract`
side is unchanged (it still serializes GUID strings).

**Constraints:**
- TASK-S401 must be completed first.
- The `Extract` path must remain unchanged — GUID strings in the DOM, not raw NetworkIds.
- Only the `Inject` path changes.
- The old direct `repo.SetComponent(entity, buffer)` call is replaced with
  `repo.SetManagedComponent(entity, new InitialPassengersIntent { ... })`.

**Success Conditions:**

1. Unit test: `Inject` with a DOM containing 2 passenger GUIDs; assert entity has
   `InitialPassengersIntent` with `PassengerNetworkIds.Count == 2`.
2. Assert entity does NOT have `PassengerBuffer` immediately after `Inject`.
3. Existing round-trip tests that relied on `PassengerBuffer` being set directly must be
   updated to account for the deferred materialization.

---

### TASK-S404: Implement GenesisMaterializationSystem

**Design Reference:** DESIGN.md § Phase 4 — GenesisMaterializationSystem

**Scope:**
Create `Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs`.

The system runs in `InitializationSystemGroup` on the SimHost node.  Each `OnUpdate`, it queries
for entities carrying any Intent component, attempts to resolve the Network IDs from
`NetworkEntityMap`, and writes the final unmanaged component once all referenced entities are
alive.

**Constraints:**
- TASK-S401 and TASK-S402/S403 must be completed first.
- Use a single `OnUpdate` that handles all Intent types in one pass.
- If any referenced Network ID cannot be resolved (`NetworkEntityMap.TryGetEntity` returns
  false, or the entity is not alive), the whole entity is skipped for this tick; the Intent
  component is NOT removed.
- After successful materialization: set the structural component; remove the Intent via
  `repo.RemoveManagedComponent<T>(entity)`.
- The system must be registered in `SimHostApp.cs` `InitializationSystemGroup`.
- Do not spin-wait; simply skip unresolved entities each tick.

**Success Conditions:**

1. Unit test: spawn two entities (A and B); add `InitialPassengersIntent` to A referencing B's
   Network ID; tick the system; assert A now has `PassengerBuffer` with `Passengers[0] == entityB`
   and no `InitialPassengersIntent`.
2. Unit test: same scenario but B is not yet alive; tick once; assert A still has
   `InitialPassengersIntent` (deferred).  Make B alive; tick again; assert materialization.
3. Same tests for `InitialVehicleIntent` → `IsEmbarkedTag`, `InitialHierarchyIntent` →
   `VisHierarchyNode`, `InitialRouteIntent` → `PersonalRouteRef`.
4. Unit test for `InitialTargetsIntent` → `TargetMemory`: spawn entity A (the unit) and
   entity B (the target); add `InitialTargetsIntent` to A with one `TargetEntry` referencing
   B's Network ID; tick the system; assert A now has `TargetMemory` with `Count == 1`,
   `EntityIds[0] == entityB.PackedValue`, and the score/position/modality values match the
   `TargetEntry`; assert `InitialTargetsIntent` is removed.
5. Unit test: `InitialTargetsIntent` entry referencing an unknown Network ID; assert that
   entry is silently dropped (not blocking) and the remaining valid entries are materialized.

---

### TASK-S405: Patch StagingEntityExtractor for Intent NetworkId Remapping

**Design Reference:** DESIGN.md § Phase 4 — StagingEntityExtractor Patch

**Scope:**
Modify `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs`.

In the extraction loop, after all component lists are assembled, intercept each Intent
component and remap its `long` NetworkIds using the `oldToNewMap` produced during Pass 1.

**Constraints:**
- TASK-S401 must be completed first.
- The remapping logic mirrors what `ScenarioBehaviorRemapper` already does for
  `ActiveMissionPlan` JSON strings.
- If an ID is not in `oldToNewMap` (e.g., references a pre-existing entity not loaded as part
  of this scenario batch), leave the ID unchanged.
- Do not change the Pass 1 ID allocation logic.

**Success Conditions:**

1. Unit test: load a scenario with two entities (A references B); assert that after extraction,
   `InitialPassengersIntent` on A's `EntityCreationRequest` carries B's *new* Network ID (not
   the original one from the scenario file).
2. Unit test: entity referencing an ID not in the map; assert the original ID is preserved
   unchanged.
3. Existing `StagingEntityExtractorTests` still pass.

---

### TASK-S406: Refactor TargetMemoryTranslator to Emit InitialTargetsIntent

**Design Reference:** DESIGN.md § Phase 4 — New Translators (TargetMemoryTranslator refactoring)

**Scope:**
Modify `Hrot/Subsystems/Hrot.SimHost/Serializers/TargetMemoryTranslator.cs`.

The `Extract` path is unchanged (it already serializes GUID strings and all score/position data).
The `Inject` path must be rewritten: instead of resolving GUIDs to `Entity` handles and writing
`TargetMemory` directly, it must write an `InitialTargetsIntent` managed component onto the entity
and return.  `GenesisMaterializationSystem` (TASK-S404) does the final binding.

Registration of `TargetMemoryTranslator` at all sites
(`SimHostApp.cs`, `EditorSubsystem.cs`, `UrbanCombatFileLifecycleTests.cs`) requires no change;
the translator keeps the same name and `GetConsumedComponentsMask()`.

**Constraints:**
- TASK-S401 must be completed first (`InitialTargetsIntent` must exist).
- TASK-S404 must be completed or in-progress concurrently (`GenesisMaterializationSystem` must
  handle `InitialTargetsIntent`).
- The `Extract` method and all `GetOutputDomKeys()` / `GetConsumedComponentsMask()` logic must
  remain unchanged.
- After `Inject`, the entity must NOT have a `TargetMemory` component; only `InitialTargetsIntent`.
- The `Inject` path must NOT call `resolver.Resolve(guidStr)` to obtain an `Entity` handle;
  it must call `IGuidResolver.ResolveNetworkId(guidStr)` (or look up the Network ID from
  `NetworkIdentity` via the repository) to populate `TargetEntry.NetworkId`.

**Success Conditions:**

1. Unit test: `Inject` with a DOM containing 2 GUID-keyed target entries; assert entity has
   `InitialTargetsIntent` with `Entries.Count == 2` and the Network IDs, positions, scores,
   and modalities match the DOM values.
2. Assert entity does NOT have `TargetMemory` immediately after `Inject`.
3. Round-trip integration test (end-to-end via `GenesisMaterializationSystem`):
   save a scenario with an entity whose `TargetMemory.Count == 2`; load on a fresh node;
   tick `GenesisMaterializationSystem` after both target entities have spawned; assert the
   `TargetMemory` is restored with correct entity handles, scores, and positions.
4. Existing `UrbanCombatFileLifecycleTests` must still pass (the test verifies the scenario
   round-trip; it must be updated to tick `GenesisMaterializationSystem` before asserting
   `TargetMemory` state).

---

## Phase 5: Checkpoint Event Preservation

### TASK-S501: Add PopulateCurrentStreams to FdpEventBus

**Design Reference:** DESIGN.md § Phase 5 — FdpEventBus: expose Read-buffer streams

**Scope:**
Add two new methods to `FDP/Engine/Fdp.Core/FdpEventBus.cs`:

```csharp
public void PopulateCurrentStreams(List<INativeEventStream> target)
public void PopulateCurrentManagedStreams(List<IManagedEventStreamInfo> target)
```

Each method clears `target` and adds stream instances whose Current (Read) buffer is non-empty.

**Constraints:**
- Zero-allocation when `target` has sufficient capacity (no `new List<>`).
- Must not affect `PopulatePendingStreams` or `PopulatePendingManagedStreams`.
- The Read buffer is the buffer that has been swapped into the "current" slot (already
  consumed by systems this tick); it is accessed via the same internal dictionary as the
  Pending streams, but reading from the "read" side.

**Success Conditions:**

1. Unit test: publish one unmanaged event; call `SwapBuffers()`; call
   `PopulateCurrentStreams(list)`; assert list has 1 entry matching the stream that received
   the event.
2. Unit test: call `PopulateCurrentStreams` before `SwapBuffers`; assert list is empty.
3. Same two tests for managed events using `PopulateCurrentManagedStreams`.
4. `PopulatePendingStreams` is unaffected (still returns the write-side buffer).

---

### TASK-S502: Update RecorderSystem.WriteEvents with Buffer-Selection Flag

**Design Reference:** DESIGN.md § Phase 5 — RecorderSystem.WriteEvents buffer-selection parameter

**Scope:**
Modify `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`.

1. Add `bool serializeReadBuffer = false` parameter to the private `WriteEvents` method.
2. When `serializeReadBuffer` is `true`, call `PopulateCurrentStreams` / `PopulateCurrentManagedStreams`
   instead of `PopulatePendingStreams` / `PopulatePendingManagedStreams`.
3. Add the same optional parameter to the public `RecordKeyframe` and `RecordDeltaFrame` methods,
   defaulting to `false`.  Pass it through to `WriteEvents`.

**Constraints:**
- TASK-S501 must be completed first.
- Default parameter value must be `false` so all existing callers are unaffected.
- The Flight Recorder live path (`AsyncRecorder` calling `RecordDeltaFrame` before
  `SwapBuffers`) must not change behavior.

**Success Conditions:**

1. Unit test: record a keyframe with `serializeReadBuffer: false`; assert 0 events written
   (Pending buffer is empty).
2. Unit test: publish an event; swap buffers; record a keyframe with `serializeReadBuffer: true`;
   assert the event appears in the recorded payload (deserialize and count events).
3. Existing `RecorderSystemTests` pass unchanged (they use the default `false`).

---

### TASK-S503: Wire EventAccumulator into ReferenceCheckpointHandler

**Design Reference:** DESIGN.md § Phase 5 — ReferenceCheckpointHandler: inject EventAccumulator

**Scope:**
Modify `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceCheckpointHandler.cs`.

1. Add `EventAccumulator eventAccumulator` as a required constructor parameter.
2. Store it as `_eventAccumulator`.
3. In `Commit()`, after `snap.SyncFrom(source)`, call
   `_eventAccumulator.FlushToReplica(snap.Bus, source.GlobalVersion - 1)`.

**Constraints:**
- All callers that construct `ReferenceCheckpointHandler` must be updated to pass the
  `EventAccumulator` instance.  In `SimHostApp.cs`, the accumulator is available as the
  second argument to `ModuleHostKernel` (see `HrotNodeBuilder` line 90).
- `EventAccumulator.FlushToReplica` is zero-allocation for the ingest path (already proven
  by existing `EventAccumulationIntegrationTests`).
- If `_liveRepo` is null, the `Commit` no-op path is unchanged.

**Success Conditions:**

1. Unit test: publish a `WeaponFireIntent` event; trigger `Commit`; retrieve the snapshot
   repository's bus; assert the event is present in the Read buffer.
2. Unit test: no events published; `Commit` completes without error.
3. The existing `CheckpointIOWorkerTests` pass; compile errors on all `ReferenceCheckpointHandler`
   construction sites are fixed.

---

### TASK-S504: Patch CheckpointIOWorker to Pass Event Bus

**Design Reference:** DESIGN.md § Phase 5 — CheckpointIOWorker: pass event bus to recorder

**Scope:**
Modify `FDP/Engine/Fdp.Core/Orchestration/CheckpointIOWorker.cs`.

In `WriteCheckpointFile`, change the `RecordKeyframe` call to:
```csharp
_recorderSystem.RecordKeyframe(snapshot, bw, DateTimeOffset.UtcNow.Ticks,
    snapshot.Bus, serializeReadBuffer: true);
```

**Constraints:**
- TASK-S502 and TASK-S503 must be completed first.
- Only the single `RecordKeyframe` call in `WriteCheckpointFile` is affected.
- Do not modify `Enqueue`, `TakeCompletedResults`, or the background loop.

**Success Conditions:**

1. Unit test (extending `CheckpointIOWorkerTests`): publish an event before triggering a
   checkpoint; drain the worker; load the resulting `.fdp` file via `PlaybackSystem`;
   assert the event is re-injected into the world.
2. Existing `CheckpointIOWorkerTests` still pass.
3. Solution builds without errors.
