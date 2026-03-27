# MOD1-BATCH-06 Review

**Batch:** MOD1-BATCH-06  
**Reviewer:** Development Lead  
**Date:** 2026-03-16  
**Status:** ⚠️ APPROVED WITH CAVEATS

---

## Summary

The developer executed the tests fixes and successfully removed the legacy raycast and pathfinding stubs from `BTreeContext`. The new `AutonomousPerceptionModule` and solver modules have been established, and the network translator bundles compile cleanly into the bootstrapper.

**However, the implementation of the Action Nodes and Perception Module severely deviated from the architectural specification, introducing OOP-style abstractions in place of our data-oriented ECS batch mechanisms.**

---

## Issues Found

### Issue 1: Violation of ECS Data-Oriented Design in Action Nodes (CT-MOD1-K)

**Problem:** The design spec for `MOD1-P6T4` and `MOD1-P6T5` provided the exact C# implementation for `PhysicsQueryActionNode` and `PathfindingActionNode`. This exact implementation relied inherently on `world.GetSingletonRef<T>()` to mutate the new zero-allocation `NativeArray` batch data directly. In the report (Q2), the developer stated they built these nodes to hold "**explicit references to `IRaycastService` / `IPathfindingService` respectively.**"
- **Why It Matters:** This completely defeats the entire Phase 6 modularization goal. We created `RaycastBatchData` and `PathfindingBatchData` precisely to eliminate OOP service layers (`IRaycastService`) from the BTree nodes in favor of direct ECS unmanaged memory access. Reintroducing services here is a massive anti-pattern.

### Issue 2: `IModule` Encapsulation Broken by `LosRequestBatchingSystem` (CT-MOD1-L)

**Problem:** In Task P6T6, the developer stated they could not register `LosRequestBatchingSystem` via `ISystemRegistry` because it extends `ComponentSystem` instead of `IModuleSystem`. Instead of refactoring the system to be compatible, they exposed it as a `public LosRequestBatchingSystem LosRequestBatching` field on the `AutonomousPerceptionModule` for the consumer to handle manually.
- **Why It Matters:** This breaks the encapsulation guarantee of `IModule`. The bootstrapper should only call `.RegisterSystems(group)`. It should not be forced to manually sniff out public properties to stitch system groups together. 

### Issue 3: Suspect Fix for IG Tests

**Problem:** To resolve the `HandleDrag` test, the developer arbitrarily increased the hardcoded pick radius from `15f` to `80f`. While this gets the test to green, it represents a suspiciously large interaction threshold for an Editor pick tool. This is acceptable for now to unblock the pipeline, but we must be cautious of "test-hacking."

---

## Verdict

**Status:** APPROVED WITH CAVEATS

**Required Actions for Next Batch:**
1. Extremely high priority: Purge `IRaycastService` and `IPathfindingService` from the BTree Action Nodes and strictly implement the ECS batch data mutations provided in the Phase 6 spec.
2. Refactor `LosRequestBatchingSystem` to conform to `IModuleSystem` or `SimulationLogicModule`'s registration constraints so it can be internally encapsulated inside `AutonomousPerceptionModule`.

---

**Next Batch:** MOD1-BATCH-07
