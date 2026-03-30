# CGF-1-BATCH-28 — Archive Export/Import Pipeline (S0505)

**Batch Number:** CGF-1-BATCH-28  
**Tasks:** CGF1-S0505 (Archive Export/Import Pipeline) + P3 debt: `_replayDuration` wiring  
**Phase:** Phase 5 — Operational UI, Real Network Dispatch & CQRS Architecture  
**Estimated Effort:** 12–16 hours  
**Design authority:** [CGF-1-ADDENDUM-3.md](../CGF-1-ADDENDUM-3.md) §6  
**Report target:** `.dev/cgf-1/reports/CGF-1-BATCH-28-REPORT.md`

---

## 1. Onboarding

### 1.1 Project Context
You are working on **Bagira** — a distributed simulation system for military exercises.
The system runs across four node types: **Orchestrator** (DrillMaster), **SimHost**
(muscle), **CGF** (brain), and **IOS** (commander UI). Nodes communicate via CycloneDDS
topics defined in `Bagira.DDS.DataModel`.

### 1.2 Relevant Design Documents
- **[CGF-1-ADDENDUM-3.md](../CGF-1-ADDENDUM-3.md) §6** — Archive Export/Import Pipeline (primary authority)
- **[CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0505** — detailed work list and success conditions
- **[CGF-1-BATCH-27-REVIEW.md](../reviews/CGF-1-BATCH-27-REVIEW.md)** — last approved batch
- **[CGF-1-BATCH-27-REPORT.md](../reports/CGF-1-BATCH-27-REPORT.md)** — developer notes and design decisions from last batch

### 1.3 Key Files You Will Touch

| File | Purpose |
|------|---------|
| `Bagira.DDS.DataModel/Orchestration/OrchestrationMessages.cs` | Confirm `SysOpType.CancelOperation = 13` already present (it is) |
| `Bagira.Orchestrator/StorageGatewayModule.cs` | Add CT threading, scan helpers, `PrefetchArchiveAsync` |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceArchiveHandler.cs` | **New file** |
| `Bagira.Orchestrator/DrillMaster.cs` | Add `_activeCancellations`, ExportArchive/ImportArchive/CancelOperation branches |
| `Bagira.Orchestrator/NodeBootstrapper.cs` | Register `ReferenceArchiveHandler` |
| `Bagira.Runner/Services/OrchestratorScenarioPanel.cs` | Archive Management section; wire `_replayDuration` on Load Replay |
| `Bagira.Orchestrator.Tests/StorageGatewayTests.cs` | Cancellation tests |
| `Bagira.Orchestrator.Tests/ReferenceArchiveHandlerTests.cs` | **New file** — handler unit tests |
| `Bagira.Orchestrator.Tests/DrillMasterArchiveTests.cs` | **New file** — CancelOperation integration test |
| `Bagira.Runner.Tests/OrchestratorScenarioPanelTests.cs` | Archive UI progress test |

### 1.4 Current Test Baseline
- `Bagira.DDS.DataModel.Tests`: 45
- `Bagira.Orchestrator.Tests`: 49
- `Bagira.Runner.Tests`: 159

All 253 tests were passing after BATCH-27. Run `dotnet build IOS-IG-SimHost.sln -c Debug`
and confirm green before starting.

---

## 2. P3 Debt to Close First — `_replayDuration` Wire-up

**Source:** BATCH-27 review Issue 2 / DEBT-TRACKER.md line ~222  
**File:** `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`

`GetReplayDuration(string metaJsonContent)` is already implemented and unit-tested.
It is never called on drill selection, so `_replayDuration` stays 3600 s.

**Fix:** After the user clicks "Load Replay" and `_selectedDrillIdx >= 0`, read the
`.meta.json` file from the selected drill directory and call `GetReplayDuration`:

```csharp
// When "Load Replay" button is clicked:
if (_selectedDrillIdx >= 0 && _selectedDrillIdx < _availableDrills.Length)
{
    string drillName  = _availableDrills[_selectedDrillIdx];
    string metaPath   = Path.Combine(@"C:\FDP_Temp", drillName, "recording.meta.json");
    if (File.Exists(metaPath))
        _replayDuration = GetReplayDuration(File.ReadAllText(metaPath));
}
```

No new test required for this P3 fix (existing `GetReplayDuration_ParsesMeta` covers the
helper; the wiring is trivial UI glue). However, do verify the call is reached in the
existing `LoadReplay_WritesCorrectSysOpRequest` test path if one exists.

---

## 3. Task A — StorageGatewayModule Cancellation & Scan Helpers

**File:** `Bagira.Orchestrator/StorageGatewayModule.cs`

### A.1 — Thread `CancellationToken` through `PullToNasAsync`

Change signature:
```csharp
public async Task<GatewayResult> PullToNasAsync(
    IReadOnlyList<FileManifestEntry> manifests,
    string nasBasePath,
    CancellationToken ct = default)
```

Implementation pattern:
```csharp
var opts = new ParallelOptions
{
    MaxDegreeOfParallelism = MaxParallelCopies,
    CancellationToken = ct
};
var partial = new ConcurrentBag<string>();
try
{
    await Task.Run(() => Parallel.ForEach(manifests, opts, entry =>
    {
        var dest = Path.Combine(nasBasePath, entry.RelativeDest);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        partial.Add(dest);
        File.Copy(entry.SourceUnc, dest, overwrite: true);
        partial.TryTake(out _);   // remove on success — only tracked while in-flight
    }), ct).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    // Delete partially-written NAS files to keep storage consistent
    foreach (var f in partial) try { File.Delete(f); } catch { /* best-effort */ }
    throw;
}
return new GatewayResult { SuccessCount = manifests.Count, FailureCount = 0 };
```

### A.2 — Thread `CancellationToken` through `PushToNodesAsync`

Same pattern — add `CancellationToken ct = default` parameter; pass to `ParallelOptions`;
on `OperationCanceledException` delete partial destination files in the same pattern.

### A.3 — Add `PrefetchArchiveAsync`

```csharp
/// <summary>
/// Fetches per-node <c>.fdp</c> archives from <paramref name="nasBasePath"/>
/// and delivers each to the node-specific <see cref="NodeDistributionTarget.DestinationPath"/>.
/// Each node receives <c>&lt;nasBasePath&gt;/&lt;drillId&gt;/node_&lt;nodeId&gt;.fdp</c>.
/// </summary>
public async Task<GatewayResult> PrefetchArchiveAsync(
    string drillId,
    IReadOnlyList<NodeDistributionTarget> targets,
    string nasBasePath,
    CancellationToken ct = default)
```

For each target construct source path:
`Path.Combine(nasBasePath, drillId, $"node_{target.NodeId}.fdp")`
and copy to `target.DestinationPath` with CT passed to `ParallelOptions`.

On cancellation, delete partial destination files and rethrow.

### A.4 — Add Scan Helpers

Add three pure, synchronous, no-DDS helpers:

```csharp
/// <summary>
/// Returns the names of subdirectories under <paramref name="root"/> that
/// contain at least one <c>*.json</c> file. Represents locally available scenarios.
/// </summary>
public IReadOnlyList<string> ScanLocalScenarios(string root)

/// <summary>
/// Returns the names of subdirectories under <paramref name="root"/> that
/// contain at least one <c>*.fdp</c> file. Represents locally recorded drills.
/// </summary>
public IReadOnlyList<string> ScanLocalDrills(string root)

/// <summary>
/// Returns the names of subdirectories under <paramref name="nasRoot"/> that
/// contain at least one <c>*.fdp</c> file. Represents drills archived to NAS.
/// </summary>
public IReadOnlyList<string> ScanNasDrills(string nasRoot)
```

Implementation: `Directory.Exists` guard → `Directory.GetDirectories` → filter by file
extension → `Path.GetFileName` → return `List<string>`. Return empty list if directory
does not exist (no throws for missing root).

---

## 4. Task B — ReferenceArchiveHandler (FDP.Toolkit)

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceArchiveHandler.cs` *(new)*

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagira.Orchestrator;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;
using FDP.Kernel.Logging;

namespace FDP.Toolkit.Orchestration.Handlers;

/// <summary>
/// Node-side archive handler (CGF1-S0505).
/// Responds to <see cref="NodeOpType.SerializeLocal"/> commands whose
/// <c>PayloadJson</c> contains a <c>"DrillId"</c> key.
/// </summary>
public sealed class ReferenceArchiveHandler : IDsmHandler
{
    private readonly string _localTempRoot;
    private readonly int    _nodeId;

    public ReferenceArchiveHandler(string localTempRoot, int nodeId)
    {
        _localTempRoot = localTempRoot;
        _nodeId        = nodeId;
    }

    public bool CanHandle(int operationId)
        => operationId == (int)NodeOpType.SerializeLocal;

    public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        => Task.FromResult<string?>(null);

    public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
    {
        string? drillId = ParseDrillId(cmd.PayloadJson);
        if (drillId is null) return;  // payload is not an archive request; skip

        var file = Path.Combine(_localTempRoot, drillId, $"node_{_nodeId}.fdp");
        if (!File.Exists(file))
        {
            FdpLog.Warn($"[ReferenceArchiveHandler] No local .fdp at {file}; cannot report manifest.");
            return;
        }

        var manifest = new[]
        {
            new FileManifestEntry
            {
                SourceUnc    = file,
                RelativeDest = Path.Combine(drillId, $"node_{_nodeId}.fdp"),
            }
        };
        // ResultJson serialised as JSON array for DrillMaster.ConsumeNodeOpStatuses to pull
        cmd.SetResultJson(JsonSerializer.Serialize(manifest));
    }

    public void Abort(OrchestrationCommand cmd, EntityRepository? repo)
    {
        string? drillId = ParseDrillId(cmd.PayloadJson);
        if (drillId is null) return;
        var file = Path.Combine(_localTempRoot, drillId, $"node_{_nodeId}.fdp");
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex)
        {
            FdpLog.Warn($"[ReferenceArchiveHandler] Abort cleanup failed for {file}: {ex.Message}");
        }
    }

    private static string? ParseDrillId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("DrillId", out var prop))
                return prop.GetString();
        }
        catch (JsonException) { /* not a JSON object; not our payload */ }
        return null;
    }
}
```

**Note on `cmd.SetResultJson`:** Check how other handlers set ResultJson (e.g.
`ReferenceCheckpointHandler` drains `CheckpointIOWorker`) — find the `OrchestrationCommand`
class or the transport `PublishStatus` call and use the same mechanism. If `SetResultJson`
doesn't exist, use whatever the correct API is (transport `PublishStatus(cmd.TransactionId, ...)`).

---

## 5. Task C — DrillMaster Archive Branches

**File:** `Bagira.Orchestrator/DrillMaster.cs`

### C.1 — `_activeCancellations` Registry

Add field:
```csharp
private readonly Dictionary<Guid, CancellationTokenSource> _activeCancellations = new();
```

Dispose in existing `Dispose()` method:
```csharp
foreach (var cts in _activeCancellations.Values) cts.Dispose();
_activeCancellations.Clear();
```

### C.2 — ExportArchive Branch

In `ProcessSingleSysOpRequest` (or equivalent switch), add case for `SysOpType.ExportArchive`:

1. Parse `DrillId` from `req.PayloadJson` (look for `"DrillId"` key).
2. If missing → write `SysOpStatus.Rejected` + return early (fail loud).
3. Create `CancellationTokenSource`, store in `_activeCancellations[req.RequestId]`.
4. Call `FanOutSerializeLocal(txId, activeNodeIds, req.PayloadJson)`.
5. In `ConsumeNodeOpStatuses`, when the associated transaction's ACK set is complete,
   collect `FileManifestEntry` arrays from each node's `ResultJson`, then call
   `_gateway.PullToNasAsync(allManifests, _nasBasePath, cts.Token)`.
   - On success: publish `SysOpStatus.Completed`.
   - On `OperationCanceledException`: publish `SysOpStatus.Rejected` ("cancelled").
   - On other exception: publish `SysOpStatus.Rejected` ("gateway error").
   - Always: `_activeCancellations.Remove(req.RequestId)`, dispose CTS.

### C.3 — ImportArchive Branch

Case `SysOpType.ImportArchive`:

1. Parse `DrillId` from payload; fail loud if missing.
2. Create CTS, store in `_activeCancellations[req.RequestId]`.
3. Build `NodeDistributionTarget` list from active node roster.
4. Fire-and-continue:
   ```csharp
   _ = _gateway.PrefetchArchiveAsync(drillId, targets, _nasBasePath, cts.Token)
       .ContinueWith(t =>
       {
           _activeCancellations.Remove(req.RequestId);
           if (t.IsCanceled)
               PublishSysOpStatus(req.RequestId, SysOpStatus.Rejected, "cancelled");
           else if (t.IsFaulted)
               PublishSysOpStatus(req.RequestId, SysOpStatus.Rejected, "gateway error");
           else
               PublishSysOpStatus(req.RequestId, SysOpStatus.Completed, null);
       });
   ```

### C.4 — CancelOperation Branch

Case `SysOpType.CancelOperation`:

1. Parse target operation `Guid` from `req.PayloadJson` (the payload is the raw GUID string
   or a JSON object with `"TargetOperationId"` key — check addendum §6.1 for the exact wire format).
   The addendum says payload = `"<target-operation-request-guid>"`.
   Parse as either `Guid.TryParse(req.PayloadJson, out var targetId)` or JSON.
2. If found in `_activeCancellations`: cancel & remove.
3. Fan out `NodeOpType.AbortTransaction` to all active nodes with `targetId` as payload.
4. No `SysOpStatus` reply needed for this operation itself.

---

## 6. Task D — NodeBootstrapper Wires ReferenceArchiveHandler

**File:** `Bagira.Orchestrator/NodeBootstrapper.cs` (or wherever `BuildOrchestration` lives)

After the existing handler registrations, add:
```csharp
handlers.Add(new ReferenceArchiveHandler(localTempRoot, nodeId));
```

`localTempRoot` and `nodeId` should already be in scope — check how `ReferenceCheckpointHandler`
is added to know the exact calling convention.

---

## 7. Task E — OrchestratorScenarioPanel Archive Management Section

**File:** `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`

### E.1 — New State Fields

```csharp
// Archive Management state
private string[] _archivedDrills         = Array.Empty<string>();
private string[] _unarchivedLocalDrills  = Array.Empty<string>();
private int      _selectedArchiveIdx     = -1;
private int      _selectedUnarchivedIdx  = -1;
private Guid     _activeArchiveOpId      = Guid.Empty;
```

### E.2 — RefreshLocalAssets Extended

Extend the existing `RefreshLocalAssets()` method to also populate archive lists:

```csharp
// After existing _availableDrills / _availableScenarios population:
var nasRoot = @"C:\FDP_Temp\nas";   // convention; can be configurable later
_archivedDrills        = _gateway?.ScanNasDrills(nasRoot).ToArray()
                         ?? Array.Empty<string>();
var archivedSet        = new HashSet<string>(_archivedDrills);
_unarchivedLocalDrills = _availableDrills
                         .Where(d => !archivedSet.Contains(d))
                         .ToArray();

if (_selectedArchiveIdx    >= _archivedDrills.Length)        _selectedArchiveIdx    = -1;
if (_selectedUnarchivedIdx >= _unarchivedLocalDrills.Length) _selectedUnarchivedIdx = -1;
```

If no gateway is available, skip gracefully with empty arrays. The `OrchestratorScenarioPanel`
constructor receives `DrillMaster drillMaster` — use `drillMaster`'s gateway reference if
accessible as a property, or pass `StorageGatewayModule` directly if refactoring is needed.
Check the existing constructor signature and field layout before deciding.

### E.3 — `RenderArchiveSection`

Add a new `CollapsingHeader("Archive Management")` section to the `Render(...)` call chain
(after Stories section, before the end of the panel):

```csharp
private void RenderArchiveSection(DSMState state, bool disableAll)
{
    if (!ImGui.CollapsingHeader("Archive Management##OrcArchive")) return;

    bool archiveDisable = disableAll;

    // — Unarchived Local Drills —
    ImGui.Text("Unarchived Local:");
    ImGui.Combo("##UnarchivedCombo", ref _selectedUnarchivedIdx,
                _unarchivedLocalDrills, _unarchivedLocalDrills.Length);
    ImGui.SameLine();
    if (ImGui.Button("⟳##RefreshUnarchived")) RefreshLocalAssets();
    using (new ImGuiDisabledScope(archiveDisable
                                  || _selectedUnarchivedIdx < 0
                                  || _activeArchiveOpId != Guid.Empty))
    {
        if (ImGui.Button("Export to NAS ▶##OrcExport")
            && _selectedUnarchivedIdx >= 0)
        {
            var drillName  = _unarchivedLocalDrills[_selectedUnarchivedIdx];
            var requestId  = Guid.NewGuid();
            _activeArchiveOpId = requestId;
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = requestId,
                OperationType = SysOpType.ExportArchive,
                PayloadJson   = $"{{\"DrillId\":\"{drillName}\"}}",
            });
        }
    }

    ImGui.Separator();

    // — Archived NAS Drills —
    ImGui.Text("Archived on NAS:");
    ImGui.Combo("##ArchivedCombo", ref _selectedArchiveIdx,
                _archivedDrills, _archivedDrills.Length);
    ImGui.SameLine();
    if (ImGui.Button("⟳##RefreshArchived")) RefreshLocalAssets();
    using (new ImGuiDisabledScope(archiveDisable
                                  || _selectedArchiveIdx < 0
                                  || _activeArchiveOpId != Guid.Empty))
    {
        if (ImGui.Button("Import from NAS ◀##OrcImport")
            && _selectedArchiveIdx >= 0)
        {
            var drillName  = _archivedDrills[_selectedArchiveIdx];
            var requestId  = Guid.NewGuid();
            _activeArchiveOpId = requestId;
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = requestId,
                OperationType = SysOpType.ImportArchive,
                PayloadJson   = $"{{\"DrillId\":\"{drillName}\"}}",
            });
        }
    }

    // — Progress / Cancel (always visible while op in-flight) —
    if (_activeArchiveOpId != Guid.Empty)
    {
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.8f, 0f, 1f));
        ImGui.Text("Archive operation in progress...");
        ImGui.PopStyleColor();
        ImGui.ProgressBar(-1f * (float)ImGui.GetTime(), new System.Numerics.Vector2(-1, 0), "");
        // Cancel button is always active regardless of disableAll
        if (ImGui.Button("CANCEL OPERATION##OrcCancelArchive"))
        {
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = SysOpType.CancelOperation,
                PayloadJson   = _activeArchiveOpId.ToString(),
            });
            _activeArchiveOpId = Guid.Empty;  // optimistic clear
        }
    }
}
```

Also clear `_activeArchiveOpId` when a `SysOpStatus` sample arrives for that request ID
if the panel is monitoring status. For now, the optimistic clear when CANCEL is clicked
is sufficient; P3 if needed.

**Note on `ImGuiDisabledScope`:** Verify the project has this helper or use `ImGui.BeginDisabled()` / `ImGui.EndDisabled()` directly.

---

## 8. Tests

### 8.1 — StorageGateway Cancellation Tests

**File:** `Bagira.Orchestrator.Tests/StorageGatewayTests.cs` (existing)

Add two tests:

**`Fact: PullToNasAsync cleans up on cancel`**
```csharp
[Fact]
public async Task PullToNasAsync_CancelsAndCleansPartialFiles()
{
    // Arrange: 3 manifest entries pointing to 3 temp source files
    // Use a CTS; cancel it after a short delay (or cancel immediately before call)
    // Assert: destination files that were partially created are deleted
    // Assert: OperationCanceledException propagates out of PullToNasAsync
}
```

**`Fact: PushToNodesAsync cleans up on cancel`** (parity test)

### 8.2 — ReferenceArchiveHandler Tests

**File:** `Bagira.Orchestrator.Tests/ReferenceArchiveHandlerTests.cs` (new)

Three facts per success conditions:

**`Fact: ReferenceArchiveHandler_Commit_ProducesManifestJson`**
- Create a temp `.fdp` file at `<localTempRoot>/<drillId>/node_<id>.fdp`.
- Call `Commit(cmd, null)`.
- Assert `cmd.ResultJson` deserialises to a `FileManifestEntry[]` with expected `SourceUnc`
  and `RelativeDest`.

**`Fact: ReferenceArchiveHandler_Abort_DeletesPartialFile`**
- Create a partial `.fdp` file.
- Call `Abort(cmd, null)`.
- Assert file no longer exists on disk.

**`Fact: ReferenceArchiveHandler_Commit_SkipsWhenNoDrillId`**
- Call `Commit` with a payload that has no `"DrillId"` key.
- Assert no exception; `ResultJson` remains null/empty.

### 8.3 — CancelOperation Integration Test

**File:** `Bagira.Orchestrator.Tests/DrillMasterArchiveTests.cs` (new)

**`Fact: CancelOperation_CancelsActiveCts`**

Use `DrillMasterTestHarness` (or equivalent test scaffolding from existing
`DrillMasterFanOutTests`) to post an `ExportArchive` request and then immediately post
a `CancelOperation` referencing the same `RequestId`. Assert the `CancellationTokenSource`
was cancelled (check `_activeCancellations.TryGetValue` returning a cancelled CTS before
removal, or capture via an event/hook). This is an internal-state test and may require
`internal` access — add `[InternalsVisibleTo]` if needed or use a testable seam.

### 8.4 — Archive UI Progress Test

**File:** `Bagira.Runner.Tests/OrchestratorScenarioPanelTests.cs` (existing)

**`Fact: Archive_ProgressVisible_WhenOpInFlight`**
- Set `_activeArchiveOpId` (via reflection or internal setter) to a non-empty Guid.
- Render one headless ImGui frame.
- Assert `ProgressBar` call happens (check `SysOpRequest` on CANCEL click or use existing
  headless render pattern from other tests).

---

## 9. Mandatory Workflow: Test-Driven Task Progression

> **COPY THIS VERBATIM — do not summarise or paraphrase:**
>
> For each task:
> 1. Write the test first (or write test structure).
> 2. Implement code until the test passes.
> 3. Run `dotnet test <project>.Tests.csproj -c Debug --no-build --logger "console;verbosity=quiet"` and confirm green before moving to the next task.
> 4. If a test was already passing before your code change, verify it still passes after.
> 5. Never leave a failing test and move on.
> 6. All success conditions in §8 must be covered by at least one `[Fact]`.

---

## 10. Developer Insights — Required Questions

In your report, explicitly answer:

1. **What issues were encountered?** (compilation errors, runtime surprises, API mismatches)
2. **What weak points were spotted in the codebase?** (fragile patterns, missing guards, silent failures)
3. **What design decisions were made beyond the spec?** (anything you had to infer or choose)
4. **How did you handle `cmd.SetResultJson` / ResultJson transport?** (explain the mechanism you found)
5. **Cancellation cleanup test fragility?** (did you need to add sleeps or stubs for the partial-file cleanup test?)

---

## 11. Report Format

Write your completion report to `.dev/cgf-1/reports/CGF-1-BATCH-28-REPORT.md` with these sections:

```markdown
# CGF-1-BATCH-28 Report

## Tasks Completed
- [ ] P3 debt: _replayDuration wire-up
- [ ] A: StorageGatewayModule CT threading + scan helpers + PrefetchArchiveAsync
- [ ] B: ReferenceArchiveHandler (FDP.Toolkit.Orchestration)
- [ ] C: DrillMaster _activeCancellations + ExportArchive / ImportArchive / CancelOperation
- [ ] D: NodeBootstrapper wires ReferenceArchiveHandler
- [ ] E: OrchestratorScenarioPanel Archive Management section
- [ ] Tests: all 5 success conditions covered

## Test Counts (before → after)
- Bagira.DDS.DataModel.Tests: 45 →
- Bagira.Orchestrator.Tests:  49 →
- Bagira.Runner.Tests:        159 →

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions
### ResultJson Transport Mechanism
### Cancellation Test Notes

## Open Items / Risks
```

---

## 12. Success Criteria

All five success conditions from CGF-1-TASK-DETAIL.md §CGF1-S0505 must pass:

1. `Fact: PullToNasAsync cleans up on cancel`
2. `Fact: ReferenceArchiveHandler Commit produces manifest`
3. `Fact: ReferenceArchiveHandler Abort deletes partial file`
4. `Fact: CancelOperation kills gateway task`
5. `Fact: Archive UI progress visible`

Plus: all pre-existing tests must continue to pass.

---

*Good luck. Do not stop for questions unless you hit a breaking architectural conflict.
Implement, test, and report.*
