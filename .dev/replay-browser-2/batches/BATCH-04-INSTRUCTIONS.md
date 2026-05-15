# BATCH-04 — Stage 2 Foundation UI + Stage 3 Diff Panel

## Context

This batch covers **all remaining Stage 2 tasks (RB-2.2..RB-2.7)** and **Stage 3 UI tasks (RB-3.4, RB-3.5)**.

**Prerequisite green tests (must stay green):**
- EX-T01..T29 (export + changelog)
- DIF-T01..T13 (diff engine)
- FND-T01..T05 (history trackers + randomized smoke)
- All earlier harness self-tests and context tests

**Already implemented (do not re-implement):**
- `EntitySelectionHistory` — `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/History/EntitySelectionHistory.cs`
- `PlaybackHistoryTracker` — `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/History/PlaybackHistoryTracker.cs`
- `ReplayBrowserContext` — `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs`
- `DiffNode` / `ComponentDiffService` — `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/`
- `RecordingExportService` — `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs`

**Key design references** (read before coding):
- `.dev/replay-browser-2/DESIGN.md` §4 (Stage 2), §5.3 (Diff panel)
- `.dev/replay-browser-2/TASK-DETAILS.md` §RB-2.2 through §RB-3.5
- `.dev/replay-browser-2/design-talk.md` lines 1862–1907 (ImGuiEntityLink), 1966–1989 + 2058–2125 (composition root + causality), 1313–1373 + 1437–1499 (windows), 753–845 (timeline expander), 1690–1782 (diff panel)

---

## Codebase Exploration (do this before writing any code)

Read the following to understand existing patterns:

1. **Subsystem pattern**: `Hrot/Subsystems/Hrot.SimHost/Hrot.SimHost.csproj` — examine project file structure, solution inclusion, references.
2. **ManagedWindow API**: `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/` — read all files to understand `ManagedWindow`, `WindowScope`, `WindowManager`, `IWindowRegistrar`.
3. **Existing panels**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/` — look at an existing panel class to understand `DrawContent()` convention.
4. **ClusterRunner subsystem discovery**: `Hrot/Runner/Hrot.ClusterRunner/Program.cs` — read `ScanForSubsystems` to understand how `-m replaybrowser` will map to your class.
5. **Presentation tests**: `FDP/Engine/Fdp.Presentation.Tests/` — understand test infrastructure (are there mock ImGui helpers? what framework?).
6. **ISubsystem contract**: `FDP/Toolkits/Fdp.Toolkits/Runner/ISubsystem.cs` (or wherever it lives) — read `SubsystemConfig`, `Initialize(SubsystemConfig)`.
7. **ImGui utilities**: `FDP/Engine/Fdp.Presentation/ImGui/Utils/` — understand where new utilities live.
8. **RepositoryAdapter / IInspectableSession**: `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/` — read `InspectorState`, `IInspectorContext`.
9. **IFileDialogService**: `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IFileDialogService.cs`.
10. **MapCanvas**: `FDP/Engine/Fdp.Presentation/Vis2D/MapCanvas.cs` — constructor signature and minimal initialization.
11. **EntityInspectorPanel, EventBrowserPanel**: how they are constructed and what they accept.
12. **ImGuiEntityLink** (check if already exists): `FDP/Engine/Fdp.Presentation/ImGui/Utils/ReplayBrowser/` — may not exist yet.

---

## Task 1 — RB-2.2: `ImGuiEntityLink` Utility

**Full spec**: TASK-DETAILS.md §RB-2.2 + DESIGN.md §4.7.

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Utils/ReplayBrowser/ImGuiEntityLink.cs`

**API**:
```csharp
public static class ImGuiEntityLink {
    public static bool Draw(string label);          // ExConViolet SmallButton
    public static bool TryParse(string text, out Entity entity);  // "[i, vN]" -> Entity
}
```

**TryParse rules** (from design-talk.md lines 1862–1907, lift verbatim):
- Input format: `[<index>, v<generation>]` or `[<index>, <generation>]`
- Tolerant of internal whitespace
- Case-insensitive `v` prefix (or absent)
- Returns `false` without throwing on any malformed input

**Draw**: Uses `ImGuiApi.SmallButton` with ExConViolet push style color — look at existing usage of ExConViolet in the codebase.

**Tests**: `FDP/Engine/Fdp.Presentation.Tests/ReplayBrowser/Foundation/ImGuiEntityLinkTests.cs`

Cover:
- FND-T13: `TryParse("[42, v3]")` returns `Entity(42, 3)`; parses `[42, 3]` (no `v`), `[42, V3]` (uppercase), `[ 42 , v3 ]` (spaces)
- FND-T14: Returns `false` without throwing for: `""`, `"foo"`, `"[,v3]"`, `"[42]"`, `"-1, v3"` (no brackets), negative index `"[-1, v3]"`, missing comma `"[42 v3]"`

> **Note**: `Draw` method cannot be tested without real ImGui context — test only `TryParse` in unit tests.

---

## Task 2 — RB-2.3: `ReplayBrowserSubsystem` Skeleton

**Full spec**: TASK-DETAILS.md §RB-2.3 + DESIGN.md §4.1.

### New project: `Hrot/Subsystems/Hrot.ReplayBrowser/Hrot.ReplayBrowser.csproj`

Model the project file on an existing Hrot subsystem (e.g. Hrot.SimHost). Add references to:
- `Fdp.Toolkits` (for `ISubsystem`, `ReplayBrowserContext`, `EntitySelectionHistory`, `PlaybackHistoryTracker`)
- `Fdp.Presentation` (for `MapCanvas`, `DebugGizmoLayer`, `GridMapLayer`, `WindowManager`, `IWindowRegistrar`, `EntityInspectorPanel`, `EventBrowserPanel`)

Add this project to:
1. `IOS-IG-SimHost.sln` (parent solution) if the Hrot subsystems are referenced from there
2. The FDP solution or Hrot.ClusterRunner references as appropriate — check how other subsystems are discovered

**Class**: `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

Implement per DESIGN.md §4.1 (code block shown there is normative):

```csharp
public sealed class ReplayBrowserSubsystem : ISubsystem, IWindowRegistrar
{
    public string Name => "ReplayBrowser";
    public Vector4 TitleBarColor => new(0.2f, 0.6f, 0.8f, 1f);
    private ReplayBrowserContext _context = null!;
    private MapCanvas _canvas = null!;
    private EntitySelectionHistory _entityHistory = null!;
    private PlaybackHistoryTracker _playbackHistory = null!;
    private bool _headless;
    // Panel fields added in Task 4/5/6
    ...
    public void Initialize(SubsystemConfig config) { ... }
    public void Update(float dt) { ... }
    public void DrawWorld() { if (!_headless) _canvas.Draw(); }
    public void DrawUI() { }
    public void Shutdown() { _context?.Dispose(); }
    public void RegisterWindows(WindowManager wm) { /* implemented in Task 4 */ }
}
```

**Initialize** (when `!Headless`):
- `_context = new ReplayBrowserContext(new ComponentDiffService())`
- `_entityHistory = new EntitySelectionHistory()`
- `_playbackHistory = new PlaybackHistoryTracker()`
- `_canvas = new MapCanvas(...)` + `DebugGizmoLayer` + `GridMapLayer`

When `Headless`:
- Only allocate `_context`, `_entityHistory`, `_playbackHistory`. No canvas/panels.

**DrawWorld**: guard with `if (!_headless)`

**Critically**: Do NOT implement `IMapCameraProvider`.

### New test project: `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/Hrot.ReplayBrowser.Tests.csproj`

Model on an existing subsystem test project. Add reference to `Hrot.ReplayBrowser`.

**Tests**: `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs`

- **FND-T09**: `new ReplayBrowserSubsystem().Initialize(new SubsystemConfig { Headless = true })` succeeds; after init, `DrawWorld()` and `DrawUI()` are no-ops (do not throw; verify by calling them).
- **FND-T10**: `new ReplayBrowserSubsystem() is IMapCameraProvider` is `false`.
- **FND-T11**: The name `"replaybrowser"` (case-insensitive strip of suffix from `ReplayBrowserSubsystem`) is what `ScanForSubsystems` / the CLI matching logic would use. Verify by calling whatever method ClusterRunner uses to match CLI names to subsystem types — read `Hrot.ClusterRunner.Program` carefully for the exact method, then call it in a test with `"replaybrowser"` and assert it returns `ReplayBrowserSubsystem` (or its type). If the discovery is done via string comparison on `Name` property, assert `subsystem.Name.Equals("ReplayBrowser", StringComparison.OrdinalIgnoreCase)` and that the CLI arg `"replaybrowser"` would match.

---

## Task 3 — RB-2.4: Window Shells + 5 Window Registration

**Full spec**: TASK-DETAILS.md §RB-2.4 + DESIGN.md §4.4.

**Window files** (all in `FDP/Engine/Fdp.Presentation/ImGui/Windows/ReplayBrowser/`):
- `ReplayTimelineWindow.cs`
- `FdpEntityInspectorWindow.cs`
- `ComponentDiffWindow.cs`
- `FdpEventBrowserWindow.cs`
- `ReplaySearchWindow.cs`

Each follows the pattern from DESIGN.md §4.4 (code block is normative):
```csharp
internal sealed class ReplayTimelineWindow : ManagedWindow {
    public ReplayTimelineWindow(string id, string title, string perspective,
        ReplayTimelinePanel panel, Vector4 color)
        : base(id, title, perspective, WindowScope.PerspectiveBound)
    { _panel = panel; TitleBarColor = color; IsOpen = true; }
    protected override void DrawClientArea() => _panel.DrawContent();
}
```

For panels not yet implemented (ReplayTimelinePanel, ComponentDiffPanel, ReplaySearchPanel), use a stub panel with an empty `DrawContent()`. They will be filled in Tasks 4, 7.

**FdpEntityInspectorWindow**: accepts `Func<IInspectableSession> sessionFactory`, `Func<InspectorState> stateFactory`, and a `EntityInspectorPanel panel`. The `DrawClientArea` calls panel with the current session/state from the factories.

**RegisterWindows** in `ReplayBrowserSubsystem`: Create all 5 windows and register with `wm.RegisterWindow(...)`. All use `owningPerspective = "ReplayBrowser"`, `WindowScope.PerspectiveBound`, `IsOpen = true`, `TitleBarColor = TitleBarColor`.

At this point panels for Diff (Task 7) and Search (future batch) are stubs; Timeline panel is implemented in Task 4.

**Tests**: `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs` (or a new file there)

- **FND-T12**: Initialize subsystem in non-headless mode (or use a testable factory), call `RegisterWindows` with an in-memory `WindowManager`. Assert:
  - Exactly 5 windows registered
  - All have `Scope == WindowScope.PerspectiveBound`
  - All have `OwningPerspective == "ReplayBrowser"`

> **Non-headless in test**: To avoid needing Raylib, make the non-headless canvas/panel construction injectable or guard with interface. Alternatively, create a `RegisterWindows(WindowManager, ReplayBrowserContext, EntitySelectionHistory, PlaybackHistoryTracker)` overload that skips canvas and lets the test pass null panels — but must still register 5 windows. Read DESIGN.md §4.4 carefully for guidance.

---

## Task 4 — RB-2.5: `ReplayTimelinePanel`

**Full spec**: TASK-DETAILS.md §RB-2.5 + DESIGN.md §4.5.

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs`

The panel holds a reference to:
- `ReplayBrowserContext _context`
- `IRecordingExportService _exportService`
- `IFileDialogService _fileDialogService`
- `PlaybackHistoryTracker _playbackHistory`
- `JsonExportOptions _options` (mutable local state)
- `bool _isExporting` (flag)

**`DrawContent()`** renders the **full wireframe** from DESIGN.md §4.5. Every row must be present. Key elements:

**Row 1 — History + transport**:
- `[<- Back]` button: calls `_playbackHistory.GoBack()`, disabled when `!_playbackHistory.CanGoBack`
- `[Fwd ->]` button: calls `_playbackHistory.GoForward()`, disabled when `!_playbackHistory.CanGoForward`
- `[|< Rewind]`, `[< Step Back]`, `[Step Forward >]`

**Row 2 — Play/Pause**:
- `[|| Pause / Play >]`

**Row 3 — Timeline slider**:
- `ImGui.SliderInt` from 0 to `_context.Playback?.TotalFrames - 1` (or 0 when null)
- Shows `Frame X / Y`

**Row 4 — Meta line**:
- Tick, SimFrame, SimTime, FrameType, CompressedSize

**Row 5 — File loader**:
- `[Load .fdp...]` button + displays current file name

**Row 6 — JSON Export Options expander** (`ImGui.TreeNode("JSON Export Options")`):
All controls from DESIGN.md §4.5 wireframe. Disabling rules (see design-talk.md lines 754–812):
- When `FullFile`: frame inputs AND time inputs are `BeginDisabled`
- When `ByFrame`: time inputs are `BeginDisabled`; frame inputs are active
- When `ByTime`: frame inputs are `BeginDisabled`; time inputs are active
- Epsilon field: `BeginDisabled` when `FormatMode != Changelog`

**Save to JSON button**:
```csharp
if (ImGui.Button("Save to JSON...") && !_isExporting) {
    var snapshot = CloneOptions(_options);  // deep clone
    _ = SaveAsync(snapshot);
}
```
`SaveAsync`: await `_fileDialogService.ShowSaveAsDialogAsync(...)`, then `Task.Factory.StartNew(() => _exportService.ExportToJson(_context.CurrentFdpPath, path, snapshot), TaskCreationOptions.LongRunning)`.

`OnEntitySelected`: expose a `Action<Entity>? OnEntitySelected` property (will be wired in Task 5).

**Tests**: `FDP/Engine/Fdp.Presentation.Tests/ReplayBrowser/Foundation/ReplayTimelinePanelTests.cs`

- **FND-T17**: After constructing the panel with a stub `IFileDialogService` that returns a temp path and a spy `IRecordingExportService`, call the Save button logic. Mutate `_options.Minified = true` after calling Save. Assert the spy's captured `JsonExportOptions` still has `Minified == false` (snapshot was taken before mutation).

Additional tests (no ImGui needed — test the logic, not ImGui calls):
- `CloneOptions` deep-clones `TargetEntities` list (modifying the original list after clone does not affect the snapshot)
- Disabled-state logic: a helper method (extracted from DrawContent) `GetDisabledFrameInputs(WindowMode) => bool` and `GetDisabledTimeInputs(WindowMode) => bool` — these can be static and are testable without ImGui.

---

## Task 5 — RB-2.6: Subsystem Composition Root + Delegate Wiring

**Full spec**: TASK-DETAILS.md §RB-2.6 + DESIGN.md §4.6.

Add delegate wiring to `ReplayBrowserSubsystem.Initialize` after panels are constructed (non-headless path only). Follow DESIGN.md §4.6 code block exactly:

```csharp
_entityHistory.OnSelectionChanged += e => _context.InspectorState.SelectedEntity = e;
_playbackHistory.OnSeekRequested  += f => _context.SeekToFrame(f);

Action<int>    seekIntent   = f => { _playbackHistory.PushFrame(f); _context.SeekToFrame(f); };
Action<Entity> selectIntent = e => _entityHistory.PushSelection(e);

_inspectorPanel.OnEntitySelected = selectIntent;
_diffPanel.OnEntityLinkClicked   = selectIntent;
_eventPanel.OnEntityLinkClicked  = selectIntent;
_searchPanel = new ReplaySearchPanel(/*...*/, seekIntent, selectIntent);
```

Also implement the causality "Step Forward and Diff Target" right-click menu in the EventBrowserPanel wrapper (see TASK-DETAILS.md §RB-2.6 concrete steps 2 and 3 for exact behavior). If `EventBrowserPanel` does not have a built-in `OnEntityLinkClicked` hook or a right-click extension point, add a minimal extension: `public Action<Entity>? OnEntityLinkClicked` property on the panel wrapper or on the window class.

Similarly, ensure `EntityInspectorPanel` exposes `OnEntitySelected` callback. Add Back/Forward arrow buttons in the inspector panel toolbar if the panel has a toolbar area — gated by `_entityHistory.CanGoBack/Forward`.

**Tests**: in `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/`

For these tests, build a testable wiring harness: construct the subsystem in headless mode, then inject spies for `EntitySelectionHistory`, `PlaybackHistoryTracker`, and `ReplayBrowserContext` via a constructor overload or internal test seam.

- **FND-T15**: `inspectorPanel.OnEntitySelected` is set to `selectIntent`. Invoking `selectIntent(entityA)` results in exactly one call to `EntitySelectionHistory.PushSelection(entityA)`.
- **FND-T16**: Causality "Step Forward and Diff Target" macro: using spy implementations, firing the event calls pre-frame `_playbackHistory.PushFrame(currentFrame)`, then `_context.StepForward()`, then `_playbackHistory.PushFrame(newFrame)`, then `_entityHistory.PushSelection(target)` — in that order.
- **FND-T18**: `seekIntent(7)` results in exactly one `PlaybackHistoryTracker.PushFrame(7)` and exactly one `ReplayBrowserContext.SeekToFrame(7)` in that order. `selectIntent(entityA)` results in exactly one `EntitySelectionHistory.PushSelection(entityA)` and the `OnSelectionChanged` chain fires once, setting `InspectorState.SelectedEntity = entityA`.

Additional tests:
- Clicking inspector Back arrow when `CanGoBack == false` is a no-op (no crash, no state change).
- Selecting same entity twice pushes only one history entry (uses `EntitySelectionHistory` duplicate suppression — already tested in FND-T02, just verify the wiring passes through).

---

## Task 6 — RB-2.7: Stage 2 Acceptance Gate

Run the full test suite. All of the following must be green:
- FND-T01..T05 (already done)
- FND-T09, T10, T11, T12 (Task 2+3)
- FND-T13, T14 (Task 1)
- FND-T15, T16, T17, T18 (Tasks 4+5)
- All EX-T and DIF-T tests must remain green

If any test fails, fix it before proceeding.

---

## Task 7 — RB-3.4: `ComponentDiffPanel`

**Full spec**: TASK-DETAILS.md §RB-3.4 + DESIGN.md §5.3.

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ComponentDiffPanel.cs`

The panel exposes:
```csharp
public Action<Entity>? OnEntityLinkClicked { get; set; }
```

**`DrawContent(IReadOnlyList<DiffNode> diffs)`** implements the wireframe from DESIGN.md §5.3 exactly.

Key implementation requirements per TASK-DETAILS.md §RB-3.4:
1. Two checkboxes at top: `[ ] Ignore Epsilon (< 0.001)` and `[x] Hide Unchanged Components & Fields` — `_hideUnchanged` **defaults to true**.
2. `BeginTable("DiffViewerTable", 2, Borders | RowBg | Resizable | SizingFixedFit)`.
3. Internal nodes: `TreeNodeEx(DefaultOpen | SpanAvailWidth)`.
4. Leaves: `TreeNodeEx(Leaf | NoTreePushOnOpen | SpanAvailWidth)`, jump to column 1.
5. **Early-return cull** at start of `DrawDiffNode`: `if (_hideUnchanged && !node.IsModified) return;`
6. Syntax palette (column 1):
   - `JsonValueKind.Number` → cyan `(0.30, 0.80, 1.00, 1)`
   - `JsonValueKind.String` → green `(0.40, 1.00, 0.40, 1)`
   - `JsonValueKind.True/False` → amber `(0.90, 0.60, 0.20, 1)`
   - Other → light gray `(0.85, 0.85, 0.85, 1)`
7. Entity-handle leaves: detect via `ImGuiEntityLink.TryParse(val.OldValue/NewValue)`. If parsed, render both old and new sides as `ImGuiEntityLink.Draw(...)` buttons. On click of new-value button, fire `OnEntityLinkClicked(parsedEntity)`.

**Tests**: `FDP/Engine/Fdp.Presentation.Tests/ReplayBrowser/ComponentDiff/ComponentDiffPanelTests.cs`

These tests use a **headless tree-walker** approach — no ImGui needed. Extract `DrawDiffNode` logic into a `VisitDiffNode(DiffNode, bool hideUnchanged, List<DiffNode> visited)` helper that records visited nodes without calling ImGui. Or: expose a `CollectVisibleNodes(IReadOnlyList<DiffNode> diffs, bool hideUnchanged) : IReadOnlyList<DiffNode>` method.

Tests:
- **Snapshot test with default settings (`_hideUnchanged = true`)**: Build a tree with 4 DiffObject levels where only one leaf at depth 4 is modified. Assert `CollectVisibleNodes` returns exactly the chain of 4 parent nodes + the 1 modified leaf; siblings of the modified leaf are pruned.
- **Toggle test**: Same tree with `_hideUnchanged = false`. Assert all nodes are returned (full structural hierarchy).
- **Entity-link detection test**: Build a `DiffValue` with `OldValue = "[10, v2]"` and `NewValue = "[11, v3]"`. Build the panel, call `CollectVisibleNodes`. Set `OnEntityLinkClicked` to a capturing spy. Simulate the click action on the new-value side. Assert the spy was called once with `Entity(11, 3)`.
- **Hide-unchanged default**: Construct `ComponentDiffPanel`; assert `_hideUnchanged == true` via a property or via the collection test (unchanged node is pruned without explicitly setting the flag).

---

## Task 8 — RB-3.5: Stage 3 Acceptance Gate

- DIF-T01..T13 remain green (already done in BATCH-03)
- EX-T27..T29 remain green (changelog export, done in BATCH-03C)
- `ComponentDiffPanel` is wired into `ComponentDiffWindow` which is registered in `ReplayBrowserSubsystem.RegisterWindows`
- FND-T12 (5 windows with correct scope) passes

---

## Implementation Notes

### FDP is a git submodule
All changes to files inside `FDP/` must be committed inside `d:\Work\IOS-IG-SimHost-FDP-2\FDP` with:
```
cd FDP
git add -A
git commit -m "BATCH-04: ..."
```
Then update the parent:
```
cd ..
git add FDP .dev
git commit -m "BATCH-04: ..."
```

### Avoid over-engineering
- Do NOT add features beyond what the tests require.
- Window shell `DrawClientArea` delegates to the panel's `DrawContent()` — no extra logic.
- If `EntityInspectorPanel` / `EventBrowserPanel` do not have `OnEntitySelected` / `OnEntityLinkClicked` callback hooks, add them minimally (a `public Action<Entity>? OnEntitySelected` property + a call site in the panel's existing entity-selection code).

### Test strategy
Many FND-T tests do NOT require ImGui rendering. They test:
- Parser logic (`TryParse`)
- Reflection assertions (`is IMapCameraProvider`)
- Subsystem name / CLI matching
- Window count + properties on `WindowManager`
- Delegate invocation counts via spies
- Options snapshot immutability

Only `DrawContent` rendering tests need ImGui mocking — for those, extract the logic into a `CollectVisibleNodes` helper (as described in Task 7). Do NOT write fake ImGui infrastructure.

### Handling `ReplaySearchPanel` stub
`ReplaySearchPanel` is a Stage 4 task. For now, create a minimal stub:
```csharp
public sealed class ReplaySearchPanel {
    public ReplaySearchPanel(Action<int> seekIntent, Action<Entity> selectIntent) { }
    public void DrawContent() { }
}
```
Registered as the 5th window shell `ReplaySearchWindow`.

---

## Deliverable

After implementing all 8 tasks, run:
```
cd FDP
dotnet test Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build -v n 2>&1 | tail -20
dotnet test Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --no-build -v n 2>&1 | tail -20
```
And (from parent or Hrot solution):
```
dotnet test Hrot/Subsystems/Hrot.ReplayBrowser.Tests/Hrot.ReplayBrowser.Tests.csproj --no-build -v n 2>&1 | tail -20
```

Write a BATCH-04-REPORT.md covering:
- Task completion status (done / partial / skipped)
- Any deviations from the spec with justification
- Final test counts (pass/fail)
- Build status (0 errors)
- Any additions to DEBT-TRACKER.md

Place the report at: `.dev/replay-browser-2/reports/BATCH-04-REPORT.md`
