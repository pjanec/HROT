# Onboarding — AI Editor Integration

Welcome. This effort **wires the existing NodeEdit-backed BTree, HSM, and Blueprint visual editors into the `Hrot.ClusterRunner` Editor subsystem** so they are usable, dockable, and debuggable inside the running editor. Most components already exist — this is integration plus a few missing pieces.

## Read these first (in order)

1. **[DESIGN.md](./DESIGN.md)** — what we're building, the verified current state (what exists vs what's missing), the target architecture (one OS window, three perspectives, one active asset), and the phase/task breakdown.
2. **[TASK-DETAIL.md](./TASK-DETAIL.md)** — every task (`AIE-xxx`) with files and **success conditions** (xUnit specs).
3. **[TASK-TRACKER.md](./TASK-TRACKER.md)** — live status and milestone gates.
4. **[DEBT-TRACKER.md](./DEBT-TRACKER.md)** — running list of known debt/deferrals.
5. **[`.dev/.guides/DEV-GUIDE.md`](../.guides/DEV-GUIDE.md)** — **how you must work** (batch process, reporting, quality bar). Read it before writing code. Also relevant: `.dev/.guides/CODE-STANDARDS.md`.
6. Source design talk: **[design-talk.md](./design-talk.md)** — the cumulative NotebookLM conversation this design is distilled from (long; the DESIGN already captures its conclusions and corrects its mistaken assumptions).

## Background specs (reference as needed)

In `docs/blueprints/`:
- `AI_Editor_Shared_Infrastructure.md` — the shared AI editor windows/services (`Hrot.Editor.AiShared`).
- `BTree_Editor_NodeEditor_Host_Design.md`, `HSM_Editor_NodeEditor_Host_Design.md` — the host designs for the two existing editors.
- `Blueprint_Subsystem_Editor_Detailed_Design.md` — the Blueprint editor design (data-flow).
- `NodeEdit/*.md`, `NodeEditor_Extension_*.md` — the NodeEdit canvas library and its extensions (attachments, container nodes, custom renderers, My Blueprint panel, pickers, mini-editors).

## Where the code lives (folder map)

| Area | Path | Role |
|---|---|---|
| **Composition root** (where wiring happens) | `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | the single place that builds & registers everything |
| **Shared AI editor infra** | `Hrot/Editor/Hrot.Editor.AiShared/` | catalog, selection store, windows, debug registry, refactor/comparison; **new adapters + AiDocumentManager + canvas window go here** |
| **BTree editor** | `Hrot/Subsystems/AI/Hrot.BTree.Editor/` | host services, catalog, type system, validator, command sink, renderers, debug session (built) |
| **HSM editor** | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/` | same for HSM (built); `HsmGlobalsStrip` stub to finish |
| **Blueprint editor** | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` | node drawers, palette, attachments, renderers (built); **host trio + My Blueprint model to build** |
| **Blueprint model/compiler** | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/` | `BlueprintAsset`, nodes |
| **NodeEdit library** | `FDP/ExtDeps/NodeEdit/src/` | `GraphView`, `CanvasRenderer`, `MyBlueprintPanel`, interfaces, `FakeBlueprint` demo (host template) |
| **Engine shell / windowing** | `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/` | `WindowManager`, `ManagedWindow`, perspectives; `Icons/IconAtlas.cs` (famfamfam-silk) |
| **Runner** | `Hrot/Runner/Hrot.ClusterRunner/` | `Program.cs`, `RaylibPresentationShell` (single OS window, ImGui docking) |

## Build & run

- Solution: `IOS-IG-SimHost.sln` (repo root). Target framework `net8.0`; `TreatWarningsAsErrors` is on.
- Build: `dotnet build IOS-IG-SimHost.sln`.
- Run the editor: launch the ClusterRunner with the editor subsystem (e.g. `dotnet run --project Hrot/Runner/Hrot.ClusterRunner -- --mode editor`). Use `--headless` for boot/integration tests without UI.
- Tests (xUnit): per-assembly test projects exist — `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `Hrot.Blueprints.Tests`, `Hrot.ClusterRunner.Integration.Tests`, plus `NodeEditor.*.Tests`. Run with `dotnet test`.

## How to explore the codebase

Use the **codebase-memory MCP** graph tools first (`search_graph`, `trace_path`, `get_code_snippet`, `get_architecture`) — see `.claude/CLAUDE.md`. The MCP server config is `.mcp.json` (server `codebase-memory-mcp`).

## Mental model in one paragraph

The editor is one OS window. Each open asset's **kind is a perspective** (`BTree` / `HSM` / `Blueprint`); the `WindowManager` shows only the active perspective's windows. A global **Asset Browser** lists all assets and the open documents (cross-kind switcher). The **`AiDocumentManager`** tracks open assets and the active one; activating one switches perspective, focuses the shared **`AiGraphCanvasWindow`**, and points that perspective's selection store at the asset. The canvas renders the asset via the kind's **`IEditorHostServices`** (which all live over a shared backing layer: one asset catalog, debug-session registry, hot-reload coordinator, and the engine adapters for input/theme/icons/clipboard/diagnostics). Save regenerates C# / `.bp.json` and hot-reloads into the live ECS sim; debug overlays light up execution on the canvas.

## First steps for a new contributor

1. Pick the lowest unchecked task in [TASK-TRACKER.md](./TASK-TRACKER.md) within the current milestone.
2. Open its entry in [TASK-DETAIL.md](./TASK-DETAIL.md); note files, dependencies, and success conditions.
3. Follow `.dev/.guides/DEV-GUIDE.md` for the working process; write the tests named in the success conditions.
4. Keep the solution building with warnings-as-errors; update the tracker; log any debt in [DEBT-TRACKER.md](./DEBT-TRACKER.md).
