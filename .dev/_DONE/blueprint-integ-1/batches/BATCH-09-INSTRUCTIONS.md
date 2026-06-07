# BATCH-09: Canvas runtime overlays + breakpoint toggles + debug windows (completes Phase 3)
**Tasks:** AIE-033, AIE-034   **Phase:** 3   **Est:** ~9h
**Dependencies:** BATCH-05 (document factories/host services), BATCH-08 (debug sessions in registry).

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/blueprint-integ-1/DESIGN.md` §5.6; `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-033, AIE-034.
3. `.dev/blueprint-integ-1/reviews/BATCH-08-REVIEW.md`.

Use **codebase-memory MCP** first; not `search_code`. Headless tests must not call ImGui without a context.

## Task 1: Canvas runtime overlays + breakpoint toggles (AIE-033) — BTree/HSM document factories + canvas
The runtime-overlay + breakpoint-gutter custom renderers already exist (`Hrot.BTree.Editor/Renderers/` — `BTreeRuntimeOverlayRenderer`, `BTreeBreakpointGutterRenderer`, `SubtreeBoundaryRenderer`, `HeatmapOverlayRenderer`, `ObserverGuardBadgeRenderer`; `Hrot.Hsm.Editor/Renderers/` — `HsmRuntimeOverlayRenderer`, `HsmBreakpointGutterRenderer`, transition-label/initial-arrow/region-conflict/history-glyph renderers). Inject them (in the documented pass/registration order, see DESIGN-talk §9) into the `BTreeEditorHostServices`/`HsmEditorHostServices` built by the BTree/HSM document factories (BATCH-05), passing the active `IDebugSession` from the registry so overlays bind to it. Wire a **breakpoint toggle** command (canvas node context menu / action) → `GraphCommand.SetNodeProperty(node,"isBreakpoint",bool)` through the command sink. Overlay renderers must report `IsActive==false` when the session is detached (no per-frame cost during authoring).
**Tests required:** `BTreeHostServices_IncludeRuntimeOverlayAndBreakpointRenderers` (custom-renderer list contains the expected renderer ids); `HsmHostServices_IncludeExpectedRenderers`; `BreakpointToggle_OnNode_DispatchesSetNodePropertyCommand` (command sink receives `isBreakpoint=true`); `RuntimeOverlay_IsActive_FalseWhenSessionDetached`.

## Task 2: Watch / Breakpoints / Diagnostics windows per perspective (AIE-034) — composition + PerspectiveWorkspaceRegistrar
`DiagnosticsWindow` is already registered per perspective (BATCH-04). Add per-perspective **Watch** and **Breakpoints** windows bound to the shared managers (the universal-breakpoint stack: `DataBreakpointManager`/`DataBreakpointManagerWindow` already in `EditorSubsystem`; reuse it — register a per-perspective view or expose it within the perspective). Confirm the windows carry the correct `OwningPerspective`. Do not duplicate the breakpoint manager — share the single instance.
**Tests required:** `Perspective_RegistersWatchAndBreakpointsAndDiagnostics_WithOwningPerspective` (ids present, correct `OwningPerspective` per kind); `Diagnostics_ShowsValidatorOutput_ForActiveAsset` (validator diagnostics surface for the active asset). Keep `BreakpointSubsystemWiringTests` green.

## Success Criteria
- [ ] AIE-033/034 per success conditions; **Phase 3 / M-Debug complete**.
- [ ] Green (full, no crashes): `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `NodeEditor.UI.Tests`, `EditorSubsystemBoot` filter, `BreakpointSubsystemWiringTests`. Blueprints no new failures beyond DEBT-006's 10.
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-09-REPORT.md`.

## Execution rules
- Tasks in sequence; run named suites yourself; fix root causes; never fake a pass. Verify renderer ids/registration order + breakpoint-manager APIs against the code (don't invent).

## Report Requirements
In `reports/BATCH-09-REPORT.md`: renderer ids + injection/registration order; breakpoint-toggle command path; how Watch/Breakpoints reuse the shared manager per perspective; actual test counts; confirm `EditorSubsystemBoot` 10/10; suggested commit message. No comprehension questions.
