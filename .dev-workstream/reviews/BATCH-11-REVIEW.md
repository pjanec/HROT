# BATCH-11 Review

**Batch:** BATCH-11  
**Reviewer:** Development Lead  
**Date:** 2026-03-27  
**Status:** APPROVED (Task 4 deferred as planned)

---

## Summary

Checked **`.dev-workstream/reports/BATCH-11-REPORT.md`** against the repository. **Tasks 1–3 match the implementation.** **Task 4** (ParallelStories → `RecordingModule.Blocking`) remains deferred; **`ParallelStoriesScenario`** still uses **`AsyncRecorder`** directly — consistent with the report.

**Tests run locally:** `Fdp.Examples.Scenarios.Tests` **56/56**, `FDP.Toolkit.Perception.Tests` **34/34**.

---

## Task-by-task verification

### Task 1 — Stale spatial-hash slot after index reuse

**Found:** **`LocalGridBuilderSystem`** adds **`Dictionary<int, Entity> _liveByIndex`**. On the incremental branch, when a live **`Entity`** is new to **`_prevPositions`** but **`_liveByIndex`** still holds a **different generation** at the same index, the code **`Remove`**s the stale handle from the grid using **`_prevPositions[staleEntity]`**, drops stale keys, then **`Add`**s the new entity and updates **`_liveByIndex`**. **`FullRebuild`** clears all three structures.

**Test:** **`LocalGridBuilder_IndexReuse_DeadEntity_NotReturnedByQueryNeighbors`** asserts the recycled **`e1`** handle never appears in **`QueryNeighbors`** and **`e2`** is present — directly targets the BATCH-10 residual bug.

**Nit:** The class-level XML block still summarizes BATCH-09/10 behaviour; it does not yet describe **`_liveByIndex`** / stale eviction (commentary only).

### Task 2 — DEM1-D009 Phase B (DDS + replication slice)

**Found:** **`DistributedTankScenario`** registers **`NetworkIdentity`** on the Brain hull, creates **`DdsWriter<EntityMasterTopic>`** / **`DdsReader<EntityMasterTopic>`** on the existing Domain 0 participants, publishes at tick **6**, polls from tick **7** with **`Take()`**, calls **`ReplicationLogicModule.GhostCreationSystem.CreateGhost`** on the Muscle world, and sets **`GhostVisibleOnMuscle`** from **`NetworkEntityMap.TryGetEntity`** + **`IsAlive`**. **`ReplicationLogicModule`** is registered on the Muscle kernel; Muscle world registers replication-related components and ELM events as described.

**`ReleaseResources`** disposes **writer/reader before participants** — correct for Cyclone teardown order.

**Instruction fit:** The batch asked for **one vertical milestone** (DDS path **or** ghost). This delivers **both** in one slice. **`DemoTkbSetup.RegisterAll`** was not used; the report’s justification (empty **`TkbDatabase`**, ghost milestone without blueprint promotion) is **acceptable** for this increment.

**Design gap vs `DEM1-TASK-DETAIL`:** Full demo (toolkits on both nodes, **`DemoTkbSetup`**, locomotion + weapon ticks) is **still out of scope** — **DEM1-D009** must remain **unchecked** on the tracker.

**Note:** **`_muscleKernel.Update(FixedDelta)`** still uses the **`[Obsolete]`** float overload (report acknowledges). Prefer parameterless **`Update()`** with **`SteppingTimeController`** driving delta — track as cleanup (see debt).

### Task 3 — `IScenario.OnShutdown` documentation

**Found:** **`IScenario.OnShutdown`** XML now states **kernel disposed → `OnShutdown()` → world disposed**, matching **`ScenarioSubsystem.Shutdown`**.

### Task 4 — Optional ParallelStories migration

**Deferred** — no regression; **`RecordingConfiguration.Blocking`** remains available for a later batch.

---

## Test quality

- **Grid:** The new test explicitly forbids **dead** handles in neighbour results — stronger than “insert only”.
- **DistributedTank:** **`GhostVisibleOnMuscle`** is observable scenario state; exit code **0** still implies Phase A/ELM/ghost gates passed at tick **10**.

---

## Suggested commit message

```
BATCH-11: LocalGridBuilder stale-slot eviction; DistributedTank EntityMaster + ghost; IScenario OnShutdown docs

- LocalGridBuilderSystem: _liveByIndex eviction on index recycle; test dead entity absent from QueryNeighbors
- DistributedTankScenario: EntityMasterTopic writer/reader; ReplicationLogicModule ghost creation; DDS dispose order
- IScenario: document actual Shutdown order (kernel → OnShutdown → world)
- Scenarios.Tests: DistributedTank_PhaseB_MuscleHasGhostForBrainHull
```

---

## Follow-ups (BATCH-12)

1. **DEM1-D009:** Locomotion round-trip, turret/split authority, **`DemoTkbSetup.RegisterAll`** per task detail; translators vs inline **`CreateGhost`** as appropriate.  
2. **Hygiene:** Replace **`_muscleKernel.Update(FixedDelta)`** with non-obsolete **`Update()`** (and any similar call sites).  
3. **Optional:** **`ParallelStoriesScenario`** → **`RecordingModule`** + **`Blocking: true`**.  
4. **Optional:** Extend **`LocalGridBuilderSystem`** XML for **`_liveByIndex`**.
