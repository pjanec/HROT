# MOD1-BATCH-07 Review

**Batch:** MOD1-BATCH-07  
**Reviewer:** Development Lead  
**Date:** 2026-03-16  
**Status:** ⚠️ APPROVED WITH CAVEATS

---

## Summary

The developer successfully delivered all seven tasks: the Fbt.Kernel submodule was cleanly reverted, the OOP service anti-pattern was eliminated in favour of static batch helpers, ground clamping Phase 7 is implemented with a solid four-phase pipeline, and the test coverage is thorough and behaviour-focused. No regressions were introduced.

However, two structural issues require correction before new feature work should continue.

---

## What Went Well

- **CT-MOD1-M (Fbt.Kernel Revert):** The submodule is clean. The `BTreeContext` correctly implements the four `IAIContext` stubs as harmless no-ops, and the developer's rationale is sound: those stubs are dead code paths in practice because the ECS batch helpers are used instead.
- **CT-MOD1-K (ECS batch helpers):** `RaycastBatchHelper` and `PathfindingBatchHelper` as static classes is a clean pattern - the `IRaycastService`/`IPathfindingService` interfaces are gone. Tests now touch real `EntityRepository` memory instead of mocked boundaries.
- **Phase 7 (Ground Clamping):** The pipeline architecture is excellent. The four-phase split (`Init → Submit → Solver → Resolution`), the `ITerrainProvider` abstraction boundary, the jump-rejection in `ResolutionSystem`, and the lerp in `TransformSyncSystem` are all correct. The integration test proving convergence in 3 frames is exactly the kind of test we need.
- **Translator dual-enum pattern:** The `GroundClampingOverrideTranslator` mapping via ordinal cast is clean and the rationale for keeping separate enums even when values match is architecturally sound.

---

## Issues Found

### Issue 1: `LosRequestBatchingSystem` Dual-Inheritance is Architecturally Wrong (CT-MOD1-N)

```csharp
public class LosRequestBatchingSystem : ComponentSystem, IModuleSystem
```

The user flagged this directly. This is not merely a style concern — it is a design problem:

- `ComponentSystem` is the **main-thread, synchronous ECS update** base class. It binds the system to the main simulation loop, accesses `World.Bus` directly, and runs once-per-frame unconditionally.
- `IModuleSystem` is the **background-thread, snapshot-isolated** execution contract. It receives a read-only `ISimulationView`, consumes an event copy via `view.ConsumeEvents<>()`, and must NOT touch live `World` state.

A class that implements both simultaneously has two conflicting update paths (`OnUpdate` vs. `IModuleSystem.Execute`) that can diverge. Worse, the design intent for `AutonomousPerceptionModule` is `ExecutionPolicy.SlowBackground(10)` — meaning it runs at 10 Hz on a background thread. Registering `LosRequestBatchingSystem` here via `ISystemRegistry.RegisterSystem` in `AutonomousPerceptionModule.RegisterSystems()` and then calling it in the main simulation group via `ComponentSystem` means **it may run twice per frame** — once on the background thread via `IModuleSystem.Execute` and once on the main thread via `SimulationSystemGroup`.

**Required fix:** `LosRequestBatchingSystem` must be refactored to implement only `IModuleSystem` (removing the `ComponentSystem` base). Its update path via `OnUpdate` must be migrated entirely to `IModuleSystem.Execute`. This properly isolates it to the background thread and eliminates the dual-path ambiguity.

### Issue 2: `GlobalComponentIds` Modified Again (DB-MOD1-16)

From the report (P7T2): `IDs 77–79 allocated in GlobalComponentIds`.

While we concluded last session that `Fdp.Kernel` is owned by the project (not a third-party library), the modularization goal is clear: toolkit-specific component IDs should live in toolkit-local registries (`NavigationComponentIds`, `PerceptionComponentIds`, `GeographicComponentIds`, etc.) rather than the monolithic `GlobalComponentIds`. Centralising IDs in `Fdp.Kernel` defeats the modular registry split that Phase 5 established. Ground clamping components belong in `Fdp.Toolkit.Geographic` — their IDs should live in a local `GeographicComponentIds` class in that same assembly.

This is tracked as new debt below.

---

## Verdict

**Status:** APPROVED WITH CAVEATS

**Required Actions for Next Batch:**
1. **Highest priority:** Refactor `LosRequestBatchingSystem` to remove `ComponentSystem` base and implement only `IModuleSystem`. Migrate the main-thread `OnUpdate` logic into `IModuleSystem.Execute` exclusively.
2. **Architecture cleanup:** Create `GeographicComponentIds` in `Fdp.Toolkit.Geographic` and move the terrain query component IDs (77–79) out of `GlobalComponentIds`.

---

**Next Batch:** MOD1-BATCH-08
