# BATCH-04 Review

**Tasks:** GZH-012 (LocalWindowController), GZH-013 (ConsoleCommandService)
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

Both tasks are cleanly implemented. The subagent introduced two minor deviations from the
instructions that are actually improvements: idempotent `Dispose()` in `ConsoleCommandService`
and `LocalWindowController?` declared before the `try` block in `Program.cs`. Both are correct.

---

## GZH-012: OpenLocalWindow() / CloseLocalWindow()

### IPresentationShell (new)

- Interface defines exactly the 5 methods needed for the window lifecycle abstraction: `InitWindow`,
  `SetupImGui`, `ShutdownImGui`, `CloseWindow`, `UnloadAtlasTexture`, `LoadIconAtlas`.
- Nothing leaked from production Raylib/rlImGui; pure C# interface.

### RaylibPresentationShell (new)

- Wraps `Raylib.SetConfigFlags`, `InitWindow`, `SetExitKey`, `SetTargetFPS` inside `InitWindow()`.
- `SetupImGui()` calls `rlImGui.Setup(true)` and enables docking flags.
- `CloseWindow()` unloads `_atlasTexture` if non-zero before `CloseWindow()` (correct GPU cleanup
  ordering).
- `LoadIconAtlas()` loads from embedded bytes, stores texture handle for later cleanup. Correct.

### LocalWindowController (new)

- Idempotent `OpenLocalWindow()` guard: `if (_isLocalWindowOpen) return` at top. Correct.
- `CloseLocalWindow()` is symmetrically idempotent.
- Wires `wm.OnPerspectiveChanged` only when `_coordinator != null`. Null-safe. Correct.
- Sets `WindowManager = null` before `ShutdownImGui()`/`CloseWindow()` calls -- avoids stale
  pointer to freed GPU resources.
- `WindowManager` property is nullable; callers in `Program.cs` use `?.` or `!.` appropriately.

### Program.cs refactoring

- Pre-init Raylib block fully moved into `RaylibPresentationShell.InitWindow()`. No Raylib calls
  remain in `Program.cs` before the render loop.
- `windowCtrl` declared nullable before `try`, assigned inside: safe for `finally` null guard.
- `consoleSvc` declared with `using var` inside `try`: disposed when `try` block exits. Correct.
- `consoleSvc.RegisterCommand("open", ...)` and `consoleSvc.RegisterCommand("close", ...)` override
  the built-in stubs with real `windowCtrl.OpenLocalWindow()` / `CloseLocalWindow()` implementations.
- Headless path: `orchestrator.Run()` now calls `DrainConsoleActions()` at start of each iteration,
  so console commands work in headless mode too. Correct integration point.

### GZH-012 test quality

All 4 tests are behaviorally meaningful:

- `GZH012_1`: Verifies `IsLocalWindowOpen`, `InitWindowCallCount`, `SetupImGuiCallCount`,
  `LoadAtlasCallCount` all equal 1 after single open. Not a trivial truth-value check.
- `GZH012_2`: Idempotency of open -- verifies `InitWindowCallCount` stays at 1 after two calls.
  Covers the guard branch.
- `GZH012_3`: Verifies `IsLocalWindowOpen == false`, `ShutdownImGuiCallCount == 1`,
  `CloseWindowCallCount == 1` after close. Symmetric coverage.
- `GZH012_4`: Idempotency of close -- verifies `CloseWindowCallCount` stays at 1 after two calls.

`FakePresentationShell.LoadIconAtlas()` returns `new IconAtlas(nint.Zero, 1, 1, 16f)` which is
sufficient for `WindowManager` construction in tests (no GPU required).

---

## GZH-013: ConsoleCommandService

### ConsoleCommandService (new)

- Background stdin reader runs on `IsBackground = true` thread. Correct -- won't block process exit.
- Case-insensitive command dictionary (`StringComparer.OrdinalIgnoreCase`). Good defensive practice.
- Built-in `open`/`close` are stubs with console output; overridden by `Program.cs` registration.
  This is the right separation: service has no dependency on `LocalWindowController`.
- Built-in `exit` calls `orch.Stop()`. Minimal and correct.
- `OnCommandDispatched` event raised from background thread; subscribers enqueue to
  `ConcurrentQueue` via `orchestrator.EnqueueConsoleAction`. Thread-safe by design.
- `Dispose()` is idempotent: checks `_cts.IsCancellationRequested` before calling `Cancel()`.
  `_cts.Dispose()` called after -- correct ordering.
- `ReadLoop()` wraps `ReadLoopCore()` with top-level `ObjectDisposedException` catch to handle the
  race where CTS is disposed between the loop condition check and token access.
- Does NOT join the background thread on dispose -- avoids blocking on `ReadLine()` during test
  teardown. Appropriate comment explains the reasoning.

### SubsystemOrchestrator additions

- `_pendingConsoleActions` is `ConcurrentQueue<Action<SubsystemOrchestrator>>` -- correct choice
  for MPSC pattern (background thread enqueues, main thread drains).
- `EnqueueConsoleAction` is one-liner, thread-safe by queue design.
- `DrainConsoleActions()` drains the full queue per call -- no partial drain risk.
- `Run()` calls `DrainConsoleActions()` before `GetDeltaTime()` and `Update(dt)`. Correct placement:
  actions that call `Stop()` will cause the next `while (_running)` check to exit cleanly.

### GZH-013 test quality

All 4 tests cover the essential behaviors:

- `GZH013_1`: Uses `StringReader("open\n")`, polls up to 500ms for dispatch, verifies exactly one
  action dispatched. Tests real async dispatch path.
- `GZH013_2`: Uses `StringReader("nonexistent\n")`, waits 200ms, verifies zero dispatches. Tests
  the unknown-command no-op path (different from no-command).
- `GZH013_3`: Uses `StringReader(string.Empty)` (EOF), starts thread, then calls `Dispose()`.
  Measures wall time and verifies < 500ms. Tests that `Dispose()` does not block.
- `GZH013_4`: Full end-to-end path: `exit` command dispatched -> enqueued via
  `OnCommandDispatched += orchestrator.EnqueueConsoleAction` -> `DrainConsoleActions()` drains ->
  `orchestrator.Stop()` called -> `orchestrator.Run()` (in a Task) completes within 500ms. Tests
  the actual integration behavior, not just internal state.

No fake tests. No "verify the mock was called" without real exercise of the feature.

---

## Test Counts

| Suite | Passed | Failed |
|-------|--------|--------|
| GZH012 | 4 | 0 |
| GZH013 | 4 | 0 |
| PerspectiveCoordinator regression | 13 | 0 |
| FDP gizmos regression | 187 | 0 |

---

## Issues Found

None. The subagent's deviations from the original instructions were improvements:
1. Idempotent `Dispose()` is strictly better (avoids double-cancel exception on shutdown).
2. Nullable `windowCtrl` before `try` is the correct C# pattern for resources needing `finally`
   cleanup when constructor can throw.

---

## Verdict: APPROVED
