# IOS-BATCH-05-REPORT

**Batch:** IOS-BATCH-05  
**Tasks:** IOS.10.1, IOS.10.2, IOS.10.3, IOS.10.4  
**Status:** ✅ Complete  
**Test Results:** 252 passed / 0 failed (IOS) · 19 passed / 0 failed (SimHost)

---

## Task Summary

### IOS.10.1 — InspectorPanel ✅

**Files:**
- `Bagira.IOS/Panels/InspectorPanel.cs` (new)
- `Bagira.IOS.Tests/InspectorPanelTests.cs` (new, 13 tests)
- `Bagira.IOS/Panels/PanelConstants.cs` — added `InspectorNoSelection`, `InspectorMaxTotalLines`

**Implementation notes:**
- `InspectorLine(Category, Field, Value)` record is the unit of display.
- `BuildDescriptorLines(IDerEntity)` is public and static for direct test invocation.
- Reflection for `GetDescriptor<T>` / `HasDescriptor<T>` is performed **only** inside `NotifySelectionChanged` (on selection change), never inside `Draw`.  Per-type `FieldInfo[]` arrays are memoised in the static `s_fieldCache` dictionary, so the cost of `Type.GetFields(…)` is paid once per descriptor type for the lifetime of the process.
- `MethodInfo` objects for `GetDescriptor` and `HasDescriptor` are stored as `private static readonly` fields — resolved once at class initialisation.
- **IOS-DEBT-029 addressed:** `HasDescriptor<T>()` is called before each `GetDescriptor<T>()` call, even though `GetAllDescriptorTypes()` already only returns types with extant descriptors.  The explicit guard is retained as a defensive invariant that survives future changes to `DerEntity` semantics.
- Total lines capped at `PanelConstants.InspectorMaxTotalLines` (256) to prevent unbounded allocation for pathological entities.

---

### IOS.10.2 — DiagnosticsPanel ✅

**Files:**
- `Bagira.IOS/Panels/DiagnosticsPanel.cs` (new)
- `Bagira.IOS.Tests/DiagnosticsPanelTests.cs` (new, 14 tests)
- `Bagira.IOS/Panels/PanelConstants.cs` — added `DiagnosticsEventRateSampleWindowS`
- `Bagira.IOS/IIosLogic.cs` — added `IRequestTransactionManager TransactionManager { get; }`

**Implementation notes:**
- `GetEntityCount(IDerRepo)` and `GetPendingRequestSnapshot(IRequestTransactionManager)` are public static helpers exercisable without ImGui.
- The rolling event-rate metric uses a simple accumulator reset model: `RecordEvent()` increments a counter; `Update(float dt)` accumulates time and commits `count / elapsed` when the window (`DiagnosticsEventRateSampleWindowS = 5 s`) expires.  Zero-or-negative `dt` values are no-ops, preventing division-by-zero.
- `IRequestTransactionManager TransactionManager` was added to `IIosLogic` so the `Draw` stub (and future live implementation) can access the pending queue through the interface abstraction without casting to the concrete `IosLogic`.  `IosLogic` already exposed this property publicly; no changes to the implementation were required.

---

### IOS.10.3 — Conflict Detection UI ✅

**Files:**
- `Bagira.IOS/Panels/MissionPanel.cs` — added `HandleConflictResult`, `DismissConflict`, `HasConflictAlert`, `ConflictMessage`
- `Bagira.IOS/Services/IMissionEditorService.cs` — added `ErrorCode` to `MissionCommitResult`
- `Bagira.IOS/Services/MissionEditorService.cs` — propagate `ack.ErrorCode` into `MissionCommitResult`
- `Bagira.IOS/Panels/PanelConstants.cs` — added `VersionConflictErrorCode = 7`, `VersionConflictErrorMessage = "ERR_VERSION_CONFLICT"`
- `Bagira.IOS.Tests/MissionPanelTests.cs` — added 9 conflict-detection tests

**Implementation notes:**
- `MissionCommitResult` previously had no `ErrorCode` field; the panel instruction said "intercept `ErrorCode=7`", so `ErrorCode` was added and propagated from `OnAckReceived`.  This is a backward-compatible addition — all existing code that built `MissionCommitResult` via object initialiser still compiles with default `ErrorCode = 0`.
- `HandleConflictResult` checks `!result.Success && result.ErrorCode == PanelConstants.VersionConflictErrorCode` (numeric check, not string matching) — robust against changes to error message text.
- If `ErrorMessage` is null (defensive case), the constant `PanelConstants.VersionConflictErrorMessage` is used as the fallback display string.
- The ImGui modal (`ImGui.OpenPopup` / `ImGui.BeginPopupModal`) is inside the commented Draw stub pending Phase P10 linkage.

---

### IOS.10.4 — Multi-IOS Synchronisation Tests ✅

**Files:**
- `Bagira.IOS.Tests/MultiIosIntegrationTests.cs` (new, 8 tests)

**Test scenarios:**
| Test | Scenario |
|---|---|
| `TwoClients_BothReadSameSnapshotVersion_BeforeAnyCommit` | Precondition: both clients see version 1 |
| `TwoClients_ClientACommitsFirst_ClientBReceivesVersionConflict` | Core conflict scenario |
| `TwoClients_ClientACommitsFirst_ClientBResultHasZeroNewVersion` | Conflict result carries NewVersion=0 |
| `ConflictingClient_MissionPanel_HasConflictAlertAfterHandling` | End-to-end to UI alert |
| `ConflictingClient_SuccessfulClient_MissionPanelNoAlert` | Negative: winner has no alert |
| `ConflictAlert_AfterDismiss_HasConflictAlertIsFalse` | Dismiss clears alert |
| `TwoClients_SequentialCommits_BothSucceed` | Sequential (no conflict) path |
| `TwoClients_DisposedDuringPendingCommit_ResolvesWithFailure` | Dispose safety (IOS-DEBT-032) |

**Design notes:**
- Two `IosClient` instances share one `DerRepo` but each has an independent `MissionEditorService`, `CapturingWriter`, and `ConcurrentEventQueue<MissionControlAck>`.
- The "SimHost" is simulated by the test itself calling `DeliverAck` / `DeliverVersionConflict` on the respective client helper, then calling `Poll()` to drain the ACK queue synchronously.  This makes the interleaving deterministic — no real threads are involved.
- Tests are added to the `[Collection("Integration")]` with `DisableParallelization = true` matching the existing integration test pattern.
- No `ObjectDisposedException` issues were observed because each `IosClient.Dispose()` calls `logic.Dispose()` which flushes orphaned `TaskCompletionSource` instances gracefully (IOS-DEBT-032 already resolved in BATCH-04).

---

## IosMock Wiring

`IosMock` was updated to include `InspectorPanel` and `DiagnosticsPanel` as optional constructor parameters (defaulting to `new InspectorPanel()` / `new DiagnosticsPanel()` if omitted), preserving backward compatibility with all existing tests.

`Update(float dt)` now:
4. Calls `_inspectorPanel.NotifySelectionChanged(selectedEntity)` — O(1) entity lookup from `Repo`.
5. Calls `_diagnosticsPanel.Update(dt)` — constant-time window accumulation.

Both panels are present in the `DrawUI` commented stub alongside the original five panels.

---

## Developer Insights

**Q1: InspectorPanel — how did you avoid GC pauses from reflection during Draw?**

Reflection is isolated to `BuildDescriptorLines`, which is called only from `NotifySelectionChanged` — once per selection change, not per frame.  Two memoisation layers ensure amortised zero cost after warm-up:
1. `FieldInfo[]` per descriptor `Type` is cached in the static `s_fieldCache` dictionary after the first encounter.
2. The `MethodInfo` objects for `GetDescriptor<T>` and `HasDescriptor<T>` are resolved once as `private static readonly` fields at class load time.
The only allocation per selection change is the `List<InspectorLine>` itself (bounded to 256 entries), which is a single heap allocation regardless of descriptor count.  `Draw` reads the already-materialized `_cachedLines` list with a plain `for`-loop — no reflection, no LINQ, no allocations on the hot path.

**Q2: Multi-IOS networking — message reflection or domain separation issues?**

The tests run entirely in-process with no live DDS participants, so there is no actual network, domain ID, or DDS participant management.  The `IDerRepo` is a shared in-memory `ConcurrentDictionary`-backed object, and each client has its own pair of `CapturingWriter<MissionControlRequest>` + `ConcurrentEventQueue<MissionControlAck>`.  The "SimHost" is entirely simulated by the test body delivering ACKs directly into the per-client queues.

One design decision worth noting: in production, two distinct IOS nodes would communicate via DDS on the same domain ID but their `MissionControlRequest` samples would be keyed by `RequestId` (a `Guid`), so there is no structural reflection of one client's requests onto the other client's ACK queue.  The in-process test accurately models this isolation by giving each client its own writer and ACK queue.

**Q3: IOS-DEBT-029 and IOS-DEBT-030 — interaction with InspectorPanel?**

*IOS-DEBT-029* (GetDescriptor returns default(T) for value types without HasDescriptor check) was directly addressed in `BuildDescriptorLines`: an explicit `HasDescriptor<T>()` call precedes every `GetDescriptor<T>()` call even though `GetAllDescriptorTypes()` already guarantees existence.  This makes the invariant visible in code and resilient to future DerEntity implementation changes.  No cascade into other panels was needed; the debt item is now safely contained by the guard.

*IOS-DEBT-030* (`TargetEntityId` type mismatch: `MissionControlRequest` uses `long`, `IDerRepo.GetEntity` uses `int`) was noted but not touched — the InspectorPanel uses `IDerEntity.EntityId` (int) exclusively for cache keying, which does not interact with the long/int mismatch on the request path.  Resolving DEBT-030 would require a coordinated change to `MissionControlRequest`, `IDerRepo`, and `DerRepo` and is deferred as scoped in the debt tracker.

---

## Debt Tracker Updates

| ID | Update |
|---|---|
| IOS-DEBT-029 | Addressed in `InspectorPanel.BuildDescriptorLines`: explicit `HasDescriptor<T>()` guard added before every `GetDescriptor<T>()` call. Remains Open in tracker pending formal TryGet interface work. |

No new debt items introduced.
