# MOD1-BATCH-09 Review

**Batch:** MOD1-BATCH-09  
**Reviewer:** Development Lead  
**Date:** 2026-03-16  
**Status:** ✅ APPROVED

---

## Summary

BATCH-09 is another strong delivery. `FDP.Framework.Runner` is cleanly extracted with zero `Hrot.*` references, `SubsystemOrchestrator` is properly generic, `Hrot.ClusterRunner.Program` is a true composition root, and `SimulationLogicModule` now skips sub-modules correctly by role. The `SubsystemStatusAnnounce` duplication issue (Q4) was handled pragmatically — the solution is correct and keeps wire compatibility.

---

## What Went Well

### DB-MOD1-08 — Role-conditional `SimulationLogicModule`
The implementation is correct and the role/module mapping table is exactly right:

| Module | AllInOne | Brain | MuscleGround | IG/Perception/NavSolver |
|---|---|---|---|---|
| CombatModule | ✅ | ✅ | ✅ | ❌ |
| MissionControlModule | ✅ | ✅ | ❌ | ❌ |
| CognitiveRuntimeModule | ✅ | ✅ | ❌ | ❌ |
| ActionDispatchModule | ✅ | ✅ | ✅ | ❌ |
| GroundKinematicsModule | ✅ | ❌ | ✅ | ❌ |

The boolean flag approach is clean and readable. Note that `TrajectoryPool` and `FormationTemplates` now correctly return `null` for roles without kinematics — callers must be null-aware.

### P9T2 — `SubsystemOrchestrator` Generalization
140 lines of Hrot-coupled code removed. The replacement is clean:
- `TitleBarColor` loop with lightened `TitleBgActive` variant is elegant.
- `DrawMainMenuBar` using `_subsystems.Where(s => s is IMapCameraProvider)` correctly generates the map toggle entries dynamically.
- `SwitchMapOwner` camera-snapping sync is a good UX touch.

### P9T5 — `Program.cs` is a Pure Composition Root
Zero Raylib/ImGui imports in `Program.cs`. Confirmed via developer's grep output. The six remaining imports (`CommandLine`, `MapConfig`, `HrotRunnerConfiguration`, concrete subsystem constructors, `DdsApplication`, `NLog`) are all legitimately composition-root concerns.

### Q4 — `SubsystemStatusAnnounce` Duplication Resolution  
Creating a parallel `FDP.Framework.Runner.SubsystemStatusAnnounce` DDS struct with identical topic name and field layout to preserve wire compatibility is the correct solution here. The alternative — referencing `Hrot.NED` from `FDP.Framework.Runner` — would create a forbidden `FDP → Hrot` dependency. The duplication is well-justified and documented.

---

## Issues Found

### Minor: `IosSubsystem.StartPlacementMode` Fix is Undocumented (DB-MOD1-20)

The report mentions that 5 `Hrot.ExCon.Tests` initially failed due to a `StartPlacementMode` signature mismatch and were fixed by serializing `EntityPropertyPatch` to JSON. This is not a debt item per se, but it represents an undocumented API change: callers that were passing `EntityPropertyPatch` directly now need to serialize to JSON first. This is a legitimate concern since:
- Any call site outside the test suite that uses the old overload will silently break.
- The `IIosLogic` interface contract changed, but there is no mention of what else calls `StartPlacementMode(long, EntityPropertyPatch)`.

A quick grep audit of all `StartPlacementMode` call sites should be done to confirm no other callers are affected.

### Minor: `TestStep.cs` and `TestMetricsCollector.cs` Not Mentioned in Design (DB-MOD1-21)

The file listing shows `FDP.Framework.Runner.Testing.TestMetricsCollector.cs`. This class was not specified in the MOD1-P9T4 task description or the design doc. Its presence is fine if it's purely generic — but it needs to be verified that it has no `Hrot.*` references.

---

## Verdict

**Status:** ✅ APPROVED

Both minor findings are low risk and logged as debt. Phase 9 is complete. The application is now correctly layered with `FDP.Framework.Runner` as a Hrot-free orchestration toolkit.

---

**Next Batch:** MOD1-BATCH-10
