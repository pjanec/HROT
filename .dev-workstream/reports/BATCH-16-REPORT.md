# BATCH-16 REPORT: Corrective-0 + BCS-P7-T7 + BCS-P7-T8 + BCS-P7-T9 (Phase 7 Completion)

**Batch:** BATCH-16  
**Status:** ✅ COMPLETE  
**Final test result:** 26/26 pass (UrbanCombat test project) · 0 build errors  
**Date:** Session completion

---

## Summary

All Phase 7 Urban Ambush deliverables are complete and green:

| Task | File | Status |
|---|---|---|
| Corrective-0a — APC BrainTier | `DemoTkbSetup.cs` | ✅ |
| Corrective-0b — Magic number sweep | `DemoTkbSetup.cs` + `UrbanCombatConstants.cs` | ✅ |
| BCS-P7-T7 — ScenarioDirector | `ScenarioDirector.cs` | ✅ |
| BCS-P7-T8 — TelemetryReporterSystem | `TelemetryReporterSystem.cs` | ✅ |
| BCS-P7-T9 — Integration test | `UrbanAmbushIntegrationTests.cs` | ✅ |
| HeadlessDemoApp wiring | `HeadlessDemoApp.cs` | ✅ |
| ExportSystemGroup | `StandardSystemGroups.cs` | ✅ |

---

## Q1: System group types in HeadlessDemoApp.Initialize()

`HeadlessDemoApp` uses four group instances created via `group.Create(World)`:

```
_inputGroup   = new InputSystemGroup()
_simGroup     = new SimulationSystemGroup()
_postSimGroup = new PostSimulationSystemGroup()
_exportGroup  = new ExportSystemGroup()
```

These are exactly the same concrete types referenced in `[UpdateInGroup(...)]` attributes on each system. The topological sort in `SystemGroup.SortSystems()` resolves system dependencies using `[UpdateBefore]` / `[UpdateAfter]` attributes on each system class. Registration order (`AddSystem`) determines tie-breaking in the Kahn's-algorithm FIFO queue when two systems share the same "tier" (same dependencies, no direct ordering constraint between them).

---

## Q2: BTree interpreter registration

Each BTree doctrine was built with:

1. **`TreeCompiler.CompileFromJson(json)`** → produces an `FbtBlob` (the compiled tree bytecode).  
2. **`ActionRegistry<BrainBlackboard, BTreeContext>`** with named delegates registered for each node label.  
3. **`new Interpreter<BrainBlackboard, BTreeContext>(blob, registry)`** stored as `DoctrineDefinition.BTreeInterpreter`.

`DoctrineRegistry.Register(int id, string name, DoctrineDefinition def)` accepts the pre-built `Interpreter<BrainBlackboard, BTreeContext>` inside the `DoctrineDefinition`; no separate `FbtBlob` property exists on `DoctrineDefinition`. The doctrines registered were:

| ID | Name | BrainTier | Notes |
|---|---|---|---|
| 1001 | WanderCivil | 0 | Handled by TrafficBrainSystem |
| 1002 | PanicFlee | 0 | Handled by TrafficBrainSystem |
| 2001 | ConvoyEscort | BrainTierHsm (1) | HSM from ApcHsmSetup.Build() |
| 2002 | InfantryCombat | BrainTierBTree (2) | Minimal hold-position BTree |
| 2003 | Ambush | BrainTierBTree (2) | Selector → Sequence[HasTarget, AimAndFire] / HoldPosition |

---

## Q3: T9 milestones — which appeared and root causes of initial failures

All 7 milestones appear in the final passing 600-frame run. Three defects were identified and fixed during this batch:

### Defect 1 — GUNFIRE never appeared (WeaponDispatcher ordering race)

**Root cause:** `WeaponDispatcherSystem` and `BTreeTickSystem` both carry only `[UpdateAfter(ChannelArbitrationSystem)]`. With no direct ordering constraint between them, the Kahn topological sort used `HashSet<ComponentSystem>` iteration order — which is non-deterministic by hash code. In the test-environment memory layout, `WeaponDispatcherSystem` consistently executed *before* `BTreeTickSystem`. `WeaponDispatcher` saw an empty channel (BTree had not yet written), took no action, then `BTree` wrote `ActiveAction = 1` — but `WeaponDispatcher` had already run for that frame. `Execute()` was never called → `FireRequestEvent` never published.

**Fix:** Added `[UpdateAfter(typeof(BTreeTickSystem))]` to `WeaponDispatcherSystem` in  
`FDP/Toolkits/FDP.Toolkit.Behavior/Systems/WeaponDispatcherSystem.cs`.  
This forced the canonical ordering: ChannelArb → BTree → WeaponDispatcher, guaranteed every frame.

### Defect 2 — HIT (and all downstream milestones) never appeared (SpatialHashGrid negative-coordinate blind spot)

**Root cause:** `SpatialHashGrid.Add()` computed `cellY = (int)(position.Y / CellSize)` and silently dropped entities when `cellY < 0`. The APC spawns at `(0, -80, 0)` on the South road arm. Because the grid origin was hard-coded to `(0, 0)`, the APC was *never inserted* into the spatial hash. Consequently `RaycastSolverSystem.QueryNeighbors()` found zero candidates near the bullet's path → `HasHit == 0` on every hit batch → `HitResolutionSystem` never published `HitEvent` → no HIT, CAPABILITY LOST, HSM TRANSITION, INTERACTION, or FLEE.

**Fix:**  
- Added `public float OriginX, OriginY` fields to `SpatialHashGrid` (new optional params with default 0 on `Create()` for full backward compatibility).  
- Updated `Add()` and `QueryNeighbors()` to subtract the origin before dividing by `CellSize`.  
- Updated `SpatialHashSystem.OnCreate()` to use `originX: -375f, originY: -375f`, giving 750 × 750 m coverage centred on (0, 0) — the entire scenario (roads ±100 m, APC at y = -80) is comfortably within bounds.

### Defect 3 — FLEE never appeared (TrafficBrainSystem / ChannelArb stamp mismatch)

**Root cause:** `TrafficBrainSystem` (with `[UpdateBefore(ChannelArbitrationSystem)]`) wrote `LocomotionChannel.ActiveAction = ActionIdFlee` for civilian[0] whose `TargetMemory.Count = 1`. However it left `channel.DoctrineInstanceId = 0` (default). `ChannelArbitrationSystem` then evaluated `channel.ActiveAction != 0 && channel.DoctrineInstanceId (0) != doctrine.InstanceId (1)` → cleared the channel to `default` in the same frame. `TelemetryReporterSystem` (Export group, same frame, after Sim group) saw `ActiveAction = 0` → no FLEE.

**Fix:** `TrafficBrainSystem.OnUpdate()` now additionally stamps `channel.DoctrineInstanceId = doctrine.InstanceId` when `HasComponent<DoctrineState>(entity)` is true. This is checked with `HasComponent` (not a query filter) so existing unit tests that create test entities without `DoctrineState` continue to pass.

---

## Q4: TelemetryReporterSystem HSM transition detection

Shadow **dictionary** — `_prevHsmState : Dictionary<int, ushort>` keyed by `entity.Index`. On each `OnUpdate()` call, the system queries all entities `.With<BrainHsm128>()`, reads `brain.State.ActiveLeafIds[0]` (via `unsafe` context), and compares against the previous-frame value stored in the dictionary. A change triggers the `HSM TRANSITION` log line. No ECS component is added; the dictionary lives entirely inside the system class. The same pattern is used for `_prevDoctrineInstanceId` (doctrine change detection) and `_prevCapabilities` (capability loss detection).

---

## Q5: Surprises

1. **HashSet iteration order in topological sort** — `SystemGroup.SortSystems()` uses `HashSet<ComponentSystem>` for graph edges and iterates it when a node's successors become eligible. Because `HashSet<T>` iterates by hash code (not insertion order), two systems with identical dependency sets can sort in either order non-deterministically. The symptom (GUNFIRE never appearing) was consistent within a single test run but would vary across machines. Fix is an explicit `[UpdateAfter]` attribute.

2. **Spatial hash grid is positive-only** — `SpatialHashGrid.Add()` silently drops entities with negative coordinates. The Urban Ambush road network centres on `(0, 0)` with the south arm at `y = -100` and the APC starting at `y = -80`. No diagnostic is raised; entities simply vanish from the broadphase. The fix (world-origin offset) is backward-compatible: all existing callers that pass no origin continue to behave identically.

3. **`RoadNetworkBlob` is a struct, not a class** — Cannot use `?? throw` or null-conditional operators; requires `if (_initialized) Road.Dispose()` in `Dispose()`. Nullable struct (`RoadNetworkBlob?`) is syntactically valid but semantically odd for a large value type; we use a plain field with an `_initialized` guard instead.

4. **`SpatialHashSystem` / `CarKinematicsSystem` belong in `SimulationSystemGroup`** — The BATCH-16 instructions suggested `PostSimulationSystemGroup` for these, but both carry `[UpdateInGroup(typeof(SimulationSystemGroup))]` (verified from source). Moving them would require removing their `[UpdateInGroup]` attributes and adding `[UpdateInGroup(typeof(PostSimulationSystemGroup))]` — a toolkit change deferred as a DEBT item. They work correctly in `SimGroup` for the current scenario.

5. **`ChannelArbitrationSystem` never sets `channel.DoctrineInstanceId`** — The system only clears channels; it does not update `DoctrineInstanceId` after the guard check. This means any system that writes to a channel but does not also set `DoctrineInstanceId` will have its work undone every frame. The intended contract is: the BTree/HSM that owns the channel is responsible for stamping `DoctrineInstanceId` (the BTree does this implicitly through `ActionInstanceId` signalling; TrafficBrainSystem needed an explicit stamp added in this batch).

---

## Files Modified / Created

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/WeaponDispatcherSystem.cs` | Added `[UpdateAfter(typeof(BTreeTickSystem))]` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Spatial/SpatialHashGrid.cs` | Added `OriginX`/`OriginY` fields; updated `Create()`, `Add()`, `QueryNeighbors()` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs` | `OnCreate()` uses `originX: -375f, originY: -375f` |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TrafficBrainSystem.cs` | Stamps `channel.DoctrineInstanceId` when entity has `DoctrineState` |
| `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` | Full rewrite: RegisterComponents, RegisterDoctrines, RegisterSystems, RunSimulation loop, DoctrineRegistry property |
| `FDP/Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs` | Created; 4-param constructor, SetupAmbushScenario, SpawnEntity, EmbarkSoldiers |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs` | Created; 7 milestones, shadow dictionaries |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/ApcBrainOutputSystem.cs` | Added `unsafe` to `OnUpdate()` |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs` | Created; Condition_HasTarget, Action_AimAndFire, Action_HoldPosition |
| `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/UrbanAmbushIntegrationTests.cs` | Created; 9 tests (T7 × 4, T8 × 3, T9 × 2) |
| `FDP/Kernel/Fdp.Kernel/StandardSystemGroups.cs` | Added `ExportSystemGroup` |
| Various toolkit + example files | Corrective-0 BrainTier fix, magic number sweep, UrbanCombatConstants |
