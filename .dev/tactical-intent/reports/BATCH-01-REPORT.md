# BATCH-01 Report

**Batch:** BATCH-01  
**Developer:** GitHub Copilot  
**Date:** 2025-07-15  
**Status:** Complete

---

## Task Completion

| Task ID   | Status   | Notes                                                                 |
|-----------|----------|-----------------------------------------------------------------------|
| TASK-TI001 | Complete | `AssignTacticalIntentEvent` created; 2/2 tests passing               |
| TASK-TI002 | Complete | `ITacticalOrderMapper` + `TacticalIntentMapperRegistry` created; 3/3 tests passing |
| TASK-TI003 | Complete | `TacticalIntentResolutionSystem` created and wired; 5/5 tests passing |

---

## Testing Results

**Unit Tests for Behavior namespace (Fdp.Toolkits.Tests):** 113 / 113 passed  
**Unit Tests for SimHost (Hrot.SimHost.Tests, TI001-TI003 scope):** 17 / 17 passed  
**Regression — CgfLogicPackTests:** 9 / 9 passed (count assertions corrected; see issues section)

**Pre-existing failures (unrelated to this batch):**
- `Fdp.Toolkits.Tests`: 14 failures in `SimTransformBridgeSystemTests`, `CombatComponentTests`, `FireProcessingSystemTests`, `PhysicsQueryActionNodeTests`, `IdAllocationTests` — present before any changes in this batch.
- `Hrot.SimHost.Tests`: 2 failures in `MissionPlanTranslatorTests` — present before any changes in this batch.

**Key Test Scenarios Verified:**

TASK-TI001:
- [x] PublishManaged + SwapBuffers + ReadManaged round-trip returns correct IntentId
- [x] Default instance has empty non-null string fields

TASK-TI002:
- [x] Register two distinct mappers; TryGetMapper returns correct one per IntentId
- [x] Duplicate TargetIntentId registration throws InvalidOperationException
- [x] Empty registry TryGetMapper returns false, out-param null

TASK-TI003:
- [x] SC-1: Mapper found → translated behavior name published
- [x] SC-2: Empty registry → IntentId passed through as BehaviorName (fallback)
- [x] SC-3: Deleted entity → no AssignBehaviorEvent, no exception
- [x] SC-4: Mapper registered but TryMap returns false → fallback publishes IntentId as BehaviorName
- [x] SC-5: Entity without BehaviorState authority → no AssignBehaviorEvent, no exception

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignTacticalIntentEvent.cs` | Managed event for intent distribution (TASK-TI001) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs` | Interface for intent-to-behavior translation (TASK-TI002) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/TacticalIntentMapperRegistry.cs` | Dictionary-backed mapper registry (TASK-TI002) |
| `Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs` | ECS system translating intent events to behavior events (TASK-TI003) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/AssignTacticalIntentEventTests.cs` | Tests for TASK-TI001 (2 tests) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/TacticalIntentMapperRegistryTests.cs` | Tests for TASK-TI002 (3 tests) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/TacticalIntentResolutionSystemTests.cs` | Tests for TASK-TI003 (5 tests) |

### Modified Files

| File | Change Summary |
|------|---------------|
| `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` | Added `TacticalIntentMapperRegistry` as required 4th constructor parameter; added `TacticalIntentResolutionSystem` field; inserted system into `simList` after `_missionAdapterSystem` |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Updated `CgfLogicPack` construction to pass `new TacticalIntentMapperRegistry()` |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Updated `CgfLogicPack` construction to pass `new TacticalIntentMapperRegistry()` |
| `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs` | Added using, updated all `new CgfLogicPack(...)` calls with 4th arg; corrected SimulationSystems count assertions from 15→16 and total from 17→18 |
| `Hrot/Subsystems/Hrot.Editor.Tests/OfflineKernelBootTests.cs` | Updated `CgfLogicPack` construction to pass `new TacticalIntentMapperRegistry()` |
| `Hrot/Subsystems/Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs` | Updated `CgfLogicPack` construction to pass `new TacticalIntentMapperRegistry()` |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | Updated `CgfLogicPack` construction to pass `new TacticalIntentMapperRegistry()` |

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Two issues arose during build verification:

1. **Missing call site in `EditorHarness.cs`**: The batch instructions mentioned updating `CgfLogicPack` construction in `CgfSubsystem.cs`, `EditorSubsystem.cs`, and the two test infrastructure files. However, `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` also instantiates `CgfLogicPack` and was not listed. The first build after wiring TASK-TI003 caught this immediately as a compiler error. Fixed by adding the missing using and passing `new TacticalIntentMapperRegistry()`.

2. **Off-by-one in CgfLogicPackTests count assertions**: The conversation context recorded the original SimulationSystems count as 14 and total as 16. In reality the counts were already 15 sim / 17 total before this batch (some prior work had added a system). After inserting `TacticalIntentResolutionSystem` the correct counts became 16 sim / 18 total. The initial fix set them to 15/17 which was still one short. The second test run caught this; corrected to 16/18.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `CgfLogicPackTests.cs` contains hardcoded integer counts for system collections (`SimulationSystems.Count`). Every time a new system is wired into the pack these assertions silently drift and break. A more maintainable approach would be to verify counts relative to the module's own collection sizes (e.g., assert that `pack.SimulationSystems.Count == pack.InputSystems.Count + missionModuleSims + cognitiveModuleSims + ...`) or use snapshot tests. At minimum, a named constant or a comment block listing each system by name would make count maintenance less brittle.

- The `TacticalIntentMapperRegistry` is passed as `new TacticalIntentMapperRegistry()` (empty) in all six call sites that were updated. There is currently no mechanism for callers to contribute mappers before the system runs. This is by design for this batch (the registry is intended to be populated later), but the pattern could easily lead to silent no-ops in production if a mapper is never registered. A future batch should consider adding a constructor or factory parameter to the subsystem so mappers can be injected at composition root level.

**Q3: What design decisions did you make beyond the instructions?**

- **`TacticalIntentMapperRegistry` as a required parameter (not optional)**: Making it required (`CgfLogicPack(..., TacticalIntentMapperRegistry mapperRegistry, ...)`) rather than optional with a default forces every instantiation site to be explicit about the registry it supplies. An optional parameter with `new TacticalIntentMapperRegistry()` as its default would silently provide an empty registry even to callers that meant to supply a real one. The required parameter makes the dependency visible in the call graph.

- **`null` check on `mapperRegistry` in `CgfLogicPack` constructor**: Consistent with the existing `ArgumentNullException` guard on `scenarioSource`, a null check was added for `mapperRegistry` to fail fast and clearly.

- **Authority gate before mapper lookup**: The authority check (`HasAuthority<BehaviorState>`) is performed before attempting to look up a mapper. This ensures the registry is never consulted for events that will be discarded, keeping the hot path fast.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **`AlwaysFailMapper` / fallback path**: SC-4 tests that when a mapper is registered but its `TryMap` returns false, the system falls back to treating `IntentId` as the behavior name. This edge case (mapper registered but mapping fails) is distinct from "no mapper registered" and exercises a different code path. The spec implied this behaviour but did not explicitly call it out as a test case; it is included to prevent future regressions if the fallback logic is accidentally removed.

- **`null` event in `ReadManaged` stream**: `TacticalIntentResolutionSystem.Execute` includes a `if (evt == null) continue;` guard. In practice `FdpEventBus.ReadManaged<T>()` does not return null entries, but the guard costs nothing and matches defensive patterns used elsewhere in the codebase (see `MissionAdapterSystem`).

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- None specific to this batch. The registry lookup is a single `Dictionary<string, ITacticalOrderMapper>` lookup per event — O(1) and allocation-free. The system is inserted before `MissionControlModule.SimulationSystems` in the tick order, which is the correct ordering (intent events from this tick must be translated before the behavior assignment is consumed by the cognitive pipeline in the same tick).

---

## Outstanding Issues / Next Steps

- [ ] A future batch should provide a way for subsystems (CGF, Editor) to register custom mappers into the `TacticalIntentMapperRegistry` before simulation starts — currently all registries are empty at runtime.
- [ ] The hardcoded system counts in `CgfLogicPackTests.cs` should be refactored to use a more maintainable counting strategy to reduce brittleness.
- [ ] `MissionPlanTranslatorTests` (2 pre-existing failures) and `Fdp.Toolkits.Tests` (14 pre-existing failures) are out of scope for this batch but should be tracked.

---

## Suggested Commit Message

```
feat(tactical-intent): add TacticalIntent event, mapper registry, and resolution system (BATCH-01)

TASK-TI001: Add AssignTacticalIntentEvent managed event class
TASK-TI002: Add ITacticalOrderMapper interface and TacticalIntentMapperRegistry
TASK-TI003: Add TacticalIntentResolutionSystem wired into CgfLogicPack

- AssignTacticalIntentEvent carries Entity, IntentId, JsonParams
- TacticalIntentMapperRegistry maps IntentId strings to ITacticalOrderMapper instances;
  throws InvalidOperationException on duplicate registration
- TacticalIntentResolutionSystem translates AssignTacticalIntentEvent to
  AssignBehaviorEvent via mapper lookup, with IntentId pass-through fallback;
  authority-gated on BehaviorState to skip remote-owned entities
- CgfLogicPack now takes TacticalIntentMapperRegistry as required 4th constructor
  parameter; TacticalIntentResolutionSystem inserted after MissionAdapterSystem
- All CgfLogicPack call sites updated (CgfSubsystem, EditorSubsystem, EditorHarness,
  SimHostInstance, OfflineKernelBootTests, CgfLogicPackTests)
- 10 new tests (2 + 3 + 5), all passing

Pre-existing failures in SimTransformBridge, CombatComponent, MissionPlanTranslator,
IdAllocation, PhysicsQueryActionNode are unrelated and unchanged.
```
