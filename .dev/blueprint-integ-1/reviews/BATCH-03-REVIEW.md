# BATCH-03 Review
**Status:** ⚠️ APPROVED WITH P1 (P1 is pre-existing from BATCH-01, not caused by this batch)   **Date:** 2026-06-02

## Summary
AIE-013 (global Asset Browser + Open-docs section + per-perspective window parameterization) and AIE-014 (`PerspectiveWorkspaceRegistrar` + `WindowManagerPerspectiveSwitcher`) are implemented correctly and their tests are green. **However, the full `Hrot.Editor.AiShared.Tests` suite aborts** with `AccessViolationException` — a latent P1 from BATCH-01's ImGui-touching adapter tests.

## Verification performed
- `Hrot.Editor.AiShared.Tests --filter "!~Adapters"`: **589/589 pass, no abort** → Batch-03 work (and all non-adapter tests) is clean.
- Full suite (no filter): **aborts — `System.AccessViolationException` (test host crashed)**, despite a "Passed: 600" line from the surviving collection. Net: `Test Run Aborted`.
- Isolation: `AIE003` 29/29 pass; **`AIE004` (`EngineEditorTheme`) hangs/crashes the runner** — `GetFontForSize` calls `ImGui.GetIO().Fonts` with no ImGui context → native AV. (`AIE005` clipboard is a likely secondary offender.)
- Root cause: BATCH-01 adapters guard headless ImGui with `try/catch`, but **`AccessViolationException` is a corrupted-state exception that managed `catch` does not handle** — the process dies. My BATCH-01/02 runs passed only by luck (UB: `GetIO()` on no context sometimes returns zeroed memory, sometimes AVs).

## P1 → Corrective Task 0 for BATCH-04
Fix the adapters to guard with a **safe pointer check** before any ImGui deref:
`if (ImGui.GetCurrentContext() == IntPtr.Zero) return <fallback>;` in `EngineEditorTheme.GetFontForSize`, `ImGuiClipboard.GetText/SetText`, and `ImGuiInputSource` frame-snapshot members. Remove reliance on `try/catch` for the no-context case (keep it only for genuinely catchable failures). Then `dotnet test Hrot.Editor.AiShared.Tests` (no filter) must run to completion deterministically, 0 crashes. (Optional: a context-fixture test that exercises the real ImGui path with `ImGui.CreateContext()`.)

## Batch-03 code review (its scope)
- `AssetBrowserWindow` → `Global` scope; Open-docs interaction extracted to static, testable methods (`BuildOpenDocsViewModel`/`HandleActivateRow`/`HandleCloseRow`/`HandleCatalogOpen`). Real assertions (active/dirty markers, Open/Activate/Close invoked).
- Six side panels gained `idOverride`/`owningPerspective` optional ctor params, defaulting to prior values (back-compat; existing window tests green).
- `PerspectiveWorkspaceRegistrar` registers per-perspective side panels with distinct `###Id`s + `OwningPerspective`; extension seam `RegisterExtraWindow` + virtual `RegisterWindows` for the Phase 2/4 canvas/kind panels. `WindowManagerPerspectiveSwitcher` activates most-recent doc of kind on switch; no-op when none.
- Test quality: asserts distinct per-perspective ids, perspective-switch invocation, open-docs view-model — real behavior. Good.

## Verdict
Batch-03 code APPROVED. Commit it. **Batch-04 must begin with Corrective Task 0 (AV fix) before AIE-015**, and end with the full AiShared suite running to completion.

## Commit Message
```
feat(editor): AIE-013..014 — global Asset Browser + perspective scaffolding (BATCH-03)

Completes AIE-013, AIE-014 (Phase 1 UI scaffolding).
- AssetBrowserWindow: Global scope + Open-docs section (cross-kind switcher) over AiDocumentManager.
- Shared windows: optional idOverride/owningPerspective ctor params for per-perspective instances.
- PerspectiveWorkspaceRegistrar (side panels + extension seam) + WindowManagerPerspectiveSwitcher.
EditorSubsystem.cs not modified (AIE-015 is BATCH-04).
Tests: AiShared non-adapter 589/589. Known P1 (pre-existing BATCH-01): full-suite AV crash from
ImGui-no-context in EngineEditorTheme/ImGuiClipboard — fixed as BATCH-04 Corrective Task 0.
```
