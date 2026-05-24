# BATCH-04 — Phase 5 (Remaining) + Phase 7

**Batch:** BATCH-04
**Assigned to:** Developer
**Tasks:** DD-P5-T01, DD-P5-T02, DD-P5-T04, DD-P5-T05, DD-P7-T01, DD-P7-T02, DD-P7-T03
**Prerequisites:** BATCH-03 approved and merged

---

## Context

The following are already done and must NOT be modified:
- `DD-P5-T03`: `HrotNodeConfig.LogDirectory` already added (`= string.Empty` default)
- `DiagnosticsConsensusAggregator` and `DiagnosticsDumpProcessManager` in `Hrot.Orchestrator`
- Phase 1–4 all complete
- `NodeOpType.CollectDiagnostics = 28` is the enum value (not `DumpDiagnostics` due to IDL scoping)
- `ClusterOpType.DumpDiagnostics = 16`
- `DiagnosticDumpPayloadDto` in `Hrot.Network.Orchestration.Payloads`
- `ExecuteDiagnosticDumpIntent` in `Fdp.Toolkits.Orchestration.Events`

---

## Key Architectural Rules (from DEV-GUIDE.md)

- `Hrot.Common` is headless — NO ImGui, NO Raylib imports
- `FdpJsonOptionsRegistry.DefaultRelaxed` and `Indented` are the only JSON options singletons to use
- `Fdp.Toolkits` is accessible from `Hrot.Common` via the transitive chain:
  `Hrot.Common` -> `Hrot.Network.Orchestration` -> `Fdp.Toolkits` (verify; add direct ref if needed)
- `NodeOpType.CollectDiagnostics` (not `DumpDiagnostics`) for the node-side enum value
- `ClusterOpType.DumpDiagnostics` for the orchestrator-side enum value

---

## Task DD-P5-T01 — NLog File Target, Layout, and Auto-Rotation

**File:** `Hrot/Runner/Hrot.ClusterRunner/Program.cs`

After the existing `logConfig.AddRule(LogLevel.Trace, ...)` lines and BEFORE
`LogManager.Configuration = logConfig`, add a file target only when
`config.LogDirectory` is not empty:

```csharp
// Add NLog file target when LogDirectory is configured
string resolvedLogDir = string.IsNullOrWhiteSpace(config.LogDirectory)
    ? Path.Combine(AppContext.BaseDirectory, "logs")
    : Path.GetFullPath(config.LogDirectory);
Directory.CreateDirectory(resolvedLogDir);
NLog.MappedDiagnosticsLogicalContext.Set("nodeId", config.NodeId.ToString());

var fileTarget = new FileTarget("logFile")
{
    Layout         = "[${longdate}] [${level:uppercase=true}] [${logger:shortName=true}] [Node-${event-properties:item=nodeId}] ${message} ${exception:format=tostring}",
    FileName       = Path.Combine(resolvedLogDir, "${appdomain:format={0\\}}_${event-properties:item=nodeId}.log"),
    ArchiveFileName = Path.Combine(resolvedLogDir, "${appdomain:format={0\\}}_${event-properties:item=nodeId}.{#}.log"),
    ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.Rolling,
    MaxArchiveFiles  = 10,
    ArchiveAboveSize = 50 * 1024 * 1024,
    KeepFileOpen     = true,
    ConcurrentWrites = false,
};
logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
```

**NOTE:** The subsystem name for file naming comes from `config.RequestedSubsystems` (joining
with underscore if multiple). Use an NLog variable or `${appdomain:format={0\}}` which resolves
to the app domain name — or simply build a static string from `string.Join("_", config.RequestedSubsystems)`.

The simpler and correct approach: build the filename as a string expression evaluated at
config-time (before setting LogManager.Configuration), using `config.RequestedSubsystems` joined:

```csharp
string subsystemTag = config.RequestedSubsystems.Count > 0
    ? string.Join("_", config.RequestedSubsystems)
    : "Hrot";
var fileTarget = new FileTarget("logFile")
{
    Layout           = "[${longdate}] [${level:uppercase=true}] [${logger:shortName=true}] [Node-${mdlc:nodeId}] ${message} ${exception:format=tostring}",
    FileName         = Path.Combine(resolvedLogDir, $"{subsystemTag}_{config.NodeId}.log"),
    ArchiveFileName  = Path.Combine(resolvedLogDir, $"{subsystemTag}_{config.NodeId}.{{#}}.log"),
    ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.Rolling,
    MaxArchiveFiles  = 10,
    ArchiveAboveSize = 50 * 1024 * 1024,
    KeepFileOpen     = true,
    ConcurrentWrites = false,
};
logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
```

Also, store `resolvedLogDir` so it can be passed to `HrotNodeConfig.LogDirectory` during
subsystem bootstrap (needed by DD-P5-T04).

**Constraints:**
- Console layout (`logConsole.Layout`) must NOT be modified
- `NLogMessageLogTarget.SharedInstance` registration must NOT be modified
- `MappedDiagnosticsLogicalContext.Set("nodeId", ...)` should be called once at startup
- `Directory.CreateDirectory` must be called before constructing the `FileTarget`

**Success Conditions:**

1. Unit test (in `Hrot.ClusterRunner.Tests` or a new `Hrot.ClusterRunner.Tests` project if
   tests exist): After setup, `LogManager.Configuration` has at least one `FileTarget` rule.
2. The layout contains `[${longdate}]` and `[${level:uppercase=true}]` strings.
3. No changes to the console target layout.

---

## Task DD-P5-T02 — HrotRunnerConfiguration `--log-dir` Option

**File:** `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`

Add the `--log-dir` option property after the existing `NetworkProtocol` option:

```csharp
/// <summary>Directory for NLog file target output. Defaults to <c>AppContext.BaseDirectory\logs</c>.</summary>
[Option("log-dir", Required = false, HelpText = "Directory for log file output. Defaults to <AppBase>\\logs.")]
public string LogDirectory { get; set; } = string.Empty;
```

The default (`string.Empty`) means "not configured" — the resolved value is computed in `Program.cs`
(already described in DD-P5-T01 above).

**Success Conditions:**

1. Unit test: Parsing `--log-dir C:\MyLogs --mode simhost` sets `LogDirectory == "C:\\MyLogs"`.
2. Unit test: Parsing `--mode simhost` (without `--log-dir`) sets `LogDirectory == string.Empty`.

**Note:** Resolving the actual path (`Path.GetFullPath`) and applying a default already
happens in `Program.cs` (DD-P5-T01). The property itself is raw.

---

## Task DD-P5-T04 — DiagnosticsDumpClusterOpHandler

**File to create:** `Hrot/Engine/Hrot.Common/Diagnostics/DiagnosticsDumpClusterOpHandler.cs`

This is the node-side 2PC participant that collects all diagnostic data and writes it to
`LocalTempRoot\dumps\{transactionId:N}\` when `PrepareAsync` is called.

**Namespace:** `Hrot.Common.Diagnostics`

**Required usings:**
```
using Fdp.Core.Diagnostics;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Diagnostics;       // IEntityStateExtractionService
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Serialization;     // JsonAestheticFormatter
using Hrot.Core.Diagnostics;         // ILogArchiveExtractionService
using Hrot.Core.Infrastructure;      // HrotNodeConfig
using Hrot.Network.Orchestration.Payloads; // DiagnosticDumpPayloadDto
using Fdp.ModuleHost.Diagnostics;    // IArchitectureDiagnosticsService
using System.Text.Json;
```

**Constructor:**
```csharp
public DiagnosticsDumpClusterOpHandler(
    IDiagnosticEventHistoryService eventHistoryService,
    IArchitectureDiagnosticsService architectureService,
    IEntityStateExtractionService entityExtractionService,
    ILogArchiveExtractionService logExtractionService,
    HrotNodeConfig config)
```

**CanHandle:** returns `true` for `NodeOpType.CollectDiagnostics`

**PrepareAsync:**
- Immediately delegates to `Task.Run(..., TaskCreationOptions.LongRunning)`, returning the
  background task
- Background task body:
  1. Deserialise `DiagnosticDumpPayloadDto dto` from `intent.PayloadJson` using
     `FdpJsonOptionsRegistry.DefaultRelaxed`
  2. If `dto.TargetNodeIds != null && !dto.TargetNodeIds.Contains(_config.NodeId)`:
     return an empty `List<FileManifestEntry>()`
  3. `string timestamp = dto.RequestedAt.ToString("yyyyMMdd_HHmmss")`
  4. `string outputDir = Path.Combine(_config.LocalTempRoot, "dumps", dto.TransactionId.ToString("N"))`
  5. `Directory.CreateDirectory(outputDir)`
  6. For **entities** (`dto.DumpEntities`):
     - Call `_entityExtractionService.ExtractAll()`
     - Serialise using `FdpJsonOptionsRegistry.Indented`
     - Post-process with `JsonAestheticFormatter.FlattenNumericArrays`
     - If `dto.UseMarkdownWrapper`: wrap: ` ```json\n{content}\n``` `
     - File: `dump_{timestamp}_entities_{_config.SubsystemName}_{_config.NodeId}.json`
  7. For **architecture** (`dto.DumpArchitecture`):
     - Call `_architectureService.CaptureSnapshot()`
     - Serialise as `object` using `FdpJsonOptionsRegistry.Indented`
     - Post-process with `JsonAestheticFormatter.FlattenNumericArrays`
     - If `dto.UseMarkdownWrapper`: wrap in markdown code block
     - File: `dump_{timestamp}_architecture_{_config.SubsystemName}_{_config.NodeId}.json`
  8. For **events** (`dto.DumpEvents`):
     - Providers: `dto.EventProviders` (may be null/empty → use `new string?[] { null }`)
     - Build `Dictionary<string, List<CapturedEventDto>>` keyed by provider name
       (use `"all"` for null provider)
     - Serialise the dictionary using `FdpJsonOptionsRegistry.Indented`
     - Post-process with `JsonAestheticFormatter.FlattenNumericArrays`
     - If `dto.UseMarkdownWrapper`: wrap
     - File: `dump_{timestamp}_events_{_config.SubsystemName}_{_config.NodeId}.json`
  9. For **logs** (`dto.DumpLogs`):
     - Build `string outputLogPath = Path.Combine(outputDir, $"dump_{timestamp}_logs_{_config.SubsystemName}_{_config.NodeId}.log")`
     - Call `await _logExtractionService.ExtractAsync(_config.LogDirectory, outputLogPath, dto.MaxAgeHours, dto.SeverityThreshold, ct)`
     - Add entry only if `_config.LogDirectory` is not empty AND the output file exists after extraction
  10. For each output file: add `new FileManifestEntry { SourceUnc = outputFile, RelativeDest = $"dumps/{dto.TransactionId:N}/{fileName}" }` to the result list
  11. Return the list as `object?`

**Commit:** no-op (return `Task.CompletedTask`)

**Abort:**
- `if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true)`
- Store `_outputDir` as a field set in `PrepareAsync`

**File Naming Pattern:**
`dump_{yyyyMMdd_HHmmss}_{kind}_{SubsystemName}_{NodeId}.{ext}`
where `ext` is `json` for all except logs (`.log`)

**Important:** `NodeOpType.CollectDiagnostics` (not `DumpDiagnostics`) is the enum value to use.

**Registration in Bootstrappers:**

Find all bootstrappers that call `clusterSlave.RegisterHandler(...)` and add the new handler.
The relevant bootstrapper files:
- `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`
- Look for CGF bootstrapper: `Hrot/Subsystems/Hrot.CGF/` (check for a similar `NodeBootstrapper.cs` or `CgfBootstrapper.cs`)
- Look for IG and ExCon bootstrappers similarly

Pattern:
```csharp
// Wire DiagnosticsDumpClusterOpHandler for cluster-wide diagnostic dumps
if (diagnosticServices != null)
    clusterSlave.RegisterHandler(new DiagnosticsDumpClusterOpHandler(
        diagnosticServices.EventHistory,
        diagnosticServices.Architecture,
        diagnosticServices.EntityExtraction,
        diagnosticServices.LogExtraction,
        hrotNodeConfig));
```

**NOTE:** Since the diagnostic services may not be wired into every bootstrapper's existing
parameter list, the simplest approach is to add an optional parameter for the handler itself
or for a services container. Check existing patterns. In `NodeBootstrapper.BuildOrchestration`
there are many optional parameters — add an optional `DiagnosticsDumpClusterOpHandler? diagnosticsDumpHandler = null` parameter and do:
```csharp
if (diagnosticsDumpHandler != null)
    clusterSlave.RegisterHandler(diagnosticsDumpHandler);
```

**Hrot.Common.csproj project references:** Verify `Hrot.Common` has access to:
- `IDiagnosticEventHistoryService` — in `Fdp.Core.Diagnostics` (from `Fdp.Core`)
- `IArchitectureDiagnosticsService` — in `Fdp.ModuleHost.Diagnostics` (from `Fdp.ModuleHost`)
- `IEntityStateExtractionService` — in `Fdp.Toolkit.Diagnostics` (from `Fdp.Toolkits`)
- `ILogArchiveExtractionService` — in `Hrot.Core.Diagnostics` (from `Hrot.Core`)
- `DiagnosticDumpPayloadDto` — in `Hrot.Network.Orchestration.Payloads` (from `Hrot.Network.Orchestration`)
- `FdpJsonOptionsRegistry` — in `Fdp.Core.Serialization` (from `Fdp.Core`)
- `JsonAestheticFormatter` — in `Fdp.Toolkit.Serialization` (from `Fdp.Toolkits`)

Check `Hrot.Common.csproj` — add any missing project references.

**Success Conditions:**

1. Unit test: Mock all 4 services + `HrotNodeConfig` with `NodeId=5`, `SubsystemName="SimHost"`,
   `LocalTempRoot=TestContext.TempDir`. Call `PrepareAsync`. Await task. Assert returned
   `List<FileManifestEntry>` has one entry per enabled dump kind.

2. Unit test: `TargetNodeIds = [999]`, `NodeId = 5` → task returns empty list.

3. Unit test: `Abort` deletes the `outputDir` created during `PrepareAsync`.

4. Unit test: `DumpEntities=true, DumpEvents=false, DumpArchitecture=false, DumpLogs=false` →
   exactly 1 entry in result.

5. Unit test: `UseMarkdownWrapper=true` → entity dump file content starts with ` ```json`.

---

## Task DD-P5-T05 — Node LocalTempRoot Isolation and ClusterConfiguration NasBasePath

### Part A: ClusterConfiguration.NasBasePath

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterConfiguration.cs`

Add the following property after `TransactionHistoryCapacity`:
```csharp
/// <summary>
/// Base path of the shared NAS directory used by process managers to pull
/// files from nodes.  Must differ from each node's <c>LocalTempRoot</c> to
/// prevent source == destination errors.  Default is for single-machine dev use only.
/// </summary>
public string NasBasePath { get; init; } = @"C:\FDP_Temp\shared";
```

### Part B: Wire NasBasePath in OrchestratorSubsystem

**File:** `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`

Find where `StorageProcessManager` is constructed and replace any hardcoded
`OrchestrationConstants.DefaultStagingDirectory` (used as NAS base) with `_config.NasBasePath`.
Also wire `_config.NasBasePath` into `AssetInventoryProcessManager`, `AssetPrefetchProcessManager`,
and `DiagnosticsDumpProcessManager`.

You need to read `OrchestratorSubsystem.cs` to see the actual construction pattern.

### Part C: Node LocalTempRoot Namespacing

**Files:** All node bootstrapper call sites where `HrotNodeConfig` is constructed or where
`localTempRoot` is passed. Find in:
- `Hrot/Runner/Hrot.ClusterRunner/Program.cs`
- Any subsystem's `OnLoad` or initialization method

At each point where `HrotNodeConfig.LocalTempRoot` is set, apply:
```csharp
var baseTempRoot = string.IsNullOrEmpty(rawConfig.LocalTempRoot)
    ? OrchestrationConstants.DefaultStagingDirectory
    : rawConfig.LocalTempRoot;
hrotNodeConfig.LocalTempRoot = Path.Combine(baseTempRoot, "nodes", $"node-{localNodeId}");
```

This namespacing must happen BEFORE any module initialization.

**Success Conditions:**

1. Unit test: `HrotNodeConfig.LocalTempRoot` ends with `nodes\node-400` when `NodeId = 400`.
2. Unit test: Two configs with `NodeId=400` and `NodeId=401` produce different `LocalTempRoot` values.
3. Unit test: `DiagnosticsDumpProcessManager` with `NasBasePath = "C:\\NAS"` and
   `RelativeDest = "dumps\\foo.json"` → destination path is `"C:\\NAS\\dumps\\foo.json"`.

---

## Task DD-P7-T01 — IFileDialogService Interface

**File to create:** `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IFileDialogService.cs`

```csharp
using System.Threading.Tasks;

namespace Fdp.Presentation.Abstractions;

/// <summary>
/// Service for presenting a modal "Save As" file dialog to the user.
/// The dialog is rendered by the <see cref="Fdp.Presentation.WindowManager.WindowManager"/>
/// each frame; it resolves asynchronously when the user confirms or cancels.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Displays a "Save As" modal dialog.
    /// </summary>
    /// <param name="defaultFileName">Pre-populated file name in the dialog's input field.</param>
    /// <param name="extensionFilter">File extension filter string, e.g. <c>"*.json"</c>.</param>
    /// <returns>
    /// The full save path chosen by the user, or <c>null</c> if the user cancelled
    /// or the dialog was superseded by a subsequent call.
    /// </returns>
    Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter);
}
```

**Success Conditions:**

1. Interface compiles and is accessible from `Hrot.Orchestrator`.

---

## Task DD-P7-T02 — ImGuiFileDialogService Implementation

**File to create:** `FDP/Engine/Fdp.Presentation/ImGui/Panels/ImGuiFileDialogService.cs`

**Namespace:** `Fdp.Presentation.Windows` (or `Fdp.Presentation.Panels` — match existing panel namespace)

**Implements:** `IFileDialogService`, and exposes a `void Draw()` method for per-frame rendering.

**Key state fields:**
```csharp
private bool _isOpen;
private string _currentDirectory = Directory.GetCurrentDirectory();
private string _fileNameBuffer = string.Empty;
private string _extensionFilter = "*";
private TaskCompletionSource<string?>? _tcs;
```

**ShowSaveAsDialogAsync:**
```csharp
public Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter)
{
    // Cancel any pending dialog
    _tcs?.TrySetCanceled();

    _fileNameBuffer = defaultFileName;
    _extensionFilter = extensionFilter;
    _currentDirectory = Directory.GetCurrentDirectory();
    _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    _isOpen = true;
    return _tcs.Task;
}
```

**Draw() method (simplified modal):**

```csharp
public void Draw()
{
    if (!_isOpen) return;

    bool open = true;
    if (Gui.BeginPopupModal("Save As##FileDialog", ref open,
        ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize | ImGuiNET.ImGuiWindowFlags.NoSavedSettings))
    {
        // Current directory label
        Gui.Text("Directory: " + _currentDirectory);

        // Up button
        if (Gui.Button("Up") && Directory.GetParent(_currentDirectory) is { } parent)
            _currentDirectory = parent.FullName;

        Gui.Separator();

        // List dirs and matching files
        Gui.BeginChild("##filelist", new System.Numerics.Vector2(400, 200));
        try
        {
            foreach (var dir in Directory.GetDirectories(_currentDirectory))
            {
                string dirName = Path.GetFileName(dir);
                if (Gui.Selectable("[DIR] " + dirName, false, ImGuiNET.ImGuiSelectableFlags.AllowDoubleClick))
                    if (Gui.IsMouseDoubleClicked(0))
                        _currentDirectory = dir;
            }
            foreach (var file in Directory.GetFiles(_currentDirectory, _extensionFilter))
            {
                string fileName = Path.GetFileName(file);
                if (Gui.Selectable(fileName, false))
                    _fileNameBuffer = fileName;
            }
        }
        catch (UnauthorizedAccessException) { Gui.TextDisabled("[Access denied]"); }
        Gui.EndChild();

        Gui.Separator();

        // File name input
        var buf = System.Text.Encoding.UTF8.GetBytes(_fileNameBuffer.PadRight(256, '\0'));
        Array.Resize(ref buf, 256);
        if (Gui.InputText("File name", buf, (uint)buf.Length))
            _fileNameBuffer = System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');

        if (Gui.Button("Save"))
        {
            string result = Path.Combine(_currentDirectory, _fileNameBuffer);
            _isOpen = false;
            _tcs?.TrySetResult(result);
            _tcs = null;
            Gui.CloseCurrentPopup();
        }
        Gui.SameLine();
        if (Gui.Button("Cancel"))
        {
            _isOpen = false;
            _tcs?.TrySetResult(null);
            _tcs = null;
            Gui.CloseCurrentPopup();
        }

        if (!open) // User clicked the X
        {
            _isOpen = false;
            _tcs?.TrySetResult(null);
            _tcs = null;
        }

        Gui.EndPopup();
    }
    else if (_isOpen)
    {
        // First frame: open the popup
        Gui.OpenPopup("Save As##FileDialog");
    }
}
```

**IMPORTANT NOTES:**
- Use `ImGuiNET.ImGui` alias as `Gui` (check existing panel files to confirm the alias used —
  `EventBrowserPanel.cs` uses `private static ImGuiNET.ImGui Gui => null!;` pattern or equivalent)
- Use `Path.Combine` for all path construction — NEVER raw string concatenation
- The dialog starts from `Directory.GetCurrentDirectory()`, not from a drive root
- Only ONE dialog at a time; second call cancels the first via `TrySetCanceled()`

**Success Conditions:**

1. Unit test (headless): calling `ShowSaveAsDialogAsync` twice — first task is cancelled.
2. Unit test (headless): `ShowSaveAsDialogAsync` returns pending task before `Draw()` completes.
3. Unit test (mock): Verify that `Draw()` calls `OpenPopup` on first frame after `ShowSaveAsDialogAsync`.

---

## Task DD-P7-T03 — Wire ImGuiFileDialogService into WindowManager

**File:** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`

Add a field:
```csharp
private IFileDialogService? _fileDialogService;
```

Add a public setter or constructor parameter (prefer a `SetFileDialogService(IFileDialogService service)` method to avoid breaking existing constructors):
```csharp
/// <summary>
/// Registers the file dialog service to be drawn each frame AFTER all other windows.
/// </summary>
public void SetFileDialogService(IFileDialogService service)
{
    _fileDialogService = service;
}
```

In `Render()`, at the very end (after `_statusBar.Render(...)`), add:
```csharp
// Draw file dialog service last so the modal overlays all other windows
(_fileDialogService as ImGuiFileDialogService)?.Draw();
```

Add the appropriate `using Fdp.Presentation.Abstractions;` and `using Fdp.Presentation.Windows;`
(or wherever `ImGuiFileDialogService` lives).

**Registration:** In each subsystem that uses the Diagnostics panel (Orchestrator, ExCon,
SimHost), after constructing `WindowManager`, call:
```csharp
windowManager.SetFileDialogService(new ImGuiFileDialogService());
```

For this batch, only add the `SetFileDialogService` method and the `Render()` call.
The actual registration in subsystems can be done in BATCH-05 when the panel is wired.

**Success Conditions:**

1. `WindowManager.SetFileDialogService` compiles and the `Render()` method invokes `Draw()`.
2. Existing `WindowManager` tests pass (regression).

---

## Build Verification

After implementing all tasks, run:

```powershell
# Build FDP solution
cd d:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln --no-incremental 2>&1 | Select-Object -Last 15

# Build Hrot solution
cd d:\Work\IOS-IG-SimHost-FDP
dotnet build Hrot\Subsystems\Hrot.Orchestrator\Hrot.Orchestrator.csproj 2>&1 | Select-Object -Last 15
dotnet build Hrot\Engine\Hrot.Common\Hrot.Common.csproj 2>&1 | Select-Object -Last 15

# Run tests
cd d:\Work\IOS-IG-SimHost-FDP\FDP
dotnet test Engine\Fdp.Presentation.Tests\Fdp.Presentation.Tests.csproj --no-build 2>&1 | Select-Object -Last 5
cd d:\Work\IOS-IG-SimHost-FDP
dotnet test Hrot\Runner\Hrot.ClusterRunner.Tests\Hrot.ClusterRunner.Tests.csproj --no-build 2>&1 | Select-Object -Last 5
dotnet test Hrot\Subsystems\Hrot.Orchestrator.Tests\Hrot.Orchestrator.Tests.csproj --no-build 2>&1 | Select-Object -Last 5
dotnet test Hrot\Engine\Hrot.Common.Tests\Hrot.Common.Tests.csproj --no-build 2>&1 | Select-Object -Last 5
```

---

## Report Template

When done, create `d:\Work\IOS-IG-SimHost-FDP\.dev\dump-diag\reports\BATCH-04-REPORT.md` with:
- Status: COMPLETE or PARTIAL
- Files created / modified
- Test results (pass/fail counts)
- Any deviations from spec (with justification)
- Any blockers or questions
