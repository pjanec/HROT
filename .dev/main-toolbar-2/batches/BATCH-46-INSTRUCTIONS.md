# BATCH-46 — BUG-A12 (P1): perspective-scoped Save target

**Bug:** BUG-A12 — `shell.save`/`shell.saveAs` use `docManager.Active` (the globally last-opened document),
ignoring the current perspective. Repro: open an HSM (Save enabled), switch to the Blueprint perspective (no
document open there) → Save stays enabled and **saves the unrelated HSM**.
**Assigned to: sonnet** (perspective ↔ document ↔ save-resolver integration). **Inspect the SOURCE end-to-end.**
**Repo root:** `D:\Work\IOS-IG-SimHost-FDP`. Do NOT use codebase-memory tooling.

## Desired behavior
The "active save target" must reflect the **current perspective**:
- **Scenario / "Editor"(="Scenario") perspective** → the scenario (already handled via `isScenarioContext`). Unchanged.
- **A canvas perspective (Blueprint / BTree / HSM)** → the document **belonging to that perspective** (the open
  document whose kind matches the current perspective). If **no** such document is open/focused → **Save and Save-As
  are DISABLED** and Ctrl+S does nothing (it must NOT fall back to some other perspective's last-opened doc).
- The dynamic label (`Save [{kind}: {name}]`) and the per-kind save dispatch must use this **resolved** document,
  not `docManager.Active`.

(Assume single-canvas-per-perspective for now — at most one document per canvas perspective, per the prior decision.)

## Where to look (trace these in source)
- `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs` — `shell.save`/`saveAs` `IsEnabled` +
  handlers currently read `docManager.Active`; `describeActiveTarget` builds the dynamic label.
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — where `ShellSaveCommands.Register(...)` is called and the
  seams (`isScenarioContext`, `hasLoadedScenario`, `describeActiveTarget`, the per-kind save delegates) are wired;
  the `_perspectiveSwitcher` (`WindowManagerPerspectiveSwitcher`) + `AiDocumentManager` (`Active`/`OpenDocuments`).
- The perspective system: how the **current perspective key** is queried, and how a perspective maps to an
  `AssetKind` (the same signal `isScenarioContext` already uses to detect the Scenario/"Editor" perspective).
  Find the existing perspective→kind correspondence (e.g. "Blueprint"/"BTree"/"Hsm" perspective keys) and the
  open-document lookup by kind in `AiDocumentManager`.

## Recommended approach (you may improve on it, but keep it surgical + testable)
Introduce a **perspective-aware active-document resolver** and use it in `ShellSaveCommands` instead of
`docManager.Active` for the canvas-document branch:
- Add an optional seam to `ShellSaveCommands.Register`, e.g.
  `Func<AiDocument?>? resolveActiveDocument = null`. When supplied, the `shell.save`/`saveAs` **non-scenario**
  branch uses `resolveActiveDocument()` for BOTH `IsEnabled` (`!= null`) and the save target (replacing the
  `docManager.Active` reads). When null, preserve today's `docManager.Active` behavior (back-compat for tests).
- In `EditorSubsystem`, implement the resolver: read the **current perspective** from `_perspectiveSwitcher`; if it
  is a canvas perspective, map it to its `AssetKind` and return the open document of that kind from
  `_aiDocumentManager` (or null if none); if it is the Scenario/"Editor" perspective return null (the scenario
  branch handles it). Wire it into the `Register(...)` call. Make `describeActiveTarget` resolve from the SAME
  source so the label matches what Ctrl+S will save (and reads e.g. "Save" / disabled when nothing is targetable).
- Keep all wiring null-safe (bare-ctor `RegisterWindows` must not throw).

## Hard requirements
- **Inspect the source path end-to-end** (perspective key → kind → open doc → save target / enablement); explain it
  in the report. Do NOT rely on headless tests as the only proof; the live GUI check is the user's.
- Do NOT regress: scenario-perspective Save still saves the scenario; opening a doc + Ctrl+S in ITS perspective
  still saves it; Save-All unchanged. No test weakening/skips/stubs. Build 0 warnings.
- Add/extend `Hrot.Editor.AiShared.Tests/Documents/SaveCommandsTests.cs`: a NEW case proving that when
  `resolveActiveDocument` returns null (current perspective has no matching doc) **Save/Save-As are disabled and the
  handler is a no-op**, even though a `docManager.Active` document exists. Keep all existing cases green.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~SaveCommands"
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
All `Failed: 0`; build 0 warnings.

## Definition of done
- In a canvas perspective with no matching open doc, Save/Save-As are disabled and Ctrl+S is a no-op (no
  cross-perspective save). With a matching doc open, Save targets it; the dynamic label matches. Scenario unchanged.
- New SaveCommands test added + green; build 0 warnings; `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/main-toolbar-2/reports/BATCH-46-REPORT.md`: the resolver design, the traced perspective→kind→doc path,
  the `ShellSaveCommands` change, files/tests, summary. Note the live GUI confirmation is the user's.

If something cannot be done as specified, STOP and report why rather than stubbing.
