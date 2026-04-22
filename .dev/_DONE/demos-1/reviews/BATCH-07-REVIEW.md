# BATCH-07 Review

**Batch:** BATCH-07  
**Reviewer:** Development Lead  
**Date:** 2026-03-26  
**Status:** APPROVED (with documented follow-ups)

---

## Summary

Task 1 (egress + test harness trigger mapping), Task 2 (DEM1-D007 bootstrap docs), and Task 3 (`ParallelStoriesScenario` + registry + tests) are **substantially complete** and **`Fdp.Examples.Scenarios.Tests` passes 51/51**. The report’s analysis of **blocking `AsyncRecorder`** vs dropped delta frames in a tight loop matches the implementation and is technically sound.

---

## Issues Found

### Issue 1: `ParallelStories_NoCarKinimSystemsInReplayKernel` does not inspect the kernel

**File:** `FDP/Examples/Fdp.Examples.Scenarios.Tests/ScenarioTests.cs` (and scenario property)

**Problem:** `DEM1-TASK-DETAIL.md` requires: *“ScenarioSubsystem.Kernel is inspected … No system of type `GroundKinematicsModule` is registered.”* The test only asserts `HasCarKinematicsInMainKernel == false`, a flag **assigned in `Configure()`**, not derived from `ModuleHostKernel` topology. If a future change registered kinematics on the main kernel but forgot to flip the flag, **the test would still pass**.

**Fix:** Next batch — reflect registered modules, add a small test-only/kernel API for module name enumeration, or assert absence of `CarKinematicsSystem` / `GroundKinematicsModule` via an approved introspection hook.

### Issue 2: DEM1-D008 task detail and scenario XML still describe `GroundKinematicsModule` + `RecordingModule`

**Files:** `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D008; `ParallelStoriesScenario.cs` class XML (lines 29–37)

**Problem:** Live phase uses **`LiveKinematicsModule`** + direct **`AsyncRecorder` with `blocking: true`**, which is a justified deviation (Examples layer, deterministic recording). The **normative task markdown** and **public API docs** still claim `GroundKinematicsModule` + `RecordingModule`, so onboarding readers and auditors will be misled.

**Fix:** Update D008 text and XML to describe the actual pattern; note why `RecordingModule` was avoided (non-blocking delta drop in CPU-bound loops).

### Issue 3: `OnShutdown` vs report claim on `.meta.json`

**File:** `ParallelStoriesScenario.OnShutdown`

**Problem:** Report mentions deleting `.fdprec.meta.json`; code only deletes `\_recFilePath` (`.fdprec`). Low severity — optional cleanup alignment or report wording.

### Issue 4: `MissionTriggerHelper` still references obsolete enum

**File:** `Hrot.Map.Common/Helpers/MissionTriggerHelper.cs`

**Problem:** `dotnet build Hrot.Map.Common` still reports **CS0618** on `MissionTrigger.ReachedDestination`. BATCH-07 fixed egress + `SimHostInstance` string map; the **string → ECS enum** helper remains on the legacy path.

**Fix:** BATCH-08 — align with BS1-T022 string mapping policy (see new DEBT row).

### Issue 5: `EntityMissionEgressTranslator` — legacy `EcsMissionTrigger.ReachedDestination` in stored queues

**File:** `Hrot.Map.Common/Replication/Egress/EntityMissionEgressTranslator.cs`

**Note:** The `switch` no longer maps `EcsMissionTrigger.ReachedDestination`; it falls through to `"TimerElapsed"`. If any **in-memory** `MissionPlanQueue` still carries the obsolete enum value (not the `DoctrineFinished` path), DDS egress would be wrong. Acceptable if all live data paths are migrated; if not, add explicit compat arm mapping legacy enum to `"DoctrineFinished"` for wire compatibility.

---

## Test Quality Assessment

- **`ParallelStories_RunToCompletion_ExitsZero`** and **tick-25 distance check** validate **real replay fidelity** (spatial tolerance on live vs replay). Good.
- **`ParallelStories_ReplayMatchesLiveAtTick25`** uses `ReplayMatchedLiveAtTick25` — meaningful latch.
- **`ParallelStories_NoCarKinimSystemsInReplayKernel`** — **weak** (see Issue 1); does not prove naked-node replay by structure, only by author flag.

---

## Verdict

**Status:** APPROVED — merge as-is for DEM1-D008 + BS-1 call-site cleanup + D007 doc note; schedule **test + doc alignment** in BATCH-08.

---

## Commit Message

```
feat(dem1): ParallelStories AAR replay, mission egress DoctrineFinished, D007 docs (BATCH-07)

- Map EcsMissionTrigger.DoctrineFinished to DDS "DoctrineFinished" in EntityMissionEgressTranslator.
- SimHostInstance: legacy "ReachedDestination" string resolves to MissionTrigger.DoctrineFinished.
- DEM1-D008: live phase records 50 ticks via LiveKinematicsModule + blocking AsyncRecorder;
  replay via ReplayModule; position checks at logical frames 25/50.
- DEM1-D007: document IgAltitudeBaselineEstablished spawn/bootstrap in DEM1-TASK-DETAIL.
- Register parallelstories in ScenarioRegistry; Fdp.Examples.Scenarios references FDP.Toolkit.Replay.

Tests: Fdp.Examples.Scenarios.Tests 51/51.
```

---

**Next Batch:** BATCH-08
