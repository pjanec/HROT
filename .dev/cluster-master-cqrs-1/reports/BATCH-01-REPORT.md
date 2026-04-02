# BATCH-01 Report: FDP Domain Enums and CQRS Event Structs

**Batch:** BATCH-01  
**Tasks Completed:** CMC-S001, CMC-S002, CMC-S003  
**Status:** ✅ Complete — all tests passing

---

## Summary

All three tasks completed. The FDP domain vocabulary for ClusterMaster CQRS is in place. Zero breaking changes to existing behavior; all work was additive.

---

## Tasks Implemented

### CMC-S001 — Domain Enums in FDP.Toolkit.Orchestration ✅

**Files created:**
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/ClusterState.cs`
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/ClusterOpType.cs`
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/NodeOpType.cs`

Integer values verified against `Hrot.NED/Orchestration/OrchestrationMessages.cs` (authoritative source). All values match exactly.

**Test file created:** `Hrot.Orchestrator.Tests/FdpOrchestrationEnumSyncTests.cs`  
**Tests added:** 3 sync tests (ClusterState, ClusterOpType, NodeOpType) — all pass.

**Constraint satisfied:** `Hrot.NED` appears only in XML doc comments inside `FDP.Toolkit.Orchestration/Enums/`. Zero code imports.

---

### CMC-S002 — Core CQRS Event Bus Structs ✅

**File created:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`

Structs:
- `ClusterOpCompletedEvent` — EventId 9011, `[DataPolicy(DataPolicy.NoRecord)]`
- `ExecuteNodeOpIntent` — EventId 9012, `[DataPolicy(DataPolicy.NoRecord)]`
- `NodeOpCompletedEvent` — EventId 9013, `[DataPolicy(DataPolicy.NoRecord)]`

All structs carry `object?` payload fields (no `string? PayloadJson`, no `string? ResultJson`). No `System.Text.Json` in the file. No `ExecuteClusterOpIntent` was created.

**Test file created:** `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FdpOrchestrationCqrsStructTests.cs`  
**Tests added (CMC-S002):** 8 tests covering DataPolicy, EventId uniqueness, field names, PublishManaged/ConsumeManaged round-trip.

---

### CMC-S003 — Specific Operation Payload Intent Structs ✅

**File created:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterOpIntents.cs`

Types:
- `TransitionStateIntent` — EventId 9050
- `ManageEpisodeIntent` — EventId 9051
- `SeekReplayIntent` — EventId 9052
- `CancelOperationIntent` — EventId 9053
- `StorageOpType` enum (Export, Import, SaveScenario — no EventId)
- `ExecuteStorageOpIntent` — EventId 9054
- `StorageOpCompletedEvent` — EventId 9055
- `TakeCheckpointIntent` — EventId 9056 (only `Guid RequestId`, no other fields)
- `LoadZoneIntent` — EventId 9057

`TransitionStateIntent.TargetState` uses `FDP.Toolkit.Orchestration.ClusterState` (not `Hrot.NED`). All structs `[DataPolicy(DataPolicy.NoRecord)]`.

**Tests added (CMC-S003 in FdpOrchestrationCqrsStructTests.cs):** 6 additional tests covering field structures, `TakeCheckpointIntent` single-field constraint, and EventId range uniqueness.

---

## Test Results

| Test Suite | Total | Passed | Failed |
|--|--|--|--|
| `FDP.Toolkit.Orchestration.Tests` | 25 | 25 | 0 |
| `Hrot.Orchestrator.Tests` | 67 | 67 | 0 |
| **Total** | **92** | **92** | **0** |

---

## Issues Encountered

### Namespace Ambiguity (Expected Side Effect)

Adding `ClusterState`, `ClusterOpType`, `NodeOpType` to `FDP.Toolkit.Orchestration` created ambiguous references in files that already `using`-import both `Hrot.NED.Descriptors.Orchestration` and `FDP.Toolkit.Orchestration`.

**Affected files fixed with C# `using` type aliases:**
- `Hrot.Common/Orchestration/HrotHandlerAdapter.cs` — qualified inline (only 2 lines affected)
- `Hrot.Common/Orchestration/DdsOrchestrationTransport.cs` — qualified inline (1 line)
- `Hrot.Orchestrator/ClusterMaster.cs` — added 3 type aliases
- `Hrot.Orchestrator/TransitionPlanner.cs` — added 2 type aliases  
- `Hrot.Orchestrator/HrotStateGraph.cs` — added 1 type alias
- `Hrot.Orchestrator.Tests/*.cs` (8 test files) — added 3 type aliases each

The aliases resolve to `Hrot.NED.Descriptors.Orchestration.*` because all these application-layer files currently operate on DDS/NED structures. No behavior changed — values are identical between both enums.

### Pre-existing Test Bug

`TransitionPlannerTests.ImpossibleRequest_ThrowsInvalidOperationException` was asserting `"RunningLive"` in the error message, but the enum member has been named `OperatingLive` in the NED enum. Updated assertion to `"OperatingLive"`.

---

## Design Decisions

1. **Type aliases over global renames:** When the Dual-Enum Pattern introduced 3 conflicting names, the minimal-change approach was C# `using` aliases in affected files. This avoids wide-impact refactors that are beyond this batch's scope.

2. **StorageOpType has no EventId:** The `StorageOpType` is an enum, not an event struct. Consistent with the design: only event structs need EventIds.

3. **`TakeCheckpointIntent` has exactly one field:** Per spec, `TakeCheckpointIntent` carries `Guid RequestId` only. A reflection test asserts this explicitly.

---

## Weak Points Spotted

1. **Handlers/ still use System.Text.Json:** The existing `FDP.Toolkit.Orchestration/Handlers/` reference handlers still call `JsonDocument.Parse()` on `OrchestrationCommand.PayloadJson`. This will be addressed in CMC-S005.

2. **Dual alias approach only bridges the gap temporarily:** CMC-S004+ will eventually remove the `OrchestrationCommand`/int-based dispatch, at which point these application-layer files will naturally migrate to use FDP enums directly, making the aliases redundant. The aliases are safe to keep or remove then.

3. **HrotHandlerAdapter will be deleted in CMC-S007:** It's currently bridge code. The inline qualification fixes are minimal and temporary.
