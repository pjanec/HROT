# BATCH-05 — Phase 6 (ClusterDiagnosticsPanel) + Phase 8 (Log Merge)

**Batch:** BATCH-05
**Assigned to:** Developer
**Tasks:** DD-P6-T01, DD-P6-T02, DD-P6-T03, DD-P8-T01, DD-P8-T02, DD-P8-T03
**Prerequisites:** BATCH-04 approved and merged

---

## Context

All Phase 1–5 and Phase 7 tasks are complete. Phase 6 and Phase 8 are the final deliverables.

### Key existing files to read before starting

- `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterScenarioPanel.cs` — the canonical ImGui panel pattern
- `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterUiCache.cs` — CQRS read-side for cluster state
- `Hrot/Subsystems/Hrot.Orchestrator/Windows/OrchestratorWindow.cs` — how panels are wrapped in ManagedWindow
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` — RegisterWindows and Update patterns
- `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticsConsensusAggregator.cs` — file manifest structure
- `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs` — DiagnosticDumpPayloadDto
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Events/ClusterOpIntents.cs` — ExecuteDiagnosticDumpIntent
- `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IFileDialogService.cs` — service interface

### Key architectural constraints

- `ClusterDiagnosticsPanel` uses ONLY `ClusterUiCache` for reading cluster state (CQRS read-side)
- NO direct DDS dependencies in the panel
- `_pendingClipboardText` pattern: background tasks write to `volatile string?`, render method calls
  `ImGui.SetClipboardText` and clears it — never call ImGui from background thread
- All async background tasks must use `Task.Run` (NOT `async void` on the render path for JSON ops)
- "Save Local Copy As" is `async void` (fire-and-forget) — acceptable for UI callback
- `NodeHeartbeat.SubsystemName` (string field) comes from `ClusterUiCache.ActiveNodes` dictionary
  keyed by `NodeId` (int). Use this to group nodes by subsystem type.

### Key facts about FileManifestEntry

- `SourceUnc` = node-local absolute path (e.g. `C:\FDP_Temp\nodes\node-400\dumps\...\foo.json`)
- `RelativeDest` = NAS-relative path (e.g. `dumps/{txId:N}/foo.json`)
- NAS full path = `Path.Combine(_config.NasBasePath, entry.RelativeDest)`
- The manifest delivered to `ClusterUiCache` has `SourceUnc = ""` (stripped by aggregator for DDS)
- Only `RelativeDest` is available in the UI panel

---

## Task DD-P8-T02 — MergeLogsIntent and LogMergeCompletedEvent (do this first, it's a prerequisite)

**File to create:** `Hrot/Subsystems/Hrot.Orchestrator/Events/DiagnosticsMergeEvents.cs`

```csharp
using Fdp.Core;

namespace Hrot.Orchestrator.Events;

/// <summary>Triggers the K-way merge of all per-node log files from the last diagnostic dump.</summary>
[DataPolicy(DataPolicy.NoRecord)]
public struct MergeLogsIntent
{
    /// <summary>NAS paths of the log files to merge (RelativeDest values for .log entries).</summary>
    public string[] LogRelativePaths { get; init; }

    /// <summary>NAS base path used to resolve full file paths.</summary>
    public string NasBasePath { get; init; }

    /// <summary>Timestamp string from the original dump (e.g. "20260503_120000").</summary>
    public string DumpTimestamp { get; init; }
}

/// <summary>Published by <see cref="DiagnosticLogMergeWorker"/> when the merged log file is ready.</summary>
[DataPolicy(DataPolicy.NoRecord)]
public struct LogMergeCompletedEvent
{
    /// <summary>Full NAS path of the merged log file.</summary>
    public string NasPath { get; init; }
}
```

**NOTE:** `DataPolicy` attribute may require an `[EventId(...)]` — check `ClusterOpIntents.cs`
to see whether `[DataPolicy]` alone is sufficient or if an `[EventId]` is also required.
If `[EventId]` is needed, use the next available ID after checking existing IDs.

---

## Task DD-P8-T01 — DiagnosticLogMergeWorker

**File to create:** `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticLogMergeWorker.cs`

**Namespace:** `Hrot.Orchestrator`

**Purpose:** K-way merge of per-node log files into a single chronologically ordered output.

**Key design:**

1. Subscribes to `MergeLogsIntent` via `FdpEventBus` in constructor or `Subscribe` method
2. On intent received: spawn `Task.Run(LongRunning, () => MergeAsync(intent, cts.Token))`
3. K-way merge algorithm:
   - Open one `StreamReader` per source log file
   - For each file: read first line, parse timestamp; if parseable enqueue `(line, reader)` with timestamp priority
   - Loop until queue is empty: dequeue head `(line, reader)`, write line to output writer;
     read next line from same reader; if parseable → enqueue; if not parseable (continuation line) → append to current output and re-read
   - Close all readers on completion
4. Output path: `Path.Combine(nasBasePath, "dumps", $"dump_{intent.DumpTimestamp}_logs_MERGED.log")`
5. On completion: `_bus.PublishManaged(new LogMergeCompletedEvent { NasPath = outputPath })`

**Timestamp parsing:** `[YYYY-MM-DD HH:mm:ss.ffff]` format prefix
```csharp
private static bool TryParseTimestamp(ReadOnlySpan<char> line, out DateTime dt)
{
    // Line starts with "[YYYY-MM-DD HH:mm:ss.ffff]"
    // Check minimum length and bracket characters
    dt = default;
    if (line.Length < 26 || line[0] != '[') return false;
    int close = line.IndexOf(']');
    if (close <= 1) return false;
    var inner = line[1..close];
    return DateTime.TryParseExact(inner, new[] { "yyyy-MM-dd HH:mm:ss.ffff", "yyyy-MM-dd HH:mm:ss.fff" },
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out dt);
}
```

**Continuation line handling:**
```
while (reader has lines):
    line = reader.ReadLine()
    if TryParseTimestamp(line, out dt):
        enqueue (line, reader, dt)
    else:
        // append to last written line in output (write with newline immediately)
        writer.WriteLine(line)
```

**Success Conditions:**

1. Unit test: Merge 3 `StringReader` sequences with interleaved timestamps. Output is correctly
   ordered by timestamp.
2. Unit test: A log with a 3-line exception stack trace merges correctly — stack trace lines
   appear immediately after the originating log record in output.
3. Unit test: An inaccessible file is skipped (write a warning to output); remaining files merge.
4. Unit test: `CancellationToken` stops the merge; `LogMergeCompletedEvent` is NOT published.

---

## Task DD-P6-T01 — ClusterDiagnosticsPanel (Configuration + Execution)

**File to create:** `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterDiagnosticsPanel.cs`

**Namespace:** `Hrot.Orchestrator.Panels`

**Constructor:**
```csharp
public ClusterDiagnosticsPanel(ClusterUiCache uiCache, FdpEventBus bus, IFileDialogService fileDialogService)
```

**State:**
```csharp
private readonly ClusterUiCache _uiCache;
private readonly FdpEventBus _bus;
private readonly IFileDialogService _fileDialogService;

// Configuration section state
private bool _useMarkdownWrapper;
private bool _dumpEvents = true;
private bool _dumpEntities = true;
private bool _dumpArchitecture = true;
private bool _dumpLogs = true;
private string _eventProvidersInput = string.Empty;    // comma-separated
private string _networkIdInput = string.Empty;         // comma-separated entity network IDs
private float  _maxAgeHours = 24f;
private int    _severityThreshold = 0;

// Results tree state
private List<FileManifestEntry> _manifest = new();
private string _operationStatus = string.Empty;
private volatile string? _pendingClipboardText;
private bool _copyInProgress;
private string? _mergedLogPath;

// Subsystem column selection (key = subsystem type name e.g. "SimHost")
private readonly Dictionary<string, bool> _subsystemSelected = new();
```

**Render() method structure:**

```
Section 1 — Configuration:
  ImGui.Text("Diagnostic Dump Configuration")
  ImGui.Checkbox("##markdown", ref _useMarkdownWrapper) + SameLine + Text("Markdown wrapper")
  ImGui.Separator()
  
  "Dump kinds:" row:
    Checkbox Events, Checkbox Entities, Checkbox Architecture, Checkbox Logs
  
  ImGui.InputText("Event providers (comma-sep)", ref _eventProvidersInput, ...)
  ImGui.InputText("Entity network IDs (comma-sep)", ref _networkIdInput, ...)
  ImGui.SliderFloat("Max log age (hours)", ref _maxAgeHours, 0, 168)
  ImGui.SliderInt("Severity threshold", ref _severityThreshold, 0, 5)
  
  Separator
  
  Node selection matrix — based on distinct subsystem names from _uiCache.ActiveNodes:
    For each distinct SubsystemName: Checkbox column "SimHost", "CGF", etc.
  
  EXECUTE button (disabled if no subsystems selected):
    -> Build DiagnosticDumpPayloadDto
    -> Publish ExecuteDiagnosticDumpIntent

Section 2 — Status:
  ImGui.Text(_operationStatus)
  
  Clipboard flush:
    if (_pendingClipboardText != null) { ImGui.SetClipboardText(_pendingClipboardText); _pendingClipboardText = null; }

Section 3 — Results tree:
  BuildResultsTree()
```

**EXECUTE button logic:**
```csharp
// Collect selected node IDs
var targetNodeIds = _uiCache.ActiveNodes
    .Where(kvp => _subsystemSelected.GetValueOrDefault(kvp.Value.SubsystemName))
    .Select(kvp => kvp.Key)
    .ToArray();

if (targetNodeIds.Length == 0) { ImGui.TextColored(red, "Select at least one subsystem"); return; }

// Sanitise network IDs
var specificNetworkIds = _networkIdInput
    .Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(s => s.Trim())
    .Where(s => long.TryParse(s, out _))
    .Select(long.Parse)
    .ToArray();

// Check for discarded tokens
bool hadMalformed = _networkIdInput.Split(',').Any(t => !string.IsNullOrWhiteSpace(t) && !long.TryParse(t.Trim(), out _));

// Build DTO
var dto = new DiagnosticDumpPayloadDto
{
    TransactionId   = Guid.NewGuid(),
    RequestedAt     = DateTime.UtcNow,
    TargetNodeIds   = targetNodeIds,
    DumpEvents      = _dumpEvents,
    DumpEntities    = _dumpEntities,
    DumpArchitecture = _dumpArchitecture,
    DumpLogs        = _dumpLogs,
    EventProviders  = _eventProvidersInput.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim()).ToArray(),
    UseMarkdownWrapper = _useMarkdownWrapper,
    MaxAgeHours     = _maxAgeHours,
    SeverityThreshold = _severityThreshold,
    SpecificNetworkIds = specificNetworkIds.Length > 0 ? specificNetworkIds : null,
};

string payloadJson = JsonSerializer.Serialize(dto, FdpJsonOptionsRegistry.DefaultRelaxed);
_bus.PublishManaged(new ExecuteDiagnosticDumpIntent
{
    RequestId   = dto.TransactionId,
    PayloadJson = payloadJson,
});
_operationStatus = $"Dump triggered: {dto.TransactionId:N}";
```

**NOTE about DiagnosticDumpPayloadDto fields:** Check the actual DTO definition in
`Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs` for the exact
field names — in particular `TargetNodeIds`, `SpecificNetworkIds`, `EventProviders`.
Also check if the DTO has `[JsonPropertyName]` attributes that differ from C# property names.

**Success Conditions:**

1. Unit test (headless): Render with mock `ClusterUiCache` containing 3 distinct subsystem names.
   Matrix has 3 subsystem column checkboxes.
2. Integration test: Click EXECUTE; verify `ExecuteDiagnosticDumpIntent` published to bus with
   `TargetNodeIds` matching selected columns.
3. Sanitisation test: Input `"4001, abc, , 4002"` → DTO's `SpecificNetworkIds == [4001L, 4002L]`.

---

## Task DD-P6-T02 — ClusterDiagnosticsPanel (Results Tree + Context Menus)

Add the results tree to `ClusterDiagnosticsPanel`. This continues the implementation of the
same file created in DD-P6-T01.

**The manifest should be updated via the bus.** Add a subscription to receive
`ClusterOpCompletedEvent` (or check how `ClusterUiCache` exposes completed operation manifests).
Check `ClusterUiCache` or `OrchestratorInternalEvents.cs` for events that carry the result
manifest after a dump operation completes.

**Results tree structure:**

```
[+] SimHost
    [+] node-400
        [ ] dump_20260503_entities_SimHost_400.json  [context menu]
        [ ] dump_20260503_events_SimHost_400.json    [context menu]
        [ ] dump_20260503_logs_SimHost_400.log       [context menu]
    [+] node-401
        ...
[+] CGF
    ...

[Cluster Aggregates]    <- appears after LogMergeCompletedEvent
    [ ] dump_..._logs_MERGED.log  [context menu]
```

**File entry context menu items:**

1. "Copy NAS Path" → `ImGui.SetClipboardText(entry.RelativeDest)`
2. "Copy Content" →
   ```
   var fullPath = Path.Combine(_nasBasePath, entry.RelativeDest);
   if (!File.Exists(fullPath)) { _inlineError = "File not found"; return; }
   var info = new FileInfo(fullPath);
   if (info.Length > 10 * 1024 * 1024) { _inlineError = "File too large (>10 MB)"; return; }
   ImGui.SetClipboardText(File.ReadAllText(fullPath));
   ```
3. "Open from NAS" → `Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true })`
4. "Save Local Copy As" →
   ```csharp
   async void SaveLocal()
   {
       var dest = await _fileDialogService.ShowSaveAsDialogAsync(Path.GetFileName(fullPath), "*" + Path.GetExtension(fullPath));
       if (dest != null) File.Copy(fullPath, dest, overwrite: true);
   }
   SaveLocal();
   ```

**Subsystem group context menu — "Copy Aggregated JSON":**

```csharp
if (ImGui.BeginPopupContextItem($"##ctx_{subsystemName}"))
{
    bool canCopy = !_copyInProgress;
    if (!canCopy) { ImGui.TextDisabled("Copying..."); }
    else if (ImGui.MenuItem("Copy Aggregated JSON"))
    {
        _copyInProgress = true;
        var entries = GetManifestForSubsystem(subsystemName); // List<FileManifestEntry>
        Task.Run(() => BuildAggregatedJson(subsystemName, entries, _nasBasePath))
            .ContinueWith(t =>
            {
                _pendingClipboardText = t.IsCompletedSuccessfully ? t.Result : string.Empty;
                _copyInProgress = false;
            }, TaskScheduler.Default);
    }
    ImGui.EndPopup();
}
```

**BuildAggregatedJson (background, NO ImGui calls):**
```csharp
private string BuildAggregatedJson(string subsystemName, List<FileManifestEntry> entries, string nasBasePath)
{
    var entityLists = new List<JsonElement>();
    var eventDicts  = new Dictionary<string, List<JsonElement>>();
    
    foreach (var entry in entries)
    {
        var fullPath = Path.Combine(nasBasePath, entry.RelativeDest);
        if (!File.Exists(fullPath)) continue;
        var info = new FileInfo(fullPath);
        if (info.Length > 10 * 1024 * 1024) { /* record warning */ continue; }
        
        string json = File.ReadAllText(fullPath);
        
        if (entry.RelativeDest.Contains("_entities_"))
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var el in doc.RootElement.EnumerateArray())
                    entityLists.Add(el.Clone());
        }
        else if (entry.RelativeDest.Contains("_events_"))
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!eventDicts.ContainsKey(prop.Name))
                        eventDicts[prop.Name] = new List<JsonElement>();
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        foreach (var ev in prop.Value.EnumerateArray())
                            eventDicts[prop.Name].Add(ev.Clone());
                }
        }
    }
    
    // Build result object
    using var ms = new System.IO.MemoryStream();
    using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
    writer.WriteStartObject();
    writer.WritePropertyName(subsystemName);
    if (entityLists.Count > 0)
    {
        writer.WriteStartArray();
        foreach (var el in entityLists) el.WriteTo(writer);
        writer.WriteEndArray();
    }
    else if (eventDicts.Count > 0)
    {
        writer.WriteStartObject();
        foreach (var (provider, events) in eventDicts)
        {
            writer.WritePropertyName(provider);
            writer.WriteStartArray();
            foreach (var ev in events) ev.WriteTo(writer);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
    writer.WriteEndObject();
    writer.Flush();
    
    string raw = System.Text.Encoding.UTF8.GetString(ms.ToArray());
    return JsonAestheticFormatter.FlattenNumericArrays(raw);
}
```

**IMPORTANT:** The panel needs `_nasBasePath`. Add it as a constructor parameter or read it
from `ClusterConfiguration`. The simplest approach is to add `string nasBasePath` to the
constructor so the subsystem can inject it.

**Update ClusterUiCache with manifest data:**
The panel needs to know the manifest after a dump completes. Check how `ClusterUiCache` exposes
the result — look for how `StorageProcessManager` publishes its completion status, and how the
`ClusterScenarioPanel` reads archive/scenario data. The manifest should be available via:
1. A new `IReadOnlyList<FileManifestEntry> LastDiagnosticManifest` property on `ClusterUiCache`, OR
2. Subscribe to `ClusterOpStatusEvent` (or equivalent) directly in the panel

Look at `ClusterScenarioPanel.cs` for how it gets updated data from `ClusterUiCache`.
Add whatever is simplest — preferably add a property to `ClusterUiCache` that stores the last
dump manifest.

**Success Conditions:**

1. Unit test: Context menu "Copy NAS Path" writes expected string.
2. Unit test: "Save Local Copy As" calls `ShowSaveAsDialogAsync` then `File.Copy`.
3. Edge case: "Copy Content" for missing file shows inline error.
4. Unit test: BuildAggregatedJson with 2 entity dump files → merged array under subsystem key.
5. Unit test: BuildAggregatedJson with 2 event dump files → merged provider-keyed object.
6. Async safety test: `_pendingClipboardText` is not set synchronously; polled in next render.

---

## Task DD-P6-T03 — Register ClusterDiagnosticsPanel

**Files to modify:**
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`
- `Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs` (or equivalent — find the file)

**In `OrchestratorSubsystem`:**

Add a field:
```csharp
private ClusterDiagnosticsPanel? _diagnosticsPanel;
```

In `Initialize()` (after `_uiCache` and `_bus` are set up):
```csharp
_diagnosticsPanel = new ClusterDiagnosticsPanel(
    _uiCache!,
    _bus!,
    // fileDialogService — obtain from WindowManager via a getter or pass directly
    // NasBasePath from _config
    new ImGuiFileDialogService(),  // or retrieve from a shared service
    _config.NasBasePath);
```

In `RegisterWindows(WindowManager windowManager)`:
```csharp
// Register file dialog service so it draws each frame
windowManager.SetFileDialogService(new ImGuiFileDialogService());

// Register diagnostics window
if (_diagnosticsPanel != null)
    windowManager.RegisterWindow(new DiagnosticsWindow(_diagnosticsPanel));
```

Create `Hrot/Subsystems/Hrot.Orchestrator/Windows/DiagnosticsWindow.cs`:
```csharp
using Fdp.Presentation.WindowManager;
using Hrot.Orchestrator.Panels;

namespace Hrot.Orchestrator.Windows;

internal sealed class DiagnosticsWindow : ManagedWindow
{
    private readonly ClusterDiagnosticsPanel _panel;

    public DiagnosticsWindow(ClusterDiagnosticsPanel panel)
        : base("orchestrator_diagnostics", "Diagnostics", string.Empty, WindowScope.Global)
    {
        _panel = panel;
        IsOpen = true;
    }

    protected override void DrawClientArea() => _panel.Render();
}
```

**In ExConSubsystem:**
Find the equivalent registration point and register the same panel.
ExCon already references Hrot.Orchestrator so `ClusterDiagnosticsPanel` is accessible.

**Success Conditions:**
1. Both subsystems compile with the new panel registration.
2. Panel renders in headless mode without exceptions.

---

## Task DD-P8-T03 — Merged Log Entry in ClusterDiagnosticsPanel

**File to modify:** `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterDiagnosticsPanel.cs`

In the `Initialize()` or `Subscribe()` method, add a subscription to `LogMergeCompletedEvent`:
```csharp
_bus.Subscribe<LogMergeCompletedEvent>(e => _mergedLogPath = e.NasPath);
```

Add field:
```csharp
private string? _mergedLogPath;
private bool _mergeInProgress;
```

In the results section, after the per-node tree, add:

**"Generate Merged Cluster Log" button:**
```csharp
// Only enabled when dump is complete and there are log files in manifest
bool hasLogs = _manifest.Any(e => e.RelativeDest.EndsWith(".log"));
if (!hasLogs || _mergeInProgress)
    ImGui.BeginDisabled();

if (ImGui.Button("Generate Merged Cluster Log"))
{
    var logPaths = _manifest
        .Where(e => e.RelativeDest.EndsWith(".log"))
        .Select(e => e.RelativeDest)
        .ToArray();
    _mergeInProgress = true;
    _bus.PublishManaged(new MergeLogsIntent
    {
        LogRelativePaths = logPaths,
        NasBasePath      = _nasBasePath,
        DumpTimestamp    = ExtractTimestampFromManifest(),
    });
}

if (!hasLogs || _mergeInProgress)
    ImGui.EndDisabled();
```

**Cluster Aggregates section (after merge):**
```csharp
if (_mergedLogPath != null)
{
    if (ImGui.TreeNode("Cluster Aggregates"))
    {
        var entry = new FileManifestEntry
        {
            SourceUnc    = _mergedLogPath,
            RelativeDest = Path.GetRelativePath(_nasBasePath, _mergedLogPath),
        };
        RenderFileEntry(entry);  // reuse the same context menu rendering method
        ImGui.TreePop();
    }
}
```

Also: subscribe to `LogMergeCompletedEvent` to set `_mergedLogPath` and clear `_mergeInProgress`.

**Success Conditions:**
1. Unit test: Button disabled when no log files in manifest.
2. Unit test: After `LogMergeCompletedEvent` with valid NAS path, "Cluster Aggregates" tree appears.

---

## Build Verification

Run in order:

```powershell
# 1. Build Hrot.Orchestrator (contains most new code)
Set-Location d:\Work\IOS-IG-SimHost-FDP
dotnet build Hrot\Subsystems\Hrot.Orchestrator\Hrot.Orchestrator.csproj --no-incremental 2>&1 | Select-Object -Last 8

# 2. Build ExCon
dotnet build Hrot\Subsystems\Hrot.ExCon\Hrot.ExCon.csproj --no-incremental 2>&1 | Select-Object -Last 5

# 3. Build FDP (verifies nothing broken in Presentation)
Set-Location d:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build Engine\Fdp.Presentation\Fdp.Presentation.csproj --no-incremental 2>&1 | Select-Object -Last 5

# 4. Run Orchestrator tests
Set-Location d:\Work\IOS-IG-SimHost-FDP
dotnet test Hrot\Subsystems\Hrot.Orchestrator.Tests\Hrot.Orchestrator.Tests.csproj 2>&1 | Select-Object -Last 5
```

---

## Additional Notes

### ClusterUiCache manifest storage

The cleanest approach to expose dump results in the panel is to add a property to `ClusterUiCache`:
```csharp
public IReadOnlyList<FileManifestEntry> LastDiagnosticManifest { get; private set; } = Array.Empty<FileManifestEntry>();
```

Update it when you observe a `ClusterOpStatus` event for `DumpDiagnostics`. Look at how
`ClusterScenarioPanel` observes status events to find the correct event type.

Alternatively, subscribe directly in `ClusterDiagnosticsPanel` to the same event — either
approach is acceptable.

### IFileDialogService ownership

The `ImGuiFileDialogService` instance must be shared between:
1. `ClusterDiagnosticsPanel` (calls `ShowSaveAsDialogAsync`)
2. `WindowManager` (calls `Draw()` each frame)

Pass one shared instance to both. In `RegisterWindows`, call `windowManager.SetFileDialogService(service)`.
Store the service in the subsystem class and inject it into both.

### DiagnosticLogMergeWorker registration

Instantiate it in `OrchestratorSubsystem.Initialize()` and call `Subscribe(bus)` or equivalent.
The worker subscribes to `MergeLogsIntent` and publishes `LogMergeCompletedEvent`.
Add it as a field `private DiagnosticLogMergeWorker? _mergeWorker;` and dispose in `Shutdown()`.

---

## Report Template

When done, create `d:\Work\IOS-IG-SimHost-FDP\.dev\dump-diag\reports\BATCH-05-REPORT.md` with:
- Status: COMPLETE or PARTIAL
- Files created / modified
- Test results (pass/fail counts, per project)
- Any deviations from spec (with justification)
- Pre-existing test failures (if any)
- Blockers or design questions
