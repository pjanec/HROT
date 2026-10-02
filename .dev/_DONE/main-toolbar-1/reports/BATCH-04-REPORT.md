# BATCH-04 Report

## Implementation Summary
Completed all 3 scope items of MTB-P1-T3 (`WindowManager.MainToolbar + dockspace inset`):

1. **`WindowManager.MainToolbar` property** — added `private readonly MainToolbarManager _mainToolbar = new()` field and `public MainToolbarManager MainToolbar => _mainToolbar` property, mirroring `StatusBar`. `WindowManager.Render()` now calls `_mainToolbar.Render(CurrentPerspective)` alongside the existing `_statusBar.Render(CurrentPerspective)`.

2. **`DockspaceLayout` helper** — new public static class in `Fdp.Presentation.WindowManager` with `CentralSize(workWidth, workHeight, toolbarHeight, statusBarHeight)` returning `(workWidth, workHeight - toolbarH - statusBarH)` clamped to ≥ 0, and `CentralPos(workPos, toolbarHeight)` returning `workPos + (0, toolbarHeight)`. Pure math, no ImGui dependency — headless-testable.

3. **`Program.cs` dockspace wiring** — replaced the bottom-only `statusBarHeight` inset with the §4.1.2 double-inset: `SetNextWindowPos` → `CentralPos`, `SetNextWindowSize` → `CentralSize`, and `DockSpace` size → `CentralSize`. Toolbar height computed before `Begin("##DockSpace")` alongside status-bar height.

## Design Decisions
- **Render order**: placed `_mainToolbar.Render()` _before_ `_statusBar.Render()` in `WindowManager.Render()`. Order is semantically irrelevant (each uses `SetNextWindowPos` independently), but toolbar-at-top-before-status-bar-at-bottom reads naturally.
- **`DockspaceLayout` namespace**: placed in `Fdp.Presentation.WindowManager` alongside `MainToolbarManager` and `StatusBarManager` — it's a dockspace concern, and `Program.cs` already imports `Fdp.Presentation.WindowManager`.
- **Test file naming**: `WindowManagerMainToolbarTests` (not `WindowManagerTests` additions) to keep the existing `WindowManagerTests` file untouched and make new tests easy to isolate.

## Deviations
None. All three scope items implemented exactly as specified in BATCH-04-INSTRUCTIONS.md.

## Test Results

### Build
- `dotnet build Fdp.Presentation` — **0 warnings, 0 errors**
- `dotnet build Fdp.Presentation.Tests` — **0 warnings, 0 errors**
- `dotnet build Hrot.ClusterRunner` — **0 warnings, 0 errors**
- `dotnet build IOS-IG-SimHost.sln` — **0 errors**, 20 pre-existing warnings (all in `Hrot.Blueprints.Tests`, unrelated)

### Targeted test run: `--filter "FullyQualifiedName~DockspaceLayout|FullyQualifiedName~MainToolbar|FullyQualifiedName~WindowManager"`
```
Passed!  - Failed:     0, Passed:    96, Skipped:     0, Total:    96, Duration: 150 ms
```

### New tests (all pass):

**`DockspaceLayoutTests`** (8 tests):
- `CentralSize_SubtractsToolbarAndStatusBar` — `CentralSize(1920, 1080, 64, 24)` → `(1920, 992)`
- `CentralSize_ZeroInsets_ReturnsFullWorkSize` — `CentralSize(800, 600, 0, 0)` → `(800, 600)`
- `CentralSize_ClampsToZero_WhenInsetsExceedWork` — `CentralSize(1024, 80, 100, 50)` → `.Y == 0`
- `CentralSize_ClampsToZero_WhenInsetsExactlyEqualWork` — `CentralSize(1920, 100, 60, 40)` → `.Y == 0`
- `CentralSize_ClampsToZero_WhenOnlyToolbarExceedsWork` — `CentralSize(800, 50, 64, 0)` → `.Y == 0`
- `CentralPos_OffsetsTopByToolbarHeight` — `CentralPos((10,20), 64)` → `(10, 84)`
- `CentralPos_ZeroToolbarHeight_ReturnsSamePosition` — `CentralPos((100,200), 0)` → `(100, 200)`
- `CentralPos_NegativeWorkPos_StillOffsets` — `CentralPos((-5,-10), 30)` → `(-5, 20)`

**`WindowManagerMainToolbarTests`** (3 tests):
- `MainToolbar_PropertyResolves` — `wm.MainToolbar` is non-null, same instance across calls
- `Render_InvokesMainToolbar` — registers a recording entry, calls `wm.Render()`, asserts delegate was invoked
- `Render_InvokesMainToolbar_WithCurrentPerspective` — perspective filtering: global + matching perspective entry render, non-matching entry skipped

### Pre-existing tests in filter scope (all still pass):
`WindowManagerTests` (20), `MainToolbarManagerTests` (9), `StatusBarManagerTests` (9), `ManagedWindowTests` (17), `WindowManagerSettingsTests` (8), `GlobalMenuRegistryTests` (10), `ReflectorExposureTests` (2), `ComponentReflectorDoubleClickTests` (1).

## Developer Insights
- **Pre-existing failures not triggered**: the filter kept us clear of the ~19 known failures in `Vis2D`/`EntityInspector`/`EventBrowser` namespaces — exactly as intended.
- **ImGui test serialization**: all tests use the `[Collection("ImGui Sequential")]` collection to avoid deadlocks from concurrent ImGui context access. The existing pattern worked without issue.
- **StatusBar/Toolbar symmetry**: the mirror pattern (`StatusBarManager` ↔ `MainToolbarManager`) made integration trivial — the `MainToolbarManager` from BATCH-03 already had a `Render(currentPerspective)` signature matching `StatusBarManager.Render`.
- **`DockspaceLayout` purity**: no ImGui dependency means tests are sub-millisecond and immune to headless-context serialization. Good pattern worth repeating for future layout helpers.

## Known Issues
- None. All three scope items implemented, all targeted tests pass, zero new warnings.

## Suggested Commit Message
```
feat(main-toolbar): WindowManager.MainToolbar + DockspaceLayout + dockspace top inset (MTB-P1-T3)

- Add MainToolbar property + Render call in WindowManager (mirror StatusBar)
- Add DockspaceLayout helper (pure, testable inset math)
- Wire Program.cs dockspace: inset TOP by toolbar + BOTTOM by status bar (§4.1.2)
- Tests: WindowManagerMainToolbarTests (3) + DockspaceLayoutTests (8)
```
