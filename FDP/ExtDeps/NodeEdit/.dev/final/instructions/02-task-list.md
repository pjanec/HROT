# 02 — Task List

Implement in order. Each row links to a brief.

| # | ID | Title | Project | Approx LOC | Brief |
|---|---|---|---|---|---|
| 1 | T-01 | Solution scaffolding | (all) | 0 (config) | `03-01-task-scaffolding.md` |
| 2 | T-02 | Primitives | NodeEditor.Primitives | 280 | `03-02-tasks-kernel-layer.md` |
| 3 | T-03 | Host contract interfaces | NodeEditor.Core | 400 | `03-02-tasks-kernel-layer.md` |
| 4 | T-04 | Commands and undo | NodeEditor.Core | 250 | `03-02-tasks-kernel-layer.md` |
| 5 | T-05 | Fuzzy matcher + tests | NodeEditor.Core | 280 + 200 tests | `03-02-tasks-kernel-layer.md` |
| 6 | T-06 | Expression evaluator + tests | NodeEditor.Core | 200 + 150 tests | `03-02-tasks-kernel-layer.md` |
| 7 | T-07 | Spatial index + tests | NodeEditor.Core | 200 + 150 tests | `03-02-tasks-kernel-layer.md` |
| 8 | T-08 | Timing/theme/catalog constants | NodeEditor.Core | 200 | `03-02-tasks-kernel-layer.md` |
| 9 | T-09 | View-model: ViewportState, SelectionState | NodeEditor.Core | 350 | `03-03-tasks-viewmodel.md` |
| 10 | T-10 | View-model: InteractionState | NodeEditor.Core | 250 | `03-03-tasks-viewmodel.md` |
| 11 | T-11 | View-model: GraphView aggregator | NodeEditor.Core | 200 | `03-03-tasks-viewmodel.md` |
| 12 | T-12 | UI: CanvasRenderer (nodes, pins, wires) | NodeEditor.UI | 700 | `03-04-task-canvas-renderer.md` |
| 13 | T-13 | UI: built-in mini-editors | NodeEditor.UI | 800 | `03-05-task-mini-editors.md` |
| 14 | T-14 | UI: Generic picker window | NodeEditor.UI | 600 | `03-06-task-picker.md` |
| 15 | T-15 | UI: My Blueprint panel | NodeEditor.UI | 400 | `03-07-task-myblueprint-details.md` |
| 16 | T-16 | UI: Details panel | NodeEditor.UI | 350 | `03-07-task-myblueprint-details.md` |
| 17 | T-17 | UI: Find bar / find-in-asset panel | NodeEditor.UI | 300 | `03-08-task-find-comments.md` |
| 18 | T-18 | UI: Comments and reroutes | NodeEditor.UI | 350 | `03-08-task-find-comments.md` |
| 19 | T-19 | Action API: commands + indicators | NodeEditor.Core/UI | 350 | `03-09-task-action-api.md` |
| 20 | T-20 | Demo: fake host, scenarios 1–6 | NodeEditor.Demo | 800 | `03-10-task-demo.md` |
| 21 | T-21 | Demo: picker scenarios 7–12 | NodeEditor.Demo | 400 | `03-10-task-demo.md` |
| 22 | T-22 | Demo: debugger viz mock | NodeEditor.Demo | 200 | `03-10-task-demo.md` |
| 23 | T-23 | Bookmarks | NodeEditor.UI | 200 | `03-11-tasks-final.md` |
| 24 | T-24 | Hot-reload badges | NodeEditor.UI | 200 | `03-11-tasks-final.md` |
| 25 | T-25 | Final polish + warnings cleanup | (all) | — | `03-11-tasks-final.md` |

Total: ~9000 LOC + ~500 LOC tests. Achievable for a competent agent over
~3–5 days of focused work.

## Notes on ordering

- T-01–T-08 are the "kernel" layer: pure logic, fully unit-testable.
  Should be done before touching ImGui.
- T-09–T-11 are the view-model: still no ImGui. Tested with the
  `FakeInputSource`.
- T-12 introduces ImGui rendering. From this point on, the demo app
  (built in T-20) is the test harness; xUnit covers what it can.
- T-19 cuts across Core (interface exposure) and UI (canvas implements
  some commands directly).
- T-20–T-22 build the demo. Without these, the editor doesn't run.

## When you're not sure

If a task brief is unclear, read the referenced spec sections, then the
spec brief, then if STILL unclear, add a question to `QUESTIONS.md` and
proceed with another task.

Mark a task complete by adding to its brief file:

```
## Status
Completed: 2026-MM-DD
Files: src/NodeEditor.Core/Spatial/SpatialIndex.cs, tests/NodeEditor.Core.Tests/Spatial/SpatialIndexTests.cs
Notes: (any deviations or follow-ups)
```
