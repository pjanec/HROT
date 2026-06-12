# DEBT-TRACKER — main-toolbar-2

Decisions + technical debt for the File-Operations & Toolbar-Polish workstream. Every debt is recorded when found,
assigned to a task/batch, and must be resolved (✅) before the workstream is declared done. Decisions (DEC-*) capture
choices so they aren't re-litigated.

**Key:** P0 = blocks correctness/gate · P1 = high · P2 = medium · P3 = low/nice-to-have

| ID | Pri | Found in | Description | Target | Status |
|----|-----|----------|-------------|--------|--------|
| DEC-A1 | — | design | Scenario stays a **non-document**; an editor-shell resolver picks the save target (architect Option b; 5 claims source-verified). Docs = local file I/O; scenarios = `IEditorLogic` cluster-2PC. Do NOT wrap scenarios as `AiDocument`s. | — | ✅ decided |
| DEC-A2 | — | design | Recipe picker (Item 1) reuses the NodeEdit Tree picker + dedicated `_shellPickers` registry (Phase-8/DEC-15 consistency), not the combo `RecipeCreateModal`. | MTB2-T6/T7 | ✅ decided |
| DEC-A3 | — | design | `IconWidgets` fix is **generic** (shared widget), not toolbar-local; hitbox/layout unchanged, only the drawn rect + state visuals. | MTB2-T1 | ✅ decided |
| DEC-A4 | — | design | **Active save target = focused document (`AiDocumentManager.Active`) when a doc surface is focused; else the scenario.** Perspective is ONLY the scenario's activity signal; document kinds (current & future) resolve via `Active` independent of perspective. | MTB2-T4 | ✅ decided |
| DEC-A5 | — | design | Explicit, always-available `scenario.save` / `scenario.saveAs` → `IEditorLogic`; Ctrl+S/File→Save call them in the scenario case. Future non-document saveables follow the same pattern; the unified Save command never changes. | MTB2-T4/T5 | ✅ decided |
| DEC-A6 | — | design | **Dynamic Save label/tooltip** via `Func<string>? DynamicDisplayName` on `EditorCommandDescriptor` (NodeEdit core). Content = resolver output `"Save [{kind}: {name}]"`; `"Save"` greyed when nothing dirty/active. One source feeds label, tooltip, dispatch. | MTB2-T3/T4 | ✅ decided |
| DEC-A7 | — | design | **Do NOT rename the `Editor` perspective key** (collides with cluster node/subsystem name `"Editor"` + ~10 `PerspectiveBound` window keys; would reset dock layouts). Decouple a **display-label** instead: id `Editor` → label "Scenario". | MTB2-T5 | ✅ decided |
| PRE-1 | P3 | cross-WS | **Pre-existing, OUT OF SCOPE (gate baseline):** `Hrot.Blueprints.Tests` has **9 PRE-1** failures unrelated to this workstream (AiPrimitive golden ×2, Stage8 ×2, MoveToAndFire snapshot, CF/breakpoint, alloc ×2 — env-sensitive; some pass on a given run, e.g. 7/9 on 2026-06-12). Any batch touching Blueprints-adjacent code must stay at this baseline — **no NEW failures**. Do not block on PRE-1. | — | noted |
| PRE-2 | P3 | cross-WS | **Pre-existing, OUT OF SCOPE:** the FULL `Fdp.Presentation.Tests` suite is flaky/deadlocks (Vis2D ImGui-fixture semaphore leak). **Run class-filtered** for icon/toolbar/window-manager/perspective tests (filter in TASK-DETAIL). Not a gate; do not "fix" by disabling tests. | MTB2-T1/T2/T3/T5 | noted |
| DBT-A2 | P3 | T5 (BATCH-34) | Duplicate Scenario-menu Save/Save-As vs the new File menu. **USER DIRECTIVE (2026-06-12): KEEP the Scenario menu** until the issues are resolved and a confirmed good replacement exists. Do NOT remove it. | hold (user) | open |
| DBT-A3 | P2→**design** | T7 (BATCH-36) | **New Asset name+subfolder picker (PROMOTED to a real requirement).** USER (2026-06-12): default-name create is OK only if a proper dialog then asks for the **name** and lets you **select/create a subfolder** — NOT a trivial textbox, more like a system "Save As" dialog but over the **logical asset-folder model** (scenarios are not single files). Likely a **generic "new-asset picker"** reused by New + Save-As. Needs design (see DESIGN addendum). | MTB2-T8 (BATCH-39+40) | in progress |
| BUG-A1 | P1 | runtime BATCH-36 | **New Asset crashes** for any document kind: `ArgumentException: Expected BlueprintFileAsset but got BlueprintEditableAssetAdapter`. `ShowNewAssetDialog` opens the **minted adapter** from `INewAssetService.CreateNew`, but `*DocumentFactory.Build` requires the **catalogued** concrete asset. **FIXED (0329272a, BATCH-37):** open the catalogued asset via `_aiCatalogBuilder.Catalog.FindByAssetId`; Scenario skipped. | BATCH-37 | ✅ |
| BUG-A2 | P1 | runtime BATCH-33 | **Scenario Save via icon broken** in the "Scenario"/Editor perspective: Save disabled + tooltip shows the **stale doc** ("blueprint: Count5"). Causes: `IsEnabled` gates on empty `LoadedScenarioName` (empty after `NewScenario`); `describeActiveTarget` falls through to the doc branch when unnamed. **FIXED (3533c059, BATCH-38):** scenario-context Save always enabled, named→SaveCurrent/unnamed→SaveAs, label always describes scenario. | BATCH-38 | ✅ |
| BUG-A3 | P2 | runtime | **No save feedback. FIXED (3533c059, BATCH-38):** document save reports `[OK] Saved {Kind}: '{Name}'.`; scenario save sets the status line. | BATCH-38 | ✅ |
| BUG-A4 | P1 | runtime BATCH-30 | **Toolbar icon UX:** icons not visibly scaled down (margin too small); toggled bg too faint; hover bg == toggle bg (toggled+hovered shows nothing); the **white hover frame was removed**. Fix: smaller icon inset; **restore the white hover frame** (independent hover indicator); distinct + visible toggled bg; the two must compose. **FIXED (2dec34d3):** inset 0.72; white hover frame restored; HeaderActive@0.85 toggle fill; composable. | direct | ✅ |
| DBT-A1 | P3 | T7 (BATCH-36) | `RecipeCreateModal`/`NewFromRecipeService` production wiring retired (classes/tests kept). **USER DIRECTIVE (2026-06-12): POSTPONE deletion — AWAITS USER APPROVAL** (only after new-asset creation fully works). | hold (user approval) | open |

## Notes
- **Verified-state references** (so they aren't re-derived): the active-asset findings, single-canvas/tab-readiness,
  and the blackboard (embedded-in-owning-document, not a standalone asset) conclusion are recorded in
  [DESIGN.md](./DESIGN.md) "Active-save-target model".
- **Zoo guardrails** (no asset exclusion / no diagnostic suppression / no test weakening / no cross-batch edits /
  don't-stop-until-`Failed:0`) live in TASK-DETAIL's "Zoo Execution Contract" and must be pasted into every batch.
