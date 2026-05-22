# NodeEditor — Blueprint-Style Visual Node Editor

A production-quality, Unreal-Blueprint-style visual node editor library for C# .NET 8,
targeting Raylib-cs + rlImGui-cs + ImGui.NET hosts. The core layer is ImGui-free and
fully unit-testable.

## Demo

> Run the demo application from `src/NodeEditor.Demo`:
>
> ```
> dotnet run --project src/NodeEditor.Demo
> ```
>
> Select any of the 13 scenarios from the menu bar to explore features.
> *(Screenshots: see Demo application)*

## Architecture

Five layers with strict upward dependency:

| Layer | Project | Summary |
|---|---|---|
| Primitives | `NodeEditor.Primitives` | ID wrappers, enums, `RectF`, `IdGenerator`, `EditorKey` |
| Core | `NodeEditor.Core` | Host interfaces, `GraphView`, commands, undo stack, spatial index |
| UI | `NodeEditor.UI` | ImGui renderers — canvas, pickers, panels, find bar |
| Demo | `NodeEditor.Demo` | Fake host + 13 runnable scenarios |
| Tests | `*.Tests` | xUnit + FluentAssertions; 67 passing |

## Building

```
dotnet build     # 0 errors, 0 warnings
dotnet test      # 67 passed
```

Requirements: .NET 8 SDK.

## Task Completion

All 25 implementation tasks are complete:

| Phase | Tasks | Status |
|---|---|---|
| P0 — Scaffolding | P0-001 | ✅ |
| P1 — Kernel / Foundation | P1-001 … P1-007 | ✅ |
| P2 — View-Model | P2-001 … P2-003 | ✅ |
| P3 — Canvas & Inline Editors | P3-001, P3-002 | ✅ |
| P4 — Panels, Picker, Search | P4-001 … P4-005 | ✅ |
| P5 — Action API | P5-001 | ✅ |
| P6 — Demo Application | P6-001 … P6-003 | ✅ |
| P7 — Polish Features | P7-001 (Bookmarks), P7-002 (Hot-Reload Badges) | ✅ |
| P8 — Final Polish | P8-001 | ✅ |

See [docs/TASK-TRACKER.md](docs/TASK-TRACKER.md) for detailed task status.

## Key Design Decisions

- **Host-agnostic core**: `NodeEditor.Core` depends only on `NodeEditor.Primitives`.
  Hosts implement 11 interfaces (`IGraphModel`, `INodeCatalog`, `ITypeSystem`, …).
- **Command pattern**: All mutations flow through `IGraphCommandSink.Apply(GraphCommand)`.
  Undo/redo is owned by the editor, not the host.
- **ImGui rendering**: The UI layer (canvas, panels, picker, find bar) uses ImGui.NET
  exclusively for rendering. Raylib is only used in the Demo project.
- **Zero allocations in render hot-path**: Renderer methods pool their buffers; no
  `new` in per-frame render loops.

## Open Questions

See [docs/TASK-TRACKER.md](docs/TASK-TRACKER.md) and `docs/` for any pending spec items.
