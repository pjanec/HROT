# BATCH-04 Report

**Batch**: BATCH-04
**Feature**: Replay Browser — Stage 2 & 3 UI foundation
**Dev path**: `.dev/replay-browser-2/batches/BATCH-04-INSTRUCTIONS.md`

---

## Status: COMPLETE

All 8 tasks delivered. Build: 0 errors in BATCH-04 projects.
Pre-existing errors in `Hrot.SimHost.Tests` (CS0246 for `AreaQueryBatchData`, `EqsTargetPool`)
were present before this batch and are not caused by any change herein.

---

## Task Summary

### RB-2.2 — ImGuiEntityLink utility

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Utils/ReplayBrowser/ImGuiEntityLink.cs`

- `Draw(string label)` renders a small button in ExCon-violet (0.7, 0.45, 0.8, 1.0).
- `TryParse(string text, out Entity entity)` handles `[i, vN]`, `[i, N]`, `[i, VN]`, whitespace tolerance; rejects negative index, malformed input.

Tests: **FND-T13** (10 valid cases) + **FND-T14** (7 invalid cases) — 17 tests, all pass.

---

### RB-2.3 — ReplayBrowserSubsystem skeleton + project wiring

**New project**: `Hrot/Subsystems/Hrot.ReplayBrowser/Hrot.ReplayBrowser.csproj`
**Main class**: `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`
**Test project**: `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/Hrot.ReplayBrowser.Tests.csproj`
**Solution**: Both projects added to `IOS-IG-SimHost.sln`

- Implements `ISubsystem` + `IWindowRegistrar`.
- `Name` returns `"ReplayBrowser"`.
- Has `(INetworkFactory)` constructor for `ScanForSubsystems` CLI discovery.
- Parameterless constructor for tests.
- `Initialize(Headless=true)` skips all Raylib/ImGui allocations.
- Internal `RegisterWindowsCore(...)` is the test seam for FND-T12.

---

### RB-2.4 — Five window shells

All windows are in `FDP/Engine/Fdp.Presentation/ImGui/Windows/ReplayBrowser/`:

| Window class | ID | Title |
|---|---|---|
| `ReplayTimelineWindow` | `rb_timeline` | Replay Timeline |
| `FdpEntityInspectorWindow` | `rb_inspector` | Replay Entity Inspector |
| `ComponentDiffWindow` | `rb_diff` | Frame Diff Viewer |
| `FdpEventBrowserWindow` | `rb_events` | Replay Event Browser |
| `ReplaySearchWindow` | `rb_search` | Replay Search |

All shells: `public sealed`, inherit `ManagedWindow`, scope = `WindowScope.PerspectiveBound`, perspective = `"ReplayBrowser"`, delegate `DrawClientArea` to their hosted panel.

Stub panel: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs`

---

### RB-2.5 — ReplayTimelinePanel

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs`

- 6 rendered rows: transport buttons, play/pause, frame slider + label, metadata, file loader, JSON export expander.
- JSON export expander: window mode radios, frame/time range inputs (auto-disabled by mode), format radios, filter checkboxes, payload toggles, epsilon input, Save button.
- `public Action<Entity>? OnEntitySelected { get; set; }`
- Save: snapshots options via `CloneOptions`, fires async `SaveAsync` (file dialog -> background export).
- `internal static JsonExportOptions CloneOptions(JsonExportOptions)` deep-clones `TargetEntities` list.
- `internal static bool GetDisabledFrameInputs(ExportWindowMode)` / `GetDisabledTimeInputs(ExportWindowMode)`.

Tests: **FND-T17** (CloneOptions immutability, GetDisabledFrameInputs/TimeInputs) — 7 tests, all pass.

---

### RB-2.6 — Subsystem composition root + delegate wiring

Implemented in `ReplayBrowserSubsystem.Initialize` and `WireDelegates()`:

- `EntitySelectionHistory.OnSelectionChanged` -> `_inspectorState.SelectedEntity`
- `PlaybackHistoryTracker.OnSeekRequested` -> `_context.SeekToFrame`
- `seekIntent` = pushFrame + seekToFrame
- `selectIntent` = pushSelection
- `_inspectorPanel.OnEntitySelected` = selectIntent; `ChainToMap = true`
- `_diffPanel.OnEntityLinkClicked` = selectIntent
- `_eventPanel.OnEntityLinkClicked` = selectIntent
- `ExecuteCausalityJump(Entity)`: internal method — PushFrame(pre) -> StepForward -> PushFrame(post) -> PushSelection

Tests: **FND-T09, T10, T11, T12, T15, T16, T18** — 8 tests, all pass.

---

### RB-2.7 — Stage 2 acceptance gate

All Stage 2 acceptance criteria met:
- [x] FND-T09: headless init + lifecycle
- [x] FND-T10: no IMapCameraProvider
- [x] FND-T11: name + CLI key + INetworkFactory ctor
- [x] FND-T12: 5 windows registered, all PerspectiveBound, all "ReplayBrowser"
- [x] FND-T15: selectIntent dispatches to EntitySelectionHistory
- [x] FND-T16: ExecuteCausalityJump correct sequence
- [x] FND-T17: snapshot immutability
- [x] FND-T18: seekIntent dispatches to PlaybackHistoryTracker + context

---

### RB-3.4 — ComponentDiffPanel

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ComponentDiffPanel.cs`

- `IReadOnlyList<DiffNode> CurrentDiffs { get; set; }` — updated by subsystem.
- `Action<Entity>? OnEntityLinkClicked { get; set; }`
- `DrawContent()` — BeginTable 2 cols, recurses `DrawDiffNode`.
- `DrawDiffNode`: hides unchanged when `_hideUnchanged=true`; entity-handle detection via `ImGuiEntityLink.TryParse`; syntax palette (cyan/green/amber/lightgray).
- `public static IReadOnlyList<DiffNode> CollectVisibleNodes(diffs, hideUnchanged)` — pure tree walker, fully testable.
- Also added `OnEntityLinkClicked` property to `EventBrowserPanel`.

Tests: 5 tests in `ComponentDiffPanelTests`, all pass.

---

### RB-3.5 — Stage 3 acceptance gate

- `ComponentDiffPanel.CollectVisibleNodes(tree, hideUnchanged:true)` returns 4 nodes (3 modified parents + 1 modified leaf) from a 4-level tree with 1 unchanged leaf.
- `CollectVisibleNodes(..., hideUnchanged:false)` returns all 5 nodes.
- Empty diffs -> empty visible list.
- Entity-handle leaf included in visible list.
- Default `_hideUnchanged == true` on fresh panel.

---

## Files Created / Modified

### New files

| Path | Purpose |
|------|---------|
| `FDP/Engine/Fdp.Presentation/ImGui/Utils/ReplayBrowser/ImGuiEntityLink.cs` | RB-2.2 |
| `FDP/Engine/Fdp.Presentation/ImGui/Windows/ReplayBrowser/ReplayTimelineWindow.cs` | RB-2.4 |
| `FDP/Engine/Fdp.Presentation/ImGui/Windows/ReplayBrowser/FdpEntityInspectorWindow.cs` | RB-2.4 |
| `FDP/Engine/Fdp.Presentation/ImGui/Windows/ReplayBrowser/ComponentDiffWindow.cs` | RB-2.4 |
| `FDP/Engine/Fdp.Presentation/ImGui/Windows/ReplayBrowser/FdpEventBrowserWindow.cs` | RB-2.4 |
| `FDP/Engine/Fdp.Presentation/ImGui/Windows/ReplayBrowser/ReplaySearchWindow.cs` | RB-2.4 |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs` | RB-2.3 stub |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs` | RB-2.5 |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ComponentDiffPanel.cs` | RB-3.4 |
| `Hrot/Subsystems/Hrot.ReplayBrowser/Hrot.ReplayBrowser.csproj` | RB-2.3 |
| `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs` | RB-2.3, RB-2.6 |
| `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/Hrot.ReplayBrowser.Tests.csproj` | RB-2.3 |
| `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs` | FND-T09..T18 |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/ImGuiEntityLinkTests.cs` | FND-T13, T14 |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/ReplayTimelinePanelTests.cs` | FND-T17 |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/ComponentDiff/ComponentDiffPanelTests.cs` | RB-3.5 |

### Modified files

| Path | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/EventBrowserPanel.cs` | Added `OnEntityLinkClicked` property |
| `IOS-IG-SimHost.sln` | Added Hrot.ReplayBrowser + Tests projects and configurations |
| All 5 window shell files | Changed `internal sealed` -> `public sealed` for cross-assembly access |

---

## Test Results

| Project | Tests | Passed | Failed |
|---------|-------|--------|--------|
| `Fdp.Presentation.Tests` (ReplayBrowser filter) | 24 | 24 | 0 |
| `Hrot.ReplayBrowser.Tests` | 8 | 8 | 0 |

**Total new tests: 32, all pass.**
