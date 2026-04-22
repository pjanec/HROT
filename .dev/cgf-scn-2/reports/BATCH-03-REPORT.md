# BATCH-03 Completion Report

**Workstream:** cgf-scn-2  
**Batch:** BATCH-03 — Intent DTO Components for Cross-Entity Reference Safety  
**Status:** COMPLETE — all 6 tasks implemented, all tests pass

---

## Task Completion Summary

| Task     | Title                                              | Status   |
|----------|----------------------------------------------------|----------|
| TASK-S401 | Define Intent DTO managed components               | COMPLETE |
| TASK-S402 | Create VisHierarchy / IsEmbarked / RouteRef translators | COMPLETE |
| TASK-S403 | Update PassengerBufferTranslator.Inject            | COMPLETE |
| TASK-S404 | Create GenesisMaterializationSystem                | COMPLETE |
| TASK-S405 | Remap Intent NetworkIds in StagingEntityExtractor  | COMPLETE |
| TASK-S406 | Update TargetMemoryTranslator.Inject               | COMPLETE |

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs` | 5 managed Intent DTO classes + `TargetEntry` struct |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/VisHierarchyNodeTranslator.cs` | Extract/Inject for `VisHierarchyNode` ↔ `InitialHierarchyIntent` |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/IsEmbarkedTagTranslator.cs` | Extract/Inject for `IsEmbarkedTag` ↔ `InitialVehicleIntent` |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/PersonalRouteRefTranslator.cs` | Extract/Inject for `PersonalRouteRef` ↔ `InitialRouteIntent` |
| `Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs` | Resolves Intent DTOs to structural components on first tick |
| `Hrot/Subsystems/Hrot.SimHost.Tests/GenesisIntentComponentsTests.cs` | 15 unit tests (TASK-S401) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/IntentTranslatorTests.cs` | 10 unit tests (TASK-S402) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/PassengerBufferTranslatorTests.cs` | 3 unit tests (TASK-S403) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/TargetMemoryTranslatorTests.cs` | 2 unit tests (TASK-S406) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/Systems/GenesisMaterializationSystemTests.cs` | 6 unit tests (TASK-S404) |

### Modified Files

| File | Change |
|------|--------|
| `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs` | Added IDs 177–181 for the 5 Intent DTOs |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/PassengerBufferTranslator.cs` | Inject now writes `InitialPassengersIntent` instead of `PassengerBuffer` |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/TargetMemoryTranslator.cs` | Inject now writes `InitialTargetsIntent` instead of `TargetMemory` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Registered 3 new translators; registered `GenesisMaterializationSystem` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostComponentRegistry.cs` | Registered 5 managed Intent DTO types |
| `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | Added Intent NetworkId remapping loop in Pass 2 |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Registered 3 new translators in CGF serializer |
| `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` | Registered all 6 translators in Editor serializer |
| `Hrot/Subsystems/Hrot.SimHost.Tests/StagingEntityExtractorTests.cs` | Added 2 Intent remapping tests (tests 13–14) |

---

## Test Results

### Hrot.SimHost.Tests
```
Passed: 445, Failed: 0, Skipped: 3, Total: 448
```
All new tests pass. No regressions.

### Fdp.Toolkits.Tests
```
Passed: 753, Failed: 7, Skipped: 0, Total: 760
```
7 failures are all pre-existing (CombatComponentTests struct size checks, FireProcessingSystemTests, PhysicsQueryActionNodeTests, NavigationIntentBridgeSystemTests). No new failures introduced.

---

## Test Coverage

| Test File | Tests | Covers |
|-----------|-------|--------|
| `GenesisIntentComponentsTests.cs` | 15 | S401: registration, type ID lookup, DataPolicy.Transient per DTO |
| `IntentTranslatorTests.cs` | 10 | S402: Extract/Inject for Hierarchy, Vehicle, Route translators |
| `PassengerBufferTranslatorTests.cs` | 3 | S403: Inject writes Intent DTO; Extract still produces GUIDs |
| `GenesisMaterializationSystemTests.cs` | 6 | S404: deferred resolution, Passengers, Vehicle, Hierarchy, Route, Targets (partial) |
| `StagingEntityExtractorTests.cs` (new) | 2 | S405: NetworkId remapping and unknown ID preservation |
| `TargetMemoryTranslatorTests.cs` | 2 | S406: Inject writes `InitialTargetsIntent`; dead GUID skipped |
| **Total new tests** | **38** | |

---

## Q&A

**Q1: `IGuidResolver` had no `ResolveNetworkId` method — how was entity-to-NetworkId resolution handled?**

`IGuidResolver.Resolve(string)` returns an `Entity`. The `NetworkIdentity.Value` (long) was then read via `repo.GetComponent<NetworkIdentity>(resolved).Value`. This two-step chain keeps the translator independent of the replication layer's internal ID allocator.

**Q2: How was `entityMap` accessible in the `SimHostApp` scope where `GenesisMaterializationSystem` is registered?**

`entityMap` is a local variable alias for `_entityMap` declared earlier in `SimHostApp.RegisterSimComponents`. The system registration line was inserted immediately after the existing `_simCorePack!.RegisterSystems(...)` call, within the same method scope, so `entityMap` was in scope without any additional wiring.

**Q3: How does the `StagingEntityExtractor` remap NetworkIds in Intent DTOs?**

`oldToNewMap` (built in Pass 1) is a `Dictionary<long, long>`. At the start of the request-building step (before appending `EpisodeTag`), a loop scans the `comps` list in-place. Each Intent DTO type is matched by `is` pattern, a remapped copy is created by looking up each NetworkId in `oldToNewMap` (falling back to the original ID if not found), and the copy replaces the original entry. The original `comps` list is mutated in-place since the staging repo is transient and discarded after extraction.

**Q4: Did any pre-existing tests break?**

No. All 7 failures in `Fdp.Toolkits.Tests` are pre-existing (unrelated to this batch). `Hrot.SimHost.Tests` went from 0 failures to 0 failures, with 445 tests now passing (38 new tests added).

**Q5: Component ID collision fix**

During testing, IDs 172–173 were found to be already claimed by `PerceptionApplicationComponentIds.SensorContactList = 172` and `ActiveSensorTracks = 173`. The Intent DTO IDs were shifted to 177–181 (free range confirmed by scanning all `ComponentId` declarations in the codebase). The range 174–176 was skipped as a buffer against the Perception block.

---

## Suggested Commit Message

```
feat(cgf-scn-2): add Intent DTO cross-entity reference pattern (BATCH-03)

- Define 5 transient managed Intent DTOs (InitialPassengersIntent,
  InitialVehicleIntent, InitialHierarchyIntent, InitialRouteIntent,
  InitialTargetsIntent) with ComponentIds 177-181
- Add VisHierarchyNodeTranslator, IsEmbarkedTagTranslator,
  PersonalRouteRefTranslator: Extract saves GUID strings; Inject
  resolves GUIDs to NetworkIds and writes Intent DTOs
- Update PassengerBufferTranslator.Inject and
  TargetMemoryTranslator.Inject to write Intent DTOs instead of
  structural components
- Add GenesisMaterializationSystem: resolves Intent DTO NetworkIds to
  live ECS handles on first tick after scenario load; always removes
  the intent after first attempt (partial materialization for targets)
- Patch StagingEntityExtractor to remap Intent DTO NetworkIds via
  oldToNewMap during CGF scenario extraction (Pass 2)
- Register all new components and translators in SimHost, CGF, Editor
- 38 new unit tests; Hrot.SimHost.Tests: 445 pass / 0 fail
```
