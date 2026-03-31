# BD1 — Brain-Death & Mission-Lifecycle Fixes: Design Document

## Overview

This workstream fixes a cluster of interconnected bugs that emerged after the **CQRS Brain/Muscle split** (NavigationIntent vs. NavState). The common thread across all bugs is that the ECS lifecycle for doctrine and channel cleanup is incomplete: when a mission ends, is aborted, or is replaced, the entity never cleanly reaches a **"brain death" state** (no active doctrine, no stimulated channels). The muscle layer stays permanently active, producing the looping, overshoot, and conflict symptoms described in the design talk.

A secondary class of bugs deals with entities that are not properly registered in the spatial collision grid (`SpatialHashSystem`) and entities spawned via a legacy local path (`SpawnEntityLocal`) that bypasses the network authority requirement.

### Affected Subsystems

| Subsystem | Key File(s) |
|---|---|
| Channel lifecycle | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs` |
| Doctrine ingress (new event) | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DoctrineIngressSystem.cs` |
| Doctrine clear event (new) | `FDP/Toolkits/FDP.Toolkit.Behavior/Events/ClearDoctrineEvent.cs` |
| Doctrine finished event (new) | `FDP/Toolkits/FDP.Toolkit.Behavior/Events/DoctrineFinishedEvent.cs` |
| BTree doctrine runner | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs` |
| Mission director | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` |
| Mission adapter (existing) | `Hrot.SimHost/Systems/MissionAdapterSystem.cs` |
| Mission abort | `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` |
| RVO spatial grid | `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs` |
| TKB vehicle builder | `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs` |
| SimHost local spawner | `Hrot.SimHost/UI/SimHostScenarioManager.cs` |
| SimHost right-click UI | `Hrot.SimHost/SimHostVisualization.cs` |
| DDS data model — DisType | `Hrot.NED/GenericDescriptors.cs` |
| DDS egress translator | `Hrot.Map.Common/Replication/Egress/EntityMasterEgressTranslator.cs` |
| Entity inspector | `FDP/Toolkits/FDP.Toolkit.ImGui/Utils/ComponentReflector.cs` |
| SimHost entity request | `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs` |

---

## Phase 1 — Core Brain-Death Lifecycle

**Goal:** Establish a reliable "no doctrine" (brain-death) state that is entered automatically when a mission completes or is aborted, and propagated correctly through the channel dispatch pipeline.

### Event Architecture: Two Distinct Events

The design introduces **two events** that must not be confused:

| Event | Direction | Meaning | Producer | Consumer |
|---|---|---|---|---|
| `DoctrineFinishedEvent` | **Bottom-up** (notification) | "The doctrine has completed naturally" | `BTreeTickSystem` (when BTree root evaluates to Success/Failure) | `MissionDirectorSystem` |
| `ClearDoctrineEvent` | **Top-down** (imperative) | "Stop/reset the doctrine immediately" | `MissionDirectorSystem` (end of plan), `MissionControlRequestSystem` (CMD_ABORT_ALL) | `DoctrineIngressSystem` |

`DoctrineFinishedEvent` flows **out** of the cognitive/behavior tier upward to the mission tier — it is a report of what has happened. `ClearDoctrineEvent` flows **into** the cognitive tier from above — it is a command to change state.

**The muscle layer (`LocomotionDispatcherSystem`, `NavigationExecutionSystem`) must never publish either event.** The Muscle tier has no awareness of doctrines; it only reports physical operation status (`NavigationStatus.Result`, `channel.Status`). The BTree machinery reads those statuses and decides whether the *doctrine* is finished — that decision belongs exclusively to the cognitive tier.

### Existing Mission Tier Architecture (Context)

The two-system arrangement in the mission tier is already in place:

- **`MissionDirectorSystem`**: Evaluates triggers on the `MissionPlanQueue` and advances `CurrentPhase` when they fire.
- **`MissionAdapterSystem`**: Detects phase advances (by shadow-tracking `MissionPlanQueue.CurrentPhase`) and publishes `AssignDoctrineEvent` — which `DoctrineIngressSystem` consumes to apply the new doctrine, bump `InstanceId`, and reset the BTree execution pointer.

The new `DoctrineFinished` trigger type plugs directly into `MissionDirectorSystem`’s existing trigger evaluation loop.

### `DoctrineState.InstanceId` — Universal Change Signal

There is **no `DoctrineChanged` event** in the FDP event bus. Instead, `DoctrineIngressSystem` increments `DoctrineState.InstanceId` on **every** change: assignment, re-assignment (same doctrine, new params), and forced clear. This integer is the single authoritative change token observable by any system.

Any external observer wanting to detect all doctrine transitions (assigned / cleared / finished) should use the **shadow-polling pattern**:

```csharp
// in the observing system:
private readonly Dictionary<int, uint> _prevDoctrineInstanceId = new();

// in OnUpdate:
foreach (var entity in query)
{
    var doctrine = World.GetComponent<DoctrineState>(entity);
    if (!_prevDoctrineInstanceId.TryGetValue(entity.Index, out uint prev)
        || prev != doctrine.InstanceId)
    {
        bool isNowBrainDead = doctrine.ActiveDoctrineHash == DoctrineIds.None;
        // handle change...
        _prevDoctrineInstanceId[entity.Index] = doctrine.InstanceId;
    }
}
```

### 1.0a DoctrineFinishedEvent (notification, bottom-up)

**Problem:** After `MoveToExecutor.Execute` sets `channel.Status = NodeStatus.Success`, the BTree root evaluates to `Success` in the same or a subsequent tick of `BTreeTickSystem`. However, `BTreeTickSystem` currently discards this terminal result silently — it calls `Interpreter.Tick()` but does not report the doctrine's completion upward. The Mission tier compensates by polling `NavState.HasArrived` directly in `MissionDirectorSystem`, coupling the Mission tier to the physics layer.

**Tier ownership clarification:**
- **`NavigationExecutionSystem` (Muscle)**: writes `NavigationStatus.Result = NavResult.Arrived` — purely physical.
- **`MoveToExecutor` (Action executor)**: reads `NavigationStatus`, sets `channel.Status = NodeStatus.Success` — the Cognitive/Action layer bridge.
- **`BTreeTickSystem` (Doctrine machinery)**: calls `Interpreter.Tick()` on the entity’s doctrine BTree. When the BTree **root** returns `NodeStatus.Success` or `NodeStatus.Failure`, the *entire doctrine* has concluded. This is the only correct place to publish `DoctrineFinishedEvent`.

The `LocomotionDispatcherSystem` must **not** publish this event. It operates at the *action* level (individual BTree leaf nodes), not at the *doctrine* level (BTree root). A doctrine may contain many sequential or conditional locomotion actions; only the BTree root result represents doctrine completion.

**Fix:** In `BTreeTickSystem.OnUpdate`, capture the BTree root result returned (or implied) by `Interpreter.Tick`. When the root transitions to `Success` or `Failure`, publish `DoctrineFinishedEvent`:

```csharp
// DoctrineFinishedEvent.cs
public sealed class DoctrineFinishedEvent
{
    public Entity Entity;
    public NodeStatus Result; // Success or Failure
}
```

In `BTreeTickSystem.OnUpdate`, after `Tick`:

```csharp
var rootResult = def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context);

if (rootResult == NodeStatus.Success || rootResult == NodeStatus.Failure)
{
    World.Bus.PublishManaged(new DoctrineFinishedEvent
    {
        Entity = entity,
        Result = rootResult
    });
}
```

`MissionDirectorSystem` adds a new `DoctrineFinished` trigger type that consumes these notifications rather than polling `NavState.HasArrived` directly.

### 1.0b ClearDoctrineEvent (imperative, top-down)

**Problem:** The original design proposed that `MissionDirectorSystem` and `MissionControlRequestSystem` directly manipulate `DoctrineState.ActiveDoctrineHash`. This violates separation of concerns — the Mission tier should not micromanage the Cognitive/Behavior tier's internal components.

The Behavior toolkit already has an event-driven path for assigning doctrines: `AssignDoctrineEvent` consumed by `DoctrineIngressSystem`. The clear operation should mirror this exact pattern, but as an imperative command flowing **downward**.

**Fix:** Create a `ClearDoctrineEvent` in `FDP/Toolkits/FDP.Toolkit.Behavior/Events/` and add a handler for it in `DoctrineIngressSystem.OnUpdate`. Any system that needs to **forcibly** put an entity into brain-death state publishes this event; `DoctrineIngressSystem` translates it into `DoctrineState` and `BrainBTreeState` resets.

```csharp
// ClearDoctrineEvent.cs
public sealed class ClearDoctrineEvent
{
    public Entity Entity;
}
```

In `DoctrineIngressSystem.OnUpdate`:

```csharp
var clearEvents = World.Bus.ConsumeManaged<ClearDoctrineEvent>();
foreach (var evt in clearEvents)
{
    if (evt == null || !World.HasComponent<DoctrineState>(evt.Entity)) continue;
    ref var doctrine = ref World.GetComponentRW<DoctrineState>(evt.Entity);
    doctrine.ActiveDoctrineHash = DoctrineIds.None;
    unchecked { doctrine.InstanceId++; }
    doctrine.BrainTier = 0;
    if (World.HasComponent<BrainBTreeState>(evt.Entity))
        World.GetComponentRW<BrainBTreeState>(evt.Entity).State = default;
}
```

Sections §1.2 and §1.3 describe when the Mission layer publishes this event.

### 1.1 ChannelArbitrationSystem — OnExit Guarantee

**Problem:** `ChannelArbitrationSystem` detects a doctrine `InstanceId` mismatch and clears the outgoing channel by assigning `channel = default`. This sets `ActionInstanceId = 0` AND `DispatchedInstanceId = 0`. On the next tick `LocomotionDispatcherSystem` evaluates `ActionInstanceId != DispatchedInstanceId` → `0 != 0` → `false`, so `OnExit` is **never called**. `MoveToExecutor.OnExit` never runs; `NavigationIntent` is never cleared to `NavigationMode.None`; the muscle keeps driving forever.

**Fix:** Instead of resetting the channel to `default`, zero only `ActiveAction` and **increment** `ActionInstanceId` (unchecked). This preserves the inequality that triggers `OnExit` in `LocomotionDispatcherSystem` while still signalling that the channel has been deactivated. Apply the same pattern to `WeaponChannel` and `InteractionChannel`.

```
if (channel.ActiveAction != 0 && channel.DoctrineInstanceId != doctrine.InstanceId)
{
    channel.ActiveAction = 0;
    unchecked { channel.ActionInstanceId++; }
}
```

### 1.2 MissionDirectorSystem — End-of-Mission Doctrine Clear

**Problem:** When `MissionDirectorSystem` detects that the trigger has fired and `CurrentPhase >= PhaseCount`, it simply `continue`s without touching `DoctrineState`. The `ActiveDoctrineHash` permanently retains the last executed doctrine (e.g. `MoveToLocation_BT`), keeping the muscle layer permanently stimulated.

**Trigger upgrade:** The existing `ReachedDestination` trigger polls `NavState.HasArrived` directly. This creates Mission → Physics tier coupling. Add a new `DoctrineFinished` trigger type that instead consumes `DoctrineFinishedEvent` notifications published by `LocomotionDispatcherSystem` (see §1.0a). This is the proper architectural channel for doctrine completion reporting.

**End-of-plan fix (using ClearDoctrineEvent):** When all phases are exhausted, publish a `ClearDoctrineEvent`. Do NOT directly write `DoctrineState` — let `DoctrineIngressSystem` handle it (see §1.0b). This preserves the Mission/Cognitive separation of concerns.

```csharp
// in the triggered block:
if (queue.CurrentPhase < queue.PhaseCount)
{
    unchecked { doctrine.InstanceId++; }
    doctrine.ActiveDoctrineHash = phases[queue.CurrentPhase].DoctrineId;
}
else
{
    // Mission complete → delegate brain-death to DoctrineIngressSystem
    World.Bus.PublishManaged(new FDP.Toolkit.Behavior.Events.ClearDoctrineEvent { Entity = entity });
}
```

### 1.3 MissionControlRequestSystem — CMD_ABORT_ALL Doctrine Clear

**Problem:** `CMD_ABORT_ALL` zeroes the `MissionPlanQueue` but never touches `DoctrineState`. The entity continues stimulating its last channel even after an operator abort.

**Fix (using ClearDoctrineEvent imperative):** After zeroing the queue, publish a `ClearDoctrineEvent`. This is the correct use of the *imperative* event — an external command forcibly overriding whatever the doctrine machinery might be doing. Do NOT directly write `DoctrineState`.

```csharp
case eMissionCommandType.CMD_ABORT_ALL:
{
    // ... existing queue clear ...
    repo.Bus.PublishManaged(new FDP.Toolkit.Behavior.Events.ClearDoctrineEvent { Entity = entity });
    // ...
}
```

---

## Phase 2 — Right-Click Mission UX (Brain vs. Muscle Routing)

**Goal:** The SimHost right-click handler must differentiate between brain-active and brain-dead entities and route commands accordingly.

### 2.1 SimHostVisualization — Brain-Aware Right-Click Handler

**Problem (overshoot loop):** Regular right-click sends a `CMD_REPLACE_MISSION` with a `MoveToLocation` task whose `Triggers` list is empty. `MissionControlRequestSystem.BuildQueue` assigns a fallback `TimerElapsed(float.MaxValue)` trigger. The task never completes; `DoctrineState` is never cleared; vehicle overshoots and loops.

**Problem (Shift+Click conflict):** Shift+right-click calls `_scenario.AddWaypoint()` directly on `NavState`. If the entity has an active doctrine, `NavigationIntentBridgeSystem` overwrites `NavState` on the very next tick, erasing the waypoint and creating a 1-frame flicker.

**Fix:** Implement a two-path handler based on `DoctrineState.ActiveDoctrineHash`:

- **Brain-dead path** (`ActiveDoctrineHash == DoctrineIds.None` or no `DoctrineState`): talk directly to the muscle layer via `_scenario.SetDestination` / `_scenario.AddWaypoint`. This restores the pre-CQRS-split behaviour for local-only entities (collision test, roamers) and for entities that have been brought into a brain-dead state by a completed or aborted mission.

- **Brain-active path** (any non-zero doctrine): send a `CMD_REPLACE_MISSION` via `_missionWriter`. The task **must** include a `ReachedDestination` trigger so `MissionDirectorSystem` can advance through the plan and ultimately clear the doctrine when the queue is exhausted (via the fix in §1.2).

**No "Idle" sentinel task:** No explicit `Idle` task is needed or desired. The brain-death mechanism from Phase 1 handles the terminal state.

---

## Phase 3 — RVO Spatial Hash / Physics Collider

**Goal:** Restore vehicle-to-vehicle collision avoidance in SimHost standalone.

### 3.1 BdcTkbBuilder — Add PhysicsCollider to WithPhysics

**Problem:** `SpatialHashSystem` requires both `SimTransform` and `PhysicsCollider` (queried via `GlobalComponentIds.PhysicsCollider`) to insert an entity into the broadphase grid. The `WithPhysics` method in `BdcTkbBuilder` adds `VehicleParams` but **not** `PhysicsCollider`. Vehicles built purely via `WithPhysics` are invisible to the spatial hash; RVO neighbor queries return 0 results; no avoidance forces are generated.

Note: `WithCombat` already adds `PhysicsCollider` correctly, so combat-capable entities are unaffected. The omission is specific to `WithPhysics` for non-combat vehicle templates.

**Fix:** Add `PhysicsCollider` at the end of `WithPhysics`, using vehicle dimension data from `physicsDef`:

```csharp
template.AddComponent(new PhysicsCollider
{
    Radius = Math.Max(physicsDef.Length, physicsDef.Width) / 2f,
    CollisionLayer = PhysicsConstants.EntityCollisionLayer
});
```

### 3.2 SimHostScenarioManager — Add PhysicsCollider to SpawnEntityLocal

**Problem:** `SpawnEntityLocal` creates a bare ECS entity with `SimTransform`, `SimVelocity`, `VehicleState`, `VehicleParams`, and `NavState`, but no `PhysicsCollider`. Any collision test or roamer spawned via this path is excluded from the RVO grid.

**Fix:** Add `PhysicsCollider` at the end of `SpawnEntityLocal`:

```csharp
var preset = VehiclePresets.GetPreset(vehicleClass);
// ...existing adds...
_repo.AddComponent(e, new PhysicsCollider
{
    Radius = Math.Max(preset.Length, preset.Width) / 2f,
    CollisionLayer = PhysicsConstants.EntityCollisionLayer
});
```

---

## Phase 4 — Camera Offset (SimHost Standalone)

**Goal:** Fix "Center on entity" teleporting the map to the top-left corner.

### 4.1 SimHostVisualization.Initialize — Set Camera Offset

**Problem:** `SimHostVisualization.Initialize` creates the `MapCanvas` but never configures `_map.Camera.Offset`. The default `Offset = Vector2.Zero` causes `FocusOn()` to pin the entity to pixel (0,0) — the top-left corner — instead of the screen centre.

**Fix:** After creating the canvas, set the offset to half the window dimensions:

```csharp
_map = new MapCanvas();
_map.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f);
```

---

## End-to-End Brain-Death Lifecycle (Post-Fix)

The complete lifecycle for a right-click-navigated entity after all fixes:

1. Right-click → `CMD_REPLACE_MISSION` with `MoveToLocation` + `DoctrineFinished` trigger.
2. `MissionDirectorSystem` advances phase 0. `MissionAdapterSystem` detects the phase change and publishes `AssignDoctrineEvent`.
3. `DoctrineIngressSystem` consumes `AssignDoctrineEvent` → sets `ActiveDoctrineHash = MoveToLocation_BT`, bumps `InstanceId`, resets BTree state.
4. `ChannelArbitrationSystem` detects `InstanceId` bump → no preemption (channel not yet active for the new doctrine).
5. `BTreeTickSystem` ticks the `MoveToLocation` BTree → leaf dispatches `MoveToExecutor.OnEnter` via `LocomotionDispatcherSystem` → writes `NavigationIntent`.
6. `NavigationIntentBridgeSystem` copies intent to `NavState`.
7. Vehicle moves. `NavigationExecutionSystem` (Muscle) writes `NavigationStatus.Result = Arrived`.
8. `MoveToExecutor.Execute` reads `NavigationStatus`, sets `channel.Status = NodeStatus.Success`.
9. `BTreeTickSystem` calls `Interpreter.Tick()` → BTree **root** evaluates to `NodeStatus.Success` → publishes `DoctrineFinishedEvent { Entity, Result = Success }` **(bottom-up notification from the cognitive machinery)**.
10. `MissionDirectorSystem` consumes `DoctrineFinishedEvent` (new `DoctrineFinished` trigger) → `CurrentPhase++` → `CurrentPhase >= PhaseCount` → publishes `ClearDoctrineEvent` **(top-down imperative)**.
11. `DoctrineIngressSystem` consumes `ClearDoctrineEvent` → sets `ActiveDoctrineHash = DoctrineIds.None`, bumps `InstanceId`, resets `BrainBTreeState`.
12. `ChannelArbitrationSystem` sees `InstanceId` mismatch → zeroes `ActiveAction`, increments `ActionInstanceId`.
13. `LocomotionDispatcherSystem` sees `ActionInstanceId != DispatchedInstanceId` → calls `MoveToExecutor.OnExit` → sets `NavigationIntent.Mode = NavigationMode.None`.
14. `NavigationIntentBridgeSystem` skips entity (mode = None).
15. Vehicle decelerates and stops. **Entity is now brain-dead.**
16. Shift+right-click now takes the muscle path → `_scenario.AddWaypoint()` works correctly.

**Abort path (CMD_ABORT_ALL):**
- `MissionControlRequestSystem` zeroes queue → publishes `ClearDoctrineEvent` **(top-down imperative, bypasses the doctrine-finished notification entirely — doctrine machinery is interrupted mid-execution)**.
- Steps 11–15 apply identically.

---

## Phase 5 — DisType DDS Struct

**Goal:** Replace the plain `long DisType` field on the `EntityMaster` DDS topic with a proper `@final` struct so monitoring tools can display each DIS field individually.

### 5.1 DDS Data Model — DisTypeStruct

**Problem:** `EntityMaster.DisType` is published as a plain `long` (= `ulong` serialised). DDS monitoring tools display it as an opaque integer; operators cannot read Kind, Domain, Country etc. at a glance.

**Engine side is already correct:** `Fdp.Kernel.DISEntityType` uses `[StructLayout(LayoutKind.Explicit, Size = 8)]` with individual byte fields overlaid on a `ulong Value`. This supports O(1) entity-query filtering via `(header.DisType.Value & mask) == expected`.

**Fix — DDS boundary only:**

1. Add `DisTypeStruct` (8 fields, all byte/ushort) to `Hrot.NED/GenericDescriptors.cs`.
2. Change `EntityMaster.DisType` from `long` → `DisTypeStruct`.
3. In `EntityMasterEgressTranslator`, map `DISEntityType` fields → `DisTypeStruct` fields before publishing.
4. In ingress translators (wherever `EntityMaster` is decoded), reconstruct `DISEntityType` from the 8 `DisTypeStruct` fields.

The engine `DISEntityType` struct and all query filters remain unchanged.

---

## Phase 6 — Entity Inspector Component Change Detection

**Goal:** Highlight ECS components whose data changed since the previous frame in the ImGui entity inspector.

### 6.1 ComponentReflector — Byte-Cache Change Detection

**Problem:** `ComponentReflector` renders all components uniformly every frame with no indication of which ones are actively mutating.

**Fix:** Introduce a per-entity, per-type byte cache inside `ComponentReflector`. Each frame, for value-type (unmanaged) components only:

1. Marshal the struct to a temporary unmanaged buffer.
2. Compare byte-by-byte with the cached bytes.
3. If different: push `ImGuiCol.Text = Yellow` before drawing the `CollapsingHeader`, pop immediately after.
4. Update the cache regardless.

Managed class components (`!type.IsValueType`) are **skipped entirely** — no cloning, no comparison, no allocation.

Cache invalidation: when `_lastInspectedEntity != e`, clear `_unmanagedCache` and update the baseline silently (no flash on first view).

---

## Phase 7 — CreateEntityRequestSystem Hot-Path Delegate Caching

**Goal:** Eliminate a per-tick delegate allocation in `CreateEntityRequestSystem`.

### 7.1 CreateEntityRequestSystem — Cache ProcessRequest Delegate

**Problem:** The call `_requestSource.ProcessRequests(request => { ... })` passes a lambda that captures `this`. Even though no local variables are captured, the C# compiler allocates a new `Action<CreateEntityRequest>` heap object on every `Execute` call (~60 fps), generating continuous Gen0 GC pressure.

**Fix:**
1. Extract the lambda body to a private method `ProcessIncomingRequest(CreateEntityRequest request)`.
2. Declare a `readonly Action<CreateEntityRequest> _processRequestDelegate` field.
3. In the constructor, assign `_processRequestDelegate = ProcessIncomingRequest`.
4. In `Execute`, call `_requestSource.ProcessRequests(_processRequestDelegate)` — reuses the same delegate instance every tick.

This is the standard FDP pattern for zero-allocation hot-path callbacks.
