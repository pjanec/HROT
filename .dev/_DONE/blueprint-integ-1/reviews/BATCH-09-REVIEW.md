# BATCH-09 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-02

## Summary
AIE-033 (canvas runtime overlays + breakpoint toggles) and AIE-034 (Watch/Breakpoints/Diagnostics per perspective) complete. **Phase 3 / M-Debug complete.**

## Verification performed (ran suites myself)
- `Hrot.Editor.AiShared.Tests` **702/702**, `Hrot.BTree.Editor.Tests` **371/371**, `Hrot.Hsm.Editor.Tests` **323/323**, `NodeEditor.UI.Tests` 40/40, `EditorSubsystemBoot` 10/10. Blueprints 889/10 (DEBT-006).
- `BreakpointSubsystemWiring` filter **23/25**; the 2 failures (`CgfSubsystem_Init_RegistersManager`, `CgfSubsystem_HeavyScenario_...`) are the **same pre-existing `Call RegisterSystems before RegisterProviders`** bootstrap issue as DEBT-008 (CGF subsystem, not the editor). The `EditorSubsystem_*` breakpoint tests pass.
- Test names confirm the required scenarios in both BTree/HSM renderer test files: `RuntimeOverlay_IsActive_FalseWhenSessionDetached`, `BreakpointToggle_OnNode_DispatchesSetNodePropertyCommand`.
- Incidental: `HsmCommandSink.ApplySetNodeProperty` (was a TODO stub) implemented for `isBreakpoint`. `BTree/Hsm RuntimeOverlayRenderer.IsActive` overridden → `_session != null`.

## Issues Found
None blocking. (DEBT-008 scope extended to include the 2 `CgfSubsystem` breakpoint-wiring tests — same root cause.)

## Verdict
APPROVED. Phases 0–3 done. Next: Phase 4 (Blueprint full structural + My Blueprint) — the largest remaining phase.

## Commit Message
```
feat(editor): AIE-033/034 — canvas runtime overlays + breakpoint toggles + debug windows (BATCH-09)

AIE-033: BTree/HSM document factories inject runtime-overlay + breakpoint-gutter + other custom
renderers (documented z-order) bound to the active debug session; RuntimeOverlay.IsActive=false when
detached; breakpoint toggle → GraphCommand.SetNodeProperty(isBreakpoint) (HsmCommandSink stub finished).
AIE-034: per-perspective AiWatchWindow + AiBreakpointsWindow reuse the single shared DataBreakpointManager.

Completes Phase 3 / M-Debug. Tests: AiShared 702, BTree 371, HSM 323, EditorSubsystemBoot 10/10,
BreakpointSubsystemWiring 23/25 (2 pre-existing DEBT-008 CGF bootstrap), Blueprints 889/10 (DEBT-006).
```
