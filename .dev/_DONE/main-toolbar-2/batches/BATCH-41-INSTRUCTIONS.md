# BATCH-41 — MTB2-T8 (generic): NodeEdit `SaveAsBrowserDialog` (name + destination browser)

**Task:** MTB2-T8 / DEC-A8 / D-T8-2 / D-T8-3 — the **generic** Save-As browser dialog **in NodeEdit** (replaces the
editor-specific `AssetNameFolderModal`). **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
This batch = the **generic NodeEdit component + demo + tests**. Editor wiring is BATCH-43; open-from-in-memory is BATCH-42.

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file. 3. Read for reuse + idioms:
   `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerWindow.cs` (auto-focus search box `SetKeyboardFocusHere`;
   floating `Begin` window + explicit `SetNextWindowSize`; Enter-confirm/Esc-cancel; once-per-frame `DrawFrame`),
   `Picker/PickerRequest.cs` (`CategoryNode` — reuse), `Picker/Layouts/TreeLayout.cs` (folder-tree render + folder
   icons via `ctx.Icons.TryGet("folder"/"folder_open")` + `ImGui.Image(handle.TextureId,size,Uv0,Uv1)`, leaf type
   icons via `IconKey` — built in T1), `Core/Interfaces/IIconProvider.cs` (`IconHandle`).

## ⚙️ RULES (non-negotiable)
1. Touch ONLY: NEW `NodeEditor.UI/Dialogs/SaveAsBrowserDialog.cs` (+ request/result/item types); NEW demo
   `NodeEditor.Demo/Scenarios/S14_SaveAsBrowser.cs` + register in `DemoShell.cs`; NEW tests
   `NodeEditor.UI.Tests/Dialogs/SaveAsBrowserDialogTests.cs`. **No `Hrot.*`/asset/editor types** — generic NodeEdit.
2. NEVER hide a problem to pass a build (no excluded assets/`[Skip]`/weakened tests/stubs/suppression).
3. Add the EXACT named tests (headless seams); assert real behavior.
4. DO NOT STOP until build = 0 warnings (incl. `NodeEditor.Demo` warnings-as-errors) AND tests `Failed: 0`
   (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact files/tests + final summary. No litter.

## Objective (the converged UX — a generic "Save As" browser)
A reusable dialog: **type a name + browse/choose a destination folder (seeing what's already there) + create folders +
confirm**, with **commit-time** overwrite confirmation. Used by document Save-As/first-save and scenario Save-As.
No "recipe" concern here (recipe is a separate prior step).

## API (`NodeEditor.UI/Dialogs/`)
```csharp
public sealed record SaveAsContentItem(string Name, string? IconKey);   // an existing item shown in a folder
public sealed record SaveAsResult(bool Confirmed, string Name, string DestinationPath, bool Overwrite);

public sealed class SaveAsRequest
{
    public required string Title { get; init; }
    public string InitialName { get; init; } = "";
    public string InitialDestination { get; init; } = "";          // selected folder relpath ("" = root)
    public string ConfirmLabel { get; init; } = "Save";
    public required Func<CategoryNode> GetFolderTree { get; init; } // re-evaluated each frame (so new folders appear)
    public Func<string, IReadOnlyList<SaveAsContentItem>>? GetFolderContents { get; init; } // items in a folder
    public Action<string, string>? OnCreateFolder { get; init; }   // (parentRelPath, newName); null => no create
    public Func<string, string, bool>? NameExists { get; init; }   // (name, destPath) => already exists? (commit-time)
    public Func<string, string?>? ValidateName { get; init; }      // (name) => error (empty/invalid chars) or null
}

public sealed class SaveAsBrowserDialog
{
    public bool IsOpen { get; }
    public void Open(SaveAsRequest request, Action<SaveAsResult> onChosen);
    public void Close();                              // cancels: onChosen(Confirmed:false,…)
    // Headless seams:
    public bool PendingOverwriteConfirm { get; }      // true after Confirm hit an existing name
    public SaveAsResult ConfirmActive();              // validate → if NameExists: set PendingOverwriteConfirm (no fire); else fire onChosen(overwrite:false)+close
    public SaveAsResult ConfirmOverwrite();           // when PendingOverwriteConfirm: fire onChosen(overwrite:true)+close
    public void SetName(string name); public void SetDestination(string path);  // headless state setters for tests
    public void DrawFrame(IIconProvider icons);       // once per frame (host passes its icon provider)
}
```

## `DrawFrame` UX (mirror PickerWindow window mechanics + T1 tree icons)
Floating window (Title + explicit `SetNextWindowSize`, e.g. 720×560):
- **Name** `InputText` at top, **auto-focused on first frame** (`SetKeyboardFocusHere` once), `EnterReturnsTrue`.
  Below it show `ValidateName(name)` error in red if any (disables Confirm).
- **Two panes** (use `BeginChild` columns):
  - **Left "Folders":** render `GetFolderTree()` (a `CategoryNode`) recursively as `TreeNodeEx`, computing each node's
    full path as `parent + "/" + Name`; draw a **folder icon** before each (closed `folder` / open `folder_open` via
    `icons.TryGet` + `ImGui.Image` with the handle UVs — mirror `TreeLayout`); single-click selects (sets the dest);
    selected node highlighted. **Right-click a folder → context menu (`BeginPopupContextItem`) → "New Folder…"** →
    opens the New-Folder popup (below) targeting that folder.
  - **Right "Contents of <dest>":** if `GetFolderContents != null`, list `GetFolderContents(selectedDest)` items, each
    with its **`IconKey`** drawn before the name (via `icons.TryGet`+`ImGui.Image`); **clicking an item prefills the
    Name** with that item's name (overwrite path).
- **"＋ New Folder"** button (when `OnCreateFolder != null`) AND the folder context-menu both open a nested popup with
  an **auto-focused** `InputText` (`EnterReturnsTrue`) + Create/Cancel: Enter/Create → `OnCreateFolder(targetFolder,
  name)`, select `targetFolder + "/" + name`, close popup; Esc/Cancel → close popup only.
- **Path preview** line: the destination + "/" + name.
- **Confirm** button (`ConfirmLabel`), enabled iff `ValidateName` returns null. On click (or Name-field Enter) → same
  as `ConfirmActive()`: if `NameExists(name,dest)` → show an **overwrite confirmation** (an inline second popup or a
  state flag) "'{name}' already exists in {dest}. Overwrite?" [Overwrite]/[Cancel]; Overwrite → `ConfirmOverwrite()`;
  Cancel → back to the dialog. If not existing → confirm (overwrite:false) + close.
- **Cancel** / **Esc** (main window) → `onChosen(Confirmed:false,…)` + close.

## Demo `S14_SaveAsBrowser.cs` (+ register in DemoShell)
A button opens the dialog with: a fake `CategoryNode` tree (root→{AI→{Combat}, Patrol, Shared}); `GetFolderContents`
returning a few fake items per folder (with icon keys, e.g. `asset/blueprint`); a fake `IIconProvider` resolving
`folder`/`folder_open` + those keys (distinct cells); `OnCreateFolder` mutating a local folder set (so the new folder
appears — proves re-fetch); `NameExists` returning true for one fake taken name (to exercise the overwrite-confirm).
Toast the `SaveAsResult`. Builds clean under warnings-as-errors.

## Tests — `NodeEditor.UI.Tests/Dialogs/SaveAsBrowserDialogTests.cs` (EXACT names, headless)
- `Open_SetsIsOpen_True_AndClose_Cancels` — Open→IsOpen; Close→`onChosen(Confirmed:false)`, IsOpen false.
- `ConfirmActive_NewName_FiresOnChosen_NoOverwrite_AndCloses` — `NameExists => false`, `SetName("Foo")`,
  `SetDestination("AI")`; `ConfirmActive()` → `onChosen(true,"Foo","AI",Overwrite:false)`, IsOpen false.
- `ConfirmActive_ExistingName_SetsPendingOverwrite_NoFire` — `NameExists => true`; `ConfirmActive()` →
  `PendingOverwriteConfirm` true, `onChosen` NOT yet called, IsOpen still true.
- `ConfirmOverwrite_AfterPending_FiresOnChosen_Overwrite_AndCloses` — continue above → `ConfirmOverwrite()` →
  `onChosen(true,name,dest,Overwrite:true)`, IsOpen false.
- `ConfirmActive_InvalidName_DoesNotConfirm` — `ValidateName => "bad"` → no fire, IsOpen true.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build FDP/ExtDeps/NodeEdit/NodeEditor.sln
dotnet test  FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj --filter "FullyQualifiedName~SaveAsBrowserDialog"
dotnet test  FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj
dotnet test  FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj
```
Filtered `Failed: 0`; FULL UI + Core tests green; Demo builds (warnings-as-errors).

## Definition of done
- Generic `SaveAsBrowserDialog` in `NodeEditor.UI/Dialogs`, reusing `CategoryNode` + `IIconProvider` + PickerWindow
  idioms + T1 folder/type-icon rendering: auto-focus name, Enter-confirm, Esc-cancel, two-pane folders|contents with
  icons, New Folder via button **and** folder right-click, click-item-to-prefill, **commit-time overwrite-confirm**,
  path preview. NO `Hrot.*` types. Demo `S14` + 5 tests pass; build green (incl. demo).
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-41-REPORT.md`: API, reuse, the overwrite-confirm seam, files/tests, summary.

If something cannot be done as specified, STOP and report why.
