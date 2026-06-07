# BATCH-19 Instructions — Phase 4 Editor UI (JM-P4-001, JM-P4-002, JM-P4-003)

**Branch:** `json-migration`
**Prereqs:** BATCH-18 committed (`5b90ff57`). All Phase 3 tasks done. JM-P4-004/005 (CLI) done.
**Scope:** Editor warning modal (JM-P4-001), degraded-mode banner (JM-P4-002), migration history menu item (JM-P4-003).

**Design refs:**
- `.dev/json-migration/TASK-DETAILS.md` §JM-P4-001, JM-P4-002, JM-P4-003
- `.dev/json-migration/Migration-system.md` §2.3, §2.4, §6 (message strings for modal/banner)
- `.dev/json-migration/05-integration-patches.md` (integration points)

---

## Context — Key Facts

**Editor stack:** `EditorSubsystem.cs` → creates `ScenarioFileService` + `EditorApplication` + `ManagedWindow` subclasses (all in `EditorWindows.cs`). Panels only call `IEditorLogic` (implemented by `EditorApplication`). `Hrot.Editor.Tests` uses `Moq` to mock `IEditorLogic`.

**Current `ScenarioFileService.LoadScenario`** (in `Hrot.Presentation`) uses `_migrationServices.ReadOnly.LoadAndMigrateAsync` — returns `ReadOnlyLoadOutcome` (no `WasMigrated`/`IsDegraded`). Phase 4 must switch to `Persistent.LoadAndMigrateAsync` which returns `MigrationLoadResult`.

**`MigrationLoadResult`** properties (all `{ get; init; }`):
- `Dom: JsonObject` — the migrated DOM
- `OriginalMeta: DocumentMeta` — version of file on disk
- `CurrentMeta: DocumentMeta` — version after migration
- `WasMigrated: bool` — `OriginalMeta.SchemaVersion != CurrentMeta.SchemaVersion`
- `IsDegraded: bool` — snapshot fallback was used
- `HasUnknownsJournal: bool`
- `UsedSnapshotPath: string?`
- `SourceContentHash: string` (internal)
- `Report: MigrationReport?`

**`PersistentMigrationAdapter`** has no public `ListSidecarsAsync` wrapper yet — it delegates to `_storage` (internal). Must add one.

**`ScenarioBrowserPanel.DrawContent(IEditorLogic logic)`** — handles New/Save/Load buttons and modals. Pattern for testable handlers: `HandleNewClick(IEditorLogic)`, `HandleSaveClick(IEditorLogic)`, etc. (all public). Tests use `Mock<IEditorLogic>`.

**`EditorBrowserWindow`** (in `EditorWindows.cs`) — `ManagedWindow` that calls `_panel.DrawContent(_logic)` from `DrawClientArea()`.

**`IEditorLogic`** (interface in `Hrot.Editor`) — all editor panels bind to this. Backed by `EditorApplication`.

---

## What to Build

### 1. `PersistentMigrationAdapter` — Add `ListSidecarsAsync` public wrapper

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/PersistentMigrationAdapter.cs`

Add a public method at the bottom of the class (before the last `}`):

```csharp
/// <summary>
/// Enumerates sidecar files (snapshots and journals) stored alongside
/// <paramref name="originalPath"/>. Returns an empty list when no sidecar
/// directory exists. Delegates to <see cref="IMigrationStorage.ListSidecarsAsync"/>.
/// </summary>
public Task<IReadOnlyList<SidecarFileInfo>> ListSidecarsAsync(
    string originalPath,
    CancellationToken ct = default)
    => _storage.ListSidecarsAsync(originalPath, ct);
```

No other changes to this file.

---

### 2. `ScenarioFileService` — Switch to Persistent Adapter, Store Last Result, Wire Save

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs`

#### 2a. Add fields

Add two new private fields alongside the existing fields:

```csharp
private MigrationLoadResult? _lastLoadResult;
private string? _lastLoadPath;
```

And add a public property to expose the last result:

```csharp
/// <summary>
/// The <see cref="MigrationLoadResult"/> from the most recent
/// <see cref="LoadScenario"/> call that went through the persistent adapter,
/// or <c>null</c> if no migration-aware load has occurred.
/// </summary>
public MigrationLoadResult? LastLoadResult => _lastLoadResult;
```

#### 2b. Update `LoadScenario` — switch from ReadOnly to Persistent adapter

In `LoadScenario(EntityRepository repo, string filePath)`, replace the block:

```csharp
if (_migrationServices != null)
{
    // Phase 2 path: run through the read-only migration adapter, which validates
    // $meta and migrates stale documents before we touch the repo.
    var outcome = _migrationServices.ReadOnly
        .LoadAndMigrateAsync(filePath)
        .GetAwaiter().GetResult();
    dom = outcome.AsJsonObject();
}
```

with:

```csharp
if (_migrationServices != null)
{
    // Phase 4 path: use the persistent adapter so that snapshots are written
    // before up-migration and journals are written on down-migration.
    // MigrationLoadResult carries WasMigrated / IsDegraded for UI alerts.
    var result = _migrationServices.Persistent
        .LoadAndMigrateAsync(filePath)
        .GetAwaiter().GetResult();
    _lastLoadResult = result;
    _lastLoadPath   = filePath;
    dom = result.Dom;
}
else
{
    _lastLoadResult = null;
    _lastLoadPath   = null;
}
```

Note: in the existing code the `else` branch calls `ValidateSubsystemType(jsonText)`. Keep that `else` block unchanged; the `else` above adds `_lastLoadResult = null; _lastLoadPath = null;` inside the existing `else`. You are replacing only the `if (_migrationServices != null)` branch.

#### 2c. Update `SaveScenario` — use `Persistent.SaveAsync` when a prior load result exists

In `SaveScenario(EntityRepository repo, string filePath)`, replace:

```csharp
        // Always use direct write path (PersistentMigrationAdapter.SaveAsync requires
        // a priorLoad — not available for fresh saves).
        var minifiedOptions = new System.Text.Json.JsonSerializerOptions(HrotSerializerOptions.HrotJsonOptions)
        {
            WriteIndented = false,
        };
        var minifiedJson = System.Text.Json.JsonSerializer.Serialize(fdpDom, minifiedOptions);

        File.WriteAllText(filePath, JsonAestheticFormatter.FlattenNumericArrays(minifiedJson));
```

with:

```csharp
        if (_migrationServices != null
            && _lastLoadResult != null
            && string.Equals(filePath, _lastLoadPath, StringComparison.OrdinalIgnoreCase))
        {
            // Use the persistent adapter so that any round-trip journal is applied
            // (restoring higher-version-only fields) and cleaned up on success.
            _migrationServices.Persistent
                .SaveAsync(filePath, fdpDom, _lastLoadResult)
                .GetAwaiter().GetResult();
            _lastLoadResult = null;  // consumed; next load will refresh
            _lastLoadPath   = null;
        }
        else
        {
            // Direct write path for fresh saves or saves without a prior load result.
            var minifiedOptions = new System.Text.Json.JsonSerializerOptions(HrotSerializerOptions.HrotJsonOptions)
            {
                WriteIndented = false,
            };
            var minifiedJson = System.Text.Json.JsonSerializer.Serialize(fdpDom, minifiedOptions);
            File.WriteAllText(filePath, JsonAestheticFormatter.FlattenNumericArrays(minifiedJson));
        }
```

#### 2d. Add `GetSidecarsForLastLoadAsync`

Add a new public method at the end of the class (before the private helpers):

```csharp
/// <summary>
/// Returns sidecar files (snapshots and journals) for the most recently
/// loaded file. Returns an empty list when no migration-aware load has
/// occurred or when no migration services are configured.
/// </summary>
public async Task<IReadOnlyList<SidecarFileInfo>> GetSidecarsForLastLoadAsync(
    CancellationToken ct = default)
{
    if (_migrationServices == null || _lastLoadPath == null)
        return Array.Empty<SidecarFileInfo>();
    return await _migrationServices.Persistent
        .ListSidecarsAsync(_lastLoadPath, ct)
        .ConfigureAwait(false);
}
```

---

### 3. New `MigrationAlertManager` — state management for modal and banner

**File (NEW):** `Hrot/Subsystems/Hrot.Editor/Migration/MigrationAlertManager.cs`

Place in the `Hrot.Editor` assembly (not `Hrot.Presentation`), so that `Hrot.Editor.Tests` (which has `InternalsVisibleTo("Hrot.Editor.Tests")`) can test it directly.

```csharp
using System;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
using ImGuiNET;

namespace Hrot.Editor.Migration;

/// <summary>
/// Manages the per-session migration alert state for the editor UI.
/// Tracks the most recent load result, queues one-time modal display
/// for migrations, and exposes the degraded-mode flag for the browser panel.
/// </summary>
/// <remarks>
/// Call <see cref="OnScenarioLoaded"/> immediately after each
/// <see cref="ScenarioFileService.LoadScenario"/> completes.
/// Call <see cref="Draw"/> once per frame from within an active ImGui window.
/// </remarks>
internal sealed class MigrationAlertManager
{
    private MigrationLoadResult? _pendingAlert;     // non-null = modal not yet shown
    private MigrationLoadResult? _currentResult;    // tracks currently-loaded file
    private bool _suppressedForSession;             // checkbox state

    // ── State queries (used by tests and IEditorLogic implementations) ────────

    /// <summary>
    /// True when a migration-warning modal has been queued but not yet dismissed.
    /// </summary>
    public bool HasPendingAlert => _pendingAlert != null;

    /// <summary>
    /// True when the currently loaded scenario was loaded via degraded-mode
    /// snapshot fallback.
    /// </summary>
    public bool IsDegradedMode => _currentResult?.IsDegraded == true;

    // ── Mutators ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called after each scenario load. Queues a migration alert if
    /// <paramref name="result"/> reports that migration occurred and the user
    /// has not suppressed alerts for this session.
    /// </summary>
    public void OnScenarioLoaded(MigrationLoadResult? result)
    {
        _currentResult = result;
        _pendingAlert  = null;   // reset for the new file

        if (result == null) return;
        if (result.WasMigrated && !_suppressedForSession)
            _pendingAlert = result;
    }

    /// <summary>Resets migration alert state when the world is cleared (new scenario).</summary>
    public void OnScenarioCleared()
    {
        _currentResult = null;
        _pendingAlert  = null;
    }

    // ── ImGui rendering ───────────────────────────────────────────────────────

    /// <summary>
    /// Renders pending migration modal and/or degraded-mode banner.
    /// Must be called from within an active ImGui window context (i.e., inside
    /// a ManagedWindow's DrawClientArea) once per frame.
    /// </summary>
    public void Draw()
    {
        DrawDegradedBanner();
        DrawMigrationModal();
    }

    // ── Private rendering helpers ─────────────────────────────────────────────

    private void DrawDegradedBanner()
    {
        if (_currentResult?.IsDegraded != true) return;

        var originalVersion = _currentResult.OriginalMeta.SchemaVersion;
        var currentVersion  = _currentResult.CurrentMeta.SchemaVersion;

        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.6f, 0f, 1f));
        ImGui.TextWrapped(
            $"[!] DEGRADED MODE: file is v{originalVersion} (binary supports v{currentVersion}). " +
            "A snapshot fallback is in use. Saving will LOSE newer-version data.");
        ImGui.PopStyleColor();
        ImGui.Separator();
    }

    private void DrawMigrationModal()
    {
        if (_pendingAlert == null) return;

        // Signal ImGui to open the popup on this frame.
        ImGui.OpenPopup("Scenario Migrated##migration_alert");
        _pendingAlert = null;   // clear so we don't re-open next frame

        // Render the modal (ImGui keeps it open until CloseCurrentPopup).
        bool open = true;
        if (!ImGui.BeginPopupModal("Scenario Migrated##migration_alert", ref open,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var from = (_pendingAlert ?? _currentResult)?.OriginalMeta.SchemaVersion ?? 0;
        var to   = (_pendingAlert ?? _currentResult)?.CurrentMeta.SchemaVersion  ?? 0;

        ImGui.TextWrapped(
            $"This scenario has been migrated from v{from} to v{to}.\n" +
            "A backup of the original file was saved to the .migration-snapshots/ directory.");
        ImGui.Spacing();

        ImGui.Checkbox("Don't show this again for this session", ref _suppressedForSession);
        ImGui.Spacing();

        if (ImGui.Button("OK", new System.Numerics.Vector2(120f, 0f)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }
}
```

**Correctness note on `DrawMigrationModal`:** `_pendingAlert` is set to `null` before `BeginPopupModal` is called so that we don't re-open on the next frame. The popup text uses `_currentResult` as fallback because `_pendingAlert` was just cleared. Adjust to capture the result before nulling it:

```csharp
private void DrawMigrationModal()
{
    if (_pendingAlert == null) return;

    var alertResult = _pendingAlert;
    ImGui.OpenPopup("Scenario Migrated##migration_alert");
    _pendingAlert = null;

    bool open = true;
    if (!ImGui.BeginPopupModal("Scenario Migrated##migration_alert", ref open,
            ImGuiWindowFlags.AlwaysAutoResize))
        return;

    ImGui.TextWrapped(
        $"This scenario has been migrated from v{alertResult.OriginalMeta.SchemaVersion} " +
        $"to v{alertResult.CurrentMeta.SchemaVersion}.\n" +
        "A backup of the original file was saved to the .migration-snapshots/ directory.");
    ImGui.Spacing();

    ImGui.Checkbox("Don't show this again for this session", ref _suppressedForSession);
    ImGui.Spacing();

    if (ImGui.Button("OK", new System.Numerics.Vector2(120f, 0f)))
        ImGui.CloseCurrentPopup();

    ImGui.EndPopup();
}
```

---

### 4. `IEditorLogic` — Add Phase 4 properties/methods

**File:** `Hrot/Subsystems/Hrot.Editor/IEditorLogic.cs`

Add the following members to the interface (add using for `Fdp.Core.Serialization.Migrations` if not present):

```csharp
/// <summary>
/// True when the currently loaded scenario was opened in degraded mode
/// (snapshot fallback; the file was too new for the current migration chain).
/// </summary>
bool IsScenarioDegraded { get; }

/// <summary>
/// Returns the sidecar files (snapshots and journals) stored alongside the
/// currently loaded scenario file. Returns an empty list when no scenario
/// has been loaded or when no migration services are configured.
/// </summary>
IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario();
```

Add using:
```csharp
using Fdp.Core.Serialization.Migrations;
```

---

### 5. `EditorApplication` — Wire MigrationAlertManager, implement new members

**File:** `Hrot/Subsystems/Hrot.Editor/EditorApplication.cs`

#### 5a. Add field and property

After the existing `private string? _loadedScenarioName;` field, add:

```csharp
private readonly MigrationAlertManager _alertManager = new();
```

Add a property (after `public IDerRepo View => _view;`) so EditorWindows can reach the manager:

```csharp
/// <summary>
/// Alert manager for migration events. Used by <see cref="EditorBrowserWindow"/>
/// to draw the per-frame alert modal and degraded-mode banner.
/// </summary>
internal MigrationAlertManager AlertManager => _alertManager;
```

#### 5b. Update `NewScenario()`

Change the existing `NewScenario()` implementation:

```csharp
/// <inheritdoc/>
public void NewScenario()
{
    _fileService.NewScenario(_world);
    _loadedScenarioName = null;
    _alertManager.OnScenarioCleared();
}
```

#### 5c. Update `LoadScenario(string filePath)`

Change the existing single-line implementation:

```csharp
/// <inheritdoc/>
public void LoadScenario(string filePath)
{
    _fileService.LoadScenario(_world, filePath);
    _alertManager.OnScenarioLoaded(_fileService.LastLoadResult);
}
```

#### 5d. Implement `IsScenarioDegraded`

Add property:

```csharp
/// <inheritdoc/>
public bool IsScenarioDegraded => _alertManager.IsDegradedMode;
```

#### 5e. Implement `GetMigrationSidecarsForCurrentScenario()`

Add method:

```csharp
/// <inheritdoc/>
public IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario()
    => _fileService.GetSidecarsForLastLoadAsync().GetAwaiter().GetResult();
```

Add using at the top:
```csharp
using Fdp.Core.Serialization.Migrations;
using Hrot.Editor.Migration;
```

---

### 6. `EditorWindows.cs` — Update `EditorBrowserWindow` to wire alert manager

**File:** `Hrot/Subsystems/Hrot.Editor/Windows/EditorWindows.cs`

Add `using Hrot.Editor.Migration;` at the top.

Update `EditorBrowserWindow` to accept and call the alert manager:

```csharp
/// <summary>Scenario file browser panel as a perspective-bound managed window.</summary>
internal sealed class EditorBrowserWindow : ManagedWindow
{
    private readonly ScenarioBrowserPanel  _panel;
    private readonly IEditorLogic          _logic;
    private readonly MigrationAlertManager _alertManager;

    public EditorBrowserWindow(
        ScenarioBrowserPanel  panel,
        IEditorLogic          logic,
        MigrationAlertManager alertManager)
        : base("editor_browser", "Scenario Browser", "Editor", WindowScope.PerspectiveBound)
    {
        _panel        = panel;
        _logic        = logic;
        _alertManager = alertManager;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
    }

    protected override void DrawClientArea()
    {
        _panel.DrawContent(_logic);
        _alertManager.Draw();
    }
}
```

---

### 7. `EditorSubsystem.cs` — Pass `AlertManager` to `EditorBrowserWindow`

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

Find the `new EditorBrowserWindow(...)` constructor call and update it to pass the alert manager. The call will currently look like:

```csharp
new EditorBrowserWindow(scenarioBrowserPanel, editorApp)
```

or equivalently use the local variable names as found in the file. Change it to:

```csharp
new EditorBrowserWindow(scenarioBrowserPanel, editorApp, editorApp.AlertManager)
```

(Use whatever local variable holds the `EditorApplication` instance — search for `new EditorBrowserWindow` in the file to find the exact call site.)

---

### 8. `ScenarioBrowserPanel` — Add degraded banner + migration history

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/UI/ScenarioBrowserPanel.cs`

Wait — check the actual file location. `ScenarioBrowserPanel` may be in `Hrot.Editor/UI/` or `Hrot.Presentation/`. It is at:
`Hrot/Subsystems/Hrot.Editor/UI/ScenarioBrowserPanel.cs`

Add the following to `ScenarioBrowserPanel`:

#### 8a. New state fields

At the top of the class, add after the existing modal state fields:

```csharp
private bool   _showMigrationHistoryDialog;
private IReadOnlyList<SidecarFileInfo>? _migrationSidecars;
```

Add using at top of file:
```csharp
using System.Collections.Generic;
using Fdp.Core.Serialization.Migrations;
```

#### 8b. New testable handler

Add a public handler method:

```csharp
public void HandleMigrationHistoryClick(IEditorLogic logic)
{
    _migrationSidecars           = logic.GetMigrationSidecarsForCurrentScenario();
    _showMigrationHistoryDialog  = true;
}
```

#### 8c. Update `DrawContent` — degraded banner + history button

At the start of `DrawContent(IEditorLogic logic)`, before the "Current scenario indicator" section, add the degraded banner:

```csharp
// ── Degraded-mode banner ───────────────────────────────────────────────────
if (logic.IsScenarioDegraded)
{
    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.5f, 0f, 1f));
    ImGui.TextWrapped("[!] Degraded mode: scenario loaded from a snapshot backup. " +
                      "Saving will lose newer-version data.");
    ImGui.PopStyleColor();
    ImGui.Separator();
}
```

After the existing Load button (after `if (ImGui.Button("Load")) HandleLoadClick();`), add a "Migration History" button on the same line:

```csharp
ImGui.SameLine();
if (ImGui.Button("Migration History"))
    HandleMigrationHistoryClick(logic);
```

#### 8d. Add migration history modal

After the existing "Save As" modal block (before the closing `}` of `DrawContent`), add:

```csharp
// ── Migration history dialog ──────────────────────────────────────────────
if (_showMigrationHistoryDialog)
{
    ImGui.OpenPopup("Migration History##browser");
    _showMigrationHistoryDialog = false;
}

bool historyOpen = true;
if (ImGui.BeginPopupModal("Migration History##browser", ref historyOpen,
        ImGuiWindowFlags.AlwaysAutoResize))
{
    if (ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
    ImGui.Text("Sidecar files for the current scenario:");
    ImGui.Separator();

    var sidecars = _migrationSidecars;
    if (sidecars == null || sidecars.Count == 0)
    {
        ImGui.TextDisabled("(no sidecars present)");
    }
    else
    {
        if (ImGui.BeginTable("##sidecars", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("File", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Kind",    ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableSetupColumn("Version", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableSetupColumn("Hash",    ImGuiTableColumnFlags.WidthFixed, 130f);
            ImGui.TableHeadersRow();

            foreach (var s in sidecars)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(s.FileName);
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(s.Kind.ToString());
                ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(s.Version.ToString());
                ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(s.ContentHash);
            }
            ImGui.EndTable();
        }
    }

    ImGui.Spacing();
    if (ImGui.Button("Close", new System.Numerics.Vector2(100f, 0f)))
        ImGui.CloseCurrentPopup();
    ImGui.EndPopup();
}
```

---

## Tests

### Test file 1 (NEW): `Hrot/Subsystems/Hrot.Editor.Tests/Migration/MigrationAlertManagerTests.cs`

```csharp
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
using Hrot.Editor.Migration;
using Xunit;

namespace Hrot.Editor.Tests.Migration;

public class MigrationAlertManagerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DocumentMeta Meta(int version) =>
        new DocumentMeta { DocType = "Hrot.Scenario", SchemaVersion = version };

    private static MigrationLoadResult MakeResult(
        bool wasMigrated, bool isDegraded = false,
        int from = 1, int to = 2) =>
        new MigrationLoadResult
        {
            Dom          = new System.Text.Json.Nodes.JsonObject(),
            OriginalMeta = Meta(from),
            CurrentMeta  = Meta(to),
            IsDegraded   = isDegraded,
        };

    // ── OnScenarioLoaded ──────────────────────────────────────────────────────

    [Fact]
    public void OnScenarioLoaded_WasMigrated_QueuesPendingAlert()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true));
        Assert.True(mgr.HasPendingAlert);
    }

    [Fact]
    public void OnScenarioLoaded_WasNotMigrated_NoPendingAlert()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: false, from: 2, to: 2));
        Assert.False(mgr.HasPendingAlert);
    }

    [Fact]
    public void OnScenarioLoaded_IsDegraded_SetsDegradedMode()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true, isDegraded: true));
        Assert.True(mgr.IsDegradedMode);
    }

    [Fact]
    public void OnScenarioLoaded_NotDegraded_NotDegradedMode()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: false, from: 2, to: 2));
        Assert.False(mgr.IsDegradedMode);
    }

    [Fact]
    public void OnScenarioLoaded_Null_NoEffect()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(null);
        Assert.False(mgr.HasPendingAlert);
        Assert.False(mgr.IsDegradedMode);
    }

    // ── Session suppression ───────────────────────────────────────────────────

    [Fact]
    public void SuppressForSession_SubsequentMigratedLoad_NoPendingAlert()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true));   // first load: alert queued
        mgr.SuppressAlertsForSession();                         // user checks checkbox
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true));   // second load: suppressed
        Assert.False(mgr.HasPendingAlert);
    }

    // ── OnScenarioCleared ─────────────────────────────────────────────────────

    [Fact]
    public void OnScenarioCleared_ClearsCurrentResultAndPendingAlert()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true, isDegraded: true));
        mgr.OnScenarioCleared();
        Assert.False(mgr.HasPendingAlert);
        Assert.False(mgr.IsDegradedMode);
    }
}
```

**Note on `SuppressAlertsForSession()`:** The `Draw()` method mutates `_suppressedForSession` via ImGui checkbox (no test context). Expose a separate method for testability:

```csharp
// Add to MigrationAlertManager:
/// <summary>
/// Marks alerts as suppressed for this session.
/// Called by the ImGui checkbox in <see cref="Draw"/>; exposed for testing.
/// </summary>
internal void SuppressAlertsForSession() => _suppressedForSession = true;
```

In `Draw()` / `DrawMigrationModal()`, the ImGui checkbox should mutate `_suppressedForSession` directly via `ref _suppressedForSession`. When checked, subsequent loads won't queue an alert. (Do NOT call `SuppressAlertsForSession()` from `Draw()` — use `ref _suppressedForSession` directly in the `ImGui.Checkbox` call.)

---

### Test file 2 (MODIFY): `Hrot/Subsystems/Hrot.Editor.Tests/ScenarioBrowserPanelTests.cs`

Add the following tests to the existing `ScenarioBrowserPanelTests` class:

```csharp
[Fact]
public void HandleMigrationHistoryClick_CallsGetMigrationSidecarsForCurrentScenario()
{
    var mock = new Mock<IEditorLogic>();
    mock.Setup(l => l.GetMigrationSidecarsForCurrentScenario())
        .Returns(new List<SidecarFileInfo>());
    var panel = new ScenarioBrowserPanel();
    panel.HandleMigrationHistoryClick(mock.Object);
    mock.Verify(l => l.GetMigrationSidecarsForCurrentScenario(), Times.Once);
}
```

Add usings at top of the file as needed:
```csharp
using System.Collections.Generic;
using Fdp.Core.Serialization.Migrations;
```

---

## Build and Test

After all changes, build the solution:
```
dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4
```

Run the editor tests:
```
dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --logger "console;verbosity=minimal"
```

Expected: 7 new MigrationAlertManagerTests + 1 new ScenarioBrowserPanelTest = 8 new tests, all passing.

Run the full common tests too:
```
dotnet test Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj --logger "console;verbosity=minimal"
```

Expected: all 46 existing tests still pass.

---

## Acceptance Criteria

1. Build passes with `TreatWarningsAsErrors`.
2. `PersistentMigrationAdapter.ListSidecarsAsync` is a public method delegating to `_storage`.
3. `ScenarioFileService.LoadScenario` uses `Persistent` adapter (NOT `ReadOnly`), stores `LastLoadResult`.
4. `ScenarioFileService.SaveScenario` uses `Persistent.SaveAsync` when `_lastLoadResult != null && filePath == _lastLoadPath`.
5. `MigrationAlertManager` (in `Hrot.Editor.Migration`) has `OnScenarioLoaded`, `OnScenarioCleared`, `SuppressAlertsForSession`, `HasPendingAlert`, `IsDegradedMode`, and `Draw()`.
6. `IEditorLogic` has `bool IsScenarioDegraded { get; }` and `IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario()`.
7. `EditorApplication` implements both new interface members and has `internal MigrationAlertManager AlertManager`.
8. `EditorBrowserWindow` accepts and calls `MigrationAlertManager.Draw()` from `DrawClientArea()`.
9. `ScenarioBrowserPanel` shows degraded banner when `logic.IsScenarioDegraded`, has "Migration History" button, has `HandleMigrationHistoryClick(IEditorLogic)`.
10. `EditorSubsystem` passes `editorApp.AlertManager` to the `EditorBrowserWindow` constructor.
11. 7 + 1 = 8 new tests pass.
12. `MigrationLoadResult.WasMigrated` — recall this means `OriginalMeta.SchemaVersion != CurrentMeta.SchemaVersion`. For the test helpers, construct results accordingly: for `wasMigrated: true` use `from=1, to=2`; for `wasMigrated: false` use `from=2, to=2`.

---

## Do NOT change

- Existing test methods in `ScenarioBrowserPanelTests.cs` (do not remove or alter any).
- `HrotMigrationBootstrap.cs` — already wired correctly.
- `MigrationPipeline`, `MigrationRegistry`, `MigrationBootstrap` — no changes needed.
- `Hrot.Common.Tests` migration tests — no changes needed.
- Any Phase 1/2/3 files not listed above.

---

## Known Pre-existing Build Errors

`Hrot.Blueprints.Tests` has pre-existing CS0234/CS0246 build errors unrelated to migration work. These may appear in the build output but are not caused by this batch. Do not attempt to fix them.
