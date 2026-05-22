# BATCH-06 Completion Report — Extended Demo Scenarios (S15–S33)

## Summary

All 19 new demo scenarios (S15–S33) have been implemented, the build is clean
(0 warnings, 0 errors), and all 67 tests pass unchanged.

---

## Build & Test Results

| Check | Result |
|---|---|
| `dotnet build NodeEditor.sln -v quiet` | **Build succeeded. 0 Warning(s), 0 Error(s)** |
| `dotnet test NodeEditor.sln --no-build -v quiet` | **Passed: 63 (Core) + 4 (UI) = 67 total, 0 Failed** |

---

## Files Created / Modified

### New scenario files

| File | Scenario Name | Group |
|---|---|---|
| `src/NodeEditor.Demo/Scenarios/S15_VariablesGetSet.cs` | 15 — Variables: Get/Set Drag | B |
| `src/NodeEditor.Demo/Scenarios/S16_PromoteToVariable.cs` | 16 — Promote to Variable | B |
| `src/NodeEditor.Demo/Scenarios/S17_CustomEvent.cs` | 17 — Custom Event | B |
| `src/NodeEditor.Demo/Scenarios/S18_FunctionAuthoring.cs` | 18 — Function Authoring | B |
| `src/NodeEditor.Demo/Scenarios/S19_MultipleReturnNodes.cs` | 19 — Multiple Return Nodes | B |
| `src/NodeEditor.Demo/Scenarios/S20_MacroWithWildcards.cs` | 20 — Macro with Wildcards | B |
| `src/NodeEditor.Demo/Scenarios/S21_EventDispatcher.cs` | 21 — Event Dispatcher | B |
| `src/NodeEditor.Demo/Scenarios/S22_CollapseToFunction.cs` | 22 — Collapse to Function (Ctrl+E) | C |
| `src/NodeEditor.Demo/Scenarios/S23_CollapseToMacro.cs` | 23 — Collapse to Macro | C |
| `src/NodeEditor.Demo/Scenarios/S24_ExpandNode.cs` | 24 — Expand Node | C |
| `src/NodeEditor.Demo/Scenarios/S25_MultiTab.cs` | 25 — Multi-Tab | C |
| `src/NodeEditor.Demo/Scenarios/S26_Comments.cs` | 26 — Comments | C |
| `src/NodeEditor.Demo/Scenarios/S27_NestedComments.cs` | 27 — Nested Comments | C |
| `src/NodeEditor.Demo/Scenarios/S28_FindInGraph.cs` | 28 — Find in Graph | D |
| `src/NodeEditor.Demo/Scenarios/S29_FindInAsset.cs` | 29 — Find in Asset | D |
| `src/NodeEditor.Demo/Scenarios/S30_GoToDefinition.cs` | 30 — Go to Definition | D |
| `src/NodeEditor.Demo/Scenarios/S31_Bookmarks.cs` | 31 — Bookmarks | D |
| `src/NodeEditor.Demo/Scenarios/S32_HotReloadConflict.cs` | 32 — Hot-Reload Conflict | D |
| `src/NodeEditor.Demo/Scenarios/S33_BigGraph.cs` | 33 — Big Graph (500 nodes) | D |

### New infrastructure file

| File | Purpose |
|---|---|
| `src/NodeEditor.Demo/FakeBlueprint/FakeGraphContainer.cs` | Lightweight multi-graph container for multi-tab scenarios |

### Modified files

| File | Changes |
|---|---|
| `src/NodeEditor.Demo/FakeBlueprint/FakeMyBlueprintModel.cs` | Added macros + dispatchers sections; mutation helpers: `AddVariable`, `RemoveVariable`, `RenameVariable`, `AddFunction`, `RemoveFunction`, `AddMacro`, `AddCustomEvent`, `RemoveCustomEvent`, `AddDispatcher`, `EnsureSection`; set `events` section `CanCreateItems = true` |
| `src/NodeEditor.Demo/FakeBlueprint/FakeHostServices.cs` | Added `ToastQueue_` property; `MyBlueprint` made writable (`private set`); added `OverrideMyBlueprint(model)` |
| `src/NodeEditor.Demo/Scenarios/Scenario.cs` | Added `virtual void Setup(FakeMyBlueprintModel mbModel)`, `virtual FakeGraphContainer? BuildMultiGraph(...)`, `virtual FakeDebugSession? Session` |
| `src/NodeEditor.Demo/Scenarios/S13_DebugVizMock.cs` | Added `override` keyword to `Session` property (CS0114 fix) |
| `src/NodeEditor.Demo/DemoShell.cs` | Registered S15–S33; added `_graphContainer`, `_lastElapsed` fields; multi-graph tab bar in canvas window; Ctrl+Tab switching; updated `ApplyScenario` to call `Setup`, `BuildMultiGraph`, assign `Session`; File menu Save/Compile mock items; Make Dirty button; FPS badge in status bar; S32 "Simulate External Modify" button |

---

## Developer Insights

**Q1 — What was the most significant design decision?**

Extending `Scenario` with three orthogonal virtuals (`Setup`, `BuildMultiGraph`,
`Session`) instead of a monolithic builder kept each concern isolated. `Setup`
populates `FakeMyBlueprintModel` before the view is created, so the My Blueprint
panel renders correctly on first frame. `BuildMultiGraph` allows a scenario to own
its entire graph topology (multiple `FakeGraphModel` instances in a
`FakeGraphContainer`) without coupling into `DemoShell`'s single-graph path.

**Q2 — What caused the most build friction?**

Two issues required quick fixes:

1. `new System.Random(seed: 42)` — named argument `seed` is not available in the
   target TFM. Fixed by using positional syntax `new System.Random(42)`.
2. `FakeInputSource.IsKeyChordPressed(string)` — this method doesn't exist. Replaced
   with idiomatic `Modifiers.HasFlag(KeyModifiers.Ctrl) && IsKeyPressed(EditorKey.Tab)`
   which matches the existing `HotkeyDispatcher` pattern.

**Q3 — How were multi-graph scenarios handled?**

`S25_MultiTab` overrides `BuildMultiGraph`, returns a `FakeGraphContainer` with three
named `FakeGraphModel` instances (EventGraph, ComputeDamage, OnEnemyKilled).
`DemoShell.ApplyScenario` detects a non-null container and skips the single-graph
`Build` path entirely. The canvas window draws an `ImGui.BeginTabBar` when a container
is active; switching tabs re-creates `FakeHostServices` for the newly active graph so
command sink, validator, and type system are all wired to the right model.

**Q4 — How was `FakeMyBlueprintModel` extended without breaking existing scenarios?**

All new mutation methods are additive. The original constructor still works;
`EnsureSection` is a private helper that lazily materialises sections only when a
scenario calls an `Add*` method. No existing scenario calls `Setup`, so the default
no-op implementation means zero regression risk.

**Q5 — Anything to watch in a production implementation?**

The multi-tab Ctrl+Tab shortcut in `DemoShell.Frame` bypasses the
`HotkeyDispatcher` / `EditorCommandsImpl` pipeline. A real product should register
`editor.nextTab` and `editor.prevTab` commands in the catalog and let
`HotkeyDispatcher` handle them, so the shortcuts appear in menus, can be rebound,
and participate in `CanExecute` guarding. The demo shortcut path is intentionally
minimal to avoid widening `CommandCatalog` scope beyond the batch.

---

## Suggested Commit Message

```
feat(demo): implement extended demo scenarios S15–S33 (BATCH-06)

- Add 19 new Scenario subclasses covering blueprint authoring (S15-S21),
  collapse/expand/multi-tab/comments (S22-S27), search/bookmark/hot-reload
  (S28-S32), and big-graph performance (S33 – 500 nodes).
- Extend Scenario base with Setup(), BuildMultiGraph(), and Session virtuals.
- Add FakeGraphContainer for multi-tab scenario support.
- Extend FakeMyBlueprintModel with mutation helpers (AddVariable, AddFunction,
  AddMacro, AddCustomEvent, AddDispatcher, etc.).
- Extend FakeHostServices with ToastQueue_ and OverrideMyBlueprint().
- Wire all 19 scenarios into DemoShell: tab bar, FPS badge, Save/Compile mocks,
  Make Dirty button, S32 Simulate External Modify button.

Build: 0 errors, 0 warnings. Tests: 67/67 passed.
```
