# BATCH-11 Report

**Batch:** BATCH-11  
**Developer:** GitHub Copilot  
**Date:** 2026-03-27  
**Status:** Complete (Tasks 1–3 fully delivered; Task 4 deferred per instructions)

---

## Task Completion

| Task | Status | Notes |
|------|--------|-------|
| Task 1 — `SpatialHashGrid` / `LocalGridBuilderSystem` stale-slot fix | ✅ Complete | `_liveByIndex` dictionary evicts dead-entity slots in incremental path |
| Task 2 — DEM1-D009 Phase B (DDS + `ReplicationLogicModule` slice) | ✅ Complete | EntityMasterTopic loopback + ghost visible on Muscle by tick 10 |
| Task 3 — `IScenario.OnShutdown` XML vs `ScenarioSubsystem.Shutdown` order | ✅ Complete | Comment aligned with actual `_kernel.Dispose() → OnShutdown() → _world.Dispose()` |
| Task 4 — `ParallelStoriesScenario` + `RecordingModule.Blocking` | — | Deferred — Tasks 1–3 used the time budget |

---

## Testing Results

| Project | Before | After | Notes |
|---------|--------|-------|-------|
| `Fdp.Examples.Scenarios.Tests` | 55/55 | **56/56** | +1 `DistributedTank_PhaseB_MuscleHasGhostForBrainHull` |
| `FDP.Toolkit.Perception.Tests` | 33/33 | **34/34** | +1 `LocalGridBuilder_IndexReuse_DeadEntity_NotReturnedByQueryNeighbors` |
| Solution build | Clean | **Clean** | Zero new errors or warnings |

---

## Implementation Details

### Task 1 — `SpatialHashGrid` / `LocalGridBuilderSystem` stale-slot fix

**Problem:** On the incremental path (entity count unchanged): when entity `e1` is destroyed and entity `e2` is created with the recycled index at a stable count, BATCH-10 fixed the *insert* path (new entity no longer silently skipped when `oldPos == newPos`). However, `e1`'s slot was still in the `SpatialHashGrid` until the next count-change full rebuild. A `QueryNeighbors` call could return both `e2` **and** the dead `e1`.

**Solution:** Added `private readonly Dictionary<int, Entity> _liveByIndex` to `LocalGridBuilderSystem`.

- **`FullRebuild`**: clears `_liveByIndex`, then sets `_liveByIndex[entity.Index] = entity` for every live entity.
- **Incremental path `else` branch** (new entity, miss in `_prevPositions`): checks `_liveByIndex.TryGetValue(entity.Index, out staleEntity)`. If `staleEntity != entity` (different generation), the stale entity's grid slot is removed via `_grid.Remove(staleEntity, _prevPositions[staleEntity])` and cleared from `_prevPositions`. Then `_liveByIndex[entity.Index] = entity` is updated.
- **Moved-entity path**: no change needed — `_liveByIndex` already maps the correct entity from the last full rebuild.

**Complexity unchanged** — the `else` branch is O(1) dictionary lookups.  
**Memory cost** — one `int → Entity` entry per live entity (≈ 12 bytes per entity overhead).

**Test added:**
- `LocalGridBuilder_IndexReuse_DeadEntity_NotReturnedByQueryNeighbors` — destroys e1, creates e2 at same index (confirmed via `e1.Index == e2.Index` assertion), runs incremental tick, asserts no result from `QueryNeighbors` has `entity == e1` AND that `e2` is still present.

**Files changed:**
- `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LocalGridBuilderSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Perception.Tests/LocalGridBuilderSystemTests.cs`

---

### Task 2 — DEM1-D009 Phase B DDS + `ReplicationLogicModule` slice

**Goal:** Wire at least one Cyclone DDS path between Brain and Muscle participants; register `ReplicationLogicModule` on Muscle; assert ghost entity visible on Muscle.

**Milestone delivered:** "EntityMaster Ghost Visible on Muscle" — Brain publishes `EntityMasterTopic` at tick 6 via `DdsWriter<EntityMasterTopic>`; Muscle polls via `DdsReader<EntityMasterTopic>` at tick 7+; `GhostCreationSystem.CreateGhost` is called, registering the ghost entity in `NetworkEntityMap`. At tick 10, `GhostVisibleOnMuscle` is asserted before returning true.

**Implementation:**

*Brain side (Configure):*
- Added `world.RegisterComponent<NetworkIdentity>()` on Brain world.
- Added `NetworkIdentity(BrainHullNetId)` component to `_brainHull`.
- Creates `DdsWriter<EntityMasterTopic>(_brainParticipant)`.

*Muscle side (Configure, before `_muscleKernel.Initialize()`):*
- Registers components required by `ReplicationLogicModule` systems: `NetworkIdentity`, `GhostStateTracker`, `TkbIdentity`, `NetworkOwnership`, `PartMetadata`.
- Registers lifecycle events: `ConstructionOrder`, `ConstructionAck`, `DestructionOrder`, `DestructionAck`.
- Creates `NetworkEntityMap _muscleEntityMap`.
- Creates `EntityLifecycleModule` (zero-participant, empty TKB) for use inside `ReplicationLogicModule`.
- Creates `ReplicationLogicModule(_muscleEntityMap, new TkbDatabase(), muscleReplicationElm)`.
- Calls `_muscleKernel.RegisterModule(_muscleReplicationModule)` **before** `Initialize()`.
- Creates `DdsReader<EntityMasterTopic>(_muscleParticipant)`.

*EvaluateTick:*
- Tick 6: `_masterWriter.Write(new EntityMasterTopic { EntityId = BrainHullNetId, OwnerId = BrainAppId, TkbTypeValue = CommandTankTkbType })`. Flag `_masterPublished` prevents re-publish.
- Tick 7+: drains `_masterReader.Take()` until a valid sample is found; calls `_muscleReplicationModule.GhostCreationSystem.CreateGhost(_muscleWorld, sample.Data.EntityId, currentTick)`; sets `GhostVisibleOnMuscle` by checking `_muscleEntityMap.TryGetEntity(BrainHullNetId, ...) && _muscleWorld.IsAlive(ghostEntity)`.
- Tick 10: added `GhostVisibleOnMuscle` check alongside existing Phase A and Phase B Phase 1 checks.

*ReleaseResources:*
- `_masterWriter?.Dispose()` and `_masterReader?.Dispose()` called **before** participant disposal (CycloneDDS requirement).

**Design decisions:**
- **Inline DDS instead of full translator**: `EntityMasterIngressTranslator` requires `NodeIdMapper` and loopback prevention. For the scenario harness, calling `GhostCreationSystem.CreateGhost` directly after reading the DDS sample is simpler and still demonstrates the full Cyclone loopback path. This follows the batch instruction to pick "the smallest change that preserves determinism".
- **Empty TKB for Muscle replication**: `GhostPromotionSystem` queries for entities with `TkbIdentity + GhostStateTracker`. Since the ghost entity only receives `NetworkIdentity + GhostStateTracker` (no `TkbIdentity`), promotion is not triggered. Ghost stays at `EntityLifecycle.Ghost` — correct for this milestone. CommandTank template registration deferred to BATCH-12.
- **`DemoTkbSetup.RegisterAll` justified stub**: The batch says "use `DemoTkbSetup.RegisterAll` OR document why a slimmer stub is justified". For the ghost-visible milestone, no blueprint template is applied. Using an empty TKB is cleaner and avoids pulling in the full `DemoTkbSetup` configuration for a test that only asserts ghost registration, not blueprint promotion.

**New constants added:**
- `BrainHullNetId = 100L` — network ID for Brain hull
- `CommandTankTkbType = 100L` — TKB type for CommandTank
- `PhaseB2PublishTick = 6` — tick at which Brain publishes EntityMaster
- `PhaseB2GhostPollTick = 7` — tick at which Muscle starts polling
- `BrainAppId` (static readonly) — `NetworkAppId { AppDomainId=1, AppInstanceId=100 }`

**Test added:**
- `DistributedTank_PhaseB_MuscleHasGhostForBrainHull` — asserts `scenario.GhostVisibleOnMuscle == true` after a 60-tick run, proving Cyclone DDS loopback path.

**Files changed:**
- `FDP/Examples/Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs`
- `FDP/Examples/Fdp.Examples.Scenarios.Tests/ScenarioTests.cs`

**Remaining Phase B work (deferred to BATCH-12):**
1. **Loco command round-trip** (tick 20–25): inject `LocomotionChannel.ActiveAction = ActionIdMoveTo` on Brain hull; assert Muscle ghost `SimVelocity.Linear.X > 0.1` by tick 25. Requires CarKinemToolkit on Muscle + transform replication translator.
2. **Turret split-authority** (tick 30–50): spawn TankTurret (TKB 101) as ghost child on Muscle; inject `WeaponChannel.ActiveAction = ActionIdAimAndFire` on Brain Turret; assert by tick 50.
3. **`DemoTkbSetup.RegisterAll`** — wire the full TKB blueprint (TKB 100 + TKB 101) with `DemoTkbSetup` to enable `GhostPromotionSystem` blueprint application.
4. **DEM1-TASK-TRACKER D009** remains **unchecked** — full Phase B success conditions not yet met.

---

### Task 3 — `IScenario.OnShutdown` XML vs `ScenarioSubsystem.Shutdown` order

**Problem:** `IScenario.OnShutdown` XML said "called after the kernel **and world** have been disposed", implying world is already gone. In reality `ScenarioSubsystem.Shutdown()` does `_kernel.Dispose()` → `OnShutdown()` → `_world.Dispose()`. This is a meaningful difference: `OnShutdown()` implementations (e.g. `DistributedTankScenario`) may still read world singletons.

**Fix:** Updated XML comment to precisely state the actual order and note that the world singleton is still intact when `OnShutdown` runs.

**Files changed:**
- `FDP/Examples/Fdp.Examples.Common/IScenario.cs`

---

## Design Decisions

**Inline DDS vs. full translator (Task 2):** The full `EntityMasterIngressTranslator` path requires `NodeIdMapper`, loopback prevention (`OwnerId != localId` guard), and `cmd.Playback` for deferred component additions. For the scenario harness where we drive both nodes synchronously in `EvaluateTick`, calling `GhostCreationSystem.CreateGhost` directly after a DDS read is equivalent and simpler. The actual DDS wire path (Cyclone loopback, `DdsWriter`, `DdsReader`) is identical.

**`_liveByIndex` key choice (Task 1):** Keying by `entity.Index` (int) rather than `entity` (Entity = Index + Generation) is intentional — we need to look up "which entity was previously live at this index slot", which requires looking up by index only. The value (`Entity`) carries the full generation, allowing the stale-entity check.

---

## Debt Tracker Notes

| Row | New Status |
|-----|-----------|
| `SpatialHashGrid` stale slot after index reuse (BATCH-10 review, Target BATCH-11+) | ✅ Resolved (`_liveByIndex` eviction in incremental path) |
| `IScenario.OnShutdown` XML misleading comment (BATCH-10 review follow-up) | ✅ Resolved (comment aligned with actual teardown order) |

---

## Known Issues / Open Items

1. **Phase B BATCH-12 scope:** Loco command round-trip (tick 25), turret split-authority (tick 40/50), and full `DemoTkbSetup` blueprint wiring are deferred. See "Remaining Phase B work" above.
2. **Task 4 deferred:** `ParallelStoriesScenario` → `RecordingModule.Blocking` not started. All baseline tests passing.
3. **Pre-existing `CS0618` on `_muscleKernel.Update(FixedDelta)`** — present since BATCH-10; uses the obsolete float overload. Can be fixed in a future batch by switching to `_muscleKernel.Update()` (with `SteppingTimeController` already set). Not introduced by this batch.
