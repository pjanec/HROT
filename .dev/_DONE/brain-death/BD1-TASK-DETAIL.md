# BD1 — Task Detail Document

**Reference Design:** [BD1-DESIGN.md](./BD1-DESIGN.md)  
**Tracker:** [BD1-TASK-TRACKER.md](./BD1-TASK-TRACKER.md)

---

## Phase 1 — Core Brain-Death Lifecycle

### BD1-P1T0a: BehaviorFinishedEvent — Bottom-Up Notification from BTreeTickSystem

**Files:**
- *(new)* `FDP/Toolkits/FDP.Toolkit.Behavior/Events/BehaviorFinishedEvent.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs`

**Design Reference:** [§1.0a BehaviorFinishedEvent (notification, bottom-up)](./BD1-DESIGN.md#10a-behaviorfinishedevent-notification-bottom-up)

**Description:**

Create the `BehaviorFinishedEvent` class — a **notification** published by the **BTree cognitive machinery** when the behavior's BTree root evaluates to a terminal state. This flows **upward** from the Cognitive tier to the Mission tier. It does NOT itself change any state.

**Critical tier boundary:** `LocomotionDispatcherSystem` must NOT publish this event. It operates at the *action* level (individual BTree leaf nodes). A single behavior may contain many locomotion actions in sequence; only the BTree **root** result represents behavior completion. `BTreeTickSystem` is the correct publisher because it evaluates the full behavior tree and observes the root result.

```csharp
// BehaviorFinishedEvent.cs
using Fdp.Kernel;
using Fbt;

namespace FDP.Toolkit.Behavior.Events
{
    public sealed class BehaviorFinishedEvent
    {
        public Entity Entity;
        public NodeStatus Result; // Success or Failure
    }
}
```

In `BTreeTickSystem.OnUpdate`, capture the root result returned by `Interpreter.Tick` and publish when terminal:

```csharp
var rootResult = def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context);

if (rootResult == NodeStatus.Success || rootResult == NodeStatus.Failure)
{
    World.Bus.PublishManaged(new BehaviorFinishedEvent
    {
        Entity = entity,
        Result = rootResult
    });
}
```

Publish only **once per terminal transition**: if the interpreter already returned `Success` on the previous tick and does so again, do not re-publish. Guard with a `BrainBTreeState` status field or by checking that the BTree was `Running` before this tick (capture status before the `Tick` call).

This task is a **prerequisite** for BD1-P1T2 (which adds the `BehaviorFinished` trigger type to `MissionDirectorSystem`).

**Success Conditions:**

1. **Unit test — `BTreeTickSystemTests.BehaviorRoot_Success_PublishesBehaviorFinishedEvent`**
   - Register a behavior whose BTree interpreter returns `NodeStatus.Success` on first tick.
   - Create an entity with `BehaviorState { ActiveBehaviorHash = X, BrainTier = BTree }` and `BrainBTreeState`.
   - Run `BTreeTickSystem.OnUpdate()` once.
   - Assert: exactly one `BehaviorFinishedEvent` was published on the bus for this entity.
   - Assert: `BehaviorFinishedEvent.Result == NodeStatus.Success`.

2. **Unit test — `BTreeTickSystemTests.BehaviorRoot_Failure_PublishesBehaviorFinishedEvent`**
   - Same setup, BTree returns `NodeStatus.Failure`.
   - Assert: `BehaviorFinishedEvent.Result == NodeStatus.Failure`.

3. **Unit test — `BTreeTickSystemTests.BehaviorRoot_Running_DoesNotPublishEvent`**
   - BTree returns `NodeStatus.Running`.
   - Assert: no `BehaviorFinishedEvent` published.

4. **Unit test — `BTreeTickSystemTests.BehaviorRoot_Success_PublishedOnlyOnce`**
   - BTree returns `Success` on frame 1; returns `Success` again on frame 2 (re-ticked).
   - Assert: event published exactly once (no repeated firing on a terminal that stays terminal).

5. **Unit test — `BTreeTickSystemTests.BehaviorFinished_NotPublishedByLocomotionDispatcher`**
   - Simulate a `MoveToExecutor` setting `channel.Status = NodeStatus.Success` inside `LocomotionDispatcherSystem`.
   - Run `LocomotionDispatcherSystem` but NOT `BTreeTickSystem`.
   - Assert: no `BehaviorFinishedEvent` on the bus (the locomotion layer has no behavior awareness).

---

### BD1-P1T0b: ClearBehaviorEvent — Top-Down Imperative via BehaviorIngressSystem

**Files:**
- *(new)* `FDP/Toolkits/FDP.Toolkit.Behavior/Events/ClearBehaviorEvent.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs`

**Design Reference:** [§1.0b ClearBehaviorEvent (imperative, top-down)](./BD1-DESIGN.md#10b-clearbehaviorevent-imperative-top-down)

**Description:**

Create the `ClearBehaviorEvent` class — an **imperative command** published by higher-level systems to forcibly clear the active behavior. This flows **downward** into the Cognitive tier and is consumed by `BehaviorIngressSystem`, which owns all `BehaviorState` writes.

```csharp
// ClearBehaviorEvent.cs
using Fdp.Kernel;

namespace FDP.Toolkit.Behavior.Events
{
    public sealed class ClearBehaviorEvent
    {
        public Entity Entity;
    }
}
```

In `BehaviorIngressSystem.OnUpdate`, add a `ConsumeManaged<ClearBehaviorEvent>()` loop:

```csharp
var clearEvents = World.Bus.ConsumeManaged<ClearBehaviorEvent>();
foreach (var evt in clearEvents)
{
    if (evt == null || !World.HasComponent<BehaviorState>(evt.Entity)) continue;
    ref var behavior = ref World.GetComponentRW<BehaviorState>(evt.Entity);
    behavior.ActiveBehaviorHash = BehaviorIds.None;
    unchecked { behavior.InstanceId++; }
    behavior.BrainTier = 0;
    if (World.HasComponent<BrainBTreeState>(evt.Entity))
        World.GetComponentRW<BrainBTreeState>(evt.Entity).State = default;
}
```

This task is a **prerequisite** for BD1-P1T2 and BD1-P1T3.

**Success Conditions:**

1. **Unit test — `BehaviorIngressSystemTests.ClearBehaviorEvent_SetsBehaviorToNone`**
   - Create entity with `BehaviorState { ActiveBehaviorHash = 2001, InstanceId = 5, BrainTier = 2 }` and `BrainBTreeState`.
   - Publish `ClearBehaviorEvent { Entity = entity }` on the bus. Run update once.
   - Assert: `BehaviorState.ActiveBehaviorHash == 0`, `InstanceId == 6`, `BrainTier == 0`, `BrainBTreeState.State == default`.

2. **Unit test — `BehaviorIngressSystemTests.ClearBehaviorEvent_NoBehaviorState_IsIgnored`**
   - Create entity **without** `BehaviorState`. Publish `ClearBehaviorEvent`. Run update.
   - Assert: no exception; entity state unchanged.

3. **Unit test — `BehaviorIngressSystemTests.ClearBehaviorEvent_DoesNotAffectOtherEntities`**
   - Create entities A and B both with `BehaviorState { ActiveBehaviorHash = 1001 }`.
   - Publish `ClearBehaviorEvent { Entity = A }`. Run update.
   - Assert: A `ActiveBehaviorHash == 0`; B `ActiveBehaviorHash == 1001`.

4. **Unit test — `BehaviorIngressSystemTests.ClearVsAssign_AreIndependent`**
   - In the same frame: publish `AssignBehaviorEvent` for entity A and `ClearBehaviorEvent` for entity B.
   - Run update. Assert: A has assigned behavior; B has `BehaviorIds.None`.

---

### BD1-P1T1: ChannelArbitrationSystem — OnExit Guarantee

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs`

**Design Reference:** [§1.1 ChannelArbitrationSystem — OnExit Guarantee](./BD1-DESIGN.md#11-channelarbitrationsystem--onexit-guarantee)

**Description:**

The current implementation clears a stale channel by assigning `channel = default`. This zeros both `ActionInstanceId` and `DispatchedInstanceId`, which makes `LocomotionDispatcherSystem` (and its weapon/interaction equivalents) evaluate `0 != 0 = false` and silently skip the `OnExit` call. The fix is to zero `ActiveAction` and **increment** `ActionInstanceId` (unchecked wrapping) so the dispatcher always fires `OnExit`.

Apply the same change to all three channel types: `LocomotionChannel`, `WeaponChannel`, `InteractionChannel`.

**Implementation:**

Replace `channel = default;` in each of the three `foreach` blocks with:

```csharp
channel.ActiveAction = 0;
unchecked { channel.ActionInstanceId++; }
```

**Success Conditions:**

1. **Unit test — `ChannelArbitrationSystemTests.ChannelClear_ShouldNotZeroActionInstanceId`**
   - Create an entity with `BehaviorState { InstanceId = 1 }` and `LocomotionChannel { ActiveAction = 5, BehaviorInstanceId = 0, ActionInstanceId = 7, DispatchedInstanceId = 7 }`.
   - Run `ChannelArbitrationSystem.OnUpdate()` once.
   - Assert: `channel.ActiveAction == 0`.
   - Assert: `channel.ActionInstanceId == 8` (incremented, not zero).
   - Assert: `channel.DispatchedInstanceId == 7` (unchanged).

2. **Unit test — `ChannelArbitrationSystemTests.NoPreemption_WhenBehaviorMatches`**
   - Create an entity with `BehaviorState { InstanceId = 3 }` and `LocomotionChannel { ActiveAction = 2, BehaviorInstanceId = 3, ActionInstanceId = 1 }`.
   - Run one update.
   - Assert: `channel.ActiveAction == 2` (unchanged — behavior matches).

3. **Unit test — `ChannelArbitrationSystemTests.WeaponChannel_ReceivesOnExitSignal`**
   - Same pattern as test 1 but for `WeaponChannel`.

4. **Unit test — `ChannelArbitrationSystemTests.InteractionChannel_ReceivesOnExitSignal`**
   - Same pattern as test 1 but for `InteractionChannel`.

---

### BD1-P1T2: MissionDirectorSystem — BehaviorFinished Trigger + End-of-Mission Clear

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`

**Design Reference:** [§1.2 MissionDirectorSystem — End-of-Mission Behavior Clear](./BD1-DESIGN.md#12-missiondirectorsystem--end-of-mission-behavior-clear)

**Prerequisites:** BD1-P1T0a and BD1-P1T0b must be complete.

**Description:**

This task has two sub-changes:

**Sub-change A — New `BehaviorFinished` trigger type:**

Add `BehaviorFinished` to the `MissionTrigger` enum. In `MissionDirectorSystem.OnUpdate`, add a new `case MissionTrigger.BehaviorFinished` that consumes `BehaviorFinishedEvent`s from the bus for the current entity. When a matching event is found, set `triggered = true`. This replaces the coupling to `NavState.HasArrived` for this trigger type.

```csharp
case MissionTrigger.BehaviorFinished:
    // Consume any BehaviorFinishedEvent for this entity published this frame.
    // (Bus provides peek/consume by entity pattern or a lookup cache built once per frame.)
    if (HasBehaviorFinishedEvent(entity, out var result))
        triggered = true;
    break;
```

**Sub-change B — End-of-plan: publish ClearBehaviorEvent:**

When the trigger fires and `CurrentPhase >= PhaseCount` (plan exhausted), publish `ClearBehaviorEvent`. Do NOT directly write `BehaviorState`.

```csharp
if (queue.CurrentPhase < queue.PhaseCount)
{
    unchecked { behavior.InstanceId++; }
    behavior.ActiveBehaviorHash = phases[queue.CurrentPhase].BehaviorId;
}
else
{
    World.Bus.PublishManaged(new FDP.Toolkit.Behavior.Events.ClearBehaviorEvent { Entity = entity });
}
```

**Success Conditions:**

1. **Unit test — `MissionDirectorSystemTests.BehaviorFinishedTrigger_AdvancesPhase`**
   - Build a single-phase queue with trigger = `BehaviorFinished`.
   - Publish `BehaviorFinishedEvent { Entity = entity, Result = NodeStatus.Success }` on bus.
   - Run one update.
   - Assert: a `ClearBehaviorEvent` was published (end of plan, one phase).
   - Assert: `queue.CurrentPhase == 1`.

2. **Unit test — `MissionDirectorSystemTests.BehaviorFinishedTrigger_MultiPhase_SetsNextBehavior`**
   - Build a two-phase queue; phase 0 trigger = `BehaviorFinished`.
   - Publish event; run update.
   - Assert: no `ClearBehaviorEvent` published (still in plan).
   - Assert: `behavior.ActiveBehaviorHash == <phase-1-behavior-id>`.

3. **Unit test — `MissionDirectorSystemTests.BehaviorFinishedTrigger_WrongEntity_DoesNotFire`**
   - Publish `BehaviorFinishedEvent` for a **different** entity.
   - Assert: no phase advance.

4. **Unit test — `MissionDirectorSystemTests.MissionComplete_PublishesClearBehaviorEvent`**
   - Same as test 1: verify `ClearBehaviorEvent` is published when plan is exhausted.

5. **Integration test — `MissionDirectorSystemTests.MissionComplete_ViaBehaviorIngress_SetsBehaviorToNone`**
   - Include `MissionDirectorSystem`, `BehaviorIngressSystem`, and bus swap in the test world.
   - Publish `BehaviorFinishedEvent` for a single-phase plan.
   - Run two updates (event published in frame 1, consumed by BehaviorIngressSystem in frame 2).
   - Assert: `behavior.ActiveBehaviorHash == 0`.

---

### BD1-P1T3: MissionControlRequestSystem — CMD_ABORT_ALL Behavior Clear

**File:** `Hrot.SimHost/Systems/MissionControlRequestSystem.cs`

**Design Reference:** [§1.3 MissionControlRequestSystem — CMD_ABORT_ALL Behavior Clear](./BD1-DESIGN.md#13-missioncontrolrequestsystem--cmd_abort_all-behavior-clear)

**Prerequisite:** BD1-P1T0b must be complete.

**Description:**

The `CMD_ABORT_ALL` case zeroes the `MissionPlanQueue` and removes the `EntityMissionHolder`. After abort the entity continues executing its last channel because `BehaviorState` is never touched.

This is a top-down **imperative** use of `ClearBehaviorEvent` — the operator is commanding an immediate behavior reset regardless of what the behavior machinery is currently doing. Add a `repo.Bus.PublishManaged(new ClearBehaviorEvent { Entity = entity })` after resetting the queue.

**Note:** This is explicitly **not** a `BehaviorFinishedEvent`. The behavior did not finish naturally; it was interrupted. The distinction matters because other systems may need to reason about natural vs. forced termination in the future.

**Success Conditions:**

1. **Unit test — `MissionControlRequestSystemTests.AbortAll_PublishesClearBehaviorEvent`**
   - Spawn entity with `BehaviorState { ActiveBehaviorHash = 2001, InstanceId = 3 }` and a non-empty `MissionPlanQueue`.
   - Send `CMD_ABORT_ALL` via `ProcessRequest`.
   - Assert: a `ClearBehaviorEvent` with the correct entity was published on the bus.
   - Assert: `MissionPlanQueue.PhaseCount == 0`.

2. **Unit test — `MissionControlRequestSystemTests.AbortAll_NoBehaviorState_DoesNotThrow`**
   - Spawn entity **without** `BehaviorState`.
   - Send `CMD_ABORT_ALL`.
   - Assert: operation completes without exception; `MissionPlanQueue.PhaseCount == 0`.
   - Assert: `ClearBehaviorEvent` is still published (the ingress system will guard against missing `BehaviorState`).

3. **Regression test — `MissionControlRequestSystemTests.AbortAll_WritesSuccessAck`**
   - Verify the existing ACK with `SstErrorCode.Success` is still written.

---

## Phase 2 — Right-Click Mission UX

### BD1-P2T1: SimHostVisualization — Brain-Aware Right-Click Handler

**File:** `Hrot.SimHost/SimHostVisualization.cs`

**Design Reference:** [§2.1 SimHostVisualization — Brain-Aware Right-Click Handler](./BD1-DESIGN.md#21-simhostvisualization--brain-aware-right-click-handler)

**Description:**

The `_interactionTool.OnWorldClick` lambda currently:
- On Shift: always calls `_scenario.AddWaypoint()` (muscle path) — broken when brain is active.
- On plain click: always sends `CMD_REPLACE_MISSION` with empty `Triggers` — produces an infinite-loop overshoot.

Rewrite the handler with two distinct code paths, selected by checking `BehaviorState.ActiveBehaviorHash`:

**Brain-dead path** (no `BehaviorState` component OR `ActiveBehaviorHash == BehaviorIds.None`):
- Plain click → `_scenario.SetDestination(e, pos, ...)`.
- Shift click → `_scenario.AddWaypoint(e, pos, ...)`.

**Brain-active path** (`ActiveBehaviorHash != BehaviorIds.None`):
- Plain click → send `CMD_REPLACE_MISSION` with a `MoveToLocation` task carrying a `ReachedDestination` trigger.
- Shift click is **not** supported for brain-active entities (to be tackled in a future increment). For this task, plain right-click is the only supported path; Shift behaves identically to plain click for brain-active entities.

**Important:** The task sent on the brain-active path **must** include the `ReachedDestination` trigger, so that `MissionDirectorSystem` fires task completion and the behavior is eventually cleared by fix BD1-P1T2.

**Success Conditions:**

1. **Unit test — `SimHostVisualizationTests.RightClick_BrainDead_CallsSetDestination`**
   - Arrange entity with no `BehaviorState` (or `ActiveBehaviorHash == 0`).
   - Simulate right-click event (non-shift).
   - Assert: `_scenario.SetDestination` was called with the clicked position.
   - Assert: `_missionWriter.Write` was NOT called.

2. **Unit test — `SimHostVisualizationTests.ShiftRightClick_BrainDead_CallsAddWaypoint`**
   - Arrange entity with no active behavior.
   - Simulate Shift+right-click.
   - Assert: `_scenario.AddWaypoint` was called.

3. **Unit test — `SimHostVisualizationTests.RightClick_BrainActive_WritesMissionWithTrigger`**
   - Arrange entity with `BehaviorState { ActiveBehaviorHash = 2001 }` and `NetworkIdentity`.
   - Simulate right-click.
   - Assert: `_missionWriter.Write` was called once.
   - Assert: the written `MissionControlRequest.Payload.FullMissionData.Tasks[0].Triggers` contains exactly one trigger of type `"ReachedDestination"`.

4. **Integration test / manual verification:** spawn a vehicle via SimHost Controls panel, right-click to navigate it, verify it reaches the destination and stops (does not overshoot or loop).

---

## Phase 3 — RVO Spatial Hash / Physics Collider

### BD1-P3T1: BdcTkbBuilder — Add PhysicsCollider to WithPhysics

**File:** `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs`

**Design Reference:** [§3.1 BdcTkbBuilder — Add PhysicsCollider to WithPhysics](./BD1-DESIGN.md#31-bdctkbbuilder--add-physicscollider-to-withphysics)

**Description:**

`WithPhysics` adds `VehicleParams` but not `PhysicsCollider`. `SpatialHashSystem` filters by `GlobalComponentIds.PhysicsCollider`, so vehicles built via `WithPhysics` alone are invisible to RVO.

Add a `PhysicsCollider` at the end of `WithPhysics` using `Math.Max(physicsDef.Length, physicsDef.Width) / 2f` as the radius and `PhysicsConstants.EntityCollisionLayer` as the layer.

**Success Conditions:**

1. **Unit test — `BdcTkbBuilderTests.WithPhysics_AddsPhysicsCollider`**
   - Call `WithPhysics` on a template.
   - Assert: the resulting template contains a `PhysicsCollider` component.
   - Assert: `PhysicsCollider.Radius` equals `Math.Max(length, width) / 2f` for the configured dimensions.
   - Assert: `PhysicsCollider.CollisionLayer == PhysicsConstants.EntityCollisionLayer`.

2. **Unit test — `BdcTkbBuilderTests.WithPhysics_ColliderRadiusIsMaxDimension`**
   - Configure a vehicle with `Length = 6f, Width = 2.5f`.
   - Assert: `Radius == 3f` (= 6 / 2).

3. **Unit test — `SpatialHashSystemTests.VehicleWithPhysicsCollider_InsertedIntoGrid`**
   - Create an entity with `SimTransform` and the `PhysicsCollider` produced by the updated `WithPhysics`.
   - Run `SpatialHashSystem` one update.
   - Assert: `SpatialGridData.Grid` contains the entity.

---

### BD1-P3T2: SimHostScenarioManager — Add PhysicsCollider to SpawnEntityLocal

**File:** `Hrot.SimHost/UI/SimHostScenarioManager.cs`

**Design Reference:** [§3.2 SimHostScenarioManager — Add PhysicsCollider to SpawnEntityLocal](./BD1-DESIGN.md#32-simhostscenariomanager--add-physicscollider-to-spawnentitylocal)

**Description:**

`SpawnEntityLocal` creates a bare ECS entity for local-only demo helpers (roamers, road users, collision test). These entities lack `PhysicsCollider`, so they are excluded from the spatial hash grid and do not participate in RVO.

Add `PhysicsCollider` at the tail of `SpawnEntityLocal` using the same radius formula: `Math.Max(preset.Length, preset.Width) / 2f`.

**Success Conditions:**

1. **Unit test — `SimHostScenarioManagerTests.SpawnEntityLocal_AddsPhysicsCollider`**
   - Call `SpawnEntityLocal(position, heading)` (accessible via a test-only wrapper or via `SpawnCollisionTest`).
   - Query the returned entity for `PhysicsCollider`.
   - Assert: component is present.
   - Assert: `Radius > 0`.

2. **Integration / manual:** spawn a "Collision Test" pair in SimHost standalone. Verify the two vehicles deviate around each other instead of driving through.

---

## Phase 4 — Camera Offset Fix

### BD1-P4T1: SimHostVisualization — Set Camera Offset on Initialize

**File:** `Hrot.SimHost/SimHostVisualization.cs`

**Design Reference:** [§4.1 SimHostVisualization.Initialize — Set Camera Offset](./BD1-DESIGN.md#41-simhostvisualizationinitialize--set-camera-offset)

**Description:**

`SimHostVisualization.Initialize` instantiates `MapCanvas` but never sets `_map.Camera.Offset`. The Raylib `Camera2D` defaults to `Offset = Vector2.Zero`, which means `FocusOn()` maps the entity world-position to screen pixel (0,0) — the top-left corner — instead of the screen centre.

Add one line immediately after `_map = new MapCanvas();`:

```csharp
_map.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f);
```

**Success Conditions:**

1. **Unit test — `SimHostVisualizationTests.Initialize_SetsMapCameraOffset`**
   - Call `SimHostVisualization.Initialize(...)`.
   - Assert: `_visualization.GetMapCamera()!.Offset` equals `new Vector2(640f, 360f)`.

2. **Manual verification:** in SimHost standalone, select any entity, use "Center on entity" from the context menu. Verify the entity appears at the centre of the 2D map viewport.

---

## Phase 5 — DisType DDS Struct

### BD1-P5T1: EntityMaster — Replace Plain long DisType with DisTypeStruct

**Files:**
- `Hrot.NED/GenericDescriptors.cs`
- `Hrot.Map.Common/Replication/Egress/EntityMasterEgressTranslator.cs`
- Ingress translator(s) where `EntityMaster.DisType` is read (e.g. `EntityMasterIngressTranslator.cs` / `DescriptorMapper.cs`)

**Design Reference:** [§5.1 DDS Data Model — DisTypeStruct](./BD1-DESIGN.md#51-dds-data-model--distypestruct)

**Description:**

`EntityMaster.DisType` is currently a plain `long`. DDS monitoring tools cannot decompose it into the 8 standard DIS fields. Change the DDS wire representation to a dedicated struct while keeping the engine-side `DISEntityType` (with its `ulong Value` overlay) completely unchanged.

**Steps:**

1. In `GenericDescriptors.cs` add:
```csharp
[DdsStruct]
public partial struct DisTypeStruct
{
    public byte Kind;
    public byte Domain;
    public ushort Country;
    public byte Category;
    public byte Subcategory;
    public byte Specific;
    public byte Extra;
}
```
2. Change `EntityMaster.DisType` from `long` → `DisTypeStruct`.
3. In the egress translator, map each field of `DISEntityType` → `DisTypeStruct` before publishing.
4. In ingress translators, reconstruct `DISEntityType` from `DisTypeStruct` fields.

**Do NOT change** `Fdp.Kernel.DISEntityType`, `GlobalComponentIds`, or any entity-query filter logic — those are engine-side and are already correct.

**Success Conditions:**

1. **Unit test — `EntityMasterEgressTranslatorTests.DisType_MappedCorrectly`**
   - Create an entity with a `DISEntityType { Kind = 1, Domain = 2, Country = 225, Category = 3, Subcategory = 4, Specific = 5, Extra = 6 }`.
   - Run the egress translator.
   - Capture the published `EntityMaster`.
   - Assert: `DisType.Kind == 1`, `DisType.Domain == 2`, `DisType.Country == 225`, all other fields match.

2. **Unit test — `EntityMasterIngressTranslatorTests.DisTypeStruct_RoundTrip`**
   - Create an `EntityMaster` with a fully-populated `DisTypeStruct`.
   - Run the ingress translator.
   - Assert: the resulting `DISEntityType.Value` encodes all 8 fields correctly (verify via individual field accessors).

3. **Build test:** solution compiles without errors after the type change.

4. **Manual:** open a DDS monitoring tool (e.g. RTI Spy / Cyclone introspection) while SimHost is running; verify the `EntityMaster.DisType` field now displays as a struct with named sub-fields.

---

## Phase 6 — Entity Inspector Component Change Detection

### BD1-P6T1: ComponentReflector — Byte-Cache Change Detection

**File:** `FDP/Toolkits/FDP.Toolkit.ImGui/Utils/ComponentReflector.cs`

**Design Reference:** [§6.1 ComponentReflector — Byte-Cache Change Detection](./BD1-DESIGN.md#61-componentreflector--byte-cache-change-detection)

**Description:**

Add per-entity, per-type byte-level change detection for **value-type (unmanaged) components only**. When a component's bytes differ from the previous frame, its `CollapsingHeader` label is drawn in **yellow** for that frame. Managed class components are skipped entirely.

**Fields to add to `ComponentReflector`:**

```csharp
private Entity _lastInspectedEntity = Entity.Null;
private readonly Dictionary<Type, byte[]> _unmanagedCache = new();
```

**Algorithm in `DrawComponents`:**

1. If `e != _lastInspectedEntity`: clear `_unmanagedCache`, update `_lastInspectedEntity`.
2. For each component type where `type.IsValueType`:
   - Marshal struct to a temp `AllocHGlobal` buffer.
   - Compare against cached bytes.
   - On first encounter: store baseline, do NOT flag as changed (avoid initial flash).
   - On change: push `ImGuiCol.Text = Yellow` before `CollapsingHeader`, pop after; update cache.
   - Free the temp buffer in `finally`.
3. Managed types (`!type.IsValueType`): skip cache/comparison entirely.

**Success Conditions:**

1. **Unit test — `ComponentReflectorTests.UnmanagedComponent_FirstFrame_NoFlash`**
   - Render a component for the first time.
   - Assert: `ImGui.PushStyleColor` was NOT called (no false initial highlight).

2. **Unit test — `ComponentReflectorTests.UnmanagedComponent_Unchanged_NoHighlight`**
   - Render the same component twice with identical data.
   - Assert: `ImGui.PushStyleColor` is NOT called on the second frame.

3. **Unit test — `ComponentReflectorTests.UnmanagedComponent_Changed_HighlightsYellow`**
   - Render component frame 1 (baseline), then change a field, render frame 2.
   - Assert: `ImGui.PushStyleColor(ImGuiCol.Text, yellow)` was called before `CollapsingHeader`.
   - Assert: `ImGui.PopStyleColor()` was called after.

4. **Unit test — `ComponentReflectorTests.ManagedComponent_NeverHighlighted`**
   - Render a managed class component (e.g. `EntityMissionHolder`).
   - Mutate it between renders.
   - Assert: `ImGui.PushStyleColor` was NOT called.

5. **Unit test — `ComponentReflectorTests.EntitySwitch_ClearsCache`**
   - Render entity A; switch to entity B.
   - Assert: cache is empty (no stale bytes from A contaminate B's first-frame baseline).

---

## Phase 7 — CreateEntityRequestSystem Hot-Path Delegate Caching

### BD1-P7T1: CreateEntityRequestSystem — Cache ProcessRequest Delegate

**File:** `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs`

**Design Reference:** [§7.1 CreateEntityRequestSystem — Cache ProcessRequest Delegate](./BD1-DESIGN.md#71-createentityrequestsystem--cache-processrequest-delegate)

**Description:**

The call `_requestSource.ProcessRequests(request => { ... })` allocates a new `Action<CreateEntityRequest>` delegate on every `Execute` call because the lambda captures `this`. At 60 fps this generates continuous Gen0 GC pressure.

**Steps:**

1. Add `private readonly Action<CreateEntityRequest> _processRequestDelegate;` field.
2. In the constructor: `_processRequestDelegate = ProcessIncomingRequest;`.
3. Extract all logic from the inline lambda into a new private method `private void ProcessIncomingRequest(CreateEntityRequest request)`.
4. In `Execute`, replace the lambda with `_requestSource.ProcessRequests(_processRequestDelegate)`.

**Success Conditions:**

1. **Unit test — `CreateEntityRequestSystemTests.ProcessRequests_UsesPreCachedDelegate`**
   - Construct the system twice.
   - Assert: `_processRequestDelegate` (accessed via reflection or a test-visible property) is the same delegate instance on both `Execute` calls (i.e., `ReferenceEquals` returns `true` for the delegate used in call 1 vs call 2).

2. **Behaviour regression test — `CreateEntityRequestSystemTests.ValidRequest_IsProcessedCorrectly`**
   - Send a valid `CreateEntityRequest` through the refactored path.
   - Assert: entity is created, ACK is sent, `_pendingQueue` is populated — identical behaviour to before the refactor.

3. **No-allocation validation:** run a profiling session (dotMemory / VS Diagnostic Tools) over 10 frames with the fix in place. Assert: zero `Action<CreateEntityRequest>` allocations in the `Execute` method.
