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
| DBT-A1 | P3 | T7 (planned) | `RecipeCreateModal` (blueprint-only combo dialog) production wiring is **retired** in T7 in favor of the generic recipe picker, but the **class + its tests are kept** (no-deletion rule). Tracks the now-unused class until a later cleanup pass (out of scope here). | MTB2-T7 | open |

## Notes
- **Verified-state references** (so they aren't re-derived): the active-asset findings, single-canvas/tab-readiness,
  and the blackboard (embedded-in-owning-document, not a standalone asset) conclusion are recorded in
  [DESIGN.md](./DESIGN.md) "Active-save-target model".
- **Zoo guardrails** (no asset exclusion / no diagnostic suppression / no test weakening / no cross-batch edits /
  don't-stop-until-`Failed:0`) live in TASK-DETAIL's "Zoo Execution Contract" and must be pasted into every batch.
