# TwoAck-BATCH-01 Report: Two-ACK Entity Lifecycle Pattern (Full Pipeline)

**Batch:** TwoAck-BATCH-01
**Date:** 2026-07-10
**Status:** ✅ COMPLETE — All tasks done, build succeeds

---

## Summary

This batch delivers the complete Two-ACK synchronisation pattern across the full
DataModel → SimHost → IOS → IG pipeline. The fundamental motivation was eliminating the
"half-baked entity" UX bug: previously, IOS received a `CreateEntityAck` the instant SimHost
allocated a network ID, well before the ECS lifecycle handshake completed. Operators could
then interact with a UI entity whose SimHost counterpart did not yet exist.

The solution introduces a two-phase acknowledgement protocol:

- **Phase 1 – InProgress (StatusCode = 1):** dispatched immediately when `CreateEntityRequestSystem`
  or the new `DeleteEntityRequestSystem` allocates/validates the request.
- **Phase 2 – Success (0) or Error (≥ 2):** dispatched by the new `SstRequestFinalizationSystem`
  (PostSimulation phase) once the ECS lifecycle confirms the entity is `Active` (create) or
  completely removed from the `NetworkEntityMap` (delete).

IOS tracks in-progress entities in a `HashSet<int>`, surfaces a modal error dialog for Phase-2
failures, and prevents UI interaction (ImGui `BeginDisabled`) while any entity is pending.

FDP components are entirely unaffected.

---

## Build Status

```
Build succeeded.    Warnings: 0    Errors: 0
```

---

## Tasks Completed

| Task ID | Title | Status |
|---------|-------|--------|
| TWOACK-DM001 | Add `DeleteEntityRequest` struct | ✅ Done |
| TWOACK-DM002 | Rename `SstErrorCode` → `SstStatusCode`, add `InProgress = 1` | ✅ Done |
| TWOACK-DM003 | Expand `CreateUpdateDeleteEntityAck`; remove `CreateEntityAck` | ✅ Done |
| TWOACK-SH001 | Implement `SstRequestFinalizationSystem` (Phase-2 dispatch) | ✅ Done |
| TWOACK-SH002 | Update `CreateEntityRequestSystem` for Two-ACK + finalization wiring | ✅ Done |
| TWOACK-SH003 | Implement `DeleteEntityRequestSystem` + `SimHostModule` wiring | ✅ Done |
| TWOACK-IOS001 | Rewrite `IosLogic.ProcessEntityCreationAcks`; `_pendingEntities`, `_globalAlert` | ✅ Done |
| TWOACK-IOS002 | `ContextMenuLogic` pending guard (suppress context menu while pending) | ✅ Done |
| TWOACK-IOS003 | `MissionPanel` pending guard (`BeginDisabled` + `IsPendingGuardActive`) | ✅ Done |
| TWOACK-IOS004 | `IosMock` GlobalAlert modal; `IsEntityPending`/`GlobalAlert`/`DismissAlert` on `IIosLogic` | ✅ Done |

---

## Files Created

| File | Description |
|------|-------------|
| `Hrot.SimHost/Systems/SstRequestFinalizationSystem.cs` | PostSimulation system dispatching Phase-2 ACKs |
| `Hrot.SimHost/Systems/IDeleteEntityRequestSource.cs` | Interface for delete request source (mirrors `ICreateEntityRequestSource`) |
| `Hrot.SimHost/Systems/DeleteEntityRequestSystem.cs` | Input-phase system handling `DeleteEntityRequest` DDS messages |
| `Hrot.SimHost.Tests/SstRequestFinalizationSystemTests.cs` | 6 unit tests for finalization system |
| `Hrot.SimHost.Tests/DeleteEntityRequestSystemTests.cs` | 2 unit tests for delete request system |
| `Hrot.ExCon.Tests/TwoAckIosTests.cs` | 13 unit tests for IOS Two-ACK logic (IosLogic, ContextMenuLogic, MissionPanel) |

---

## Files Modified

### DataModel
| File | Change |
|------|--------|
| `Hrot.NED/GenericMessages.cs` | `SstErrorCode` → `SstStatusCode` with `InProgress = 1`; added `DeleteEntityRequest`; expanded `CreateUpdateDeleteEntityAck` with `EntityId` + renamed `ErrorCode` → `StatusCode`; removed `CreateEntityAck` |

### Rename SstErrorCode → SstStatusCode consumers
| File | Change |
|------|--------|
| `Hrot.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` | `SstErrorCode` → `SstStatusCode`; `ErrorCode` → `StatusCode` in DDS writes |
| `Hrot.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs` | Same renames |
| `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` | Same renames |
| `Hrot.SimHost.Tests/UpdateEntityDescriptorRequestSystemTests.cs` | Same renames in assertions |

### SimHost — new ack type
| File | Change |
|------|--------|
| `Hrot.SimHost/Systems/ICreateEntityAckSink.cs` | Renamed interface to `ICreateUpdateDeleteEntityAckSink` |
| `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs` | Phase-1 InProgress ACK; calls `_finalizationSystem?.Track(...)` |
| `Hrot.SimHost/Modules/SimHostModule.cs` | Wires `SstRequestFinalizationSystem`, `DdsDeleteEntityRequestSource`, `DeleteEntityRequestSystem` |

### IOS / Runner
| File | Change |
|------|--------|
| `Hrot.ExCon/IIosLogic.cs` | Added `IsEntityPending`, `GlobalAlert`, `DismissAlert` |
| `Hrot.ExCon/IosLogic.cs` | `_pendingEntities`, `_globalAlert`; rewrote `ProcessEntityCreationAcks` |
| `Hrot.ExCon/Services/DdsEventIngressHandlers.cs` | `CreateUpdateDeleteEntityAckIngressHandler` |
| `Hrot.ClusterRunner/Services/IosSubsystem.cs` | `ConcurrentEventQueue<CreateUpdateDeleteEntityAck>` |
| `Hrot.ExCon/Logic/IContextMenuLogic.cs` | Optional `isEntityPending` param on `OnSelectionChanged` |
| `Hrot.ExCon/Logic/ContextMenuLogic.cs` | Guard: return empty menu for pending entities |
| `Hrot.ExCon/Panels/MissionPanel.cs` | `BeginDisabled` pending guard; `IsPendingGuardActive` helper |
| `Hrot.ExCon/IosMock.cs` | `GlobalAlert` modal popup |

### IG
| File | Change |
|------|--------|
| `Hrot.IG/IgApplication.cs` | `DdsReader<CreateUpdateDeleteEntityAck>` |
| `Hrot.IG/Systems/MapCommandController.cs` | `OnCreateEntityAck` accepts new type |
| `Hrot.Map.Common/Commands/BdcCommandGateway.cs` | Command client uses new ack type |
| `Hrot.IG/UI/MiniIosPanelState.cs` | New ack type; `StatusCode` check; `EntityId` field |

### Updated Tests
| File | Change |
|------|--------|
| `Hrot.SimHost.Tests/CreateEntityRequestSystemTests.cs` | `StubAckSink` updated; `StatusCode`/`InProgress` assertions |
| `Hrot.SimHost.Tests/AttributeCompilerFactoryTests.cs` | `ErrorCode` → `StatusCode` |
| `Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs` | `StubAckSink` and `CreateEntity()` return type updated |
| `Hrot.SimHost.Integration.Tests/Infrastructure/MockIOSClient.cs` | `WaitForAckAsync` returns `CreateUpdateDeleteEntityAck?` |
| `Hrot.SimHost.Integration.Tests/EntityCreationFlowTests.cs` | `StatusCode`/`EntityId` field names |
| `Hrot.SimHost.Integration.Tests/NavComponentsPresenceTests.cs` | Same |
| `Hrot.SimHost.Integration.Tests/MissionExecutionFlowTests.cs` | Same |
| `Hrot.SimHost.Integration.Tests/PerformanceTests.cs` | Same |
| `Hrot.ClusterRunner.Integration.Tests/SpawnMovingVehicleWithGatewayIntegrationTests.cs` | New DDS topic + field names |
| `Hrot.ClusterRunner.Integration.Tests/MiniIosIntegrationTests.cs` | New DDS topic + field names in all three test methods |
| `Hrot.ClusterRunner.Integration.Tests/AreaAuthoringIntegrationTests.cs` | New DDS topic + field names |
| `Hrot.ClusterRunner.Integration.Tests/MapPlacementIntegrationTests.cs` | New DDS topic + field names |
| `Hrot.NED.Tests/GenericMessageFieldTests.cs` | Added 9 Two-ACK data model tests |
| `Hrot.IG.Tests/MapCommandControllerTests.cs` | `CreateUpdateDeleteEntityAck` with `EntityId`/`StatusCode` |

---

## New Tests Added

### `Hrot.SimHost.Tests/SstRequestFinalizationSystemTests.cs` (6 tests)
- `TrackCreate_EntityStillConstructing_DoesNotDispatchAck` — no Phase-2 if entity not yet Active
- `TrackCreate_EntityBecomesActive_DispatchesSuccessAck` — Phase-2 Success once Active
- `TrackCreate_AfterSuccess_NoRedispatch` — idempotent; no duplicate ACK on second Execute
- `TrackDelete_EntityStillAlive_DoesNotDispatchAck` — no Phase-2 if entity still in map
- `TrackDelete_EntityGone_DispatchesSuccessAck` — Phase-2 Success once entity removed
- `TrackDelete_EntityNeverInMap_DispatchesEntityNotFoundAck` — handles entity that was never registered

### `Hrot.SimHost.Tests/DeleteEntityRequestSystemTests.cs` (2 tests)
- `ProcessRequest_UnknownEntity_SendsEntityNotFoundAck` — Phase-1 error for unknown entity ID
- `ProcessRequest_KnownEntity_SendsInProgressAckAndPublishesCommand` — Phase-1 InProgress + `DestroyEntityCommand`

### `Hrot.ExCon.Tests/TwoAckIosTests.cs` (13 tests)

**`TwoAckIosTests` class (IosLogic state machine):**
- `InProgressAck_AddsEntityToPendingSet`
- `SuccessAck_RemovesEntityFromPendingSet`
- `ErrorAck_RemovesEntityFromPendingSetAndSetsAlert`
- `DismissAlert_ClearsGlobalAlert`
- `IsEntityPending_ReturnsCorrectly`

**`ContextMenuLogicPendingTests` class:**
- `OnSelectionChanged_PendingEntity_ReturnsEmptyMenu`
- `OnSelectionChanged_NonPendingEntity_ReturnsNonEmptyMenu`

**`MissionPanelPendingTests` class:**
- `IsPendingGuardActive_NoSelection_ReturnsFalse`
- `IsPendingGuardActive_EntityNotPending_ReturnsFalse`
- `IsPendingGuardActive_EntityIsPending_ReturnsTrue`

### `Hrot.NED.Tests/GenericMessageFieldTests.cs` (9 new tests added)
- `DeleteEntityRequest_HasRequestIdField`
- `DeleteEntityRequest_HasEntityIdField`
- `SstStatusCode_HasInProgress_Value1`
- `SstStatusCode_HasSuccess_Value0`
- `SstStatusCode_ErrorsStartAt2`
- `CreateUpdateDeleteEntityAck_HasEntityIdField`
- `CreateUpdateDeleteEntityAck_HasStatusCodeField`
- `CreateUpdateDeleteEntityAck_HasRequestIdField`
- `CreateEntityAck_DoesNotExist`

---

## Design Decisions

### `RequestKind` internal enum at namespace level
Both `CreateEntityRequestSystem` and `DeleteEntityRequestSystem` need to pass
`RequestKind.Create` / `RequestKind.Delete` to `SstRequestFinalizationSystem.Track()`.
Defining `RequestKind` at the namespace level (not nested inside one class) avoids requiring a
qualified reference across the two classes and keeps the API clean.

### `SstStatusCode` as `int` on the wire struct
The DDS struct uses `int StatusCode` for forward compatibility and IDL compatibility. All
test assertions use `(int)SstStatusCode.X` casts to compare with the raw wire value. This
avoids changing the generated IDL while adding type-safe enum constants.

### `IsPendingGuardActive` as public helper on `MissionPanel`
`MissionPanel.Draw()` contains the ImGui rendering logic that calls `BeginDisabled`.
Extracting `IsPendingGuardActive(IIosLogic logic)` as a public method enables unit testing
the guard condition without requiring an ImGui rendering context.

### Optional `isEntityPending` parameter on `ContextMenuLogic.OnSelectionChanged`
Making the parameter optional (default `null`) preserves full backward compatibility for all
existing callers and tests that do not pass an entity-pending predicate. Callers that want the
guard (i.e. `IosLogic.ProcessSelectionEvents`) supply the delegate.

---

## Deviations from Spec

None. All tasks were implemented as specified. No FDP files were modified.
