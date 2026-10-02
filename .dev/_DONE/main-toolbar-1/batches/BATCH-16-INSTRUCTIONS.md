# BATCH-16: Scenario nested-name support + caller wiring (pick → action)
**Tasks:** MTB-P5-T5, MTB-P5-T6   **Phase:** 5   **Est:** ~9h
**Dependencies:** BATCH-13 (AssetKind.Scenario + contributor), BATCH-15 (picker/docked hosts).
Completes Phase 5.

> Do T5 then T6 in sequence; do NOT advance until the current task's impl + tests pass.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §19 (scenario subfolders) + §10.5 (callers).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P5-T5, MTB-P5-T6.
4. Existing code (read):
   - `Hrot/Subsystems/Hrot.Editor/EditorApplication.cs` — `SaveCurrentScenario`/`SaveScenarioAs`
     (already `Path.Combine(ScenariosRoot, name)` + `Directory.CreateDirectory`),
     `LoadScenarioByName`, `AvailableScenarios` (from injected source via
     `SetAvailableScenariosSource`).
   - `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` ~L1285–1294 — where `ScenariosRoot` is used to
     build the scenario list and `SetAvailableScenariosSource(() => _uiCache?.AvailableScenarios …)`
     is wired. Trace `_uiCache.AvailableScenarios` to the actual enumeration (currently top-level
     directory names) and make it recursive.
   - `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` — `ScenariosRoot`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocumentManager.cs` — `Open(IEditableAsset)`.
   - `Hrot/Subsystems/Hrot.Editor/IEditorLogic.cs` — `LoadScenarioByName(string)`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs` (now includes `Scenario`).

## Task 1 — Scenario nested-name support (MTB-P5-T5) — §19
- **Enumeration → recursive relpaths:** extract a testable static helper, e.g.
  `ScenarioEnumeration.EnumerateRelPaths(string scenariosRoot)` that **recursively** finds every
  directory containing a `scenario.json` marker and returns each directory's path **relative to
  `scenariosRoot`**, normalized to `/` separators (e.g. `["Combat/Patrol", "alpha"]`), stably sorted.
  Wire the editor's `AvailableScenarios` source (currently top-level-only) to this helper so nested
  scenarios are listed. Do NOT change `IEditorLogic.AvailableScenarios`'s type (still
  `IReadOnlyList<string>`); just make the values nested relpaths.
- **Save honors nested names:** confirm `SaveScenarioAs`/`SaveCurrentScenario` write
  `ScenariosRoot/<relpath>/scenario.json` for a nested `<relpath>` (they already use `Path.Combine`
  + `Directory.CreateDirectory`, which create nested folders — verify and add a test; normalize any
  `/`↔`\` as needed so a name like `"Combat/Patrol"` round-trips).

**Tests required (`ScenarioNestedNameTests`, using a temp dir as the scenarios root):**
- `SaveAs_NestedName_CreatesNestedFolder` — saving with name `"Combat/Patrol"` creates
  `<root>/Combat/Patrol/scenario.json` (assert the nested file exists). (If the save path is hard to
  exercise without the full editor, test the path-composition + the `EnumerateRelPaths` against a
  hand-built temp tree — but prefer exercising the real save if feasible with a fake file service.)
- `AvailableScenarios_ReturnsNestedRelPaths` — given a temp tree with
  `alpha/scenario.json`, `Combat/Patrol/scenario.json`, `Combat/Ambush/scenario.json`,
  `EnumerateRelPaths` returns exactly `["Combat/Ambush","Combat/Patrol","alpha"]` (relpaths, `/`-norm,
  sorted), and dirs WITHOUT a `scenario.json` are excluded.
- `RoundTrip_SaveThenEnumerate_MatchesName` — save a scenario as `"Combat/Patrol"` into a temp root,
  then `EnumerateRelPaths(root)` contains `"Combat/Patrol"` (the exact name saved).

## Task 2 — Caller wiring: pick → action (MTB-P5-T6) — §10.5
**File (NEW):** a small testable router in `Hrot.Editor` (editor-host, where both
`AiDocumentManager` and `IEditorLogic` are available), e.g.
`Hrot/Subsystems/Hrot.Editor/Browser/AssetPickActionRouter.cs`:
- `Route(IEditableAsset asset)`:
  - `asset.Kind == AssetKind.Scenario` → `editorLogic.LoadScenarioByName(asset.Name)` (the Name is the
    scenario **relpath**).
  - file kinds (Blueprint/BTree/Hsm) → `documentManager.Open(asset)`.
  - other/unsupported kinds → no-op (or report); never throw on a normal path.
- Wire this `Route` as the callback supplied to the picker/docked hosts (BATCH-15) at the editor
  composition point (minimal, documented wiring — e.g. register the docked window / open the modal
  with `router.Route` as the callback). Keep the wiring thin.

**Tests required (integration-style, fake `AiDocumentManager`-shape + fake `IEditorLogic`):**
- `Pick_FileAsset_OpensDocument` — routing a Blueprint (and a BTree/Hsm) asset calls
  `documentManager.Open` with that asset; does NOT call `LoadScenarioByName`.
- `Pick_Scenario_CallsLoadScenarioByName_WithRelPath` — routing a Scenario asset whose `Name` is
  `"Combat/Patrol"` calls `editorLogic.LoadScenarioByName("Combat/Patrol")`; does NOT call
  `documentManager.Open`.

> If `AiDocumentManager` is a concrete sealed class hard to fake, route through a minimal interface
> seam (an `Action<IEditableAsset> openDocument` + `Action<string> loadScenario`) that production
> wires to `documentManager.Open` / `editorLogic.LoadScenarioByName`, so the router is unit-testable.
> Document the seam.

## Hard constraints
- Do NOT delete/modify legacy/assembly-loading code. Keep `IEditorLogic`/`AiDocumentManager` public
  APIs intact (additive only). No scope creep beyond T5/T6.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Editor.Tests` (or the test project hosting the new tests), `Hrot.Editor.AiShared.Tests`,
  and the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests` (PRE-3 EQS flake → re-run if it appears).
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-16-REPORT.md`: files changed, the recursive enumeration
  helper, the save nested-folder confirmation, the router seam, the wiring point, each new test +
  assertions, paste actual test-run summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
