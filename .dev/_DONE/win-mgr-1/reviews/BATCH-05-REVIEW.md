# BATCH-05 Review

**Batch:** BATCH-05  
**Reviewed by:** Dev Lead  
**Date:** 2026-04-01  
**Decision:** ✅ APPROVED

---

## Scope Check

| Task | Delivered | Notes |
|------|-----------|-------|
| WM-S601 — `StatusBarManager` | ✅ | Deferred sort, Height property, null guard, duplicate-Id replacement |
| WM-S602 — `StatusBar` property + integration | ✅ | StatusBar replaces stub; Render() called last |
| WM-S603 — Reference section in ClusterRunner | ✅ | `system_health` section in Program.cs |
| WM-S701 — `TogglePerspectiveEvent` | ✅ | Record already existed from BATCH-04; verified |
| WM-S702 — `ActivePerspective` component | ✅ | Sealed class (not struct); managed singleton pattern documented |
| WM-S703 — `PerspectiveCoordinatorSystem` | ✅ | ConcurrentQueue bridge; `PerspectiveUpdateSubsystem` wrapper |

---

## Test Quality

- **152/152 ImGui tests pass** (9 new: StatusBarManager + WindowManagerSettings tests).
- `PerspectiveCoordinatorSystemTests`: 5 tests covering enqueue/process, unknown perspective, multiple events, CurrentPerspective tracking.
- `StatusBarManagerTests`: sort order, deferred sort, duplicate id, separator count, null delegate, empty registry.
- All ClusterRunner builds succeed with zero errors.

---

## Design Alignment

- `StatusBarManager` deferred sort correctly uses `List.Sort` (stable for equal keys) — sets `_needsSort` on every `RegisterSection` call.
- `PerspectiveCoordinatorSystem` uses `ConcurrentQueue<TogglePerspectiveEvent>` for thread-safe enqueue (UI thread) / dequeue (frame thread). Correct.
- `PerspectiveUpdateSubsystem` is added FIRST in subsystem list so it processes events before other subsystems' `Update()` calls — correct ordering.
- `ActivePerspective` as sealed class rather than struct: correct pragmatic decision given string field incompatibility with `unmanaged` constraint. Documented in report.
- `StatusBar` replaces the `GetStatusBarHeight()` stub from BATCH-04 cleanly.

---

## Issues Found

None structural. One minor P3 note:

| ID | Priority | Description | Target |
|----|----------|-------------|--------|
| DEBT-004 | P3 | `PerspectiveUpdateSubsystem.Coordinator` uses a deferred-set property pattern to resolve chicken-and-egg construction order. A cleaner approach would be constructor injection with late binding. | Future |

---

## Suggested Git Commit Message

```
feat(status-bar,perspective): BATCH-05 complete (WM-S601-S703)

Phase 6 (Status Bar):
  StatusBarManager: deferred-sorted delegate registry, Height property,
    frame render with separators. Null delegate -> ArgumentNullException.
  WindowManager: StatusBar property + _statusBar.Render() last in Render().
  SubsystemOrchestrator: dockspace uses StatusBar.Height.
  Program.cs: system_health reference section registered.

Phase 7 (Background Map Perspective):
  ActivePerspective: sealed class singleton (managed, not unmanaged — string field).
  PerspectiveCoordinatorSystem: ConcurrentQueue bridge, ProcessPendingEvents().
  PerspectiveUpdateSubsystem: thin ISubsystem wrapper (first in list).
  Program.cs: OnPerspectiveChanged -> coordinator.Enqueue().

Tests: 9+5 new (152/152 ImGui, ClusterRunner builds 0 errors).
```
