# BATCH-41-REPORT — Generic NodeEdit `SaveAsBrowserDialog`

**Batch:** BATCH-41  
**Task:** MTB2-T8 / DEC-A8 — generic Save-As browser dialog in NodeEdit  
**Date:** 2026-06-12  
**Status:** ✅ COMPLETE  

---

## Implementation Summary

Built the generic `SaveAsBrowserDialog` in `NodeEditor.UI/Dialogs/` — a reusable "Save As" browser that lets users type a name, browse/choose a destination folder via a two-pane UI, create new folders, and confirm with commit-time overwrite protection. The component is **generic NodeEdit** — no `Hrot.*`/asset/editor types.

### Files created

| File | Purpose |
|------|---------|
| `NodeEditor.UI/Dialogs/SaveAsBrowserDialog.cs` | Core dialog + `SaveAsRequest`, `SaveAsResult`, `SaveAsContentItem` |
| `NodeEditor.Demo/Scenarios/S14_SaveAsBrowser.cs` | Demo scenario with fake folder tree, contents, icon provider |
| `NodeEditor.UI.Tests/Dialogs/SaveAsBrowserDialogTests.cs` | 5 headless tests exercising the public seams |

### Files modified

| File | Change |
|------|--------|
| `NodeEditor.Demo/DemoShell.cs` | Registered `S14_SaveAsBrowser` in the scenario list |

---

## API

```csharp
public sealed record SaveAsContentItem(string Name, string? IconKey);
public sealed record SaveAsResult(bool Confirmed, string Name, string DestinationPath, bool Overwrite);

public sealed class SaveAsRequest
{
    public required string Title { get; init; }
    public string InitialName { get; init; } = "";
    public string InitialDestination { get; init; } = "";
    public string ConfirmLabel { get; init; } = "Save";
    public required Func<CategoryNode> GetFolderTree { get; init; }
    public Func<string, IReadOnlyList<SaveAsContentItem>>? GetFolderContents { get; init; }
    public Action<string, string>? OnCreateFolder { get; init; }
    public Func<string, string, bool>? NameExists { get; init; }
    public Func<string, string?>? ValidateName { get; init; }
}

public sealed class SaveAsBrowserDialog
{
    public bool IsOpen { get; }
    public bool PendingOverwriteConfirm { get; }
    public void Open(SaveAsRequest request, Action<SaveAsResult> onChosen);
    public void Close();
    public SaveAsResult ConfirmActive();
    public SaveAsResult ConfirmOverwrite();
    public void SetName(string name);
    public void SetDestination(string path);
    public void DrawFrame(IIconProvider icons);
}
```

---

## Reuse & Idioms

- **`CategoryNode`** (from `NodeEditor.UI.Picker.PickerRequest`) — reused for the folder tree data model. The dialog calls `GetFolderTree()` every frame so newly created folders appear automatically.
- **`IIconProvider` + `IconHandle`** (from `NodeEditor.Core.Interfaces`) — the host passes its icon provider to `DrawFrame` each frame. Folder icons (`folder`/`folder_open`) and content-item icons (via `IconKey`) resolved through `icons.TryGet()` + `ImGui.Image(handle.TextureId, size, uv0, uv1)`, mirroring the **TreeLayout T1 icon idiom** exactly.
- **`PickerWindow` window mechanics** — floating window with `SetNextWindowSize` (720×560), `Begin`/`End` with `NoCollapse | NoSavedSettings` flags, `SetKeyboardFocusHere` for auto-focus on first frame, and `ImGui.IsKeyPressed(ImGuiKey.Escape)` for cancel.
- **`TreeLayout` folder rendering** — folder icon before each `TreeNodeEx`, using `GetStateStorage().GetInt(nodeId)` to detect open/closed state and pick `folder_open` vs `folder`. Content-item icon+label rendering uses the same draw-list pattern as `TreeLayout.DrawLeafItem`.
- **`DemoShell` popup pattern** — `OpenPopup` + `BeginPopupModal` + `AlwaysAutoResize | NoMove` for the new-folder creation popup and overwrite-confirmation popup.

---

## Overwrite Confirmation Seam

The overwrite confirmation is **commit-time** (not pre-validated while typing). The flow:

1. User clicks Confirm (or presses Enter in the name field)
2. `ConfirmActive()` is called internally
3. If `ValidateName(name)` returns an error → no-op (stays open)
4. If `NameExists(name, dest)` returns `true` → sets `PendingOverwriteConfirm = true`, does **not** fire `onChosen`, dialog stays open
5. If `NameExists` returns `false` → fires `onChosen(overwrite:false)`, closes

When `PendingOverwriteConfirm` is true:
- The UI renders a modal popup: `"'{name}' already exists in {dest}. Overwrite?"` with [Overwrite] / [Cancel]
- Overwrite → `ConfirmOverwrite()` → fires `onChosen(overwrite:true)`, closes
- Cancel / Esc → resets `PendingOverwriteConfirm` to false, returns to the dialog

The headless seams (`ConfirmActive`, `ConfirmOverwrite`, `PendingOverwriteConfirm`) allow tests to exercise the full state machine without ImGui.

---

## UX Implementation

| Feature | Implementation |
|---------|---------------|
| Name field + auto-focus | `SetKeyboardFocusHere` on first frame, `InputText` with `EnterReturnsTrue` |
| Enter-confirm / Esc-cancel | Enter in name field or global Enter triggers `ConfirmActive()`; Esc calls `Close()` |
| Validation error | Red text below name field when `ValidateName` returns non-null; disables Confirm button |
| Two-pane layout | `BeginChild` columns: 40% folders, 60% contents |
| Folder tree | Recursive `TreeNodeEx` of `CategoryNode` children; full path computed as `parent + "/" + Name` |
| Folder icons | `folder` / `folder_open` via `icons.TryGet` + `ImGui.Image` (mirrors T1 TreeLayout) |
| Content-item icons | `IconKey` resolved via `icons.TryGet` + `ImGui.Image` before `Selectable` |
| Click-item-to-prefill | `Selectable` click sets `_name` to the item's name |
| New Folder button | `"+ New Folder"` → opens new-folder popup targeting current `_destination` |
| Folder right-click menu | `BeginPopupContextItem` → `"New Folder…"` → opens new-folder popup targeting the right-clicked folder |
| New Folder popup | Modal with auto-focused `InputText` (`EnterReturnsTrue`) + Create/Cancel; Create → `OnCreateFolder(target, name)`, selects new folder, closes |
| Path preview | `"Path: " + destination + "/" + name` below the panes |
| Overwrite confirm | Modal popup triggered by `PendingOverwriteConfirm`; Overwrite/Cancel buttons |
| Confirm button | Uses `ConfirmLabel` (default "Save"), disabled when name invalid |

---

## Demo `S14_SaveAsBrowser`

- Button `"Open SaveAs Browser"` opens the dialog
- Fake `CategoryNode` tree: root → {AI→{Combat}, Patrol, Shared}
- `GetFolderContents` returns 2 items per folder with distinct `IconKey`s (`asset/blueprint`, `asset/btree`, `asset/hsm`)
- `DemoIconProvider` resolves `folder`/`folder_open` + all content icon keys (distinct UV cells — follows S13 pattern)
- `OnCreateFolder` mutates a local `_createdFolders` set; `GetFolderTree` re-builds from it each frame
- `NameExists` returns `true` for `("BP_Enemy", "AI/Combat")` to exercise the overwrite path
- `ValidateName` rejects empty/whitespace and invalid filename chars
- Result toasted via `ToastQueue`

---

## Tests — 5 exact named (headless)

All tests run without ImGui context, exercising the public seams directly.

| Test | What it verifies |
|------|-----------------|
| `Open_SetsIsOpen_True_AndClose_Cancels` | `Open` → `IsOpen` true; `Close` → `onChosen(Confirmed:false)`, `IsOpen` false |
| `ConfirmActive_NewName_FiresOnChosen_NoOverwrite_AndCloses` | `NameExists => false`; `SetName("Foo")`, `SetDestination("AI")`; `ConfirmActive()` → `onChosen(true,"Foo","AI",Overwrite:false)`, `IsOpen` false |
| `ConfirmActive_ExistingName_SetsPendingOverwrite_NoFire` | `NameExists => true`; `ConfirmActive()` → `PendingOverwriteConfirm` true, `onChosen` NOT called, `IsOpen` still true |
| `ConfirmOverwrite_AfterPending_FiresOnChosen_Overwrite_AndCloses` | Continues above; `ConfirmOverwrite()` → `onChosen(true,name,dest,Overwrite:true)`, `IsOpen` false |
| `ConfirmActive_InvalidName_DoesNotConfirm` | `ValidateName => "bad"`; no fire, `IsOpen` true |

---

## Build & Test Results

### Build
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Filtered tests (SaveAsBrowserDialog)
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 13 ms
```

### Full NodeEditor.UI.Tests
```
Passed!  - Failed:     0, Passed:    56, Skipped:     0, Total:    56, Duration: 56 ms
```

### Full NodeEditor.Core.Tests
```
Passed!  - Failed:     0, Passed:   181, Skipped:     0, Total:   181, Duration: 39 ms
```

No `BLUEPRINT_REGENERATE_SNAPSHOTS` was used.

---

## Design Decisions

1. **State-flag popups (not nested ImGui Begin/End):** The new-folder and overwrite-confirmation popups use `_newFolderTarget` and `_pendingOverwriteConfirm` state flags + `BeginPopupModal`. The popups are rendered **outside** the main dialog's `Begin`/`End` block so they stack correctly as top-level modals. This avoids child-window clipping issues.

2. **`GetFolderTree` called every frame:** The spec says "re-evaluated each frame (so new folders appear)." After `OnCreateFolder` mutates state, the next frame's `GetFolderTree()` call reconstructs the tree including the new folder. The demo mutates a `HashSet<string>` and rebuilds the `CategoryNode` tree from it.

3. **Overwrite confirmation as modal popup:** Rather than inline replacement (which would hide the folder tree context), a small focused modal appears showing the name, destination, and Overwrite/Cancel buttons. The `PendingOverwriteConfirm` seam is the single source of truth; the popup reads it.

4. **`EnterReturnsTrue` on name field + global Enter fallback:** The name field uses `EnterReturnsTrue` for confirm. A global `ImGui.IsKeyPressed(ImGuiKey.Enter)` handles the case where Enter is pressed without the name field focused (but only when the name is valid and no other item is active).

5. **Folder open/closed icon detection via `GetStateStorage`:** Mirroring `TreeLayout`, the dialog reads `ImGui.GetStateStorage().GetInt(nodeId, 0)` to determine whether a `TreeNodeEx` was open last frame, selecting `folder_open` vs `folder` accordingly. There is a 1-frame lag, which is acceptable per the established pattern.

6. **Icon alignment for content items:** Content items without icons get the same left-padding as icon-bearing items (`IconSize.X + 4f`) so text aligns vertically in the list.

---

## Deviations

None. The implementation follows the spec exactly.

---

## Challenges

- **ImGui popup rendering location:** Initially attempted to render popups inside the main `Begin`/`End` block but realized this constrains them to the child region. Moved popup rendering after `ImGui.End()` so they appear as top-level modals — cleaner and avoids clipping.
- **Headless seam design:** The `ConfirmActive`/`ConfirmOverwrite` seam needed to both return `SaveAsResult` AND fire the callback for the UI path to work. For headless tests, the callback is captured and verified; for the UI path, the returned result is used to close the dialog correctly.

---

## Integration Notes

- This dialog is ready for **BATCH-42** (open-from-in-memory) and **BATCH-43** (editor wiring).
- Hosts must provide an `IIconProvider` to `DrawFrame` each frame. The dialog uses `folder`/`folder_open` + whatever `IconKey` strings the host puts in `SaveAsContentItem`.
- The `GetFolderTree` callback is called every frame — hosts should ensure it's cheap (the demo rebuilds a tiny tree from a HashSet; production hosts should memoize or use a pre-built tree).
- No `Hrot.*` types are referenced — the dialog is fully generic and can be used anywhere `CategoryNode` + `IIconProvider` are available.

---

## Summary

| Metric | Result |
|--------|--------|
| New files | 3 (dialog, demo, tests) |
| Modified files | 1 (DemoShell registration) |
| Build warnings | 0 |
| Filtered tests | 5 passed, 0 failed |
| Full UI tests | 56 passed, 0 failed |
| Full Core tests | 181 passed, 0 failed |
| BLUEPRINT_REGENERATE_SNAPSHOTS | Not used |
