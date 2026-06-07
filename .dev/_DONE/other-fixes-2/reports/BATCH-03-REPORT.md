# BATCH-03 Report

**Batch:** BATCH-03
**Tasks:** FIX2-005, FIX2-006
**Status:** APPROVED -- all tests green

---

## Test Results

```
Passed!  - Failed: 0, Passed: 886, Skipped: 8, Total: 894, Duration: 35 s
```

Command used (per batch instructions):
```
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```

- Pre-existing count before this batch: 882
- New tests added: 4 (one for FIX2-005, three for FIX2-006; one existing callstack test renamed)
- Final pass count: 886
- Regressions: 0

Note: `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` is excluded by the
batch instructions filter. Investigation confirmed it also fails on the committed baseline
when run in isolation -- it is a pre-existing JIT warm-up dependency, not a regression
introduced by this batch.

---

## Task FIX2-005 -- Register blueprint editor windows via engine `IWindowRegistrar`

### Files changed

| File | Change |
|---|---|
| `Hrot.Blueprints.Editor/BlueprintWindowRegistrar.cs` | Added explicit implementation of `Fdp.Toolkit.Runner.IWindowRegistrar`; added inner `WindowManagerRegistry` adapter |
| `Hrot.Blueprints.Editor/BlueprintManagedWindowAdapter.cs` | New file -- bridges `IBlueprintEditorWindow` factories to the engine `ManagedWindow` base class |
| `Hrot.Blueprints.Editor/BlueprintEditorServiceCollectionExtensions.cs` | Added `AddSingleton<BlueprintWindowRegistrar>` and `AddSingleton<IWindowRegistrar>` forwarding registrations |
| `Hrot.Blueprints.Tests/Editor/BlueprintWindowRegistrarTests.cs` | Added `BlueprintWindowRegistrar_RegistersAllSevenWindows_ViaEngineInterface` test |

### Design decisions

`BlueprintWindowRegistrar` already had a local `RegisterWindows(IBlueprintWindowRegistry)` method.
Rather than rewrite that, the engine interface is implemented as an explicit interface method that
delegates to the existing method via a private `WindowManagerRegistry` adapter, which wraps each
factory call in a `BlueprintManagedWindowAdapter` before calling `WindowManager.RegisterWindow`.

The original `RegisterWindows(IBlueprintWindowRegistry)` is kept unchanged because
`BlueprintEditorModule` (wired in BATCH-01) calls it with the local registry.

`BlueprintManagedWindowAdapter` extends `ManagedWindow` with:
- `owningPerspective = "Blueprints"`
- `scope = WindowScope.PerspectiveBound`
- Lazy-instantiates the window via the factory on first `DrawClientArea()` call, then propagates
  the window's `Title` back to the adapter so the engine tab bar shows the correct name.

DI registers `BlueprintWindowRegistrar` as a singleton and exposes it under both its concrete type
and `Fdp.Toolkit.Runner.IWindowRegistrar` so the engine orchestrator can resolve either.

### Test

`Hrot.Blueprints.Tests/Editor/BlueprintWindowRegistrarTests.cs`
-- test `BlueprintWindowRegistrar_RegistersAllSevenWindows_ViaEngineInterface`.

Constructs a `BlueprintWindowRegistrar` via the existing `MakeRegistrar()` helper, casts it to
`Fdp.Toolkit.Runner.IWindowRegistrar`, creates an `IconAtlas(IntPtr.Zero, 16f, 16f)` (GPU-free
in tests) and `WindowManager(atlas)`, calls `RegisterWindows(wm)`, then asserts
`wm.TryGetWindow(name, out _)` for each of the 7 expected window names.
If the `IWindowRegistrar` explicit implementation is removed the cast throws `InvalidCastException`
and the test fails.

---

## Task FIX2-006 -- Implement debug panel rendering + add `GetCurrentCallStack()`

### Files changed

| File | Change |
|---|---|
| `Hrot.Blueprints.Core/IBlueprintDebugSession.cs` | Added `CallFrame` readonly record struct; added `GetCurrentCallStack()` to `IBlueprintDebugSession` |
| `Hrot.Blueprints.Editor/BlueprintDebugSession.cs` | Added `_callStacks` field; updated `OnPeerCallEnter`/`OnPeerCallExit` to push/pop frames; implemented `GetCurrentCallStack()`; `Detach()` clears `_callStacks` |
| `Hrot.Blueprints.Editor/Debug/DebugPanelWindow.cs` | Replaced `_ = ...` discards with real ImGui table rendering; added `LastRenderedPausedState` and `LastRenderedBreakpoints` observable properties |
| `Hrot.Blueprints.Editor/Debug/WatchPanelWindow.cs` | Replaced `_ = ...` discards with real ImGui table rendering; added `LastRenderedWatches` observable property |
| `Hrot.Blueprints.Editor/Debug/CallstackWindow.cs` | Replaced `GetRecentNodeHistory()` with `GetCurrentCallStack()`; added real ImGui table rendering; added `LastRenderedFrames` observable property; added `OnActivated()`/`OnDeactivated()` overrides |
| `Hrot.Blueprints.Tests/CapturingDebugSession.cs` | Added `GetCurrentCallStack()` stub |
| `Hrot.Blueprints.Tests/Editor/MockDebugSession.cs` | Added `GetCurrentCallStack()` stub |
| `Hrot.Blueprints.Tests/Editor/DebugWindowDrawUITests.cs` | Updated `SpyDebugSession`; renamed existing callstack test; added 3 new `LastRendered*` tests |

### Design decisions

**ImGui headless approach:** No existing ImGui mock infrastructure exists in the codebase.
The approach used: each window now calls the real ImGui API but guards all ImGui calls with
`if (ImGui.GetCurrentContext() == IntPtr.Zero) return;` placed AFTER the data-fetch and
AFTER writing to the `LastRendered*` properties. In tests with no ImGui context, `DrawUI()`
fetches and stores the data but skips all `ImGui.*` calls. This lets tests assert on
`LastRenderedBreakpoints`, `LastRenderedWatches`, and `LastRenderedFrames` without needing
a real GPU context.

**`CallFrame` struct:**
```csharp
public readonly record struct CallFrame(string PeerAssetIdString, string MethodName, int Depth);
```
`PeerAssetIdString` is the Guid in "D" format (matching `OnPeerCallEnter` parameters).
`Depth` is the call depth at the time of entry (0 = outermost peer call), matching the
ordering specified in Editor DD §8.7.

**Call stack tracking in `BlueprintDebugSession`:**
`OnPeerCallEnter` pushes a `CallFrame` onto a `List<CallFrame>` keyed by `Entity` in
`_callStacks`. `OnPeerCallExit` removes the last frame. `GetCurrentCallStack()` returns
the stack for `_pausedOnEntity` as a read-only view, or `Array.Empty<CallFrame>()` when
not paused or no frames recorded for the paused entity.

### Tests

`Hrot.Blueprints.Tests/Editor/DebugWindowDrawUITests.cs` -- 6 tests total:

| Test | What it verifies |
|---|---|
| `DebugPanelWindow_DrawUI_Queries_Breakpoints_From_Session` | `GetBreakpoints()` called |
| `DebugPanelWindow_DrawUI_LastRenderedBreakpoints_Reflects_Session_Data` | `LastRenderedBreakpoints` matches session data |
| `WatchPanelWindow_DrawUI_Queries_Watches_From_Session` | `GetWatches()` called |
| `WatchPanelWindow_DrawUI_LastRenderedWatches_Reflects_Session_Data` | `LastRenderedWatches` matches session data |
| `CallstackWindow_DrawUI_Queries_CurrentCallStack_From_Session` | `GetCurrentCallStack()` called (not `GetRecentNodeHistory`) |
| `CallstackWindow_DrawUI_LastRenderedFrames_Reflects_Session_CallStack` | `LastRenderedFrames` matches session call stack |

---

## Developer Insights

### 1. Engine `IWindowRegistrar` interface

Found at `FDP/Engine/Fdp.Presentation/ImGui/IWindowRegistrar.cs` under namespace
`Fdp.Toolkit.Runner` (lives in `Fdp.Presentation` project despite the namespace). The
single method is `void RegisterWindows(WindowManager windowManager)`.

There is a name collision with the local `Hrot.Blueprints.Editor.IWindowRegistrar` (used
for menu/toolbar registration). Resolved via using alias:
```csharp
using EngineWindowRegistrar = Fdp.Toolkit.Runner.IWindowRegistrar;
```

### 2. ImGui headless approach

No existing mock/capture infrastructure. Used `LastRendered*` observable properties on
each window class: data is fetched and stored before the `ImGui.GetCurrentContext()` guard,
so tests can assert on what would have been rendered without needing a GPU context.
The guard ensures the test DLL (which has no ImGui renderer) does not crash on `ImGui.*`
calls.

### 3. `GetCurrentCallStack()` implementation

`CallFrame` is a value type (`readonly record struct`) to avoid per-frame heap pressure.
The call stack is maintained as `Dictionary<Entity, List<CallFrame>>`. On
`OnPeerCallEnter`, a `List<CallFrame>` is lazily created per entity and frames are pushed.
On `OnPeerCallExit`, the last frame is removed. `GetCurrentCallStack()` returns the list
for `_pausedOnEntity` (when paused) as a `ReadOnlyCollection<CallFrame>` wrapper, or
`Array.Empty<CallFrame>()` otherwise. The `Detach()` method clears `_callStacks` to
release entity references.

### 4. Dead-code gaps found

- `CallstackWindow` had `OnActivated()`/`OnDeactivated()` lifecycle methods missing --
  added as empty overrides so the class satisfies the `ManagedWindow` contract cleanly.
- The previous `GetRecentNodeHistory()` call in `CallstackWindow` referenced a method that
  returns node IDs (not peer-call frames) -- this was the wrong API per Editor DD §8.7;
  the callstack window is now correctly wired to `GetCurrentCallStack()`.

### 5. Suggested commit message

```
fix: implement BlueprintWindowRegistrar engine interface and debug panel rendering (FIX2-005, FIX2-006)

FIX2-005: BlueprintWindowRegistrar now implements Fdp.Toolkit.Runner.IWindowRegistrar.
Added BlueprintManagedWindowAdapter to bridge IBlueprintEditorWindow factories to the
engine ManagedWindow contract. Added DI registrations in
BlueprintEditorServiceCollectionExtensions so the orchestrator can resolve IWindowRegistrar
and call RegisterWindows(WindowManager).

FIX2-006: Added CallFrame readonly record struct and GetCurrentCallStack() to
IBlueprintDebugSession. BlueprintDebugSession tracks per-entity peer-call stacks via
OnPeerCallEnter/OnPeerCallExit. All three debug panels (DebugPanelWindow,
WatchPanelWindow, CallstackWindow) now do real ImGui rendering guarded by a context
check for headless test environments. Added LastRendered* observable properties to
support assertions in tests without requiring a GPU context.

Tests: +4 (FIX2_005_WindowRegistrarViaEngineInterface, 3x FIX2_006_DebugWindowRendering)
Suite: 886 passed, 0 failed, 8 skipped (filter: FullyQualifiedName!~AllocationFree)
```
