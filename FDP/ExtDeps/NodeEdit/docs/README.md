# NodeEditor — Spec & Implementation Package

A blueprint-style visual node editor for C# .NET 8, targeting use in Raylib-cs + rlImGui-cs + ImGui.NET hosts. Designed to be UI-framework-agnostic at the core, with an ImGui rendering layer and a pluggable host model.

This package is the authoritative reference for implementing the editor. The conversation that produced it is closed; everything decided lives here.

## Reader's guide

This package is split into four directories, in the order you should consume them:

### `01_interaction_specs/` — what the editor *does*

The UX behavior, in detail. State machines for every interaction, every keyboard shortcut, every visual feedback rule. Read this before touching code.

| File | Topic |
|---|---|
| `A_canvas_interactions.md` | Pan, zoom, selection, drag, wire creation, every mouse and keyboard gesture on the canvas |
| `B_mini_editors.md` | Inline default-value editors for pin types (bool, int, float, Vector3, Color, enums, struct splits, etc.) |
| `C_generic_picker.md` | The universal "long list with IntelliSense" widget used everywhere — node search, variable picker, asset picker, type picker, etc. |
| `D0_command_action_api.md` | Editor command catalog and indicator API — how host shells (toolbars/menus/statusbars) integrate without coupling to the editor |
| `D1_custom_events.md` | Custom event creation and authoring UX |
| `D2_functions.md` | Function creation, editing, calling, collapse-to-function |
| `D3_macros.md` | Macro creation, wildcards, collapse-to-macro |
| `D4_find_in_graph.md` | Find/replace, scope-based search, go-to-definition, find-references |
| `D6_my_blueprint_panel.md` | Outliner panel — sections, items, drag-drop sources, context menus |
| `D7_details_panel.md` | Property editor panel — view dispatch, integration with StructEdit |
| `D8_comments_and_reroutes.md` | Comment boxes and reroute nodes (visual decorations on the canvas) |
| `D9_bookmarks.md` | Bookmark system for viewport positions |
| `D10_hot_reload_indicators.md` | Visual feedback when graphs change externally |

### `02_architecture/` — how the editor is *structured*

The internal architecture: layers, interfaces, data structures, how the editor talks to the host.

| File | Topic |
|---|---|
| `architecture_overview.md` | The five layers, what each owns, dependency direction |
| `data_model.md` | Identity types, view-model, separation between asset-owned and editor-owned state |
| `interfaces_host_contract.md` | Every interface the host implements: `IGraphModel`, `INodeCatalog`, `ITypeSystem`, `ILinkValidator`, `IGraphCommandSink`, `IPickerRegistry`, `IDebugSession`, `IDiagnosticsSink`, `IInputSource`, `IEditorTheme` |
| `interfaces_editor_internal.md` | Editor-internal interfaces: `GraphView`, `SelectionState`, `InteractionState`, `SpatialIndex`, `UndoStack`, `CommandSink` |
| `command_pipeline.md` | The mutation flow: command → host validation → host apply → model change event → view refresh |
| `performance_model.md` | Per-frame budgets, virtualization rules, cache strategies, target framerates |

### `03_implementation_plan/` — how to *build* it

Project structure, task list, the agent's playbook.

| File | Topic |
|---|---|
| `agent_brief.md` | **Start here.** What the agent needs to know to execute this package. Workflow, conventions, definition of done. |
| `project_structure.md` | Folder layout, namespace conventions, target frameworks, package references |
| `task_list.md` | Numbered tasks with dependencies. Each task references which spec sections to follow and which kernel files to use. |
| `code_conventions.md` | C# style, threading rules, allocation rules, ImGui patterns |

### `04_kernel_code/` — the *foundation* code

Hand-written by the human architect; the agent should treat these as ground truth and build on them, not modify them. All code is inlined into markdown files for review and copy-paste.

| File | Topic |
|---|---|
| `K01_primitives.md` | `NodeId`, `PinId`, `LinkId`, `GraphId`, `CommentId`, `RerouteId`, `TypeKey`, `NodeKindKey` |
| `K02_host_interfaces.md` | The public contract the host implements |
| `K03_editor_interfaces.md` | Editor-internal contracts |
| `K04_fuzzy_matcher.md` | The ranking algorithm with character-position output for highlighting |
| `K05_undo_stack.md` | Command/inverse pair model with batching |
| `K06_spatial_index.md` | Coarse-grid spatial structure for hit-test and viewport culling |
| `K07_interaction_dispatcher.md` | The top-level state machine for canvas interactions |
| `K08_constants_and_theme.md` | Color palettes, timing constants, default keybindings, category colors |
| `K09_command_catalog.md` | The default `IEditorCommands` implementation and command descriptor list |

## How the agent should work

1. Read `03_implementation_plan/agent_brief.md` first.
2. Read all of `01_interaction_specs/` to understand the UX.
3. Skim `02_architecture/` for the structural picture.
4. Study `04_kernel_code/` — these files define the contracts that all generated code must conform to.
5. Pick tasks from `03_implementation_plan/task_list.md` in order (some tasks have prerequisites listed).
6. For each task, the brief lists which spec sections are authoritative for that task.
7. If a spec is ambiguous or contradicts another, stop and surface the question — don't invent behavior.

## How the human reviewer should work

1. Read `01_interaction_specs/` and confirm everything matches what was decided in the design conversation.
2. Skim `02_architecture/` to verify the structural decisions.
3. Treat `04_kernel_code/` as the locked foundation; if something needs to change here, it cascades into many tasks.
4. Tasks in `03_implementation_plan/task_list.md` are sized for ~200–400 LOC of agent output each. Easy to review one at a time.

## Provenance

These specs were produced in collaboration between the architect (the human) and Claude during a design session in May 2026. Major decisions:

- Match Unreal Blueprint UX wherever possible — users will find familiar interactions "right."
- Mouse + keyboard only, no pen/touch.
- Multi-graph tabs.
- Performance target: 500+ node graphs at 60 FPS vsync.
- Use raylib-cs + rlImGui-cs + ImGui.NET 1.91.6.1 on .NET 8.
- Generic core, ImGui rendering layer, pluggable host model.
- Includes a demo app with a fake blueprint host for testing without engine integration.
