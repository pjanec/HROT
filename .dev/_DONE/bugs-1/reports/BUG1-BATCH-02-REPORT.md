# BUG1-BATCH-02 Report

**Batch:** BUG1-BATCH-02  
**Developer:** GitHub Copilot  
**Date:** 2025-07-23  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| BUG1-T001 | ✅ Complete | `IDdsWriter<UpdateEntityDescriptorAck>` optional param added to constructor; `DdsWriterAdapter` wraps live writer; ownership tracked via `_ownedAckWriter` field |
| BUG1-T002 | ✅ Complete | `egressTranslators` list separated in `SimHostApp.OnLoad()`; only egress passed to `CycloneNetworkCleanupSystem` |
| BUG1-T003 | ✅ Complete | `_nodeIdOverride` field added to `IosSubsystem`; stored from `config.NodeId` in `Initialize()`; `TestHook_NodeIdOverride` exposed |
| BUG1-T004 | ✅ Complete | Two distinct root causes fixed — see Q5 below |
| BUG1-I001 | ✅ Complete | `ContinuousDragUpdates` flag + `_continuousDragTimer` throttle + `IBdcCommandGateway` interface + `SendWorldPosUpdate` refactor |
| BUG1-M001 | ✅ Complete | `HandleAddTask()` now seeds `Triggers = [{ Type = "BehaviorFinished" }]` on every new task |
| BUG1-M002 | ✅ Complete | `SendControlCommandAsync` added to `IMissionEditorService`/`MissionEditorService`; `HandleJump`/`HandleAbort` now use the async path and set `_commitInFlight = true` |

---

## 🧪 Testing Results

**Unit Tests Passed:** 1015 / 1015  
**Integration Tests Passed:** (N/A — integration tests not run in this batch)

### Test Suite Breakdown

| Project | Before | After | Delta |
|---|---|---|---|
| `Hrot.ExCon.Tests` | 263 | 274 | +11 (3 CtxMenu fixed, 4 MissionPanel updated, 4 new) |
| `Hrot.IG.Tests` | 311 | 315 | +4 new `ContinuousDragTests` |
| `Hrot.ClusterRunner.Tests` | 111 | 112 | +1 new `IosSubsystem.Initialize_StoresNodeIdFromConfig` |
| `Hrot.SimHost.Tests` | 261 | 263 | +2 new `InjectedAckWriter_*` tests |
| `Hrot.Map.Common.Tests` | 51 | 51 | unchanged |

**Key Test Scenarios Verified:**
- ✅ `InjectedAckWriter_NotAuthoritative_WriterNotCalled` — non-auth entity triggers zero writes on injected stub
- ✅ `InjectedAckWriter_Authoritative_WriterCalledWithSuccessAck` — auth entity triggers exactly one write
- ✅ `Initialize_StoresNodeIdFromConfig` — `IosSubsystem` stores `SubsystemConfig.NodeId` 
- ✅ `ContinuousDragOff_RepeatMoves_NoGatewayCalls` — 20 moves with flag off → 0 gateway calls
- ✅ `ContinuousDragOn_CallsFiredAtThreshold` — timer crosses 0.1 s on 4th frame → 1 call
- ✅ `DragEnd_AlwaysSendsExactlyOneUpdate` — drop always issues exactly one call
- ✅ `DragEnd_ResetsContinuousDragTimer` — timer zeroed after drag end
- ✅ `AddTask_NewTask_HasBehaviorFinishedTrigger` — new task has exactly one "BehaviorFinished" trigger
- ✅ `AddTask_MultipleTasksEach_HaveBehaviorFinishedTrigger` — all new tasks have the trigger
- ✅ `HandleJump_WithSelection_SetsCommitInFlight` — Jump sets `CommitInFlight = true`
- ✅ `HandleAbort_WithSelection_SetsCommitInFlight` — Abort sets `CommitInFlight = true`
- ✅ All 6 previously-failing `Hrot.IG.Tests` now pass (311 → 315)
- ✅ 3 pre-existing `ContextMenuLogicTests` failures fixed (Delete moved to Standard in BATCH-01; tests updated to match)
- ✅ 4 `MissionPanelTests` updated: sync `SendControlCommand` → async `SendControlCommandAsync` assertions

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **`_ownedAckWriter` ownership pattern (T001):** The constructor originally called `_ackWriter.Dispose()` in `OnDestroy`. With injection, the stub must not be disposed by the system (caller owns it). Solution: added `_ownedAckWriter` field — only populated when the system creates its own writer — and redirected `OnDestroy` to `_ownedAckWriter?.Dispose()`.

2. **`_geoTransform` creation timing in IgApplication (I001):** `SendWorldPosUpdate` needs `_geoTransform`, but it was previously created inside the DDS try-catch block. This meant it could be null when the command gateway is injected via `TestHook_SetCommandGateway` in a headless test where DDS fails. Fixed by hoisting `_geoTransform = new WGS84Transform()` to before the try block.

3. **Floating-point precision in `ContinuousDragOn_CallsFiredAtThreshold` (I001 test):** Initially used `Dt = 1f / 30f`. Due to float rounding, `3 × (1f/30f) = 0.100000005f`, which crossed the 0.1 s threshold on the 3rd call instead of the 4th. Fixed by using `const float Dt = 0.033f` to make the arithmetic analytically exact.

4. **`ContextMenuLogicTests` were pre-existing failures, not caused by this batch.** Confirmed via `git stash` before implementing changes — all 3 tests failed on the original codebase. The `Delete` action had been moved to `Standard` strategy in BATCH-01, but the tests still expected it in `Admin`. Updated the test assertions to reflect the post-BATCH-01 state.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

1. **`SimHostApp.OnLoad()` — no test coverage for translator separation.** The `CycloneNetworkCleanupSystem` receives only egress translators, but there's no automated test that verifies this. A test that counts `Dispose()` calls via a mock translator list would prevent regression.

2. **`IosSubsystem` doesn't fully thread `NodeId` into `IosMock`.** The `_nodeIdOverride` is stored, but `IosApplication` / `IosMock` currently doesn't use it. The plumbing is done at the `IosSubsystem` level, but the next step would be to pass it into `IosMock.InitializeEmbedded` when that API is ready.

3. **`MissionEditorService.SendControlCommandAsync` reuses the base-version-0 convention** from the design spec, but there's no integration test covering the full ACK round-trip through `OnAckReceived`. The unit test verifies the in-flight state, but doesn't exercise the TCS resolution path.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

1. **`IBdcCommandGateway` interface (I001):** The spec said "inject a testable interface" without specifying what to name it. Chose `IBdcCommandGateway` and placed it in `Hrot.Map.Common.Commands` alongside `BdcCommandGateway` to keep the interface/implementation co-located and match the existing naming convention. An alternative was `ISendUpdateDescriptor` (single-method interface per ISP), which would have been marginally lighter, but the existing `BdcCommandGateway` already had multiple methods, so grouping them under one interface was more pragmatic.

2. **`_commandGatewayInterface` field + `TestHook_SetCommandGateway`:** Rather than replacing `_commandGateway` directly (which carries ownership of DDS resources), added a parallel `_commandGatewayInterface` field that is set to `_commandGateway` in production but can be overridden in tests. This separates testability concern from the ownership/lifecycle concern.

3. **`SendWorldPosUpdate` extracted as a shared helper (I001):** Both `OnEntityDragEnded` and the continuous-drag throttle path need to write a WorldPos update. Without extraction, this would be duplicated code. The alternative (inline at both call sites) would have violated DRY but been slightly simpler to trace. Extraction wins for maintainability.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

1. **`HandleAddTask` when `SelectedEntityId == 0`:** `EnsureDraftForEdit` returns false early, so the test `AddTask_AppendsToDraftPlan` works because it sets `SelectedEntityId = 1`. No issue here but worth noting: the BehaviorFinished trigger is only added when the edit can proceed.

2. **`HandleAbort`/`HandleJump` with `SelectedEntityId == 0`:** The guarded path short-circuits before calling `SendControlCommandAsync`, so `CommitInFlight` stays false. This is correct — verified by updating `HandleAbort_NoSelection_DoesNotCallService` and `HandleJump_NoSelection_DoesNotCallService` to check `SendControlCommandAsync` instead.

3. **Timer pre-seeding edge case (I001):** If `TestHook_ContinuousDragTimer` is seeded to 0.09 and `ContinuousDragUpdates = true`, a single `TestHook_SimulateEntityMoved` with any `dt >= 0.01` will trigger a send. This is correct and was explicitly validated by `DragEnd_ResetsContinuousDragTimer`.

**Q5: What were the exact root causes of the 6 failing IG tests?**

**Root cause 1 — `EditToolTests` (×4) and `AdvancedFeaturesIntegrationTests` (×1):**  
File: `Hrot.IG/Tools/EditTool.cs`, method `HandleDrag`.  
The null-check guard was written as:
```csharp
if (_canvas?.Input.IsMouseButtonDown(MouseButton.Left) != true) return false;
```
When `_canvas` is `null` (headless tests never assign a canvas), the null-conditional evaluates to `null`. The comparison `null != true` evaluates to `true`, so the method unconditionally returned `false` — causing every drag-related test to report "drag did nothing".  

**Fix:**
```csharp
if (_canvas != null && !_canvas.Input.IsMouseButtonDown(MouseButton.Left))
    return false;
```
Semantics: when `_canvas` is null (headless), skip the mouse-button guard entirely.

**Root cause 2 — `TraceLoggingTests.IngressAndRender_EmitsTraceLines` (×1):**  
File: `Hrot.Map.Common/Replication/Ingress/WorldPosIngressTranslator.cs`, `OnSample` method.  
The `[TRACE-IG] Ingress: WorldPos Entity=` log line was commented out with `//`. The test waited for that log line to appear (using NLog memory target) and timed out because it was never emitted.  

**Fix:** Uncommented the `FdpLog<WorldPosIngressTranslator>.Debug(...)` call.

---

## ⚠️ Outstanding Issues / Next Steps

- `IosSubsystem._nodeIdOverride` is stored but not yet plumbed into the `IosMock.InitializeEmbedded` call. This is safe for now because `IosMock` doesn't yet expose a NodeId parameter, but will need wiring when that API is added.
- `SendControlCommandAsync` uses `BaseVersion = 0` per design spec, which bypasses OCC for control commands. If the server ever introduces version-checking for Jump/Abort, this will need revisiting.
- No integration test covering the full `HandleAbort → SendControlCommandAsync → OnAckReceived → CommitInFlight = false` round-trip.
