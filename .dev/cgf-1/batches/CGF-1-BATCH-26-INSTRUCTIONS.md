# CGF-1-BATCH-26: ImGui window overhaul + real network dispatch

**Batch number:** CGF-1-BATCH-26  
**Goal:** Implement **Phase 5 tasks S0501 + S0502** — the Orchestrator ImGui window overhaul
(beige title bar, `Begin`/`End` wrapper, banner, 2PC history upgrade, `DistributedTransaction`
extensions) and the **critical network dispatch fix** (DDS `SysOpRequest` writer, swap panel
from `HandleSysOpRequest` to writer, fan-out `PrepareXxx`/`CommitState` loop in `DrillMaster`).  
**Phase:** Phase 5 — Operational UI, Real Network Dispatch & CQRS Architecture  
**Estimated effort:** 6–10 h  
**Priority:** P1 — S0502 is a critical bug; all cluster buttons are currently a no-op on the network  
**Dependencies:** CGF1-S0106 complete (existing `OrchestratorScenarioPanel`); BATCH-25 approved  
**Design authority:** [CGF-1-ADDENDUM-3.md §2](../CGF-1-ADDENDUM-3.md#2-orchestrator-imgui-window--2pc-history-overhaul)
and [§3](../CGF-1-ADDENDUM-3.md#3-real-network-dispatch-fix)

---

## Onboarding

1. [CGF-1-ADDENDUM-3.md](../CGF-1-ADDENDUM-3.md) — read §1 (motivation), §2, §3 before touching any file  
2. [CGF-1-TASK-DETAIL.md §CGF1-S0501 and §CGF1-S0502](../CGF-1-TASK-DETAIL.md)  
3. [`Bagira.Runner/Services/OrchestratorSubsystem.cs`](../../../Bagira.Runner/Services/OrchestratorSubsystem.cs)  
4. [`Bagira.Runner/Services/OrchestratorScenarioPanel.cs`](../../../Bagira.Runner/Services/OrchestratorScenarioPanel.cs)  
5. [`Bagira.Orchestrator/DistributedTransaction.cs`](../../../Bagira.Orchestrator/DistributedTransaction.cs)  
6. [`Bagira.Orchestrator/DrillMaster.cs`](../../../Bagira.Orchestrator/DrillMaster.cs) — read `ProcessSingleSysOpRequest` and `FanOutNodeOp` before writing the fan-out loop  
7. [`Bagira.Orchestrator.Tests/`](../../../Bagira.Orchestrator.Tests/) — existing test coverage to stay green  

**Report file to create:** `.dev/cgf-1/reports/CGF-1-BATCH-26-REPORT.md`

---

## Part A — S0501: Orchestrator ImGui Window & 2PC History Overhaul

### A.1 — Beige title bar  (`OrchestratorSubsystem.cs`)

Change the `TitleBarColor` property from the current dark-blue vector to the beige vector:

```csharp
// Before
public System.Numerics.Vector4 TitleBarColor => new(0.12f, 0.18f, 0.42f, 1f);

// After
public System.Numerics.Vector4 TitleBarColor => new(0.72f, 0.64f, 0.47f, 1f);
```

**Acceptance:** Unit test `OrchestratorSubsystemTests.TitleBarColor_IsBeige` asserts
`Math.Abs(sub.TitleBarColor.X - 0.72f) < 0.001f`.

---

### A.2 — `ImGui.Begin` / `ImGui.End` wrapper  (`OrchestratorSubsystem.cs`)

Wrap the entire body of `DrawUI()` in an `ImGui.Begin("Orchestrator")` block.  
The method currently starts with `if (_drillMaster == null) return;` — keep that guard,
then add the window wrapper immediately after:

```csharp
public void DrawUI()
{
    if (_drillMaster == null) return;
    if (!ImGui.Begin("Orchestrator")) { ImGui.End(); return; }

    // ... existing body unchanged ...

    _scenarioPanel?.Render();
    ImGui.End();
}
```

**Acceptance:** `OrchestratorSubsystemTests.DrawUI_HasImGuiBeginEndWrapper` verifies
`ImGui.Begin` is called exactly once with `"Orchestrator"` and `ImGui.End` is called
once per `DrawUI` invocation.

---

### A.3 — Remove `BeigeChildBg` from `OrchestratorScenarioPanel.cs`

The beige colour is now applied by the enclosing ImGui window (via `SubsystemOrchestrator`'s
title-bar push); the per-child pushes are redundant and bleed into nested areas.

- Delete `private static readonly Vector4 BeigeChildBg = new(0.72f, 0.64f, 0.47f, 1f);`
- Remove every `ImGui.PushStyleColor(ImGuiCol.ChildBg, BeigeChildBg)` call (there are
  exactly 6 — one per `Render*` helper).
- Remove every matching `ImGui.PopStyleColor()` call that immediately follows an
  `ImGui.EndChild()` in those same helpers.

**Do not** touch any other `PushStyleColor`/`PopStyleColor` calls (there may be others for
text colour, button colour, etc.).

**Acceptance:** `grep "BeigeChildBg" OrchestratorScenarioPanel.cs` returns zero matches.

---

### A.4 — New fields on `DistributedTransaction`  (`DistributedTransaction.cs`)

Add three properties to the `DistributedTransaction` class:

```csharp
/// <summary>DSM state the cluster was in immediately before this transaction started.</summary>
public DSMState SourceDsmState { get; set; }

/// <summary>The SysOpRequest JSON payload that initiated this transaction.</summary>
public string PayloadJson { get; set; } = string.Empty;

/// <summary>Per-node ResultJson from each node's final NodeOpStatus ACK, keyed by node ID.</summary>
public Dictionary<int, string> NodeResponses { get; } = new();
```

No other changes to this file.

**Acceptance:** `DistributedTransactionTests.NewTransaction_HasDefaultValues` asserts
`SourceDsmState == DSMState.Standby`, `PayloadJson == ""`, `NodeResponses.Count == 0`.

---

### A.5 — Populate new fields in `DrillMaster.cs`

Three specific changes in `ProcessSingleSysOpRequest` and `ConsumeNodeOpStatuses`:

**Change 1 — capture source state before optimistic advance.**

In `ProcessSingleSysOpRequest`, the existing code has:
```csharp
// Capture current state before optimistic advance (needed for S0305 detection).
var stateBeforeAdvance = _currentDsmState;
```

This variable exists already. When the new `DistributedTransaction` object is created
(search for `var tx = new DistributedTransaction` or `_activeTransaction = new`):
- Add `SourceDsmState = stateBeforeAdvance` (or `_currentDsmState` captured at the
  top of the `TransitionState` branch, before any mutation).
- Add `PayloadJson = req.PayloadJson ?? string.Empty`.

**Change 2 — populate `NodeResponses` on each ACK.**

In `ConsumeNodeOpStatuses`, find the loop over `_nodeOpStatusReader.Take()`.
After the existing handlers for `_pendingBranchTasks`, `_pendingManageStoryTasks`, and
`_pendingSerializeTasks` — for **all** ACKs that reach a non-null `_activeTransaction`
matching the `status.TransactionId` — add:

```csharp
_activeTransaction.NodeResponses[status.NodeId] = status.ResultJson ?? string.Empty;
```

Place this immediately before or after the existing `NodeAckLatencyMs` population line
(which already tracks per-node latency).  The correct spot is wherever the code
confirms `status.TransactionId == _activeTransaction?.TransactionId`.

> **Note:** You need to determine the exact location by reading the existing
> `ConsumeNodeOpStatuses` method.  Look for where `_activeTransaction` is referenced;
> add the `NodeResponses` update alongside it.  If there is no current `_activeTransaction`
> tracking in that method (only `_pendingSerializeTasks`), add a general fallback:
> after all the task-specific `continue` paths, if `_activeTransaction?.TransactionId == status.TransactionId`,
> record the response.

**Acceptance:**
- `DistributedTransactionTests.SourceDsmState_CapturedBeforeOptimisticAdvance`:
  Start `DrillMaster` in `Standby`; call `HandleSysOpRequest(TransitionState → LoadingLive)`;
  assert `TransactionHistory.First().SourceDsmState == DSMState.Standby`.
- `DistributedTransactionTests.PayloadJson_PopulatedFromSysOpRequest`:
  Send a request with `PayloadJson = "{\"TargetState\":2}"`;
  assert `TransactionHistory.First().PayloadJson == "{\"TargetState\":2}"`.

---

### A.6 — Overhaul 2PC History table in `OrchestratorSubsystem.cs`

Replace the existing 4-column `BeginTable("TxHistory", 4, ...)` block (roughly lines
180–210) with a 5-column scrollable table.

**New table setup:**
```csharp
float rowHeight = ImGui.GetTextLineHeightWithSpacing();
if (ImGui.BeginTable("TxHistory", 5,
        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
        ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
        new Vector2(0, rowHeight * 11.5f)))
{
    ImGui.TableSetupScrollFreeze(0, 1);
    ImGui.TableSetupColumn("TransactionId");
    ImGui.TableSetupColumn("Target State");
    ImGui.TableSetupColumn("Result");
    ImGui.TableSetupColumn("ACK Latency (ms)");
    ImGui.TableSetupColumn("Payload");
    ImGui.TableHeadersRow();

    foreach (var tx in history)
    {
        ImGui.TableNextRow();

        // Column 1: full GUID as a TreeNode for expandability
        ImGui.TableNextColumn();
        bool open = ImGui.TreeNodeEx(tx.TransactionId.ToString(),
            ImGuiTreeNodeFlags.SpanFullWidth);

        // Context menu on row
        if (ImGui.BeginPopupContextItem($"ctx_{tx.TransactionId}"))
        {
            string line = $"{tx.TransactionId} | {tx.TargetDsmState} | " +
                          $"{(tx.IsAborted ? "Aborted" : "Completed")} | {tx.PayloadJson}";
            if (ImGui.MenuItem("Copy line to clipboard"))
                ImGui.SetClipboardText(line);
            ImGui.EndPopup();
        }

        // Column 2: target state
        ImGui.TableNextColumn(); ImGui.Text(tx.TargetDsmState.ToString());

        // Column 3: result
        ImGui.TableNextColumn(); ImGui.Text(tx.IsAborted ? "Aborted" : "Completed");

        // Column 4: aggregate ACK latency summary
        string latency = tx.NodeAckLatencyMs.Count == 0
            ? "—"
            : string.Join(", ", tx.NodeAckLatencyMs.Select(kv => $"{kv.Key}:{kv.Value:F0}ms"));
        ImGui.TableNextColumn(); ImGui.Text(latency);

        // Column 5: payload snippet with tooltip
        ImGui.TableNextColumn();
        string payloadSnippet = tx.PayloadJson.Length > 25
            ? tx.PayloadJson[..25] + "..."
            : tx.PayloadJson;
        ImGui.TextUnformatted(payloadSnippet);
        if (!string.IsNullOrWhiteSpace(tx.PayloadJson) && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(FormatPrettyJson(tx.PayloadJson));
            ImGui.EndTooltip();
        }

        // Expanded rows: one child row per NodeResponse entry
        if (open)
        {
            foreach (var nr in tx.NodeResponses)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TreeNodeEx($"↳ Node {nr.Key}",
                    ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen |
                    ImGuiTreeNodeFlags.SpanFullWidth);
                ImGui.TableNextColumn(); ImGui.Text("—");
                ImGui.TableNextColumn(); ImGui.Text("—");
                ImGui.TableNextColumn();
                string nodeLatency = tx.NodeAckLatencyMs.TryGetValue(nr.Key, out float ms)
                    ? $"{ms:F0}ms" : "—";
                ImGui.Text(nodeLatency);
                ImGui.TableNextColumn();
                string nodePayloadSnippet = nr.Value.Length > 25
                    ? nr.Value[..25] + "..."
                    : nr.Value;
                ImGui.Text(nodePayloadSnippet);
            }
            ImGui.TreePop();
        }
    }

    ImGui.EndTable();
}
```

**Add the `FormatPrettyJson` helper as a private static method of `OrchestratorSubsystem`:**

```csharp
private static string FormatPrettyJson(string json)
{
    if (string.IsNullOrWhiteSpace(json)) return string.Empty;
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return System.Text.Json.JsonSerializer.Serialize(doc,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
    catch { return json; }
}
```

**Acceptance:**
- `OrchestratorSubsystemTests.TxHistory_Table_Has5Columns` — headless ImGui test confirms 5 columns rendered.
- `OrchestratorSubsystemTests.FormatPrettyJson_IndentsJson` — unit test: `FormatPrettyJson("{\"a\":1}")` returns a string containing `"\n"` (indented output).
- `OrchestratorSubsystemTests.FormatPrettyJson_InvalidJson_ReturnsOriginal` — `FormatPrettyJson("not-json")` returns `"not-json"`.

---

### A.7 — Source→Target transition banner (`OrchestratorScenarioPanel.cs`)

Update `RenderStatusBanner` to display the transition direction when a transaction is in flight:

```csharp
if (hasInFlight && activeTx != null &&
    activeTx.SourceDsmState != activeTx.TargetDsmState)
{
    ImGui.Text($"State: {activeTx.SourceDsmState} → {activeTx.TargetDsmState}");
}
else
{
    ImGui.Text($"State: {currentState}");
}
```

Replace the existing single `ImGui.Text($"State: {currentState}");` line.

**Acceptance:**
- `OrchestratorScenarioPanelTests.StatusBanner_ShowsSourceArrowTarget_WhenInFlight`:
  Pass a mock `activeTx` with `SourceDsmState = Standby`, `TargetDsmState = RunningLive`,
  `hasInFlight = true`; assert the rendered text contains `"Standby → RunningLive"`.

---

## Part B — S0502: Real Network Dispatch + DrillMaster Fan-out

### B.1 — `DdsWriter<SysOpRequest>` in `OrchestratorSubsystem.cs`

**Add field:**
```csharp
private DdsWriter<SysOpRequest>? _sysOpWriter;
```

**In `Initialize`** (after `_participant` is created, before `_scenarioPanel`):
```csharp
_sysOpWriter = new DdsWriter<SysOpRequest>(_participant);
```

**In `Shutdown`** (before `_participant?.Dispose()`):
```csharp
_sysOpWriter?.Dispose();
_sysOpWriter = null;
```

**Pass to panel constructor** — change the existing `new OrchestratorScenarioPanel(_drillMaster)` to:
```csharp
_scenarioPanel = new OrchestratorScenarioPanel(_drillMaster, _sysOpWriter!);
```

**Fix the TODO buttons** — replace the three button stubs in `DrawUI()`:
```csharp
// Before
if (ImGui.Button("Initialize Live"))  { /* TODO: S0201 SysOpRequest */ }
ImGui.SameLine();
if (ImGui.Button("Pause"))            { /* TODO: S0201 SysOpRequest */ }
ImGui.SameLine();
if (ImGui.Button("Resume"))           { /* TODO: S0201 SysOpRequest */ }

// After
if (ImGui.Button("Initialize Live") && _sysOpWriter != null)
    _sysOpWriter.Write(new SysOpRequest
    {
        RequestId     = Guid.NewGuid(),
        OperationType = SysOpType.TransitionState,
        PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingLive}}}",
    });
ImGui.SameLine();
if (ImGui.Button("Pause") && _sysOpWriter != null)
    _sysOpWriter.Write(new SysOpRequest
    {
        RequestId     = Guid.NewGuid(),
        OperationType = SysOpType.PauseTime,
        PayloadJson   = string.Empty,
    });
ImGui.SameLine();
if (ImGui.Button("Resume") && _sysOpWriter != null)
    _sysOpWriter.Write(new SysOpRequest
    {
        RequestId     = Guid.NewGuid(),
        OperationType = SysOpType.ResumeTime,
        PayloadJson   = string.Empty,
    });
```

**Acceptance:** `OrchestratorSubsystemTests.Shutdown_DisposesWriter` — call `Initialize` then
`Shutdown`; assert `_sysOpWriter` is null and no `ObjectDisposedException` is thrown.

---

### B.2 — Update `OrchestratorScenarioPanel` constructor + swap all calls

**Constructor change:**
```csharp
private readonly DdsWriter<SysOpRequest> _sysOpWriter;

public OrchestratorScenarioPanel(DrillMaster drillMaster, DdsWriter<SysOpRequest> sysOpWriter)
{
    _drillMaster = drillMaster ?? throw new ArgumentNullException(nameof(drillMaster));
    _sysOpWriter = sysOpWriter ?? throw new ArgumentNullException(nameof(sysOpWriter));
}
```

**Replace every `_drillMaster.HandleSysOpRequest(...)` call** in the six render helpers.
The exact current calls are (line numbers approximate):

| Helper | Current call | Replace `_drillMaster.HandleSysOpRequest(req)` with |
|--------|-------------|------------------------------------------------------|
| `RenderDrillControl` | target-state button | `_sysOpWriter.Write(req)` |
| `RenderCheckpointSection` | "Take Checkpoint" | `_sysOpWriter.Write(req)` |
| `RenderScenarioSection` | "Save Scenario" | `_sysOpWriter.Write(req)` |
| `RenderScenarioSection` | "Load into Edit" | `_sysOpWriter.Write(req)` |
| `RenderScenarioSection` | "Load into Live" | `_sysOpWriter.Write(req)` |
| `RenderReplaySection` | "Load Replay" | `_sysOpWriter.Write(req)` |
| `RenderReplaySection` | seek slider | `_sysOpWriter.Write(req)` |
| `RenderStoriesSection` | "Unload" per story | `_sysOpWriter.Write(req)` |
| `RenderStoriesSection` | "Inject Story" | `_sysOpWriter.Write(req)` |

**Important:** The `SysOpRequest` objects being constructed are _identical_ to the
current ones — only the dispatch mechanism changes (from `_drillMaster.HandleSysOpRequest`
to `_sysOpWriter.Write`).  Do not change any `RequestId`, `OperationType`, or
`PayloadJson` fields.

The `_drillMaster` field is still needed for `ActiveStories`, `GetReachableTargets()`,
and state reads — do **not** remove it.

**Acceptance:** `grep "HandleSysOpRequest" OrchestratorScenarioPanel.cs` returns zero matches.

---

### B.3 — Fan-out loop in `DrillMaster.ProcessSingleSysOpRequest`

This is the critical fix.  Currently, after `_currentDsmState` is advanced optimistically
and the transaction is recorded, no `NodeOpCommand` DDS messages are ever sent.  Nodes
remain unaware of any state transition.

**Add the fan-out block** immediately after the new transaction is stored in
`_activeTransaction` (or in the transaction history ring buffer), **within** the
`SysOpType.TransitionState` branch, **after** all the existing per-step logic
(PrefetchScenario, TimeMode, live-from-replay):

```csharp
// S0502: Fan out PrepareXxx + CommitState commands to all active nodes.
var activeNodeIds = new List<int>(_roster.ActiveNodes.Keys);
if (activeNodeIds.Count > 0)
{
    foreach (var step in trajectory)
    {
        if (step is TransitionStep tStep)
        {
            NodeOpType prepareOp = tStep.TargetState switch
            {
                DSMState.LoadingLive     => NodeOpType.PrepareLive,
                DSMState.UnloadingLive   => NodeOpType.FinalizeLive,
                DSMState.LoadingReplay   => NodeOpType.PrepareReplay,
                DSMState.UnloadingReplay => NodeOpType.FinalizeReplay,
                DSMState.LoadingEdit     => NodeOpType.PrepareEdit,
                DSMState.UnloadingEdit   => NodeOpType.FinalizeEdit,
                _                       => NodeOpType.PrepareState,
            };

            FanOutNodeOp(new NodeOpCommand
            {
                TransactionId = tx.TransactionId,
                Operation     = prepareOp,
                PayloadJson   = req.PayloadJson ?? string.Empty,
            }, activeNodeIds);

            FanOutNodeOp(new NodeOpCommand
            {
                TransactionId = tx.TransactionId,
                Operation     = NodeOpType.CommitState,
                PayloadJson   = ((int)tStep.TargetState).ToString(),
            }, activeNodeIds);
        }
        else if (step is OperationStep opStep &&
                 opStep.Operation == SysOpType.ReplaySeek)
        {
            FanOutNodeOp(new NodeOpCommand
            {
                TransactionId = tx.TransactionId,
                Operation     = NodeOpType.NodeReplaySeek,
                PayloadJson   = opStep.PayloadJson,
            }, activeNodeIds);
        }
    }
}
```

**Where `tx` is the variable name used for the new `DistributedTransaction`** in that
scope.  Read the existing code to find its exact name — it may be `_activeTransaction`,
`tx`, or similar.

**Important guards:**
- This block must **only** run when `req.OperationType == SysOpType.TransitionState`
  (it is already inside the `if (req.OperationType == SysOpType.TransitionState)` branch).
- The S0305 live-from-replay path already fans out its own `PrepareLive` with a branched
  DrillId; do **not** add the new fan-out inside the S0305 guard block (it is already
  inside a `passesLoadingLive && stateBeforeAdvance == DSMState.RunningReplay` guard).
  The new fan-out should be outside that guard so it covers the non-branch case.

**Acceptance:**
- `DrillMasterFanOutTests.TransitionState_Standby_To_LoadingLive_FansOutPrepareLive`:
  Construct `DrillMaster` with a mock `FanOutNodeOp` interceptor (or real DDS participant
  in a test domain); call `HandleSysOpRequest(TransitionState, TargetState=LoadingLive)`;
  assert that `NodeOpCommand { Operation = PrepareLive }` is recorded.
- `DrillMasterFanOutTests.TransitionState_FansOut_CommitState_AfterPrepare`:
  Same setup; assert `NodeOpCommand { Operation = CommitState, PayloadJson = "2" }`
  (or the integer value of `LoadingLive`) also reached the mock.
- `DrillMasterFanOutTests.NoActiveNodes_FanOutIsSkipped`:
  Bootstrap `DrillMaster` with no nodes; call `HandleSysOpRequest`; assert no
  `NodeOpCommand` was written and no exception was thrown.
- `DrillMasterFanOutTests.ReplaySeekStep_FansOutNodeReplaySeek`:
  Provide a trajectory with an `OperationStep(ReplaySeek, "{\"TargetWallTicks\":1234}")`;
  assert `NodeOpCommand { Operation = NodeReplaySeek, PayloadJson = "{\"TargetWallTicks\":1234}" }`.

---

## Sequencing

Implement in this order:

1. **A.4** — Add fields to `DistributedTransaction` (no tests break).
2. **A.5** — Populate fields in `DrillMaster` (extends existing logic).
3. **A.1 + A.2** — Title bar + window wrapper in `OrchestratorSubsystem`.
4. **A.3** — Remove `BeigeChildBg` from panel.
5. **A.7** — Update status banner in panel.
6. **A.6** — Overhaul 2PC table in `OrchestratorSubsystem`.
7. **B.1** — Add `DdsWriter<SysOpRequest>` to `OrchestratorSubsystem`.
8. **B.2** — Update panel constructor and swap all calls.
9. **B.3** — Add fan-out loop in `DrillMaster`.
10. **Write tests** — after all production changes, write or update tests.
11. **Full build + test run.**

---

## Testing guide

All tests are in `Bagira.Orchestrator.Tests` and/or `Bagira.Runner.Tests`
(services tests for `OrchestratorSubsystem`).

New tests to write (minimum set):

| Test class | Method | What it verifies |
|------------|--------|-----------------|
| `OrchestratorSubsystemTests` | `TitleBarColor_IsBeige` | `TitleBarColor.X ≈ 0.72f` |
| `OrchestratorSubsystemTests` | `Shutdown_DisposesWriter` | writer null after Shutdown |
| `OrchestratorSubsystemTests` | `FormatPrettyJson_IndentsJson` | formatted output has newline |
| `OrchestratorSubsystemTests` | `FormatPrettyJson_InvalidJson_ReturnsOriginal` | bad JSON passthrough |
| `DistributedTransactionTests` | `SourceDsmState_CapturedBeforeOptimisticAdvance` | SourceDsmState == Standby for Standby→Live |
| `DistributedTransactionTests` | `PayloadJson_PopulatedFromSysOpRequest` | PayloadJson round-trips |
| `OrchestratorScenarioPanelTests` | `NoHandleSysOpRequest_Calls` | grep / mock: 0 direct calls |
| `OrchestratorScenarioPanelTests` | `StatusBanner_ShowsArrow_WhenInFlight` | banner has `→` visible |
| `DrillMasterFanOutTests` | `TransitionState_FansOut_PrepareAndCommit` | PrepareLive + CommitState reach mock interceptor |
| `DrillMasterFanOutTests` | `NoActiveNodes_FanOutSkipped` | no NodeOpCommand, no exception |
| `DrillMasterFanOutTests` | `ReplaySeekStep_FansOut_NodeReplaySeek` | correct operation code |

**Existing tests that must stay green (run before and after changes):**
- `Bagira.Orchestrator.Tests` — all (109 currently passing).
- `Bagira.Runner.Tests` — all (138 currently passing).
- `Bagira.Orchestrator.Integration.Tests` — all passing tests.

---

## Success criteria

- [ ] `TitleBarColor` returns `(0.72f, 0.64f, 0.47f, 1f)`.
- [ ] `DrawUI()` contains one `ImGui.Begin("Orchestrator")` call balanced by `ImGui.End()`.
- [ ] `grep "BeigeChildBg" Bagira.Runner/Services/OrchestratorScenarioPanel.cs` returns zero.
- [ ] `DistributedTransaction` has `SourceDsmState`, `PayloadJson`, `NodeResponses` fields.
- [ ] `DrillMaster` populates all three new fields (`SourceDsmState`, `PayloadJson`, `NodeResponses`).
- [ ] 2PC table has 5 columns, `ScrollY`, `TreeNodeEx` rows with GUID tooltip, expandable node rows.
- [ ] `OrchestratorSubsystem` has `_sysOpWriter` field, initialized in `Initialize`, disposed in `Shutdown`.
- [ ] `grep "HandleSysOpRequest" Bagira.Runner/Services/OrchestratorScenarioPanel.cs` returns zero.
- [ ] `DrillMaster.ProcessSingleSysOpRequest` fans out `PrepareXxx` + `CommitState` for every `TransitionStep`.
- [ ] Fan-out loop correctly skips nodes when `activeNodeIds.Count == 0`.
- [ ] All new tests (listed above) pass.
- [ ] All existing `Bagira.Orchestrator.Tests` and `Bagira.Runner.Tests` continue to pass.

---

## Reference

- [CGF-1-ADDENDUM-3.md §2 — 2PC History Overhaul](../CGF-1-ADDENDUM-3.md#2-orchestrator-imgui-window--2pc-history-overhaul)
- [CGF-1-ADDENDUM-3.md §3 — Real Network Dispatch Fix](../CGF-1-ADDENDUM-3.md#3-real-network-dispatch-fix)
- [CGF-1-TASK-DETAIL.md §CGF1-S0501](../CGF-1-TASK-DETAIL.md)
- [CGF-1-TASK-DETAIL.md §CGF1-S0502](../CGF-1-TASK-DETAIL.md)
- [BATCH-25 Review](../reviews/CGF-1-BATCH-25-REVIEW.md) — approved, no open debt targeted at BATCH-26
