# BATCH-05 Report

**Batch:** BATCH-05  
**Developer:** GitHub Copilot  
**Date:** 2025-07-15  
**Status:** Complete

---

## 📊 Task Completion

| Task ID   | Status | Notes |
|-----------|--------|-------|
| PACK-E001 | ✅ Done | `ClusterScenarioPanel` purged of `DdsWriter<ClusterOpRequest>`; `FdpEventBus` + `ClusterOpIntent` pipeline wired |
| PACK-E002 | ✅ Done | `MissionEditorService` purged of `IDdsWriter<MissionControlRequest>` + `IEventQueue<MissionControlAck>`; `FdpEventBus` pipeline wired |

---

## 🧪 Testing Results

**Hrot.ClusterRunner.Tests:** 179 / 182 (3 pre-existing failures — see below)  
**Hrot.ExCon.Tests:** 347 / 347 ✅

**Key Test Scenarios Verified:**

PACK-E001:
- [x] `ClusterScenarioPanelTests` — all DDS-based tests rewritten to consume `ClusterOpIntent` from bus
- [x] `ClusterScenarioPanel.Constructor_NullBus_Throws` — replaces old null-writer test
- [x] `SeekDebounce` tests — assert `ClusterOpIntent` published to bus instead of DDS write
- [x] `LoadScenario`, `LoadIntoLive` tests — check bus payload matches `req.PayloadJson`
- [x] `OrchestratorSubsystemTests.Initialize_Lifecycle_DoesNotThrow` — verifies dead writer removal doesn't break lifecycle

PACK-E002:
- [x] `MissionEditorServiceTests` — all 10 tests rewritten to use `FdpEventBus`; `DrainIntent()` helper extracts `RequestId` via `SwapBuffers` + `ConsumeManaged`
- [x] `WorkflowTests.FullWorkflow_PlacementToMissionCommit_CompletesSuccessfully` — ACK delivered via bus publish + SwapBuffers + Poll
- [x] `ConflictDetectionWorkflowTests` — two separate buses (BusA, BusB) per operator; conflict and sequential commit scenarios both pass
- [x] `IntegrationTests.IosSimHostIntegrationTests` — three ACK tests rewritten to use bus pattern
- [x] `MultiIosIntegrationTests.TwoClients_ClientACommitsFirst_ClientBReceivesVersionConflict` — `IosClient.DeliverVersionConflict()` uses bus pattern; error message `"ERR_VERSION_CONFLICT"` forwarded via error-code mapping in `OnAckReceived`

**Pre-existing failures (documented in BATCH-01-REPORT.md, not introduced by this batch):**
- `OrchestratorSubsystemTests.PauseButton_WhenNotPaused_DispatchesPauseTime` — DDS timing
- `OrchestratorTimeModeTests.PendingTimeMode_Deterministic_PublishesSwitchTimeModeEvent` — DDS timing
- `SwitchTimeModeEchoLoopTests.PollIngress_ThenScanAndPublish_DoesNotEchoBack` — DDS timing

---

## 🏗️ New / Modified Files

### New files
| File | Purpose |
|------|---------|
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs` | Added `ClusterOpIntent` class (`[EventId(9018)]`) |
| `Hrot.Common/Orchestration/ClusterOpEgressTranslator.cs` | ACL translator: consumes `ClusterOpIntent` from bus → writes `ClusterOpRequest` DDS |
| `Hrot.Common/Events/MissionControlCqrsEvents.cs` | `MissionControlIntent` + `MissionControlAckEvent` moved here from `Hrot.SimHost.Events` |
| `Hrot.ExCon/Services/MissionControlEgressTranslator.cs` | ACL translator: consumes `MissionControlIntent` from bus → writes `MissionControlRequest` DDS |
| `Hrot.ExCon/Services/MissionControlAckIngressTranslator.cs` | ACL translator: reads `MissionControlAck` DDS → publishes `MissionControlAckEvent` to bus |

### Modified files
| File | Change |
|------|--------|
| `Hrot.ClusterRunner/Services/ClusterScenarioPanel.cs` | `DdsWriter<ClusterOpRequest>` → `FdpEventBus`; `SendRequest` publishes `ClusterOpIntent` |
| `Hrot.ClusterRunner/Services/ExConSubsystem.cs` | Wired `ClusterOpEgressTranslator` + `MissionControlEgress/IngressTranslator`; `MissionEditorService` now takes bus |
| `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` | Removed dead `_sysOpWriter` creation and disposal |
| `Hrot.SimHost/Events/MissionControlCqrsEvents.cs` | Replaced type definitions with `global using` re-exports pointing to `Hrot.Common.Events` |
| `Hrot.ExCon/Services/IMissionEditorService.cs` | Removed `OnAckReceived(MissionControlAck)` (no longer part of public contract) |
| `Hrot.ExCon/Services/MissionEditorService.cs` | Full rewrite: `_requestWriter` + `_ackQueue` → `FdpEventBus`; `Poll` uses `_bus.Consume<MissionControlAckEvent>()` |
| `Hrot.ClusterRunner.Tests/ClusterScenarioPanelTests.cs` | All DDS-based assertions replaced with bus-intent assertions |
| `Hrot.ClusterRunner.Tests/OrchestratorSubsystemTests.cs` | Dead writer test replaced with lifecycle smoke test |
| `Hrot.ExCon.Tests/MissionEditorServiceTests.cs` | `CapturingWriter` → `FdpEventBus`; `MissionControlAck` → `MissionControlAckEvent` |
| `Hrot.ExCon.Tests/WorkflowTests.cs` | `WorkflowFixture` + `TwoOperatorFixture`: writers/queues → buses; dispose tests updated |
| `Hrot.ExCon.Tests/IntegrationTests.cs` | `SimHostStub`: `AckQueue` → `FdpEventBus MissionBus`; inline ACK tests rewritten |
| `Hrot.ExCon.Tests/MultiIosIntegrationTests.cs` | `IosClient`: `RequestWriter` + `AckQueue` → `FdpEventBus Bus` |

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **`replace_string_in_file` produced duplicate class body in `MissionEditorService.cs`:**  
   Using the top `using` block alone as `oldString` caused the tool to insert the new full class ABOVE the old one rather than replacing it. The old class body remained below, causing `CS0101` duplicate-type errors. Resolved by restoring the original from `git checkout HEAD` and then applying a set of targeted partial replacements (one per logical section: usings, class doc, fields, constructor, methods, etc.) instead of one large substitution.

2. **`Set-Content` emptied the file inadvertently:**  
   An attempt to truncate the file using PowerShell `Get-Content | Select-Object -First N | Set-Content` inside a compound statement failed silently, leaving an empty file. Detected via immediate line-count check. Fixed by restoring from git and taking the safer multi-replace approach.

3. **`ClusterOpType` dual-namespace ambiguity:**  
   `FDP.Toolkit.Orchestration.ClusterOpType` and `Hrot.NED.Descriptors.Orchestration.ClusterOpType` collide when both namespaces are in scope. Resolved by NOT importing `FDP.Toolkit.Orchestration` wholesale; instead using targeted type aliases `using FdpClusterOpType = FDP.Toolkit.Orchestration.ClusterOpType;` in `ClusterScenarioPanel.cs` and its tests.

4. **`MissionControlAckEvent.ErrorMessage` absent — version-conflict test assertions would fail:**  
   The `MissionControlAckEvent` struct (unmanaged, for `bus.Consume<T>`) has no `ErrorMessage` string field. Yet tests and `MissionPanel` both rely on the exact string `"ERR_VERSION_CONFLICT"` from the result's `ErrorMessage`. Resolved by adding an error-code → message mapping in `MissionEditorService.OnAckReceived` using `PanelConstants.VersionConflictErrorCode` / `PanelConstants.VersionConflictErrorMessage` (same project, no layer violation), so error code 7 produces the expected string.

5. **`IMissionEditorService.OnAckReceived(MissionControlAck)` — interface/implementation mismatch:**  
   After removing the DDS `OnAckReceived` from the implementation, CS0535 appeared. Removed `OnAckReceived(MissionControlAck)` from the interface entirely since ACK delivery is now internal to the bus pipeline (the `internal void OnAckReceived(MissionControlAckEvent)` helper is test-accessible but not part of the public contract).

**Q2: Were there any deviations from the batch spec? If so, why?**

- `MissionControlAckEvent` was specified as a struct. Kept as struct (unmanaged). Added error-code mapping in `OnAckReceived` to preserve the semantic `ErrorMessage` string for known error codes rather than a raw `"Error 7"` fallback. This keeps existing test expectations intact without adding a string field to the struct.
- `IMissionEditorService.OnAckReceived(MissionControlAck)` was not mentioned in the spec as requiring removal, but it was a direct consequence of replacing the DDS type at the service boundary. Removed it to eliminate the compile error.

**Q3: What is the overall quality of the solution?**

Both PACK-E001 and PACK-E002 are complete. The DDS boundary has been pushed outward to dedicated translator classes (`ClusterOpEgressTranslator`, `MissionControlEgressTranslator`, `MissionControlAckIngressTranslator`). Both `ClusterScenarioPanel` and `MissionEditorService` now contain zero DDS or `JsonSerializer` references. All 347 ExCon tests and 179/182 ClusterRunner tests pass.
