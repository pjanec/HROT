# BATCH-06: Save / Save-As / Save-All commands + Ctrl+S fix
**Tasks:** MTB-P2-T4   **Phase:** 2 — Shell Command Set & Binding Adapters   **Est:** ~8h
**Dependencies:** BATCH-05 (`WindowManager.ShellCommands`, command adapters). Completes Phase 2.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §20 (Save Commands & the Ctrl+S Fix) + §6.3 (hotkey dispatch).
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P2-T4.
4. Existing types you build on (read them):
   - `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocumentManager.cs` — `Active` (`AiDocument?`),
     `OpenDocuments`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocument.cs` — `Asset` (`IEditableAsset`),
     `Kind` (`AssetKind`), `IsDirty`, `MarkClean()`.
   - `IEditableAsset` — `Name`, `Kind`, `SourceFilePath`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Documents/SaveAllAiDocumentsCommand.cs` —
     `Execute(manager, saveBlueprintDelegate, saveBTreeDelegate, saveHsmDelegate, report?)`,
     `SaveDelegate(IEditableAsset asset, string path)`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Windows/EditorHotkeyDispatcher.cs` —
     `ProcessThisFrame(IEditorCommands?)` matches each command's `DefaultKey` against current input.
   - `NodeEditor.Core.Action.IEditorCommands` / `EditorCommandDescriptor` / `KeyBinding` /
     `EditorKey` / `KeyModifiers` (BATCH-05).

## Decision context (DEV-LEAD DEC-9 — read before coding)
The full **Save-As dialog** is a later task (MTB-P6-T6). In THIS batch, `SaveAs` and the
"empty `SourceFilePath` → Save-As" route invoke a **seam**: an injected
`Action<AiDocument> requestSaveAs`. Phase 6 wires it to the real dialog; for now production passes a
seam that reports "Save As not yet available" (must not crash). Tests inject a recording seam.

## Scope — do ONLY this (MTB-P2-T4)
### 1. Testable registrar (NEW) — `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs`
A class that **registers** the three commands into a supplied `IEditorCommands` (the shell set) and
contains the pure decision logic so it is unit-testable with a mock/fake `AiDocumentManager`:
- Constructor/Register takes: the command set, the `AiDocumentManager`, per-kind save delegates
  (`SaveAllAiDocumentsCommand.SaveDelegate` for Blueprint/BTree/HSM — and scenario save if available),
  and `Action<AiDocument> requestSaveAs`.
- **`Save`** — id e.g. `shell.save`, `DefaultKey = Ctrl+S`, `IsEnabled = () => manager.Active != null`.
  Handler: let `doc = manager.Active`; if `doc == null` no-op; else if
  `string.IsNullOrEmpty(doc.Asset.SourceFilePath)` → `requestSaveAs(doc)` (do NOT write); else
  dispatch by `doc.Kind` to the matching per-kind delegate with `doc.Asset.SourceFilePath`, then
  `doc.MarkClean()`.
- **`SaveAs`** — id `shell.saveAs`, `DefaultKey = null`, `IsEnabled = () => manager.Active != null`.
  Handler: `requestSaveAs(manager.Active)` when Active != null.
- **`SaveAll`** — id `shell.saveAll`, `DefaultKey = Ctrl+Shift+S`,
  `IsEnabled = () => manager.OpenDocuments.Any(d => d.IsDirty)`. Handler: delegate to
  `SaveAllAiDocumentsCommand.Execute(manager, blueprintDelegate, btreeDelegate, hsmDelegate, report)`.
- Keep the per-kind dispatch readable; throw nothing on an unsupported kind — report/skip.

### 2. Wire into the composition root (`Hrot.Editor`)
- Register the three commands into `WindowManager.ShellCommands` at editor startup, passing
  production save delegates: Blueprint → `SaveActiveBlueprintCommand.Save(asset, path)` (or the
  existing `_blueprintSaveCallback` path), BTree/HSM → their JSON save services, scenario → existing
  scenario save if present. Pass a production `requestSaveAs` seam that (per DEC-9) reports
  "Save As not yet available" via the existing status/report channel (no crash).
- **Perspective-level hotkey pump (the actual Ctrl+S fix):** ensure an `EditorHotkeyDispatcher` is
  pumped **once per frame at the perspective/editor level** with the shell command set
  (`ProcessThisFrame(WindowManager.ShellCommands)`), so Ctrl+S/Ctrl+Shift+S fire regardless of which
  sub-window is focused. Add a **text-field gate** so hotkeys don't fire while typing
  (e.g. skip when `ImGui.GetIO().WantTextInput` / an active text item). Find the editor's per-frame
  render entry (likely in `EditorSubsystem`) and add this pump; do not disturb the existing
  document-scoped pump in `AiGraphCanvasWindow`.
### 3. Remove the inline focus-gated Ctrl+S / Ctrl+Shift+S (EditorSubsystem.cs)
- At `EditorSubsystem.cs` ~L1622-1628 (Save Blueprint) and ~L1658-1664 (Save All): remove the
  `(isWindowFocused && ctrlDown && ... && sPressed)` hotkey conditions so the **Buttons remain** but
  the key handling no longer lives here (it now goes through the dispatcher). Keep the
  `_blueprintSaveCallback`/`_saveAllCallback` button invocations intact. Remove now-unused locals
  (`ctrlDown`/`shiftDown`/`sPressed`/`isWindowFocused`) only if they become fully unused (avoid new
  warnings — TreatWarningsAsErrors).

## Tests required — `SaveCommandsTests` in `Hrot/Editor/Hrot.Editor.AiShared.Tests/Documents/`
Use a fake/mock `AiDocumentManager` (or real one populated with fake `IEditableAsset` docs) + recording
save delegates + a recording `requestSaveAs`:
- `Save_WithSourcePath_WritesActiveDocument` — Active doc with non-empty `SourceFilePath` → the matching
  per-kind delegate is called with that path, and `MarkClean()` is invoked; `requestSaveAs` NOT called.
- `Save_EmptySourcePath_RoutesToSaveAs` — Active doc with empty `SourceFilePath` → `requestSaveAs` is
  called with that doc and NO per-kind write delegate runs.
- `SaveAll_SavesEveryDirtyDocument` — several docs, some dirty/some clean → every DIRTY doc's delegate
  runs (and clean ones don't); assert via recording delegates.
- `Hotkey_CtrlS_InvokesSave_RegardlessOfFocusedWindow` — register the commands into an
  `IEditorCommands`, feed it to `EditorHotkeyDispatcher.ProcessThisFrame` with a fake `IInputSource`
  reporting Ctrl + S pressed → the `shell.save` command is invoked (and Ctrl+Shift+S invokes
  `shell.saveAll`, not `shell.save`). Mirror the existing dispatcher test
  `BcpBatch02FixCanvasTests` fake-input pattern.

## Hard constraints
- Do NOT delete/modify legacy/assembly-loading code. Do NOT implement the Save-As DIALOG (Phase 6);
  only the seam. No scope creep beyond the three scope items + the test file.
- Keep public APIs of existing types intact (additive only).
- Do NOT weaken/skip/auto-pass tests or add a Stability trait to dodge a failure.
- Zero new warnings (TreatWarningsAsErrors on across these projects).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for: `Hrot.Editor.AiShared.Tests`, and the hot suites `Fdp.Toolkits.Tests` +
  `Hrot.SimHost.Tests`. (For `Fdp.Presentation.Tests`, if touched, use a class filter — PRE-2 deadlock.)
- Write `.dev/main-toolbar-1/reports/BATCH-06-REPORT.md`: files changed, where the perspective-level
  pump was added, how the per-kind delegates + requestSaveAs seam are wired in production, confirmation
  the inline Ctrl+S was removed (file:line), each new test + assertions, paste actual test-run
  summaries, and the insight questions.

If something cannot be done as specified, stop and report why rather than stubbing it.
