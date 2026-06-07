# BATCH-49 Review

**Reviewer**: Dev Lead  
**Decision**: ✅ APPROVED WITH NOTES  
**Date**: 2025-07-09

---

## Build & Test Summary

| Metric | Result |
|--------|--------|
| Build errors | 0 |
| Build warnings | 0 |
| New integration tests | 18 (BreakpointSubsystemWiringTests) |
| New BTree wiring tests | 4 |
| New HSM wiring tests | 4 |
| New unit tests | 6 (BlueprintContextMenu + HotReload + Watches) |
| All tests passing | ✅ |

---

## Production Code Review

### P10T3 — DataBreakpointManagerWindow registration ✅
`EditorSubsystem.RegisterWindows()` registers the window **before** the `if (_headless) return` guard.
`bpWin = new DataBreakpointManagerWindow("editor_bp_manager", "Editor", ...)` — correct ID, correct perspective, correct title-bar color.

### P10T4 — BP wiring block ordering fix ✅
`_bpManager` is created before the gizmo systems. Both `DataDrivenGizmoSystem` and `GlobalGizmoManager` constructors now receive `breakpointManager: _bpManager`. The ordering issue (wiring at line ~895 after gizmo creation) is resolved.

### P10T5 — MutationInterceptor wired before headless guard ✅
`_fdpEntityInspector.Reflector.MutationInterceptor = _bpManager` is set before `if (_headless) return`. Headless integration tests can verify interceptor wiring.

### P10T6 — BlueprintDebugSession wired ✅
`_blueprintDebugSession` field added; `DebugProbe.Sink` set in Initialize; properly nulled-out in Shutdown. Correct.

### P10T7 / P10T8 — BTree / HSM host services SetBreakpointManager ✅
Both `BTreeEditorHostServices.SetBreakpointManager` and `HsmEditorHostServices.SetBreakpointManager` added; `IEditorHostServices.CustomElementContextMenu` property wired to the context menu providers. Context menu providers correctly delegate to existing `*BreakpointMenuPopulator.PopulateMenu`.

### P10T9 — Blueprint GraphEditorWindow SetBreakpointManager ✅
`SetBreakpointManager` method added; `_bpManager` field ready. Canvas right-click handler is still a stub — acceptable for this wiring task, noted in developer report as "infrastructure ready."

### P10T10 — AiHotReloadCoordinator.OnReloadBegin event ✅
`OnReloadBegin` event fires at Step 5.5 (after staging commits, before ALC swap) in both `DrainPendingCallbacks()` (line 272) and `ApplyQuickReload()` (line 346). EditorSubsystem subscribes: `_aiCoordinator.OnReloadBegin += () => _bpManager?.OnHotReloadBegin()` and `_aiCoordinator.OnReloadCompleted += _ => _bpManager?.OnHotReloadCompleted()`. Correct.

### P10T11 — Watch persistence (LoadWatches / SaveWatches) ✅
`LoadWatches` called in Initialize after BP block setup; `SaveWatches` called in Shutdown before `_aiCoordinator.Dispose()`. Correct.

---

## Test Quality Review

### Tests 6–7 (P10T4 gizmo ordering) — ACCEPTABLE
Tests verify that `IActiveViewProvider` returns correct view based on pause state. They exercise the `_bpActiveViewProvider` mock path, not `DataDrivenGizmoSystem.Execute`. Acceptable for a wiring task since the gizmo rendering code already existed before this batch.

### Tests 8–10 (P10T3 manager window) — GOOD
Three integration tests cover: window registered in editor perspective, NOT registered in unrelated perspective, and opens via menu command. Full scenario coverage.

### Tests 11–12 (P10T5 inspector) — ACCEPTABLE
Check that the interceptor is set (`_bpManager != null`). Do not verify actual mutation routing (PendingMutationsCount). Acceptable for a wiring task; behavioral coverage exists in P5 unit tests.

### Test 13 (P10T6 Blueprint probe routing) — WEAK ⚠️
`Blueprint_NodeBP_RoutesThroughManager_TripleBufferApplied` only asserts `DebugProbe.Sink != null` and that it is an `IBlueprintProbeSink`. **DOES NOT** fire `OnNodeEnter` and verify `manager.IsPaused == true` as specified in TASK-DETAIL.

Mitigation: `BlueprintContextMenuTests.Blueprint_NodeBP_RoutesToManager_TripleBufferRewindApplied` (unit-level) covers the behavioral path. The integration test covers wiring. **Acceptable with note — no new batch required.**

### Tests 14–16 (P10T10 hot-reload) — STRUCTURAL WEAKNESS ⚠️
Call `mgr.OnHotReloadBegin()` / `mgr.OnHotReloadCompleted()` **directly**, bypassing the `_aiCoordinator.OnReloadBegin` event subscription wired in `EditorSubsystem.Initialize()`. Tests verify **manager behavior** during hot-reload but NOT the event subscription chain. Developer acknowledged in report.

**Logged as D-BP-05 — requires a test in BATCH-50 that fires `_aiCoordinator.OnReloadBegin` via the coordinator and confirms the manager receives the call.**

### Tests 17–18 (P10T11 watches) — GOOD
Round-trip persistence test and schema-drift graceful failure test. Both are solid.

### BTree wiring tests (4) — GOOD
Cover context menu item presence, renderer ID match, manager wired/ready, and clear-when-null. Well structured.

### HSM wiring tests (4) — GOOD
Mirror BTree, same quality.

---

## Code Quality Issues

### ⚠️ D-BP-03: `new BTreeBreakpointGutterRenderer(asset: null!)`
In `BTreeEditorHostServices.SetBreakpointManager`, the gutter renderer is constructed with `asset: null!`. The renderer's `CountManagerBreakpoints()` (and any method that calls `_asset.FindNode()`) will NRE if invoked in production. Since BTree canvas doesn't call `SetBreakpointManager` in production yet, this is a latent risk.

**Logged as D-BP-03 — fix in BATCH-50: add null guard in `BTreeBreakpointGutterRenderer.CountManagerBreakpoints()` and `Render()`, or pass a proper asset when wiring.**

---

## Issues to Address in BATCH-50

| # | Severity | Description | Target |
|---|----------|-------------|--------|
| D-BP-03 | P2 | `BTreeBreakpointGutterRenderer(asset: null!)` — NRE if `_asset` used | BATCH-50 |
| D-BP-05 | P2 | HotReload tests bypass coordinator event; add coordinator-subscription wiring test | BATCH-50 |

---

## Final Decision

**APPROVED** — All required tasks delivered, build clean, tests passing. Two technical debt items logged (D-BP-03, D-BP-05) to be fixed in BATCH-50 alongside P11 tasks.
