# BATCH-03: EQS Network Translators + Hill Attack Integration Test

## Purpose

This batch completes the hill-attack feature with two final tasks:

- **TASK-HA004** — EQS Network Translators (DDS bridge for AreaQueryBatchData between Brain and Muscle nodes)
- **TASK-HA015** — Integration Test proving the full PlatoonHillAttack behavior works end-to-end headlessly

**Reference material:** `.dev/hill-attack/TASK-DETAIL.md` (full task specs), `.dev/hill-attack/DESIGN.md`.

---

## Pre-conditions

- `dotnet build IOS-IG-SimHost.sln` must succeed (currently 0 errors).
- Current test baseline: 558 passing, 6 pre-existing failures (do NOT touch those 6).
- Pre-existing failures (never fix, never count as regressions):
  - 3 in `UnitSubordinateTranslatorTests`
  - 2 in `MissionPlanTranslatorTests`
  - 1 in `CreateEntityRequestSystemTests.C013_ChildOverride_KeyAbsent_AllocatorCalledForChild`

---

## TASK-HA004: EQS Network Translators

**Full spec:** TASK-DETAIL.md `### TASK-HA004` section.

### Files to create

All new files in `Hrot/Network/Hrot.Network.NED/SimHost/`:

- `AreaQueryTranslators.cs` — contains all 4 translator classes
- (Optional) update `BrainPathfindingTranslatorPack.cs` and `SimPathfindingTranslatorPack.cs`
  as the model for how packs are structured

### Step 1 — Add EDescriptorType entries

File: `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs`

Add two new entries to `EDescriptorType` enum after `dtTacticalIntentRequest = 92`:

```csharp
dtAreaQueryRequestBatch  = 93,
dtAreaQueryResponseBatch = 94,
```

### Step 2 — Define DDS message types

File: `Hrot/Network/Hrot.Network.NED/Messages/AreaQueryMessages.cs` (new file)

```csharp
// One request item per entry in AreaQueryBatchData.Requests.
public struct DdsAreaQueryRequest
{
    public long  RequestId;
    public long  TargetAreaNetworkId;   // NetworkIdentity.Value of the area entity
    public int   SourceNodeId;
    public int   ForceId;               // cast of ForceId enum
}

// One response item per request. TargetNetworkIds contains resolved hostile entity network IDs.
public class DdsAreaQueryResponse
{
    public long           RequestId;
    public int            TargetCount;
    [DdsManaged]
    public List<long>     TargetNetworkIds;
}

// Top-level DDS messages.
public class AreaQueryRequestBatch
{
    public int                       SourceNodeId;
    public List<DdsAreaQueryRequest> Requests;
}

public class AreaQueryResponseBatch
{
    public List<DdsAreaQueryResponse> Responses;
}
```

**Constraint:** `DdsAreaQueryResponse.TargetNetworkIds` must be `[DdsManaged]` — it is the
only managed field allowed. All other fields are value types.

### Step 3 — Implement the 4 translator classes

File: `Hrot/Network/Hrot.Network.NED/SimHost/AreaQueryTranslators.cs`

Use `PathfindingTranslators.cs` as the reference pattern for all four classes.

---

#### 3a. `AreaQueryBrainEgressTranslator`

Direction: Brain → Muscle (egress from Brain, ingress to Muscle).

```
Topic: "AreaQueryRequestBatch"
DescriptorOrdinal: EDescriptorType.dtAreaQueryRequestBatch
Direction: TranslatorDirection.Egress
```

`ScanAndPublish(ISimulationView view)`:
1. Cast view to `EntityRepository repo`. If not, return.
2. Check `repo.HasSingleton<AreaQueryBatchData>()`. If false, return.
3. `ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();`
4. If `batch.Count == 0`, return (nothing to send).
5. Get local node ID. Only forward requests where `batch.Requests[i].SourceNodeId == _localNodeId`
   (authority check — only forward locally-originated requests).
6. Collect qualifying requests into a `List<DdsAreaQueryRequest>`, resolving
   `TargetAreaEntity` to a network ID via `_entityMap.TryGetNetworkId(batch.Requests[i].TargetAreaEntity, out long netId)`.
   If lookup fails, skip that request (log at Warn level).
7. Write `AreaQueryRequestBatch { SourceNodeId = _localNodeId, Requests = ... }` via DDS writer.
8. Increment `SentSampleCount`.
9. Set `batch.Count = 0` after publishing (Brain does not run AreaQuerySolverSystem).

`PollIngress`: no-op.

Constructor: internal test constructor accepting `IDdsWriter<AreaQueryRequestBatch>` + `NetworkEntityMap` + `int localNodeId`.

---

#### 3b. `AreaQueryMuscleIngressTranslator`

Direction: Brain → Muscle (ingress on Muscle side).

```
Topic: "AreaQueryRequestBatch"
DescriptorOrdinal: EDescriptorType.dtAreaQueryRequestBatch
Direction: TranslatorDirection.Ingress
```

`PollIngress(IEntityCommandBuffer cmd, ISimulationView view)`:
1. Read samples from `IDdsReader<AreaQueryRequestBatch>`.
2. If no samples, return.
3. Get or create `AreaQueryBatchData` singleton. If not present, return (Muscle not initialized yet).
4. For each `DdsAreaQueryRequest` in the batch:
   a. Resolve `TargetAreaNetworkId` to a local `Entity` via `_entityMap.TryGetEntity(req.TargetAreaNetworkId, out var areaEntity)`.
   b. If resolution fails: write a response immediately with `TargetCount = 0` for this `RequestId`
      (do not crash; the area entity may not have materialized yet on this node).
   c. If resolution succeeds: call `AreaQueryBatchHelper.RequestAreaQuery(repo, ...)` using the
      resolved local entity, or manually write a slot into `AreaQueryBatchData` with the correct
      `RequestId` (preserve the original Brain-originated `RequestId` so the Brain can match it).
5. Increment `ReceivedSampleCount` per batch received.

**Important:** Preserve the original `RequestId` from the Brain. The Muscle solver uses this ID
to store the result; the Brain ingress translator matches on it.

`ScanAndPublish`: no-op.

---

#### 3c. `AreaQueryMuscleEgressTranslator`

Direction: Muscle → Brain (egress from Muscle).

```
Topic: "AreaQueryResponseBatch"
DescriptorOrdinal: EDescriptorType.dtAreaQueryResponseBatch
Direction: TranslatorDirection.Egress
```

`ScanAndPublish(ISimulationView view)`:
1. Cast view to `EntityRepository repo`. If not, return.
2. Check `repo.HasSingleton<AreaQueryBatchData>()`. If false, return.
3. `ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();`
4. Collect all results where `batch.Results[i].IsReady == true` (up to `batch.Count` entries).
5. For each ready result:
   a. Get the `EqsTargetPool` and read target entity packed values (up to `result.TargetCount`).
   b. For each target entity, resolve to a network ID via
      `_entityMap.TryGetNetworkId(targetEntity, out long networkId)`.
      If lookup fails, skip that entity (entity may have died between solve and egress).
   c. Build `DdsAreaQueryResponse { RequestId, TargetCount = resolvedIds.Count, TargetNetworkIds = resolvedIds }`.
6. Publish `AreaQueryResponseBatch { Responses = ... }` via DDS writer. Only publish if responses non-empty.
7. Increment `SentSampleCount`.

`PollIngress`: no-op.

---

#### 3d. `AreaQueryBrainIngressTranslator`

Direction: Muscle → Brain (ingress on Brain side).

```
Topic: "AreaQueryResponseBatch"
DescriptorOrdinal: EDescriptorType.dtAreaQueryResponseBatch
Direction: TranslatorDirection.Ingress
```

`PollIngress(IEntityCommandBuffer cmd, ISimulationView view)`:
1. Read samples from `IDdsReader<AreaQueryResponseBatch>`.
2. If no samples, return.
3. Get `AreaQueryBatchData` singleton. If not present, return.
4. For each `DdsAreaQueryResponse` in all batches:
   a. Find the matching slot in `batch.Results` by `RequestId`.
   b. Resolve each `TargetNetworkId` to a local entity via `_entityMap.TryGetEntity`.
      Skip any ID that fails to resolve (entity not yet materialized on Brain).
   c. Write resolved entities into `EqsTargetPool` at the appropriate handle offset.
   d. Set `batch.Results[slot] = new AreaQueryResult { RequestId, IsReady = true, TargetCount = resolvedCount, TargetGroupHandle = slot }`.
5. Increment `ReceivedSampleCount` per batch received.

`ScanAndPublish`: no-op.

---

### Step 4 — Pack registration

Create `EqsTranslatorPack.cs` (or add to existing `BrainPathfindingTranslatorPack.cs` / `SimPathfindingTranslatorPack.cs`):

```csharp
// Brain side pack: Brain Egress + Brain Ingress
public static IReadOnlyList<IDescriptorTranslator> CreateBrainEqsTranslators(
    DdsParticipant participant, NetworkEntityMap entityMap, int localNodeId)

// Muscle side pack: Muscle Ingress + Muscle Egress
public static IReadOnlyList<IDescriptorTranslator> CreateMuscleEqsTranslators(
    DdsParticipant participant, NetworkEntityMap entityMap)
```

Register them via the existing `NedSimHostPathfindingTranslators` constructor pattern:
add them when `role.HasFlag(NodeRole.Brain)` (for Brain translators)
and when `role.HasFlag(NodeRole.MuscleGround)` (for Muscle translators).

If `NedSimHostPathfindingTranslators` is not the right place, look at
`SimHostAuxiliaryTranslatorPack.cs` and add EQS to the appropriate translator pack.

---

### Step 5 — Tests for TASK-HA004

**File:** `Hrot/Network/Hrot.Network.NED.Tests/AreaQueryTranslatorTests.cs` (new)
OR `Hrot/Subsystems/Hrot.SimHost.Tests/AreaQueryTranslatorTests.cs` if Hrot.Network.NED.Tests
does not have access to `AreaQueryBatchData`.

**Note on SC-HA004-1 (two-node fixture):** A full two-node DDS test is out of scope for
batch unit testing. Instead, test the translators in-process by:
- Creating two `EntityRepository` instances (Brain repo + Muscle repo).
- Using stub reader/writer adapters (same pattern as `TacticalIntentEgressTranslator`'s
  internal test constructor).
- Running BrainEgress → MuscleIngress → AreaQuerySolverSystem → MuscleEgress → BrainIngress
  in sequence within a single test.

#### Test SC-HA004-1: End-to-end pipeline (single process, stub DDS)

```
SC_HA004_1_AreaQueryPipeline_BrainRequestReachesBack_WithTargets

Setup:
- Brain repo: AreaQueryBatchData singleton with 1 request (slot 0).
  RequestId computed via AreaQueryBatchHelper.RequestAreaQuery(brainRepo, commanderEntity, areaNetId_as_Entity, ForceId.Hostile).
- Muscle repo: 2 enemy entities inside polygon; area entity with EditablePolyline; SpatialGridData.
- NetworkEntityMap shared between both sides (maps enemy network IDs and area entity network ID).
- Stub DDS writer captures the AreaQueryRequestBatch written by BrainEgressTranslator.
- Stub DDS reader delivers the captured batch to MuscleIngressTranslator.

Steps:
1. Run AreaQueryBrainEgressTranslator.ScanAndPublish(brainRepo).
   Verify stub writer captured a batch with 1 request.
2. Run AreaQueryMuscleIngressTranslator.PollIngress(cmd, muscleRepo).
   Verify muscleRepo.AreaQueryBatchData has the request.
3. Run AreaQuerySolverSystem.Execute(muscleRepo, 0.016f).
   Verify result has TargetCount == 2.
4. Run AreaQueryMuscleEgressTranslator.ScanAndPublish(muscleRepo).
   Verify stub writer captured a response batch with TargetCount == 2.
5. Run AreaQueryBrainIngressTranslator.PollIngress(cmd, brainRepo).
   Verify brainRepo.AreaQueryBatchData.Results[0].IsReady == true.
   Verify Results[0].TargetCount == 2.

Assert: Full round-trip completes with TargetCount == 2.
```

#### Test SC-HA004-2: Unresolved area entity on Muscle → TargetCount == 0

```
SC_HA004_2_MuscleIngress_UnresolvedAreaEntity_WritesZeroTargetResponse

Setup: Brain sends request with TargetAreaNetworkId = 9999L (not in Muscle's NetworkEntityMap).
Steps: Run MuscleIngressTranslator only.
Assert: A TargetCount=0 response is queued (or directly placed into AreaQueryBatchData with IsReady=true, TargetCount=0).
No exception thrown.
```

#### Test SC-HA004-3: Unresolved target entity silently skipped

```
SC_HA004_3_MuscleEgress_UnresolvedTargetEntity_SkippedInResponse

Setup: Muscle repo has a solved result with TargetCount=3. One of the 3 target entities
  is NOT in the NetworkEntityMap (simulating death between solve and egress).
Steps: Run AreaQueryMuscleEgressTranslator.ScanAndPublish.
Assert: The DDS response has TargetCount == 2 (only 2 resolved). No exception.
```

---

## TASK-HA015: Integration Test (Scenario-based)

**Full spec:** TASK-DETAIL.md `### TASK-HA015` section.

This is the most important task in the batch. The integration test must prove the full
PlatoonHillAttack behavior works end-to-end in a single-process headless simulation.

**Philosophy:** The test drives the CGF system (BTreeTickSystem + BehaviorIngressSystem +
TacticalIntentResolutionSystem) directly. There is NO muscle tier executing real kinematics.
Instead, the test manually sets component values to simulate muscle-tier responses
(NavigationStatus, LocomotionChannel feedback, WeaponChannel feedback, entity death).

### File

`Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackIntegrationTests.cs` (new file)

### World Setup Helper

Create a `CreateIntegrationWorld()` helper that:

1. Creates a new `EntityRepository`.
2. Calls `SimHostComponentRegistry.RegisterAll(world)` to register all components.
   (Or replicate the component registration from `HillAttackNodeTests.CreateWorld()`,
   adding any missing registrations needed for the full BTree pipeline.)
3. Registers events: `AssignTacticalIntentEvent`, `AssignBehaviorEvent`,
   `AssignBehaviorHashEvent`, `BehaviorFinishedEvent`, and any others needed by
   `BehaviorIngressSystem` and `TacticalIntentResolutionSystem`.
4. Sets `GlobalTime` singleton: `{ DeltaTime = 0.1f, TimeScale = 1.0f }`.
   (Using 0.1s steps gives 10 simulated steps per real second.)
5. Creates and sets `AreaQueryBatchData` singleton (DefaultCapacity=64).
6. Creates and sets `EqsTargetPool` singleton.
7. Creates and sets a `SpatialGridData` singleton sufficient for the polygon test.
8. Creates and sets `NetworkEntityMap` singleton.
9. Returns the world.

### System Pipeline Helper

Create a `TickOnce(EntityRepository repo, systems)` method that executes one simulation
frame in this order:

```csharp
private static void TickOnce(
    EntityRepository repo,
    AreaQueryInitializationSystem eqsInit,
    BehaviorIngressSystem behaviorIngress,
    TacticalIntentResolutionSystem tacticalResolution,
    BTreeTickSystem btreeTick,
    AreaQuerySolverSystem eqsSolver,
    float dt = 0.1f)
{
    var view = (ISimulationView)repo;
    // Frame start: swap event buffers so events from last frame are readable.
    repo.Bus.SwapBuffers();
    // Input phase:
    eqsInit.Execute(view, dt);
    behaviorIngress.Execute(view, dt);
    // Simulation phase:
    tacticalResolution.Execute(view, dt);
    btreeTick.Execute(view, dt);
    // SoD (run locally in tests at full rate):
    eqsSolver.Execute(view, dt);
}
```

### Entity Setup Helpers

```csharp
private static Entity CreateCommander(EntityRepository repo, Entity[] subordinates,
    PlatoonHillAttackParams p, NetworkEntityMap entityMap)
{
    // Create commander entity with all required components.
    var commander = repo.CreateEntity();
    repo.AddComponent<Blackboard1024>(commander, default);
    repo.AddComponent<BehaviorState>(commander, default);
    repo.AddComponent<BrainBTreeState>(commander, default);
    repo.AddComponent<BrainBlackboard>(commander, default);
    repo.AddComponent<NetworkIdentity>(commander, new NetworkIdentity { Value = 1000L });
    // Build UnitRoster with the provided subordinates.
    // Use AddRoster helper (same as HillAttackNodeTests.AddRoster).
    // Commander must also have UnitSubordinate? No -- commander is the boss entity.
    // Return commander entity.
    return commander;
}

private static Entity CreateTank(EntityRepository repo, int networkId,
    float x, float y, NetworkEntityMap entityMap)
{
    var tank = repo.CreateEntity();
    repo.AddComponent(tank, new SimTransform { Position = new Vector3(x, y, 0f) });
    repo.AddComponent(tank, new LocomotionChannel());
    repo.AddComponent(tank, new WeaponChannel());
    repo.AddComponent(tank, new TargetMemory());
    repo.AddComponent(tank, new BehaviorState());
    repo.AddComponent(tank, new BrainBTreeState());
    repo.AddComponent(tank, new BrainBlackboard());
    repo.AddComponent(tank, new NavigationStatus { Result = NavigationResult.Arrived });
    repo.AddComponent(tank, new NetworkIdentity { Value = (long)networkId });
    repo.AddComponent(tank, new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
    repo.AddComponent(tank, new UnitSubordinate());
    entityMap.Register(tank, (long)networkId);
    return tank;
}

private static Entity CreateEnemy(EntityRepository repo, long networkId,
    float x, float y, NetworkEntityMap entityMap)
{
    var enemy = repo.CreateEntity();
    repo.AddComponent(enemy, new SimTransform { Position = new Vector3(x, y, 0f) });
    repo.AddComponent(enemy, new NetworkIdentity { Value = networkId });
    repo.AddComponent(enemy, new ForceIdComponent { Value = ForceId.Hostile });
    entityMap.Register(enemy, networkId);
    return enemy;
}

private static Entity CreateAreaPolygon(EntityRepository repo, long networkId,
    List<Vector2> polygonPoints, NetworkEntityMap entityMap)
{
    // Create area entity with EditablePolyline component.
    var area = repo.CreateEntity();
    repo.AddComponent(area, new NetworkIdentity { Value = networkId });
    var polyline = new EditablePolyline { Points = polygonPoints };
    repo.AddComponentManaged(area, polyline);
    entityMap.Register(area, networkId);
    return area;
}
```

### Core Integration Test: SC-HA015-1

```csharp
[Fact]
public void SC_HA015_1_PlatoonHillAttack_CommanderFinishes_AfterAreaCleared()
```

**Setup:**
- Firing line: `(0, 0)` to `(90, 0)` (90m segment, 3 slots at 30m spacing)
- Baseline: `(0, 50)` to `(90, 50)` (50m behind firing line along Y)
- Attack direction: `(0, -1)` (Y-negative, computed as perpendicular to firing line)
- 4 tank entities at baseline positions `(0,50)`, `(30,50)`, `(60,50)`, `(90,50)`
- 2 enemy entities INSIDE a square polygon from `(-10, -50)` to `(100, -50)` to `(100, 10)` to `(-10, 10)` (polygon that contains the firing line).
  Enemies at `(20, -20)` and `(70, -20)`.
- `PlatoonHillAttackParams`:
  ```
  StartX=0, StartY=0, EndX=90, EndY=0,
  BaselineStartX=0, BaselineStartY=50, BaselineEndX=90, BaselineEndY=50,
  AttackDirX=0, AttackDirY=-1,
  TankSpacing=30f,
  ApproachSpeed=10f, CreepSpeed=3f,
  TargetAreaEntity = areaEntity
  ```

**System creation:**
```csharp
var entityMap    = new NetworkEntityMap();
var registry     = new BehaviorRegistry();
AiBehaviorFactory.BuildRegistrationAction(null, entityMap)(registry);

var mapperRegistry = new TacticalIntentMapperRegistry();
mapperRegistry.Register(new HullDownAttackMapper());

var behaviorIngress    = new BehaviorIngressSystem(registry);
var tacticalResolution = new TacticalIntentResolutionSystem(mapperRegistry);
var btreeTick          = new BTreeTickSystem(registry);
var eqsInit            = new AreaQueryInitializationSystem();
var eqsSolver          = new AreaQuerySolverSystem();
```

**Behavior activation:**
```csharp
// Directly write PlatoonHillAttackParams into commander's BrainBlackboard
// using ParsePlatoonHillAttackParams (pass null geoTransform for Cartesian bypass,
// pre-resolved areaEntity).
// Then publish AssignBehaviorEvent for commander.
unsafe
{
    ref var bb  = ref repo.GetComponentRW<BrainBlackboard>(commander);
    fixed (byte* ptr = bb.Memory)
        HillAttackCommanderNodes.ParsePlatoonHillAttackParams(
            BuildTestJson(areaNetworkId), ptr, geoTransform: null, entityMap);
}
// Publish event on the bus BEFORE the first tick (BehaviorIngressSystem will read it).
repo.Bus.PublishManaged(new AssignBehaviorEvent
{
    Entity       = commander,
    BehaviorName = "PlatoonHillAttack",
});
```

**Simulation loop:**

```csharp
bool finished = false;
int maxFrames = 300;  // 30 simulated seconds at 0.1s/frame

for (int frame = 0; frame < maxFrames && !finished; frame++)
{
    TickOnce(repo, eqsInit, behaviorIngress, tacticalResolution, btreeTick, eqsSolver);

    // Simulate muscle tier: set NavigationStatus.Arrived for tanks that have been
    // dispatched (LocomotionChannel has a MoveToLocation command).
    SimulateNavigation(repo, tankEntities, frame);

    // Simulate weapon: when WeaponChannel has AimAndFire, resolve after 3 ticks.
    SimulateWeapon(repo, tankEntities, enemyEntities, ref frame);

    // Check for BehaviorFinishedEvent on commander.
    var finishedEvents = repo.Bus.ReadManaged<BehaviorFinishedEvent>();
    foreach (var ev in finishedEvents)
    {
        if (ev.Entity == commander)
        {
            finished = true;
            break;
        }
    }
}

Assert.True(finished, $"PlatoonHillAttack did not finish within {maxFrames} frames (30 simulated seconds).");
```

**SimulateNavigation helper:**
```csharp
// When a tank has NavigationStatus != Arrived AND LocomotionChannel.ActiveAction is set,
// set NavigationStatus.Arrived after a short delay (simulate the muscle completing movement).
// Use a simple "set Arrived 5 frames after command was issued" strategy.
```

**SimulateWeapon helper:**
```csharp
// When a tank has WeaponChannel.ActiveAction == AimAndFire:
//   - After 3 frames: set WeaponChannel.Status = NodeStatus.Success.
//   - Also: destroy the targeted enemy entity (simulating kill).
// Use the TargetNetworkId from the tank's HullDownAttackParams to find the enemy.
```

**Assertions:**
```csharp
Assert.True(finished, "Commander BehaviorFinishedEvent not observed within time limit.");
// Both enemies should be destroyed.
Assert.False(repo.IsAlive(enemy1), "Enemy 1 should be dead.");
Assert.False(repo.IsAlive(enemy2), "Enemy 2 should be dead.");
```

---

### Test SC-HA015-2: No two tanks in same wave assigned same slot

```csharp
[Fact]
public void SC_HA015_2_WaveDispatch_NoTwoTanksShareSameFiringSlot()
```

**Approach:**
- Run a smaller scenario (4 tanks, 2 enemies, 4 firing slots).
- Capture all `AssignTacticalIntentEvent` events that have `IntentId == "HullDownAttack"`.
- Parse the `SlotX`/`SlotY` coordinates from each event's `JsonParams`.
- Assert that within each wave (consecutive pairs of events from the same wave dispatch),
  no two events have identical slot coordinates.
- Run for at most 10 ticks; the commander only needs to reach `Action_DispatchWaveWithTargets`
  (you can manually pre-set all navigation as Arrived and EQS batch as pre-resolved).

**Shortcut for determinism:** Instead of running the full pipeline, set up the commander's
`HillAttackMutableState` directly after `Action_CalculateSegments`, pre-fill the
`AreaQueryBatchData.Results[0]` with a ready result (2 targets), then call
`Action_DispatchWaveWithTargets` directly and capture events. This is a unit-style
approach that still proves the functional requirement.

Capture `AssignTacticalIntentEvent` entries and parse slot coordinate pairs. Assert all
slot pairs are distinct within a single wave dispatch.

---

### Test SC-HA015-3: Tank killed mid-wave, wave still completes, slot burned

```csharp
[Fact]
public void SC_HA015_3_TankKilledMidWave_WaveCompletes_SlotBurned()
```

**Setup:** 4 tanks, 2 enemies. Run commander to the point where Wave 0 has been dispatched
(2 tanks selected, `ActiveAttackerCount == 2`). Manually kill one of the 2 active
attackers (call `repo.DestroyEntity(attacker)`). Continue ticking. Assert:
1. `Condition_IsWaveCompleted` eventually returns Success (ActiveAttackerCount goes to 0).
2. The killed tank's firing slot bit is set in `BurnedSlotsMask`.
3. The remaining tank (the survivor) still completes its run (BehaviorState hash cycles
   through HullDownAttackRun → back to non-HullDown).

**Approach:**
- Use `HillAttackMutableState` debug projection to inspect state.
- Reference the same `Unsafe.As<Blackboard1024, HillAttackMutableState>` pattern
  from existing HillAttackNodeTests helper `GetHeavyState`.

---

### Test SC-HA015-4: Round-robin target distribution (3 tanks, 2 enemies)

```csharp
[Fact]
public void SC_HA015_4_RoundRobin_ThreeTanksTwoEnemies_CorrectAssignment()
```

This test verifies SC-HA012-3 via the full dispatch pipeline (not just node-level):

**Setup:**
- 3 tanks (roster.Count <= 3 → allParticipate), 2 enemy entities with network IDs 101 and 202.
- Pre-resolve EQS result with TargetCount=2, TargetGroupHandle=0.
- Fill `EqsTargetPool.Targets[0] = packed(enemy1)`, `Targets[1] = packed(enemy2)`.
- Set commander state: CachedTargetGroupHandle=0.

**Execute:** `Action_DispatchWaveWithTargets` directly.

**Capture events:** Read `AssignTacticalIntentEvent` entries.
Parse `TargetNetworkId` from each event's JSON payload.

**Assert:**
```csharp
Assert.Equal(3, events.Count);
Assert.Equal(101L, ParseTargetNetworkId(events[0].JsonParams));
Assert.Equal(202L, ParseTargetNetworkId(events[1].JsonParams));
Assert.Equal(101L, ParseTargetNetworkId(events[2].JsonParams));
```

---

### Test SC-HA015-5: Full behavior activation via AssignBehaviorEvent

```csharp
[Fact]
public void SC_HA015_5_AssignBehavior_PlatoonHillAttack_ActivatesWithinOneFrame()
```

Verifies that `BehaviorIngressSystem` picks up `AssignBehaviorEvent { BehaviorName = "PlatoonHillAttack" }`
and sets `BehaviorState.ActiveBehaviorHash` to the PlatoonHillAttack behavior hash within
one simulation frame. This proves the wiring from event → behavior activation is intact.

**Steps:**
1. Create world with BehaviorRegistry populated via `AiBehaviorFactory.BuildRegistrationAction`.
2. Create commander entity with `BrainBlackboard`, `BrainBTreeState`, `BehaviorState`.
3. Publish `AssignBehaviorEvent { Entity = commander, BehaviorName = "PlatoonHillAttack" }`.
4. Swap bus buffers. Run `BehaviorIngressSystem.Execute`.
5. Assert `repo.GetComponentRO<BehaviorState>(commander).ActiveBehaviorHash == PlatoonHillAttackBehaviorHash`.
   (Behavior hash can be obtained via `registry.GetByName("PlatoonHillAttack").BehaviorId`
   or by reading the BehaviorState after activation.)

---

### Test SC-HA015-6: AreaQuerySolverSystem resolves enemies inside polygon

```csharp
[Fact]
public void SC_HA015_6_AreaQuerySolverSystem_LocalRun_FindsEnemiesInsidePolygon()
```

Verifies that `AreaQuerySolverSystem` correctly solves the query in a single process
without any network translators:

**Setup:**
- Area entity with `EditablePolyline` polygon `(-10,-50) → (100,-50) → (100,10) → (-10,10)`.
- Enemy 1 at `(20,-20)` inside polygon with `ForceId.Hostile`.
- Enemy 2 at `(70,-20)` inside polygon with `ForceId.Hostile`.
- Friendly tank at `(45, 50)` OUTSIDE polygon.
- 1 `AreaQueryRequest` in `AreaQueryBatchData` for the area entity, `ForceId.Hostile`.

**Execute:** `AreaQuerySolverSystem.Execute(repo, 0.1f)`.

**Assert:**
- `AreaQueryBatchData.Results[0].IsReady == true`.
- `AreaQueryBatchData.Results[0].TargetCount == 2` (only enemies inside, friendlies excluded).

---

## Implementation Notes for Developer

### ParsePlatoonHillAttackParams with null geoTransform

When `geoTransform == null`, the `ParsePlatoonHillAttackParams` method must handle a
Cartesian-only code path where input coordinates are already ENU floats (no geodetic
conversion needed). Check the existing implementation and add this guard:

```csharp
if (geoTransform != null)
{
    // geodetic conversion path
}
else
{
    // direct assignment path (test/Cartesian mode)
    p->StartX = dto.FiringLineStart.X;  // assuming DTO has Vector2 or float fields
    p->StartY = dto.FiringLineStart.Y;
    // etc.
}
```

If `ParsePlatoonHillAttackParams` already has this, do not change it.

### Test JSON format for PlatoonHillAttackParams

```json
{
  "firingLineStart": { "x": 0, "y": 0 },
  "firingLineEnd": { "x": 90, "y": 0 },
  "baselineStart": { "x": 0, "y": 50 },
  "baselineEnd": { "x": 90, "y": 50 },
  "tankSpacing": 30,
  "targetAreaNetworkId": 9001
}
```

Match the field names in `PlatoonHillAttackParamsJsonDto`.

### GetHeavyState helper

Reuse the exact `GetHeavyState` helper from `HillAttackNodeTests.cs`:

```csharp
private static unsafe ref HillAttackMutableState GetHeavyState(
    EntityRepository repo, Entity entity)
{
    ref var bb = ref repo.GetComponentRW<Blackboard1024>(entity);
    return ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref bb);
}
```

### BehaviorFinishedEvent

Find `BehaviorFinishedEvent` in the behavior toolkit. It is published by `MissionDirectorSystem`
or `BTreeTickSystem` when a behavior terminates. Check:
```
grep -r "BehaviorFinishedEvent" Hrot/ FDP/
```
to confirm the event name and namespace.

If `BehaviorFinishedEvent` does not exist, use an alternative observable signal:
`BehaviorState.ActiveBehaviorHash == 0` (cleared when behavior ends) OR subscribe to
`ClearBehaviorEvent` if that is what the system publishes. Document which signal you use.

### SpatialGridData setup

`AreaQuerySolverSystem` uses `SpatialGridData` for broadphase. In tests, set up a minimal
spatial grid that covers the polygon area:

```csharp
repo.SetSingletonUnmanaged(new SpatialGridData
{
    CellSize = 50f,
    // ... other fields as required
});
```

Check `AreaQuerySolverSystem.cs` for what fields it reads from `SpatialGridData`.
If the system does not strictly require a populated spatial grid (falls back to linear scan),
set a minimal default and note this in a comment.

### DisposeEqsSingletons pattern

For any test that uses `AreaQueryBatchData` and `EqsTargetPool`, call
`AreaQueryBatchHelper.ResetBatch(repo)` in a `finally` block to avoid NativeArray leaks.

---

## Build Validation

After implementation:

```bash
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln
```

Must succeed with 0 errors.

Then:

```bash
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --no-build
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Network\Hrot.Network.NED.Tests\Hrot.Network.NED.Tests.csproj --no-build
```

**Pass criteria:**
- All 6 pre-existing failures are still the only failures.
- All new tests pass: at minimum 3 translator tests (SC-HA004-1/2/3) + 6 integration tests (SC-HA015-1 through SC-HA015-6).
- Total passing test count increases by at least 9 from the 558 baseline.

---

## Completion Checklist

Before reporting done:

- [ ] `AllDescriptors.cs` has `dtAreaQueryRequestBatch = 93` and `dtAreaQueryResponseBatch = 94`
- [ ] `AreaQueryMessages.cs` defines `AreaQueryRequestBatch` and `AreaQueryResponseBatch`
- [ ] `AreaQueryTranslators.cs` implements all 4 translator classes with internal test constructors
- [ ] Translators registered in the appropriate pack for Brain and Muscle roles
- [ ] `AreaQueryTranslatorTests.cs` has SC-HA004-1, SC-HA004-2, SC-HA004-3
- [ ] `HillAttackIntegrationTests.cs` has SC-HA015-1, SC-HA015-2, SC-HA015-3, SC-HA015-4, SC-HA015-5, SC-HA015-6
- [ ] Build: 0 errors
- [ ] Tests: all new tests pass, only 6 pre-existing failures remain
- [ ] TASK-TRACKER.md does NOT need to be updated (dev-lead will do that after review)
