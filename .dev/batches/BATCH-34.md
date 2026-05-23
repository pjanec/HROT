# BATCH-34 — Phase 7: FindResultsWindow + Inspector/Browser refactor integration + E2E test

## Tasks

- **TASK-S7-03**: `FindResultsWindow` + DI wiring
- **TASK-S7-04**: Inspector right-click integration
- **TASK-S7-05**: Asset Browser refactor integration
- **TASK-S7-06**: Refactor end-to-end integration test

## Context

All source files reside under `d:\Work\IOS-IG-SimHost-FDP-2\`.

Key namespaces and types already in place:
- `Hrot.Editor.AiShared.Refactor` — `IRefactorService`, `RefactorService`, `AtomicMultiFileWriter`, all data records
- `Hrot.Editor.AiShared.References` — `IReferenceCatalog`, `AssetReference`, `SubElementKind`, `AssetReferenceInfo`
- `Hrot.Editor.AiShared.Catalog` — `IAssetCatalog`, `IEditableAsset`
- `Hrot.Editor.AiShared` (parent namespace) — `AssetKind`
- `Hrot.Editor.AiShared.Selection` — `EditorSelectionStore`
- `Hrot.Editor.AiShared.Windows` — `AssetBrowserWindow`, `InspectorWindow`, `SharedAiWindowRegistrar`
- `Hrot.Editor.AiShared.Di` — `SharedAiEditorServiceCollectionExtensions`

Windows inherit from `ManagedWindow` (in `Fdp.Presentation.WindowManager`) which already has an `ImGuiNET` using from implicit usings.
The `DrawClientArea()` override is the rendering entry point. Use `ImGuiNET.ImGui.*` directly.

## Coding rules

- `TreatWarningsAsErrors` is active. No unused variables, no CS0067 for events.
- Fake event implementations in test classes: `event Action? Changed { add { } remove { } }`
- No Unicode characters in comments — ASCII only.
- No `using System;` etc. needed — implicit usings cover System, LINQ, IO, Threading, Collections.
- Parent-namespace types (`AssetKind`, `IEditableAsset`) are accessible without explicit `using`.
- Sibling-namespace types need explicit `using` directives.
- Do NOT add docstrings/comments beyond what is already the convention.

## Files to create or modify

### 1. CREATE `Hrot/Editor/Hrot.Editor.AiShared/Windows/FindResultsWindow.cs`

Namespace: `Hrot.Editor.AiShared.Windows`
Usings needed: `Fdp.Presentation.WindowManager`, `Hrot.Editor.AiShared.Refactor`, `Hrot.Editor.AiShared.References`

```csharp
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Shared window for find-references results and refactor preview.
/// Registered as "ai_find_results" in the Authoring perspective.
/// </summary>
public sealed class FindResultsWindow : ManagedWindow
{
    private string _queryLabel = string.Empty;
    private IReadOnlyList<AssetReferenceInfo>? _results;
    private RefactorPreview? _renamePreview;

    public FindResultsWindow()
        : base("ai_find_results", "Find Results", "Authoring", WindowScope.PerspectiveBound)
    {
    }

    /// <summary>
    /// Show plain find-references results (no rename preview).
    /// </summary>
    public void ShowReferences(string query, IReadOnlyList<AssetReferenceInfo> results)
    {
        _queryLabel = query;
        _results = results;
        _renamePreview = null;
    }

    /// <summary>
    /// Show a rename preview with line edits visible.
    /// </summary>
    public void ShowRenamePreview(RefactorPreview preview)
    {
        _queryLabel = $"Rename: {preview.FromKey} -> {preview.ToKey}";
        _results = null;
        _renamePreview = preview;
    }

    protected override void DrawClientArea()
    {
        if (_renamePreview != null)
        {
            DrawRenamePreview(_renamePreview);
        }
        else if (_results != null)
        {
            DrawFindResults(_queryLabel, _results);
        }
        else
        {
            ImGuiNET.ImGui.TextDisabled("No results. Use right-click on a reference to find usages.");
        }
    }

    private static void DrawFindResults(string query, IReadOnlyList<AssetReferenceInfo> results)
    {
        ImGuiNET.ImGui.Text($"FIND RESULTS -- \"{query}\"  ({results.Count} references)");
        ImGuiNET.ImGui.Separator();

        var groups = results.GroupBy(r => r.SourceFilePath);
        foreach (var group in groups)
        {
            var header = $"{group.Key}  ({group.Count()} refs)";
            if (ImGuiNET.ImGui.TreeNodeEx(header, ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var r in group)
                {
                    ImGuiNET.ImGui.BulletText($"{r.HostKind}:{r.HostDisplayPath}  \"{r.TargetKey}\"");
                }
                ImGuiNET.ImGui.TreePop();
            }
        }
    }

    private static void DrawRenamePreview(RefactorPreview preview)
    {
        ImGuiNET.ImGui.Text($"RENAME PREVIEW -- \"{preview.FromKey}\" -> \"{preview.ToKey}\"");
        ImGuiNET.ImGui.Separator();

        foreach (var fileEdit in preview.Edits)
        {
            var header = $"{fileEdit.FilePath}  ({fileEdit.LineEdits.Count} edits)";
            if (ImGuiNET.ImGui.TreeNodeEx(header, ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var lineEdit in fileEdit.LineEdits)
                {
                    ImGuiNET.ImGui.TextColored(
                        new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f),
                        $"  - L{lineEdit.LineNumber}: {lineEdit.OriginalText.Trim()}");
                    ImGuiNET.ImGui.TextColored(
                        new System.Numerics.Vector4(0.4f, 1f, 0.4f, 1f),
                        $"  + L{lineEdit.LineNumber}: {lineEdit.ReplacementText.Trim()}");
                }
                ImGuiNET.ImGui.TreePop();
            }
        }

        if (preview.Issues.Count > 0)
        {
            ImGuiNET.ImGui.Separator();
            foreach (var issue in preview.Issues)
            {
                ImGuiNET.ImGui.TextColored(
                    new System.Numerics.Vector4(1f, 0.8f, 0f, 1f),
                    $"[{issue.Severity}] {issue.Description}");
            }
        }
    }
}
```

### 2. MODIFY `Hrot/Editor/Hrot.Editor.AiShared/Windows/SharedAiWindowRegistrar.cs`

Add `FindResultsWindow` field, constructor parameter, and registration call.

Current state (read the file before editing):
```
private readonly AssetBrowserWindow _assetBrowser;
private readonly InspectorWindow _inspector;
private readonly RuntimeInspectorWindow _runtimeInspector;
private readonly TraceTimelineWindow _traceTimeline;

public SharedAiWindowRegistrar(
    AssetBrowserWindow assetBrowser,
    InspectorWindow inspector,
    RuntimeInspectorWindow runtimeInspector,
    TraceTimelineWindow traceTimeline)
```

Add `FindResultsWindow _findResults` field and constructor parameter. Add `windowManager.RegisterWindow(_findResults);` in `RegisterWindows`.

### 3. MODIFY `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs`

Add `IRefactorService` and `FindResultsWindow` injection. In `DrawClientArea()`, replace the simple `ImGui.Text(asset.Name)` call with a selectable that opens a context menu on right-click with items: "Find References", "Rename...", "Go to Definition".

For "Find References": call `_refactorService.FindReferences(asset.Name)` and show via `_findResults.ShowReferences(asset.Name, refs)`.

For "Rename...", open a small ImGui modal dialog using `ImGui.OpenPopup("##rename_inspector")`. Inside the modal, display an input field for the new name and on confirm, call `PreviewRename` + `ShowRenamePreview`.

The modal approach:
```csharp
// State fields
private string _renameBuffer = string.Empty;
private string? _pendingRenameFromKey;
private bool _openRenameModal;

// In DrawClientArea:
if (_openRenameModal)
{
    ImGuiNET.ImGui.OpenPopup("Rename##inspector");
    _openRenameModal = false;
}

if (ImGuiNET.ImGui.BeginPopupModal("Rename##inspector",
    ref _modalOpen, ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
{
    // input field + OK/Cancel
    ImGuiNET.ImGui.EndPopup();
}
```

Use `_modalOpen` as a bool field initialized to `true`. Actually the simpler pattern for a triggered modal is:

```csharp
private bool _renameModalOpen;
private string _renameFrom = string.Empty;
private byte[] _renameBuffer = new byte[256];
```

Use `System.Text.Encoding.UTF8.GetBytes` to initialize and `System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0))` to read back. Or even simpler: just use a C# `string` field and the `ImGui.InputText` overload with a ref string (not available in ImGuiNET) — instead use fixed-size byte buffer.

Actually, looking at the existing code pattern, use `ImGuiNET.ImGui.InputText` with a byte array. But given this is a shell window that will be expanded later, keep it minimal:

For the modal rename, just use a simple approach:
- Track `_pendingRenameKey` (string?)
- In DrawClientArea, draw the modal if `_pendingRenameKey != null`
- Use a `byte[256]` field for the input buffer

### 4. MODIFY `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs`

Add `IRefactorService` and `FindResultsWindow` injection. In `DrawClientArea()`, after `ImGui.Text(asset.Name)`, add `ImGui.OpenPopupOnItemClick("##browser_ctx_" + asset.AssetId, ImGuiNET.ImGuiPopupFlags.MouseButtonRight)` and the popup body with menu items.

Context menu:
- "Find References" → `_refactorService.FindReferences(asset.Name)` → `_findResults.ShowReferences(...)`
- "Rename..." → opens rename modal
- "Delete (preview)..." → calls `_refactorService.PreviewDelete(asset.AssetId, new DeleteOptions())` and shows `_findResults.ShowRenamePreview` with the delete preview info displayed (repurpose `ShowReferences` with the dangling refs)

### 5. MODIFY `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs`

Add registrations for `IReferenceCatalog`, `IRefactorService`, `AtomicMultiFileWriter`, and `FindResultsWindow`.

Add these usings:
```csharp
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
```

Add to `AddSharedAiEditor()`:
```csharp
services.AddSingleton<IReferenceCatalog, ReferenceCatalog>();
services.AddSingleton<AtomicMultiFileWriter>();
services.AddSingleton<IRefactorService, RefactorService>();
services.AddSingleton<FindResultsWindow>();
```

### 6. CREATE `Hrot/Editor/Hrot.Editor.AiShared.Tests/Refactor/RefactorEndToEndTests.cs`

Namespace: `Hrot.Editor.AiShared.Tests.Refactor`
Usings: `Hrot.Editor.AiShared.Catalog`, `Hrot.Editor.AiShared.Refactor`, `Hrot.Editor.AiShared.References`

**Do NOT re-define the fake classes** (`FakeReferenceCatalog`, `FakeAssetCatalog`, `FakeAsset`, `FakeSubElement`) — they already exist in `RefactorServiceTests.cs` in the SAME namespace. Add them `internal` in a new file only if there are compilation issues; otherwise the types already defined in `RefactorServiceTests.cs` in the same namespace should be visible.

Wait — check: in C#, two files in the same namespace can share types (the fakes are `internal`, so they're visible within the test assembly). So `RefactorEndToEndTests.cs` can use `FakeReferenceCatalog`, `FakeAssetCatalog`, `FakeAsset`, `FakeSubElement` directly without redeclaring them.

Test class: `RefactorEndToEndTests`

Tests to implement:

```csharp
[Fact]
public void RenameAction_across_multiple_files_updates_all_files()
{
    // Create 3 temp files, each with a reference to "action://OldName"
    // Build refCat + assetCat with 3 assets pointing to those files
    // Call PreviewRename then ApplyRename
    // Assert all 3 files now contain "action://NewName"
    // Cleanup temp files
}

[Fact]
public void RenameAction_leaves_non_matching_lines_unchanged()
{
    // Create temp file with "action://OldName" on line 1 and "action://OtherAction" on line 2
    // Rename "OldName" -> "NewName"
    // Assert line 1 changed, line 2 unchanged
}

[Fact]
public void PreviewDelete_with_dangling_refs_across_two_assets_reports_all_refs()
{
    // Asset A has element "action://ToDel"
    // Assets B and C both reference "action://ToDel"
    // PreviewDelete(assetA) should return 2 dangling refs
}

[Fact]
public async Task PreviewRenameAsync_returns_same_result_as_sync()
{
    // Use real temp file, call both sync and async variants
    // Assert the preview edits match
}
```

Ensure temp files are cleaned up in `finally` blocks.

### 7. MODIFY `Hrot/Editor/Hrot.Editor.AiShared.Tests/Di/SharedAiEditorDiTests.cs`

Add a test verifying `IRefactorService` resolves:

```csharp
[Fact]
public void AddSharedAiEditor_Resolves_IRefactorService()
{
    var services = new ServiceCollection();
    services.AddSharedAiEditor();
    var provider = services.BuildServiceProvider();
    var svc = provider.GetRequiredService<IRefactorService>();
    Assert.IsType<RefactorService>(svc);
}
```

And one for `FindResultsWindow`:
```csharp
[Fact]
public void AddSharedAiEditor_Resolves_FindResultsWindow()
{
    var services = new ServiceCollection();
    services.AddSharedAiEditor();
    var provider = services.BuildServiceProvider();
    var win = provider.GetService<FindResultsWindow>();
    Assert.NotNull(win);
}
```

Add usings at the top: `using Hrot.Editor.AiShared.Refactor;`

## Implementation notes

### InspectorWindow rename modal

Use this pattern for the rename dialog (keeping it minimal):

```csharp
private string? _pendingRenameKey;
private readonly byte[] _renameBuf = new byte[512];

// In DrawClientArea(), AFTER the context menu code:
if (_pendingRenameKey != null && ImGuiNET.ImGui.BeginPopupModal(
    "Rename##insp", ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
{
    ImGuiNET.ImGui.Text($"Rename: {_pendingRenameKey}");
    ImGuiNET.ImGui.Text("New name:");
    ImGuiNET.ImGui.SameLine();
    ImGuiNET.ImGui.InputText("##rname_insp", _renameBuf, (uint)_renameBuf.Length);
    if (ImGuiNET.ImGui.Button("OK"))
    {
        var newKey = System.Text.Encoding.UTF8.GetString(_renameBuf).TrimEnd('\0');
        if (!string.IsNullOrWhiteSpace(newKey))
        {
            var preview = _refactorService.PreviewRename(
                _pendingRenameKey, newKey, new RefactorOptions());
            _findResults.ShowRenamePreview(preview);
        }
        _pendingRenameKey = null;
        Array.Clear(_renameBuf, 0, _renameBuf.Length);
        ImGuiNET.ImGui.CloseCurrentPopup();
    }
    ImGuiNET.ImGui.SameLine();
    if (ImGuiNET.ImGui.Button("Cancel"))
    {
        _pendingRenameKey = null;
        Array.Clear(_renameBuf, 0, _renameBuf.Length);
        ImGuiNET.ImGui.CloseCurrentPopup();
    }
    ImGuiNET.ImGui.EndPopup();
}
```

Opening the modal requires calling `ImGui.OpenPopup("Rename##insp")` at the point you decide to open it (in the context menu item handler), then the `BeginPopupModal` code can live anywhere in the same `DrawClientArea()` frame.

### AssetBrowserWindow context menu

```csharp
foreach (var asset in _catalog.All)
{
    var label = asset.Name;
    ImGuiNET.ImGui.Selectable(label);
    var popupId = $"##bctx_{asset.AssetId}";
    if (ImGuiNET.ImGui.BeginPopupContextItem(popupId))
    {
        if (ImGuiNET.ImGui.MenuItem("Find References"))
        {
            var refs = _refactorService.FindReferences(asset.Name);
            _findResults.ShowReferences(asset.Name, refs);
        }
        if (ImGuiNET.ImGui.MenuItem("Rename..."))
        {
            _pendingRenameAsset = asset;
            _pendingRenameOpen = true;
            Array.Clear(_browserRenameBuf, 0, _browserRenameBuf.Length);
        }
        if (ImGuiNET.ImGui.MenuItem("Delete (preview)..."))
        {
            var deletePreview = _refactorService.PreviewDelete(
                asset.AssetId, new DeleteOptions());
            _findResults.ShowReferences(
                $"Delete preview: {asset.Name}",
                deletePreview.DanglingReferences);
        }
        ImGuiNET.ImGui.EndPopup();
    }
}
```

For the rename modal in AssetBrowserWindow, use a similar pattern to InspectorWindow with a `byte[512]` buffer and `BeginPopupModal("Rename##browser", ...)`.

### Avoid CS0067 for events in fakes

No new fake event fields are needed for BATCH-34 (we reuse existing fakes).

## Success conditions

1. `dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj` → 0 errors, 0 warnings
2. `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` → all pass (156 existing + ~8 new)
3. New file `Hrot/Editor/Hrot.Editor.AiShared/Windows/FindResultsWindow.cs` exists
4. `SharedAiWindowRegistrar` has `FindResultsWindow` added
5. `SharedAiEditorServiceCollectionExtensions` registers `IReferenceCatalog`, `IRefactorService`, `AtomicMultiFileWriter`, `FindResultsWindow`
6. `InspectorWindow` has right-click context menu with Find References and Rename
7. `AssetBrowserWindow` has right-click context menu with Find References, Rename, Delete preview
8. `RefactorEndToEndTests.cs` created with 4 tests, all passing
9. `SharedAiEditorDiTests.cs` has 2 new tests for `IRefactorService` and `FindResultsWindow`

## Checklist

- [ ] S7-03: `FindResultsWindow` created and registered
- [ ] S7-04: Inspector right-click added
- [ ] S7-05: Asset Browser right-click added
- [ ] S7-06: E2E test created with 4 tests
- [ ] DI registrations updated
- [ ] Build passes (0 errors, 0 warnings)
- [ ] All tests pass
