# BATCH-23: TASK-ED-004 + TASK-ED-005 -- Debug Windows + Reload Services

**Batch Number:** BATCH-23
**Tasks:** TASK-ED-004, TASK-ED-005
**Phase:** 6 -- Editor
**Estimated Effort:** 4-5 days
**Priority:** HIGH
**Dependencies:** BATCH-22 (ED-002/ED-003 infrastructure)

---

## 0. Onboarding

### Required Reading

1. `.dev/blueprints-1/batches/BATCH-23-INSTRUCTIONS.md` (this file)
2. `.dev/blueprints-1/TASK-DETAIL.md` §ED-004 (Debug/Watch/Callstack/HotReloadLog), §ED-005 (QuickReload/FullRebuild)
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorWindowBase.cs` -- base class
4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/ReloadInfo.cs` -- ReloadCompletedInfo, ReloadSource
5. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs` -- IBlueprintDebugSession interface
6. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorModule.cs` -- existing module

### IMPORTANT: Namespace conflicts

`Hrot.Editor.ReloadSource` and `Hrot.Blueprints.Editor.ReloadSource` are DIFFERENT types.
All Blueprint editor code uses `Hrot.Blueprints.Editor.ReloadSource` -- do NOT use the one from `Hrot.Editor`.

### Report Submission

`.dev/blueprints-1/reports/BATCH-23-REPORT.md`

---

## 1. TASK-ED-004: Debug Panel, Watch Panel, Callstack, Hot Reload Log

### 1.1 ReloadLogEntry (data model -- testable)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/ReloadLogEntry.cs`:
```csharp
namespace Hrot.Blueprints.Editor.Debug;

public sealed record ReloadLogEntry(
    DateTime Timestamp,
    ReloadSource Source,
    bool Succeeded,
    string? Message,
    long DurationMs);
```

### 1.2 HotReloadLogModel (ring buffer -- testable)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/HotReloadLogModel.cs`:
```csharp
namespace Hrot.Blueprints.Editor.Debug;

/// <summary>Ring-buffer model for hot reload log entries. Max 1000 entries.</summary>
public sealed class HotReloadLogModel
{
    public const int MaxEntries = 1000;
    private readonly Queue<ReloadLogEntry> _entries = new(MaxEntries + 1);

    public IReadOnlyCollection<ReloadLogEntry> Entries => _entries;
    public int Count => _entries.Count;

    public void AddEntry(ReloadLogEntry entry)
    {
        _entries.Enqueue(entry);
        if (_entries.Count > MaxEntries)
            _entries.Dequeue();
    }

    public void Clear() => _entries.Clear();
}
```

### 1.3 DebugPanelWindow (skeleton)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/DebugPanelWindow.cs`:
```csharp
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

public sealed class DebugPanelWindow : BlueprintEditorWindowBase
{
    private readonly IBlueprintDebugSession _session;

    public override string Title => _session.IsPaused ? "Debug [PAUSED]" : "Debug";

    public DebugPanelWindow(IBlueprintDebugSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public override void DrawUI()
    {
        // ImGui rendering: pause indicator, breakpoint list, step buttons.
        // Requires ImGui runtime. Stub for Slice 1.
    }

    public override void OnActivated()   { }
    public override void OnDeactivated() { }
}
```

### 1.4 WatchPanelWindow (skeleton with event subscription)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/WatchPanelWindow.cs`:
```csharp
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

public sealed class WatchPanelWindow : BlueprintEditorWindowBase
{
    private readonly IBlueprintDebugSession _session;

    public override string Title => "Watches";

    public WatchPanelWindow(IBlueprintDebugSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public override void OnActivated()
        => _session.OnPinValueChanged += HandlePinValueChanged;

    public override void OnDeactivated()
        => _session.OnPinValueChanged -= HandlePinValueChanged;

    private void HandlePinValueChanged(PinValueChanged evt) { /* refresh row data */ }

    public override void DrawUI()
    {
        // ImGui table: Name, Type, Value, Tick, Stale? -- requires ImGui runtime.
    }
}
```

### 1.5 CallstackWindow (skeleton)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/CallstackWindow.cs`:
```csharp
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

public sealed class CallstackWindow : BlueprintEditorWindowBase
{
    private readonly IBlueprintDebugSession _session;
    private readonly EditorSelectionStore _selectionStore;

    public override string Title => "Callstack";

    public CallstackWindow(IBlueprintDebugSession session, EditorSelectionStore selectionStore)
    {
        _session        = session        ?? throw new ArgumentNullException(nameof(session));
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
    }

    public override void DrawUI()
    {
        // ImGui list of GetActiveEntities() node history -- requires ImGui runtime.
    }
}
```

### 1.6 HotReloadLogWindow (skeleton + model)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/HotReloadLogWindow.cs`:
```csharp
namespace Hrot.Blueprints.Editor.Debug;

public sealed class HotReloadLogWindow : BlueprintEditorWindowBase
{
    public HotReloadLogModel Model { get; } = new();

    public override string Title => "Hot Reload Log";

    public void OnReloadCompleted(ReloadCompletedInfo info)
    {
        Model.AddEntry(new ReloadLogEntry(
            Timestamp:  DateTime.UtcNow,
            Source:     info.Source,
            Succeeded:  true,
            Message:    $"{info.ReloadedAssetIds.Length} asset(s) reloaded in {info.DurationMs}ms",
            DurationMs: info.DurationMs));
    }

    public void OnReloadFailed(string message, ReloadSource source)
    {
        Model.AddEntry(new ReloadLogEntry(
            Timestamp:  DateTime.UtcNow,
            Source:     source,
            Succeeded:  false,
            Message:    message,
            DurationMs: 0));
    }

    public override void DrawUI()
    {
        // ImGui scrollable table with Clear button -- requires ImGui runtime.
    }
}
```

---

## 2. TASK-ED-005: Quick Reload and Full Rebuild Services

### 2.1 QuickReloadResult

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/QuickReloadResult.cs`:
```csharp
namespace Hrot.Blueprints.Editor.Reload;

public sealed record QuickReloadResult(
    bool Succeeded,
    string? ErrorMessage,
    long DurationMs);
```

### 2.2 FullRebuildResult

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/FullRebuildResult.cs`:
```csharp
namespace Hrot.Blueprints.Editor.Reload;

public sealed record FullRebuildResult(
    bool Succeeded,
    int ExitCode,
    long DurationMs);
```

### 2.3 QuickReloadService (stub)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/QuickReloadService.cs`:

NOTE: ED-005 calls for BuildSiblingSignatures + registrar invocation + ApplyQuickReload. These deep integration steps require live ALC + IAssetCatalog + compiler pipeline. For Slice 1, implement the shape/interface with stubs, not a live implementation. The full implementation is deferred to Slice 2 per Q-16.2. See TASK-DETAIL.md §ED-005.

```csharp
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Debug;
using System.Diagnostics;

namespace Hrot.Blueprints.Editor.Reload;

public sealed class QuickReloadService
{
    private readonly IAssetCatalog _catalog;
    private readonly EditorState _editorState;
    private readonly IBlueprintDebugSession? _session;
    private readonly IOutputConsole _outputConsole;

    // Internal test accessor: signatures built for the last reload.
    public IReadOnlyList<BlueprintSignature>? LastSignaturesUsedForTesting { get; private set; }

    public QuickReloadService(
        IAssetCatalog catalog,
        EditorState editorState,
        IOutputConsole outputConsole,
        IBlueprintDebugSession? session = null)
    {
        _catalog        = catalog        ?? throw new ArgumentNullException(nameof(catalog));
        _editorState    = editorState    ?? throw new ArgumentNullException(nameof(editorState));
        _outputConsole  = outputConsole  ?? throw new ArgumentNullException(nameof(outputConsole));
        _session        = session;
    }

    /// <summary>
    /// Triggers an in-memory quick reload for <paramref name="asset"/>.
    /// Slice 1: logs the intent and returns a stub result.
    /// </summary>
    public Task<QuickReloadResult> TriggerAsync(BlueprintAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        var sw = Stopwatch.StartNew();
        _outputConsole.LogInfo($"Quick reload requested for asset {asset.AssetId}.");
        sw.Stop();
        return Task.FromResult(new QuickReloadResult(
            Succeeded: false,
            ErrorMessage: "QuickReload pipeline not yet wired (Slice 1 stub).",
            DurationMs: sw.ElapsedMilliseconds));
    }
}
```

**IMPORTANT:** Before writing this, find the `BlueprintAsset` type and `BlueprintSignature` type. If `BlueprintSignature` doesn't exist, use `string` as a placeholder. Check `Fdp.Toolkit.Blueprints` namespace.

### 2.4 FullRebuildService (stub)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/FullRebuildService.cs`:
```csharp
using System.Diagnostics;

namespace Hrot.Blueprints.Editor.Reload;

public sealed class FullRebuildService
{
    private readonly IOutputConsole _outputConsole;
    private readonly string _buildTarget;

    public bool PendingDrainAfterBuild { get; private set; }

    public FullRebuildService(IOutputConsole outputConsole, string buildTarget = "")
    {
        _outputConsole = outputConsole ?? throw new ArgumentNullException(nameof(outputConsole));
        _buildTarget   = buildTarget;
    }

    public async Task<FullRebuildResult> TriggerAsync()
    {
        var sw = Stopwatch.StartNew();
        _outputConsole.LogInfo("Starting full rebuild...");

        var args = string.IsNullOrEmpty(_buildTarget)
            ? "build"
            : $"build {_buildTarget}";

        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            sw.Stop();
            return new FullRebuildResult(false, -1, sw.ElapsedMilliseconds);
        }

        string stdout = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        sw.Stop();

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            _outputConsole.LogInfo(line.TrimEnd());

        bool success = proc.ExitCode == 0;
        if (success) PendingDrainAfterBuild = true;

        return new FullRebuildResult(success, proc.ExitCode, sw.ElapsedMilliseconds);
    }
}
```

---

## 3. Tests Required

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/HotReloadLogModelTests.cs`:

**SC1: `HotReloadLogModel_AddEntry_IncreasesCount`**
- Add one entry. Assert `Model.Count == 1`.

**SC2: `HotReloadLogModel_Add_BeyondMax_Evicts_Oldest`**
- Add `MaxEntries + 1` entries. Assert `Model.Count == MaxEntries`. Assert first entry is NOT the oldest.

**SC3: `HotReloadLogModel_Clear_ResetsCount`**
- Add 5 entries. `Clear()`. Assert `Count == 0`.

**SC4: `HotReloadLogWindow_OnReloadCompleted_AddsEntry`**
- Create `HotReloadLogWindow`. Call `OnReloadCompleted(new ReloadCompletedInfo(QuickReloadViaApi, [Guid.NewGuid()], null, 42))`.
- Assert `Model.Count == 1`. Assert `Model.Entries.First().Succeeded == true`.

**SC5: `HotReloadLogWindow_OnReloadFailed_AddsFailedEntry`**
- Call `OnReloadFailed("build error", QuickReloadViaApi)`. Assert `Model.Entries.First().Succeeded == false`.

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/DebugWindowsTests.cs`:

**SC1: `DebugPanelWindow_Title_Reflects_PauseState`**
- Create `MockDebugSession` that returns `IsPaused = false`. Title should NOT contain "PAUSED".
- Set `IsPaused = true`. Title should contain "PAUSED".

**SC2: `WatchPanelWindow_OnActivated_Subscribes_OnDeactivated_Unsubscribes`**
- Create `MockDebugSession` that tracks `OnPinValueChanged` subscription count.
- `OnActivated()` -> assert subscribed. `OnDeactivated()` -> assert unsubscribed.

For these tests, create `MockDebugSession` in the test file or a shared `Editor/MockDebugSession.cs` helper:
```csharp
internal sealed class MockDebugSession : IBlueprintDebugSession
{
    public bool IsPaused { get; set; }
    public event Action<PinValueChanged>? OnPinValueChanged;
    public event Action<BreakpointHit>? OnBreakpointHit;
    public event Action? OnBreakpointListChanged;
    // Implement all interface members with no-ops or throw NotSupportedException.
    // All members that return IEnumerable return [].
    // All Add/Remove/Set methods are no-ops.
}
```

**IMPORTANT:** Read `IBlueprintDebugSession` fully before writing `MockDebugSession`. Implement ALL interface members (check the actual interface in `Hrot.Blueprints.Core/IBlueprintDebugSession.cs`).

---

## 4. Build + Verify

```powershell
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor -v quiet
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~Editor" -v minimal
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 errors, 0 failures. Total count >= 456 (449 + 7 new tests).

---

## 5. Order of Operations

1. Read BATCH-23-INSTRUCTIONS.md (this file).
2. Read `IBlueprintDebugSession.cs` fully -- you need ALL interface members for MockDebugSession.
3. Read `BlueprintEditorWindowBase.cs`, `ReloadInfo.cs`, `IAssetCatalog.cs`, `EditorSelectionStore.cs`.
4. Find `BlueprintAsset` and `BlueprintSignature` in `Fdp.Toolkit.Blueprints`.
5. Create `Debug/` subfolder files: ReloadLogEntry.cs, HotReloadLogModel.cs, DebugPanelWindow.cs, WatchPanelWindow.cs, CallstackWindow.cs, HotReloadLogWindow.cs.
6. Create `Reload/` subfolder files: QuickReloadResult.cs, FullRebuildResult.cs, QuickReloadService.cs, FullRebuildService.cs.
7. Build Editor project. Fix errors.
8. Create `Editor/MockDebugSession.cs` test helper.
9. Create `Editor/HotReloadLogModelTests.cs` (SC1-SC5).
10. Create `Editor/DebugWindowsTests.cs` (SC1-SC2).
11. Build Tests project. Fix errors.
12. Run Editor filter tests. Fix failures.
13. Run full suite. Fix failures.
14. Commit.
15. Write report.

---

## 6. Commit

```
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/
git commit -m "feat(blueprints): BATCH-23 ED-004 debug windows + ED-005 reload services stubs

- ReloadLogEntry record: Timestamp, Source, Succeeded, Message, DurationMs
- HotReloadLogModel: ring buffer max 1000 entries, Enqueue/Dequeue eviction
- DebugPanelWindow: Title shows [PAUSED] when session.IsPaused
- WatchPanelWindow: subscribes OnPinValueChanged on Activated, unsubscribes on Deactivated
- CallstackWindow: skeleton with session + selectionStore deps
- HotReloadLogWindow: OnReloadCompleted/OnReloadFailed routing to model
- QuickReloadResult + FullRebuildResult records
- QuickReloadService: Slice 1 stub with IOutputConsole logging
- FullRebuildService: spawns dotnet build process, streams output, PendingDrainAfterBuild flag
- HotReloadLogModelTests: SC1-SC5 ring buffer + window routing
- DebugWindowsTests: SC1-SC2 pause title + event subscription

Baseline: 449 -> X pass / 5 skip / 0 fail"
```

---

## 7. Troubleshooting

- If `BlueprintSignature` does not exist, replace `IReadOnlyList<BlueprintSignature>?` with `IReadOnlyList<string>?` in `QuickReloadService.LastSignaturesUsedForTesting`.
- If `BlueprintAsset.AssetId` has a different property name, adapt accordingly.
- If `IBlueprintDebugSession.OnPinValueChanged` is not an `Action<PinValueChanged>` event but something else, adapt `WatchPanelWindow` and `MockDebugSession` to match.
- Do NOT modify any existing files outside `Hrot.Blueprints.Editor/` and `Hrot.Blueprints.Tests/Editor/`.

---

## Success Criteria

| SC | Check |
|----|-------|
| SC1 | HotReloadLogModel: add entry increases count |
| SC2 | HotReloadLogModel: evicts oldest beyond 1000 |
| SC3 | HotReloadLogModel: clear resets count |
| SC4 | HotReloadLogWindow.OnReloadCompleted adds success entry |
| SC5 | HotReloadLogWindow.OnReloadFailed adds failed entry |
| SC6 | DebugPanelWindow.Title contains [PAUSED] when IsPaused |
| SC7 | WatchPanelWindow subscribes on Activated, unsubscribes on Deactivated |
| Build | dotnet build Hrot.Blueprints.Editor zero errors |
| Tests | 0 failures full suite |
