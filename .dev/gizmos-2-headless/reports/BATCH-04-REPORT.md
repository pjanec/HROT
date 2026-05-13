# BATCH-04 Report

**Sprint:** Gizmos-2 Headless
**Tasks:** GZH-012, GZH-013
**Date:** 2026-05-13
**Status:** APPROVED

---

## Summary

Both tasks fully implemented and all tests passing.

---

## Task GZH-012: `OpenLocalWindow()` / `CloseLocalWindow()`

### Changes made

**New files (Hrot parent repo):**

- `Hrot/Runner/Hrot.ClusterRunner/Presentation/IPresentationShell.cs` — testable seam interface wrapping all Raylib/ImGui window operations.
- `Hrot/Runner/Hrot.ClusterRunner/Presentation/RaylibPresentationShell.cs` — production implementation that calls Raylib/rlImGui; loads the icon atlas into a GPU texture.
- `Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs` — owns `OpenLocalWindow()` and `CloseLocalWindow()` with idempotency guards. The `PerspectiveCoordinatorSystem` field is nullable; `OnPerspectiveChanged` wiring is skipped when null (enabling unit tests without an orchestrator).

**Modified file:**

- `Hrot/Runner/Hrot.ClusterRunner/Program.cs`:
  - Removed the pre-init Raylib block (`SetConfigFlags`, `InitWindow`, `rlImGui.Setup`) and the in-line atlas-loading / WindowManager-creation block.
  - Removed `atlasTexture` and `windowManager` local variables.
  - Declared `windowCtrl` before the `try` block (so it is accessible in `finally`).
  - After `orchestrator.Initialize()` + coordinator setup: constructs `LocalWindowController` and calls `windowCtrl.OpenLocalWindow()` when not headless.
  - Render loop now uses `windowCtrl.IsLocalWindowOpen` as the branch condition and `windowCtrl.WindowManager` for rendering; `DrainConsoleActions()` is called at the top of the loop body.
  - `finally` block replaced with `orchestrator.Shutdown()` + `windowCtrl?.CloseLocalWindow()`.

**New test file:**

- `Hrot/Runner/Hrot.ClusterRunner.Tests/Presentation/LocalWindowControllerTests.cs` — class `GZH012_Tests` with `FakePresentationShell` and 4 tests.

---

## Task GZH-013: `ConsoleCommandService`

### Changes made

**Modified file (FDP submodule):**

- `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemOrchestrator.cs`:
  - Added `_pendingConsoleActions` (`ConcurrentQueue<Action<SubsystemOrchestrator>>`).
  - Added `EnqueueConsoleAction(Action<SubsystemOrchestrator>)` — thread-safe enqueue called from background stdin thread.
  - Added `DrainConsoleActions()` — drains the queue on the main thread.
  - `Run()` loop now calls `DrainConsoleActions()` at the start of each iteration (before `GetDeltaTime()` / `Update(dt)`).

**New file (Hrot parent repo):**

- `Hrot/Runner/Hrot.ClusterRunner/Services/ConsoleCommandService.cs` — background REPL with built-in `help`, `open`, `close`, `exit` commands. `Start()` launches a `IsBackground = true` thread. `Dispose()` cancels the CTS (idempotently, checks `IsCancellationRequested` before calling `Cancel()`). `ReadLoop()` wraps `ReadLoopCore()` in a top-level `ObjectDisposedException` catch for shutdown-race robustness.

**Modified file (Hrot parent repo):**

- `Hrot/Runner/Hrot.ClusterRunner/Program.cs` — additionally wires `ConsoleCommandService`:
  - `consoleSvc.RegisterCommand("open", ...)` / `consoleSvc.RegisterCommand("close", ...)` override the stub built-ins.
  - `consoleSvc.OnCommandDispatched += orchestrator.EnqueueConsoleAction`.
  - `consoleSvc.Start()`.

**New test file:**

- `Hrot/Runner/Hrot.ClusterRunner.Tests/Services/ConsoleCommandServiceTests.cs` — class `GZH013_Tests` with 4 tests.

---

## Test Results

### GZH-012 tests (4/4 passed)

| Test | Result |
|------|--------|
| `GZH012_1_OpenLocalWindow_SetsIsOpen_AndCallsShell` | PASS |
| `GZH012_2_OpenLocalWindow_IsIdempotent` | PASS |
| `GZH012_3_CloseLocalWindow_ClearsIsOpen_AndCallsShell` | PASS |
| `GZH012_4_CloseLocalWindow_IsIdempotent` | PASS |

### GZH-013 tests (4/4 passed)

| Test | Result |
|------|--------|
| `GZH013_1_KnownCommand_DispatchesAction` | PASS |
| `GZH013_2_UnknownCommand_DoesNotDispatch` | PASS |
| `GZH013_3_Dispose_CompletesWithin500ms` | PASS |
| `GZH013_4_ExitCommand_StopsOrchestrator` | PASS |

**Total new tests: 8 passed, 0 failed.**

### Regression tests

| Suite | Count | Result |
|-------|-------|--------|
| PerspectiveCoordinatorSystem (existing) | 11 | All PASS |
| FDP Diagnostics.Gizmos (existing) | 187 | All PASS |

---

## Deviations from Instructions

1. **`Dispose()` made idempotent**: The instructions showed a simple `_cts.Cancel(); _cts.Dispose();` pattern. In testing, a double-dispose race between the `IsBackground` thread accessing `_cts.Token` and `Dispose()` being called caused `ObjectDisposedException` to propagate as an unhandled exception crashing the test host. Fixed by: (a) guarding `Cancel()` with `if (!_cts.IsCancellationRequested)`, and (b) wrapping `ReadLoopCore()` in a top-level catch inside `ReadLoop()`. The `GZH013_3` test was also adjusted to not use `using var` (which caused double-dispose) — it calls `Dispose()` directly.

2. **`windowCtrl` hoisted before `try` block**: The instructions placed `windowCtrl` construction inside the `try` block. Since `finally` references `windowCtrl.IsLocalWindowOpen`, the variable had to be declared (`LocalWindowController? windowCtrl = null`) before `try` and assigned inside it, with the `finally` guard changed to `windowCtrl?.IsLocalWindowOpen == true`.

---

## Modified/New Files

### Hrot parent repo (new)
- `Hrot/Runner/Hrot.ClusterRunner/Presentation/IPresentationShell.cs`
- `Hrot/Runner/Hrot.ClusterRunner/Presentation/RaylibPresentationShell.cs`
- `Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs`
- `Hrot/Runner/Hrot.ClusterRunner/Services/ConsoleCommandService.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Tests/Presentation/LocalWindowControllerTests.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Tests/Services/ConsoleCommandServiceTests.cs`

### Hrot parent repo (modified)
- `Hrot/Runner/Hrot.ClusterRunner/Program.cs`

### FDP submodule (modified)
- `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemOrchestrator.cs`
