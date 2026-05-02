# BATCH-06 Review

**Batch:** BATCH-06  
**Reviewer:** Development Lead  
**Date:** 2026-03-26  
**Status:** APPROVED (with mandatory corrections applied — see below)

---

## Summary

Core deliverables match the **intent** of DEM1-D006/D007 and the two BATCH-05 debt items: manual mission pipeline + channel arbitration, terrain stack + `MockTerrainProvider`, scoped perception bus, and dirty-gated grid rebuilds are in place.  

**As originally committed**, `dotnet test Fdp.Examples.Scenarios.Tests` **did not pass**: wrong test constant binding, geographic jump-rejection broken at sea level, Z-reference feedback in the terrain demo, `ScenarioRegistry` missing new scenarios, and unsafe `ScenarioSubsystem` shutdown ordering relative to `OnShutdown()`.

---

## Issues Found (developer submission)

### Issue 1: MissionCommand test used wrong `DemoBehaviorIds`

**File:** `FDP/Examples/Fdp.Examples.Scenarios.Tests/ScenarioTests.cs`  
**Problem:** Namespace `Fdp.Examples.Scenarios.Tests` is nested under `Fdp.Examples.Scenarios` for name lookup. Unqualified `DemoBehaviorIds.Combat` bound to `Fdp.Examples.Scenarios.DemoBehaviorIds` (2900), not `Fdp.Examples.Common.Constants.DemoBehaviorIds` (200).  
**Fix:** Qualify `Fdp.Examples.Common.Constants.DemoBehaviorIds.Combat` (applied).

### Issue 2: `TerrainQueryResolutionSystem` jump-rejection never engaged at Z=0

**File:** `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/TerrainQueryResolutionSystem.cs`  
**Problem:** `LastValidIgAltitude == 0` was treated as perpetual “bootstrap”, so **valid** sea-level terrain kept bypassing jump rejection; the DEM1 spike (Z=100) was accepted and Phase 3 never held.  
**Fix:** Added `GroundClampingState.IgAltitudeBaselineEstablished` and gate bootstrap on that flag; updated geographic tests (applied).

### Issue 3: TerrainClamping Z reference feedback

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Perception/TerrainClampingScenario.cs`  
**Problem:** `TransformSyncSystem` writes `SimTransform.Position.Z` from `NetworkTransform` + offset while `TerrainQuerySubmitSystem` uses `tf.Position.Z` as `ReferenceSimZ`, creating a feedback loop and bogus `TargetZOffset` / Phase 1 failures.  
**Fix:** After the terrain pipeline and ECB flushes, reset authoritative `SimTransform.Position.Z` to 0 and re-sync `NetworkTransform` for this 2.5D demo path (applied).

### Issue 4: `ScenarioSubsystem.Shutdown` vs `IScenario.OnShutdown`

**File:** `FDP/Examples/Fdp.Examples.Common/ScenarioSubsystem.cs`  
**Problem:** `OnShutdown()` ran **after** `EntityRepository.Dispose()`, so scenarios could not read singletons (e.g. `TerrainQueryBatchData`) to dispose `NativeArray`s safely.  
**Fix:** Call `_scenario.OnShutdown()` before `_world.Dispose()` (applied).

### Issue 5: `ScenarioRegistry` incomplete

**File:** `FDP/Examples/Fdp.Examples.Runner/ScenarioRegistry.cs`  
**Problem:** `MissionCommand`, `TerrainClamping`, and **pre-existing** `BehaviorValidation` were absent despite `ScenarioNames` / DEM1-F003.  
**Fix:** Register all three (applied).

### Issue 6: DEM1-D006 literal “register MissionControlModule + CognitiveRuntimeModule”

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Cognitive/MissionCommandScenario.cs`  
**Problem:** Task text asks for modules; implementation drives `BehaviorIngressSystem`, `MissionDirectorSystem`, `ChannelArbitrationSystem` manually (same pattern as `SensorGridScenario`). Acceptable for deterministic tick proofs **if documented** — XMl doc already explains; no code change required.

### Issue 7: LocalGridBuilder “incremental” debt

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LocalGridBuilderSystem.cs`  
**Problem:** Dirty detection avoids work when static, but **dirty path** still clears and rebuilds the whole grid (`O(n)`). Debt text asked for incremental updates at 100+ entities — only **partially** satisfied.  
**Fix:** Leave as P3 follow-up in `DEBT-TRACKER.md`.

### Issue 8: `AutonomousPerceptionModule` scoped view

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Modules/AutonomousPerceptionModule.cs`  
**Problem:** `PerceptionScopedView.ConsumeEvents<T>()` always reads the scoped bus. Safe **today** (only LOS pipeline types) but fragile if a system consumes another unmanaged event type.  
**Fix:** Track as P3 architecture debt — forward to BATCH-07 if no quick hardening.

---

## Test Quality Assessment

- Mission / terrain xUnit tests assert **exit codes**, **phase observables**, and **meaningful thresholds** (behavior hash, `ActiveAction`, offsets, `LastValidIgAltitude`). Not string-shallow.  
- Phase 4 terrain test mostly duplicates full run but adds an extra `TargetZOffset` bound — acceptable.  
- **Regression:** After fixes, `Fdp.Examples.Scenarios.Tests`: **48/48 passed**; `Fdp.Toolkit.Geographic.Tests` passes.

---

## Verdict

**Status:** APPROVED — with the corrections above merged so CI is green and CLI/registry/shutdown behavior match DEM1 expectations.

---

## Commit Message

```
feat(dem1): Phase 4 scenarios, perception bus isolation, clamping fixes (BATCH-06)

Completes DEM1-D006 (MissionCommandScenario), DEM1-D007 (TerrainClampingScenario).
Closes DEBT: LocalGridBuilder dirty fast path; AutonomousPerception scoped bus.

- Cognitive: manual BehaviorIngress / MissionDirector / ChannelArbitration pipeline
  with same-tick behavior apply via double SwapBuffers.
- Perception: TerrainQuery* + TransformSync + MockTerrainProvider; Z=0 authority
  reset after sync to avoid ReferenceSimZ feedback.
- Geographic: IgAltitudeBaselineEstablished for jump-rejection (sea-level safe).
- Runner: register behaviorvalidation, missioncommand, terrainclamping.
- Common: ScenarioSubsystem calls OnShutdown before world dispose.
- Tests: fix DemoBehaviorIds qualification in nested test namespace.

Tests: Fdp.Examples.Scenarios.Tests 48/48; Fdp.Toolkit.Geographic.Tests.
```

---

**Next Batch:** BATCH-07
