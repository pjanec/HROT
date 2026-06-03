# MVE-BATCH-04 Report — Editor Save (active blueprint → disk), Corrected Real-Editor Wiring

## Implementation Summary

### GraphEditorWindow Revert

The original MVE-BATCH-04 delivery wired Ctrl+S and a Save button into `GraphEditorWindow.DrawUI()`.  
This window is **orphaned** (in-degree 0, out-degree 0 per codebase-memory graph; zero references from `EditorSubsystem`; not registered with the production `WindowManager`).  All Save wiring in `GraphEditorWindow.DrawUI()` was dead code.

**Reverted changes:**
- Removed the `ImGui.IsKeyDown/IsKeyPressed` Ctrl+S block from `GraphEditorWindow.DrawUI()`.
- Removed the `if (ImGui.Button("Save"))` call.
- Removed the `internal void ExecuteSave()` method and `public string LastSaveMessage` property.

`GraphEditorWindow` is now back to its pre-BATCH-04 state: it renders a "Quick Reload" and "Full Rebuild" toolbar and the canvas placeholder only. The window remains in the codebase (it is used by `BlueprintWindowRegistrar` and the old editor test infrastructure), but is not shown in the production editor.

---

### Task 1 — SaveActiveBlueprintCommand: updated resolver

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/SaveActiveBlueprintCommand.cs`

**Core (headless, kept as-is):** `SaveActiveBlueprintCommand.Save(BlueprintAsset asset, string path)`
- Same pin-clear swap/restore logic (documented in the original report). Tested by TC-1..TC-4, TC-8.

**Resolver — replaced `SaveFromWindow` with `SaveFromActiveDocument`:**

```csharp
public static SaveResult SaveFromActiveDocument(
    AiDocumentManager? manager,
    DirtyTracker       dirtyTracker,
    Action<string>?    report = null)
```

Resolution chain (mirrors the MVE-BATCH-03 run-button):

| Step | Expression | Source |
|------|-----------|--------|
| Active document | `manager.Active` | `AiDocumentManager.cs:73` |
| Live BlueprintAsset | `active.ViewState as AiCanvasContext → ctx.AssetRef as BlueprintAsset` | `AiGraphCanvasWindow.cs:32` |
| Source file path | `active.Asset.SourceFilePath` | `IEditableAsset.cs:8` (implemented by `BlueprintFileAsset.cs:101`) |

After a successful save:
- `active.MarkClean()` clears the `AiDocument`'s dirty flag.
- `dirtyTracker.MarkClean(asset.AssetId)` clears the Blueprint-subsystem dirty tracker.
- `report?.Invoke(ok.Message)` notifies the UI (called with all outcomes — save path on success, error message on no-op).

---

### Task 1 — EditorSubsystem wiring

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

**New fields (line ~292):**
```csharp
private Action? _blueprintSaveCallback;
private string _blueprintSaveStatus = string.Empty;
private Hrot.Blueprints.Editor.DirtyTracker _blueprintSaveDirtyTracker = new();
```

**Registration in `RegisterWindows` (after the run-button block, line ~1809):**
```csharp
var saveRegistrar = new Hrot.Blueprints.Editor.Internal.CaptureWindowRegistrar();
saveRegistrar.RegisterToolbarEntry("Save Blueprint", () =>
{
    Hrot.Blueprints.Editor.SaveActiveBlueprintCommand.SaveFromActiveDocument(
        _aiDocumentManager,
        _blueprintSaveDirtyTracker,
        msg => _blueprintSaveStatus = msg);
});
_blueprintSaveCallback = saveRegistrar.GetToolbarCallback("Save Blueprint");
```

- Uses the same `CaptureWindowRegistrar` pattern as MVE-BATCH-03.
- `_aiDocumentManager` is the same `AiDocumentManager` instance already used by the run-button.
- `_blueprintSaveDirtyTracker` is a fresh `DirtyTracker` owned by `EditorSubsystem`.

**DrawUI render (gated on `!_headless && ImGui.GetCurrentContext() != Zero`, line ~1481):**
```csharp
if (_blueprintSaveCallback != null && ImGuiNET.ImGui.GetCurrentContext() != System.IntPtr.Zero)
{
    if (ImGuiNET.ImGui.Begin("Blueprint Save"))
    {
        // Ctrl+S shortcut (ImGui scope).
        if (IsWindowFocused(...) && IsKeyDown(ModCtrl) && IsKeyPressed(S))
            _blueprintSaveCallback.Invoke();

        if (ImGui.Button("Save Blueprint"))
            _blueprintSaveCallback.Invoke();

        if (!string.IsNullOrEmpty(_blueprintSaveStatus))
        { ImGui.SameLine(); ImGui.TextUnformatted(_blueprintSaveStatus); }
    }
    ImGui.End();
}
```

- Callback itself is ImGui-free and headlessly testable.
- The `_headless` guard means `DrawUI` returns early in all test paths; `GetCurrentContext() != Zero` is a secondary guard for when `_headless` is not set but no ImGui context has been initialized.
- `EditorSubsystemBootTests` run headlessly (`_headless = true`), so no ImGui window is attempted — 10/10 still pass.

---

### Task 2 — Tests updated

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/SaveActiveBlueprintCommandTests.cs`

TC-5/TC-6/TC-7 replaced:

| Test | Scenario | Assertion |
|------|----------|-----------|
| `SaveFromActiveDocument_NoDocumentOpen_ReturnsNoBlueprintOpen` | `AiDocumentManager` with no open docs → `Active == null` | `Status == NoBlueprintOpen`; `report` callback called |
| `SaveFromActiveDocument_EmptySourcePath_ReturnsNoSourcePath` | Open document but `StubEditableAsset.SourceFilePath == ""` | `Status == NoSourcePath` |
| `SaveFromActiveDocument_ValidPath_SavesAndMarksBothClean` | Valid path; `doc.MarkDirty()` + `tracker.MarkDirty(...)` before save | File written; `tracker.IsDirty == false`; `doc.IsDirty == false`; status contains path |

TC-1/TC-2/TC-3/TC-4/TC-8 (test `Save(asset, path)` core) are unchanged and still pass.

Helpers added:
- `MakeManagerWithDocument(fileAsset, blueprintAsset)` — builds `AiDocumentManager`, wires `DocumentOpened` to populate `AiCanvasContext { AssetRef = blueprintAsset }`, then opens the document.
- `MakeMinimalGraphView(asset)` — constructs `BlueprintGraphModel → BlueprintTypeSystem → BlueprintLinkValidator → BlueprintNodeCatalog → GraphView` (same approach as `BcpBatch02BlueprintTests`).
- `StubEditableAsset` — configurable `SourceFilePath`, implements `IEditableAsset`.
- `StubCommandSink` / `StubEditorHostServices` — minimal stubs for `GraphView` construction.

The old `MakeWindow` / `MakeQuickReload` / `StubCatalog` helpers were removed (no longer needed).

---

## Design Decisions

**Use `CaptureWindowRegistrar` pattern (same as run-button):** The task explicitly requested mirroring MVE-BATCH-03's approach. `CaptureWindowRegistrar` captures the `Action` in `RegisterWindows` and the `DrawUI` renders the ImGui button with the captured callback. This keeps the save logic fully ImGui-free and headlessly testable.

**Dedicated `_blueprintSaveDirtyTracker`:** EditorSubsystem does not have a pre-existing shared `DirtyTracker` for the blueprint pipeline at this MVE stage (`BlueprintEditorModule._dirtyTracker` is a separate legacy instance; `EditService` has no `DirtyTracker` property). Adding a dedicated tracker in `EditorSubsystem` is the cleanest approach without touching the legacy module. When the full blueprint pipeline is unified, this can be replaced with the shared tracker.

**Ctrl+S inside the ImGui window block:** The BCP-BATCH-02-FIX `EditorHotkeyDispatcher`/`IEditorCommands` is part of NodeEdit canvas rendering (pumped per-document in `AiGraphCanvasWindow.DrawPickerAndPumpHotkeys`). Blueprint `SaveFromActiveDocument` is a cross-cutting concern triggered from `EditorSubsystem`, not from within a canvas render pass. Implementing it via direct ImGui key checks inside the `"Blueprint Save"` window is the simplest correct approach; migrating to the canvas-level dispatcher is a future step once the blueprint canvas is fully wired.

---

## Deviations

| What | Why | Benefit | Risk |
|------|-----|---------|------|
| Reverted all `GraphEditorWindow` Save wiring | `GraphEditorWindow` is not referenced by `EditorSubsystem` (in-degree 0 in graph) and is not shown in the production editor — Save was dead code | Removes misleading dead code path | None |
| Replaced `SaveFromWindow` resolver with `SaveFromActiveDocument` | Original resolver used `GraphEditorWindow.CurrentAsset/CurrentAssetPath`; production wiring uses `AiDocumentManager.Active` | Correct production-path resolver | None |

---

## Test Results

```
SaveActiveBlueprintCommandTests (Hrot.Blueprints.Tests):
  Passed: 8 / 8
  (TC-1: round-trip mutations, TC-2: projection-only pins empty, TC-3: live pins untouched,
   TC-4: byte-stable, TC-5: no-doc returns NoBlueprintOpen, TC-6: empty path returns NoSourcePath,
   TC-7: valid path saves + marks both doc and tracker clean, TC-8: multi-graph pins all empty)

Full Hrot.Blueprints.Tests suite:
  Total: 1165, Passed: 1147, Failed: 10 (all pre-existing DEBT-006), Skipped: 8

Hrot.ClusterRunner.Integration.Tests --filter EditorSubsystemBoot:
  Total: 10, Passed: 10

Hrot.Editor.AiShared.Tests:
  Total: 761, Passed: 761
```

Pre-existing DEBT-006 failures (unchanged, 10 total):
- `InstanceEmitGoldenTests` ×3, `LibraryEmitGoldenTests` ×1, `AiPrimitiveEmitGoldenTests` ×2
- `ConditionSummaryAttachmentTests` ×1, `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` ×1
- `LibraryMathDemoTests` ×1, `MoveToAndFireDemoTests` ×1

---

## Build Status

```
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet
  Build succeeded.  0 Error(s),  1 Warning(s)
  (1 warning: pre-existing CS0618 in Hrot.Diagnostics.Breakpoints.Tests — not a touched project)

Hrot.Blueprints.Editor  (TreatWarningsAsErrors=true): 0 warnings
Hrot.Editor             (TreatWarningsAsErrors=true): 0 warnings
```

---

## Developer Insights

1. **`GraphEditorWindow` is a legacy orphan.** It has zero in-degree and zero out-degree in the codebase-memory graph, meaning nothing references it. It was the original Blueprint editor before the AiShared perspective/document infrastructure was added. It should be marked `[Obsolete]` or removed in a future cleanup batch.

2. **Active-document resolution is identical to run-button (MVE-BATCH-03):** `_aiDocumentManager.Active?.ViewState as AiCanvasContext → ctx.AssetRef as BlueprintAsset` and `active.Asset.SourceFilePath`. The Save command closes the loop: open-in-editor-path is `AiDocumentManager.Open → DocumentOpened → BlueprintDocumentFactory.Build → AiCanvasContext { AssetRef = blueprintAsset }`.

3. **AiDocument.MarkClean() is important:** Without it, the document's `IsDirty` flag stays true even after Save. Both the `AiDocument` and the `DirtyTracker` (legacy blueprint subsystem) are cleared together for consistency.

4. **`_blueprintSaveDirtyTracker` duplication:** At the MVE stage there are two DirtyTrackers for blueprints: the one in `BlueprintEditorModule._dirtyTracker` (used by `AssetBrowserWindow`, `GraphEditorWindow`, `InspectorWindow`) and the new one in `EditorSubsystem`. This will need unification when the legacy `BlueprintEditorModule` is retired.

---

## Known Issues

- **"Blueprint Save" ImGui window is a floating panel**, not integrated into the Blueprint perspective toolbar. Future step: integrate into the perspective toolbar seam once a toolbar API is available in `PerspectiveWorkspaceRegistrar`.
- **`_blueprintSaveDirtyTracker` vs. legacy tracker divergence:** The new tracker tracks only Save-triggered clean events; the legacy tracker (in `BlueprintEditorModule`) tracks edits from the legacy node editor. These should be unified.

---

## Remaining MVE Steps

- **MVE-05 (compile-on-demand):** Wire `QuickReloadService.TriggerAsync` to compile the active `.bp.json` without a prior `dotnet build`.
- **MVE-06 (hot-reload):** Running instance + recompile → `AiHotReloadCoordinator` commit → verify live instance picks up change.
- **MVE-07 (debug):** Breakpoint/watch on a running instance via `BlueprintDebugSession`.
- **MVE-08 (editor button):** "Run Opened Blueprint on a Test Entity" spawns into real running sim world.

---

## Suggested Commit Message

`fix(blueprint-mve): wire Save into EditorSubsystem (real editor); revert dead GraphEditorWindow Save path (MVE-BATCH-04)`
