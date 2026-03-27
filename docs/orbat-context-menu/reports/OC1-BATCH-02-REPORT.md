# OC1-BATCH-02 Report

**Batch:** OC1-BATCH-02  
**Developer:** Copilot  
**Date:** 2025-07-14  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| OC1-CORRECTIVE-01 Bug 1 | ✅ Done | `DerEntityInspectorPanel` — reset `_selectedEntityId = NoSelection` on null entity |
| OC1-CORRECTIVE-01 Bug 2 | ✅ Done | `DescriptorMapper` — added `dtMapRoute` case in `MapToComponents` |
| OC1-CORRECTIVE-01 Bug 3 | ✅ Done | `MapOverlayStyle.ToJson()` added; `ActivateAreaEditingTool` now preserves `StyleOverrideJson` on commit |
| OC1-DEBT-01 | ✅ Done | `ActivateAreaAuthoringTool` guard unified to `!_networkEnabled && sink == null` |
| OC1-DEBT-02 | ✅ Done | `MapCommandController.OnAreaEntityCreated` now emits `FdpLog.Warn` on empty session |
| OC1-S001 | ✅ Done | `MissionControlRequestSystem` translates FollowRoute network ID via `TryTranslateFollowRouteBehaviorParams`; retries until trajectory compiled |
| OC1-G001 | ✅ Done | `CMD_SET_SELECTION` handler + `TestHook_ParseCommandAndSetSelection` |
| OC1-G002 | ✅ Done | `CMD_SET_VIEW` handler + `TestHook_ParseCommandAndSetView` |
| OC1-G003 | ✅ Done | `CMD_DRAW_PERSONAL_ROUTE` handler + `OrchestratePersonalRouteAsync` + `TestHook_ParseCommandAndActivatePersonalRoute` |

---

## 🧪 Testing Results

**Unit Tests Passed:** 970 / 970  

| Project | Before | After | New |
|---------|--------|-------|-----|
| Bagira.SimHost.Tests | 326 | 332 | +6 |
| Bagira.IG.Tests | 387 | 399 | +12 |
| Bagira.DDS.DataModel.Tests | 33 | 33 | 0 |
| Bagira.Map.Common.Tests | 94 | 94 | 0 |
| Bagira.Runner.Tests | 112 | 112 | 0 |

**New Test Files Created:**
- `Bagira.SimHost.Tests/Systems/MissionControlRequestSystemFollowRouteTests.cs` (6 tests) — OC1-S001
- `Bagira.IG.Tests/CommandHandling/SetSelectionCommandTests.cs` (4 tests) — OC1-G001
- `Bagira.IG.Tests/CommandHandling/SetViewCommandTests.cs` (3 tests) — OC1-G002
- `Bagira.IG.Tests/CommandHandling/DrawPersonalRouteCommandTests.cs` (5 tests) — OC1-G003

**Key Test Scenarios Verified:**
- ✅ OC1-S001 Sc1: FollowRoute with compiled trajectory → BehaviorParams rewritten from networkId to trajectoryId
- ✅ OC1-S001 Sc2: Route entity not found → request enqueued for retry
- ✅ OC1-S001 Sc3: Route entity exists but TrajectoryId==0 → retry
- ✅ OC1-S001 Sc4: Route compiles between retry cycles → committed on second drain
- ✅ OC1-S001 Sc5: Non-FollowRoute task → BehaviorParams unchanged
- ✅ OC1-S001 Sc6: `TryTranslateFollowRouteBehaviorParams` roundtrip JSON correctness
- ✅ OC1-G001 Sc1: Known entity → becomes selected
- ✅ OC1-G001 Sc2: Unknown entity → no exception
- ✅ OC1-G001 Sc3: Empty JSON → silently ignored
- ✅ OC1-G001 Sc4: Selecting entity B deselects entity A
- ✅ OC1-G002 Sc1: Known entity → camera `_keyboardPanTarget` updated
- ✅ OC1-G002 Sc2: Unknown entity → no exception, camera unchanged
- ✅ OC1-G002 Sc3: Empty JSON → silently ignored
- ✅ OC1-G003 Sc1–5: Route creation flow, ACK path, gateway invocations

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **Duplicate code fragment in `IgApplication.cs`**: An earlier edit accidentally duplicated the body of `ParseCommandAndActivateEditTool`. Detected during first build, removed the orphaned block.

2. **`MissionCommandPayload` renamed to `MissionCommandUnion`**: The spec referenced `MissionCommandPayload` but the actual type is `MissionCommandUnion`. Resolved by grepping the codebase.

3. **`NetworkIdentity` namespace**: `NetworkIdentity` requires `using FDP.Toolkit.Replication.Components;` in the SimHost project. Discovered from compile error, resolved by adding the using.

4. **`TallyGateway` stub in `ContinuousDragTests.cs`**: Extended `IBdcCommandGateway` with `CreateEntityAsync` and `SendMissionControlRequestAsync`. The existing stub needed two new stub implementations returning completed empty Tasks.

5. **`ISimulationView` namespace confusion**: The interface is in `ModuleHost.Core.Abstractions`, not `Fdp.Interfaces`. Tests in the same project use both; the correct using directive is `using ModuleHost.Core.Abstractions;`.

6. **Ghost entity lifecycle in `SelectEntityOnMap`**: When testing G001 deselection, `SelectEntityOnMap`'s clearing query used the default lifecycle filter (Active only), so ghost/spawning entities were invisible to it. Fixed by adding `.WithLifecycle(EntityLifecycle.All)` to the deselection query — correct for production too, since CMD_SET_SELECTION may target pre-promoted entities.

7. **`_ghostCreationSystem` null when DDS fails**: `InitializeEmbedded` calls `InitializeNetwork(enableNetwork: true)`, but if `BagiraEnvironment.CreateParticipant()` throws (normal in CI/test environments without a DDS daemon), the catch block leaves `_ghostCreationSystem = null`. Fixed by hoisting the assignment to just after `ReplicationLogicModule` is created, before the `try` — safe because `GhostCreationSystem` is a pure ECS object that doesn't depend on DDS.

8. **`TestHook_InjectGeoSpatialDescriptor` requires DDS**: The GeoSpatial ingress translator is wired only when DDS initializes. In `SetViewCommandTests`, removed the GeoSpatial inject step and used `TestHook_SetEntitySimTransform` directly to set world position — functionally equivalent for the camera test.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- The `SelectEntityOnMap` lifecycle filter omission was a latent bug — any code path that selects an entity before ghost promotion would silently fail to deselect previously-selected ghosts. The fix in this batch closes it.
- `InitializeNetwork` initializing `_ghostCreationSystem` only inside the `try` block was fragile. The module system already creates the replication module unconditionally; the system reference should always be available.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- For OC1-G003 `SendPersonalRouteAck`, I followed the established ACK pattern from `MapCommandController` (using `_mapCommandAckWriter?.Write(new MapCommandAck{...})`). Alternative: pass an ACK callback to the async method; rejected as unnecessary coupling.
- `TryTranslateFollowRouteBehaviorParams` is `internal static` per spec, enabling direct unit-test invocation (Scenario 6) without needing a full `MissionControlRequestSystem` instance.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- When DDS initialization fails silently (connection refused), `_ghostCreationSystem` was null. This would cause `TestHook_InjectEntityMasterDescriptor` to throw, masking any test that relied on it. Addressed with the hoisted assignment.
- `SelectEntityOnMap` didn't handle ghost entities in the selection-clear loop. This would cause incorrect multi-selection state in the DDS-disabled test environment and potentially in production for fast-arriving entities.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `TryTranslateFollowRouteBehaviorParams` iterates all entities with `NetworkIdentity` + `RouteTrajectoryCache` to find a matching network ID. At scale (hundreds of routes), a reverse-lookup map (netId → entity) would be more efficient. For current sim sizes this is not a concern.
- `SelectEntityOnMap` now queries with `EntityLifecycle.All`; this is marginally wider than before but correct and still O(entities-with-SelectionState) which is a small hot set.

---

## ⚠️ Outstanding Issues / Next Steps

None. All Phase 0 correctives, debt tasks, Phase 2 (OC1-S001), and Phase 3 (OC1-G001, G002, G003) tasks are complete and green.
