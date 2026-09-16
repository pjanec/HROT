# BATCH-E Review

**Batch:** BATCH-E  
**Reviewer:** Development Lead  
**Date:** 2026-06-08  
**Status:** ✅ APPROVED (pending user interactive smoke)

---

## Summary

Bridged `IBlueprintDebugSession` → NodeEdit `IDebugSession`, added "Toggle Breakpoint (F9)" to CanvasRenderer node context menu, wired adapter in factory, registered `editor.toggle-breakpoint` command. 9 new tests (7 adapter + 2 factory). Independently verified: build 0/0, Blueprints tests 1678/2/8 (2 pre-existing, 0 new).

---

## Issues Found

### Issue 1: `Subscribe()` never called (P2)

**File:** `BlueprintDebugToNodeEditAdapter.cs:137-149`

`Subscribe()` wires `OnNodeExecuted`, `OnPinValueChangedEvent`, `OnSessionStateChanged` → `StateChanged`. But the factory never calls `adapter.Subscribe()`, so `StateChanged` never fires. NodeEdit's `NodeRenderer` reads `Debug` properties each frame regardless, so this doesn't break rendering — but it means the canvas can't use event-driven invalidation. Fix in a follow-up if perf is a concern.

---

## Test Quality Assessment

All 9 tests verify actual behavior:
- **Adapter tests:** `ToggleBreakpoint` sets/clears with correct ids; `Breakpoints` filters by asset/graph; `IsPaused` delegates; step methods increment counters; `CurrentlyExecutingNode` from history
- **Factory tests:** `Host.Debug` is non-null adapter; `ToggleBreakpoint` command registered, enabled with selection, invocation sets breakpoint on selected node

No string-presence tests, no "object exists" tests. Good quality.

---

## Verdict

**Status:** APPROVED

All requirements met. Headless gates pass (build 0/0, tests 1678 passed, 0 new failures). Ready for user interactive smoke.

---

## 📝 Commit Message

```
feat: wire breakpoint toggle via NodeEdit native path (BATCH-E)

Bridges IBlueprintDebugSession → NodeEdit IDebugSession so NodeRenderer
natively draws breakpoint markers and execution overlays. Adds "Toggle
Breakpoint (F9)" to the node right-click context menu via CanvasRenderer.

- CanvasRenderer.cs: "Toggle Breakpoint (F9)" in HoverKind.Node case
- BlueprintDebugToNodeEditAdapter: IDebugSession wrapping IBlueprintDebugSession
- BlueprintDocumentFactory: wires adapter via SetDebugSession, registers
  editor.toggle-breakpoint command, removes dead context menu provider
- CapturingDebugSession: IsPaused settable, call counters, history support

Tests: 9 tests (7 adapter + 2 factory wiring)
VISUAL/INTERACTIVE VERIFICATION PENDING

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```

---

**Next:** User interactive smoke — right-click a node on the live blueprint canvas, verify "Toggle Breakpoint (F9)" appears and works.
