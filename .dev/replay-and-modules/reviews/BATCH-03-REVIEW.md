# BATCH-03 Review — T-RMF-13..19

**Status: APPROVED**
**Build:** 0 errors  
**Tests:** 458 passed, 0 failed, 3 skipped (Hrot.SimHost.Tests); 219/219 (ClusterRunner.Tests); 40/41 (Integration.Tests — 1 pre-existing failure unrelated to BATCH-03)

---

## Summary

T-RMF-13..19 (Phase 3 composition roots) are complete. All composition roots have been migrated from `RegisterSystems(SystemGroup...)` overloads to exposing `IReadOnlyList<IEcsModuleSystem>` arrays and wiring `TogglableXxxGroup` instances through the kernel.

---

## Task-by-task

### T-RMF-13: SimHostCoreLogicPack
- DONE. Exposes `InputSystems`, `SimulationSystems`, `PostSimulationSystems`.
- `RegisterSystems(SystemGroup, SystemGroup, SystemGroup)` overload deleted.
- Private fields for bridge systems (`_navIntentBridge`, `_routeTrajSync`, `_personalRouteAuthoring`) held on the pack.

### T-RMF-14: CgfLogicPack
- DONE. Exposes `InputSystems`, `SimulationSystems`.
- Both `RegisterSystems` overloads deleted.
- 2 null-guard tests (`TwoGroupOverload_NullInputGroup_Throws`, `TwoGroupOverload_NullSimGroup_Throws`) correctly removed — they tested the deleted API.

### T-RMF-15: SimHostApp
- DONE. `_kernelGroup` replaced by `_toggleInput`, `_toggleSim`, `_togglePostSim`.
- Key fix: `TogglableSimulationGroup` cannot use `RegisterGlobalSystem` — Simulation phase is reserved for `IEcsModule.Tick()`. Added private nested `SimHostSimulationModule : IEcsModule` wrapping `_toggleSim`.
- `_kernel.RegisterGlobalSystem(_toggleInput)`, `_kernel.RegisterModule(new SimHostSimulationModule(_toggleSim))`, `_kernel.RegisterGlobalSystem(_togglePostSim)`.
- `_kernelGroup?.Run()` removed from `OnUpdate`.

### T-RMF-16: CgfSubsystem
- DONE. Legacy `SystemGroup? _simGroup`, `SystemGroup? _inputGroup` fields removed.
- Added `CgfSimulationModule : IEcsModule` nested class wrapping `_toggleSim`.
- `ReferenceReplayLoadHandler` now receives `inputGroup: _toggleInput, simGroup: _toggleSim`.

### T-RMF-17: CgfApplication
- DONE. Optional `CgfLogicPack? logicPack = null` constructor parameter wired through.

### T-RMF-18: EditorSubsystem + EditorHarness
- DONE. Adapter nested classes deleted. Systems registered individually via `RegisterGlobalSystem` in foreach loops over pack arrays.
- Companion changes: `MovingEntitySystem` converted from `ComponentSystem` to `IEcsModuleSystem` (necessary for EditorHarness test use); `SimHostSubsystem.TestHook_AddSystem` signature updated to `IEcsModuleSystem`.

### T-RMF-19: SimHostInstance
- DONE. Replaced 3 `SystemGroup` fields with `IReadOnlyList<IEcsModuleSystem>` lists. Uses `CgfLogicPack` + `SimHostCoreLogicPack`.

---

## Corrections made during review

1. **GenesisMaterializationSystem phase** — subagent fixed `[UpdateInPhase(SystemPhase.Simulation)]` → `[UpdateInPhase(SystemPhase.Input)]`.
2. **SimHostCoreLogicPackTests.cs cosmetic** — corrected 2 lines with extra indentation (24 spaces → 12) and 2 closing braces placed on the wrong line.
3. **Integration test failure (`EntityMission_MovesEntity`)** — confirmed pre-existing failure (also fails at HEAD before BATCH-03 changes). Not a BATCH-03 regression.

---

## FDP submodule note

The FDP submodule contains uncommitted changes from both BATCH-02 (T-RMF-06..12 toolkit system conversions) and BATCH-03 (T-RMF-18 companion: MovingEntitySystem). These will be committed in the FDP submodule as part of the BATCH-03 commit.
