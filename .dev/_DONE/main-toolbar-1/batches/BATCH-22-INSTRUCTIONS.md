# BATCH-22: Retire ScenarioBrowserPanel + AiShared AssetBrowserWindow (register docked host)
**Tasks:** MTB-P7-T2, MTB-P7-T4   **Phase:** 7   **Est:** ~7h
**Dependencies:** BATCH-15 (`AssetBrowserDockedWindow`), BATCH-16 (`AssetPickActionRouter`),
BATCH-21 (scenario menu + Workspace submenu now own the relocated logic).

> These are AUTHORIZED deletions (ORCH §2.5 names the Phase-7 retirements as the ONLY permitted
> deletions). Do T2 then T4. Delete ONLY the named types; reject any other deletion.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §10.6 (retirement) + §12 (relocated logic).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P7-T2, MTB-P7-T4.
4. Targets + references (read):
   - **Delete:** `Hrot/Subsystems/Hrot.Editor/UI/ScenarioBrowserPanel.cs`;
     `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs`.
   - ScenarioBrowserPanel refs: `EditorSubsystem.cs:209` (field `_browserPanel`), `:1467`
     (instantiation), `Windows/EditorWindows.cs:38/43`.
   - AssetBrowserWindow refs: `Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs:57`
     (`AddSingleton<AssetBrowserWindow>`), `Windows/SharedAiWindowRegistrar.cs:13/23`,
     tests `Tests/Di/SharedAiEditorDiTests.cs` + `Tests/Windows/AssetBrowserWindowTests.cs`.
   - Replacement: `Browser/AssetBrowserDockedWindow.cs` (BATCH-15, `ExpectedId="AssetBrowser"`, Global),
     `AssetPickActionRouter` (BATCH-16) — but it's in `Hrot.Editor`; for the AiShared registrar wire a
     callback that opens file-kind docs via the document manager (see below).

## Task 1 — Delete `ScenarioBrowserPanel` (MTB-P7-T2)
- Remove `UI/ScenarioBrowserPanel.cs` and ALL its references: the `_browserPanel` field +
  instantiation in `EditorSubsystem.cs`, and the `EditorWindows.cs` field/ctor param. Its actions are
  already shell commands under the Scenario menu (BATCH-21) — remove any now-dead wiring that only
  served the panel (do NOT remove scenario LOGIC in `IEditorLogic`/`EditorApplication`).
- If `EditorWindows` registered the panel as a window, remove that registration.

**Success conditions:** `ScenarioBrowserPanel` type removed; no references remain (grep-clean);
build + suite green.

## Task 2 — Retire AiShared `AssetBrowserWindow`; register the docked host (MTB-P7-T4) — §10.6
- Delete `Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs`. Its open-docs view-model logic
  (`BuildOpenDocsViewModel`/`HandleActivateRow`) is re-homed in the Workspace submenu (BATCH-21) — do
  not reintroduce it.
- **Register `AssetBrowserDockedWindow` in its place** (BATCH-15) where the old window was registered
  (`SharedAiWindowRegistrar` + the DI `AddSingleton`): register with the docked host's id/scope
  (`AssetBrowserDockedWindow.ExpectedId` / Global). Supply the host's `Action<IEditableAsset>` callback
  so activation opens the asset — wire it to `AiDocumentManager.Open` for file kinds (reuse the
  `AssetPickActionRouter` pattern; if the router lives in `Hrot.Editor` and the registrar is in AiShared,
  inject the callback delegate at the AiShared registration point and have the editor host provide
  `router.Route` / `documentManager.Open`). This completes the DBT-2 docked-host wiring.
- Update the DI/registrar so `AssetBrowserDockedWindow` (not the deleted window) is resolved/registered.

**Success conditions:** old `AssetBrowserWindow` type removed; the docked host is registered with the
prior id/scope; build + suite green.

## Tests
- Update/replace the obsolete tests: remove `AssetBrowserWindowTests.cs` (type deleted); update
  `SharedAiEditorDiTests` so it resolves/asserts `AssetBrowserDockedWindow` (with the expected id)
  instead of the deleted window. Add a small test asserting the docked host is registered with
  `ExpectedId` and that its activation callback opens a file-kind doc via the document-manager seam
  (recording fake) if not already covered.

## Hard constraints
- Delete ONLY `ScenarioBrowserPanel` and the AiShared `AssetBrowserWindow` (+ their now-dead refs/tests).
  Do NOT delete the Blueprints `AssetBrowserWindow` or `FileSystemAssetCatalog` (that is MTB-P7-T5).
  Do NOT remove any other legacy/assembly code or scenario LOGIC.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings) — all references to the deleted types gone.
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. Updated/new tests pass UNFILTERED. 0-failed with the
  Stability filter for `Hrot.Editor.AiShared.Tests`, `Hrot.Editor.Tests`, + the hot suites
  `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests` (PRE-3 EQS flake → re-run if it appears).
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-22-REPORT.md`: types deleted + every reference removed,
  the docked-host registration (id/scope + callback wiring), tests updated/removed, paste actual
  test-run summaries, insights. Note DBT-2 docked-host wiring status.

If something cannot be done as specified, stop and report why rather than stubbing it.
