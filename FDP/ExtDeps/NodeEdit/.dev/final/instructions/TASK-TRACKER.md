# Task Tracker

Tracks implementation progress across the 25 tasks in this package.

**How to use:**
- Tick a checkbox `[ ]` → `[x]` when a task is complete (build green,
  tests green where required, demo behavior verified for UI tasks).
- Each task links to its full brief.
- Update the task's brief file with a `## Status` block as described in
  `00-START-HERE.md`.

---

## Phase 0 — Infrastructure
**Goal:** Solution compiles, all six projects exist with correct
dependencies, nothing is implemented yet but `dotnet build` succeeds.
- [x] **TASK-P0-001** Solution Scaffolding — [details](./03-01-task-scaffolding.md)

---

## Phase 1 — Kernel / Foundation
**Goal:** All pure-logic, zero-ImGui code lands. IDs, host interfaces,
command records, algorithms (fuzzy matcher, expression evaluator, spatial
index), theme/timing constants. Fully unit-testable.
- [x] **TASK-P1-001** Primitives (IDs, enums, IdGenerator, RectF) — [details](./03-02-tasks-kernel-layer.md#t-02--primitives)
- [x] **TASK-P1-002** Host Contract Interfaces — [details](./03-02-tasks-kernel-layer.md#t-03--host-contract-interfaces)
- [x] **TASK-P1-003** Commands and Undo Stack — [details](./03-02-tasks-kernel-layer.md#t-04--commands-and-undo)
- [x] **TASK-P1-004** Fuzzy Matcher + Tests — [details](./03-02-tasks-kernel-layer.md#t-05--fuzzy-matcher)
- [x] **TASK-P1-005** Expression Evaluator + Tests — [details](./03-02-tasks-kernel-layer.md#t-06--expression-evaluator)
- [x] **TASK-P1-006** Spatial Index + Tests — [details](./03-02-tasks-kernel-layer.md#t-07--spatial-index)
- [x] **TASK-P1-007** Timing / Theme / Catalog Constants — [details](./03-02-tasks-kernel-layer.md#t-08--timing--theme--catalog-constants)

---

## Phase 2 — View-Model
**Goal:** All editor-side state (viewport, selection, transient
interaction) lives in `NodeEditor.Core`, still ImGui-free, testable via
`FakeInputSource`.
- [x] **TASK-P2-001** ViewportState + SelectionState — [details](./03-03-tasks-viewmodel.md#t-09--viewportstate-and-selectionstate)
- [x] **TASK-P2-002** InteractionState — [details](./03-03-tasks-viewmodel.md#t-10--interactionstate)
- [x] **TASK-P2-003** GraphView Aggregator — [details](./03-03-tasks-viewmodel.md#t-11--graphview-aggregator)

---

## Phase 3 — Canvas & Inline Editors
**Goal:** The main editor surface runs. Nodes/pins/wires render, drag
and select work, mini-editors edit pin defaults inline. From this point
on the demo app is the visual test harness.
- [x] **TASK-P3-001** Canvas Renderer (nodes, pins, wires, hit-testing, state machine) — [details](./03-04-task-canvas-renderer.md)
- [x] **TASK-P3-002** Built-in Mini-Editors (bool, int, float, vector, color, …) — [details](./03-05-task-mini-editors.md)

---

## Phase 4 — Panels, Picker, and Search
**Goal:** All side surfaces work: generic picker (reusable across 12+
contexts), My Blueprint outline, Details inspector, Find bar, comments
and reroutes.
- [x] **TASK-P4-001** Generic Picker Window — [details](./03-06-task-picker.md)
- [x] **TASK-P4-002** My Blueprint Panel — [details](./03-07-task-myblueprint-details.md#t-15--ui-my-blueprint-panel)
- [x] **TASK-P4-003** Details Panel — [details](./03-07-task-myblueprint-details.md#t-16--ui-details-panel)
- [x] **TASK-P4-004** Find Bar / Find-in-Asset Panel — [details](./03-08-task-find-comments.md#t-17--ui-find-bar-and-find-in-asset-panel)
- [x] **TASK-P4-005** Comments and Reroutes — [details](./03-08-task-find-comments.md#t-18--ui-comments-and-reroutes)

---

## Phase 5 — Action API
**Goal:** The editor publishes commands and status; the host's chrome
(toolbar, menu, status bar, hotkeys) binds to them. Editor never draws
chrome.
- [x] **TASK-P5-001** IEditorCommands + IEditorIndicators — [details](./03-09-task-action-api.md)

---

## Phase 6 — Demo Application
**Goal:** The editor library runs end-to-end against a fake host. Proves
every spec'd feature with at least one scenario.
- [x] **TASK-P6-001** Fake Host + Canvas Scenarios 1–6 — [details](./03-10-task-demo.md#t-20--demo-fake-host--scenarios-16)
- [x] **TASK-P6-002** Picker Scenarios 7–12 — [details](./03-10-task-demo.md#t-21--demo-picker-scenarios-712)
- [x] **TASK-P6-003** Debugger Visualization Mock — [details](./03-10-task-demo.md#t-22--demo-debugger-visualization-mock)

---

## Phase 7 — Polish Features
**Goal:** Bookmark slots and hot-reload visual feedback. Both are
small, both are highly visible.
- [x] **TASK-P7-001** Bookmarks (Ctrl+1..9 jump, Ctrl+Shift+1..9 set) — [details](./03-11-tasks-final.md#t-23--bookmarks)
- [x] **TASK-P7-002** Hot-Reload Badges + Toast — [details](./03-11-tasks-final.md#t-24--hot-reload-badges)
- [x] **TASK-P7-003** Extended Demo Scenarios (S15–S33: functions, macros, custom events, dispatchers, variables, multi-tab, refactor, find, bookmarks, comments, hot-reload conflict, big graph) — [details](./03-12-task-extended-scenarios.md)

---

## Phase 8 — Final
**Goal:** Zero-warning build, complete XML docs, demo polish, README
updated, every checklist item in the acceptance pass green.
- [x] **TASK-P8-001** Final Polish + Warnings Cleanup — [details](./03-11-tasks-final.md#t-25--final-polish--warnings-cleanup)

---

## Summary

| Phase | Tasks | Approx LOC |
|---|---|---|
| Phase 0 — Infrastructure | 1 | (config) |
| Phase 1 — Kernel / Foundation | 7 | ~1,800 + tests |
| Phase 2 — View-Model | 3 | ~800 + tests |
| Phase 3 — Canvas & Inline Editors | 2 | ~1,500 |
| Phase 4 — Panels, Picker, Search | 5 | ~2,000 |
| Phase 5 — Action API | 1 | ~350 |
| Phase 6 — Demo Application | 3 | ~1,400 |
| Phase 7 — Polish Features | 2 | ~400 |
| Phase 8 — Final | 1 | (cleanup) |
| **Total** | **25** | **~8,250 + ~500 tests** |

Estimated effort for a focused agent: **3–5 days**.

## Dependencies between phases

```
Phase 0  →  Phase 1  →  Phase 2  →  Phase 3  →  Phase 4  →  Phase 6
                                       ↓            ↓          ↑
                                    Phase 5 ────────┴──────────┘
                                                               ↑
                                                            Phase 7
                                                               ↓
                                                            Phase 8
```

Within a phase, tasks should be done in their listed order (later tasks
build on earlier ones).
