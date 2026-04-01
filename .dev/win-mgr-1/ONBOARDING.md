# Onboarding Guide — `win-mgr-1`: Window Manager & Icon System

Welcome to the `win-mgr-1` workstream. This guide gets you up to speed quickly.

---

## What We Are Building

This workstream adds four closely related capabilities:

1. **Icon System** (`FDP.Toolkit.ImGui.Icons`) — A texture-atlas-based reusable icon widget library for immediate-mode GUIs. Covers the full range from simple inline rendering to interactive toggle buttons and multi-face dropdown pickers.

2. **Window Manager** (`FDP.Toolkit.ImGui.WindowManager`) — A generic, perspective-aware panel management system for multi-subsystem FDP applications. Subsystems register their panels, inject global menu items, and get perspective-grouped show/hide/pin behaviour for free.

3. **Status Bar** (`FDP.Toolkit.ImGui.WindowManager`) — A persistent, fixed-height bar at the bottom of the viewport. Subsystems register independent render **delegates** (`Action`) with a sort order. The `StatusBarManager` calls each delegate in sort order, separated by vertical dividers. Delegates render whatever they want — icons, text, toggles — using the icon widget API. Always visible regardless of the active perspective.

4. **Background Map Perspective Manager** (`Hrot.ClusterRunner` / `Hrot.Common`) — The ECS-side complement to the Window Manager's perspective switching. When the window perspective changes, a `TogglePerspectiveEvent` is published on the `FdpEventBus`. `PerspectiveCoordinatorSystem` consumes it, calls `SubsystemOrchestrator.SwitchMapOwner()` (camera snap), and writes the `ActivePerspective` singleton ECS component so individual map layers can gate their `Draw()` calls.

Features 1–3 know nothing about the ECS or domain. Feature 4 is the composition-root wiring that keeps both worlds synchronized.

> **Entity context menus** are NOT in scope for this workstream. The existing `ContextMenuBuilder` / `IEntityContextMenuHandler` in `FDP.Toolkit.ImGui` already handles that.

---

## Design & Task Documents

All design documents live in `.dev/win-mgr-1/`:

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Full architecture, design decisions, and phase/task table of contents |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task descriptions with file targets and unit-test success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Binary progress checklist — check off tasks as they are completed |

Read [DESIGN.md](./docs/DESIGN.md) first. Then consult [TASK-DETAIL.md](./TASK-DETAIL.md) before starting each task.

The original design conversation is at [design_talk.md](./design_talk.md). It is the source of truth for user-facing intent and design rationale. Read it if you need to understand _why_ a decision was made.

---

## Developer Workflow

This project uses **batch-based development**. Read the developer guide before writing any code:

**[.dev/.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md)**

Key rules:
- Work is performed in discrete batches described in `.dev/win-mgr-1/batches/BATCH-xx-INSTRUCTIONS.md`.
- After completing a batch, write a report to `.dev/win-mgr-1/reports/BATCH-xx-REPORT.md` using the template.
- Do not start the next batch without the dev lead's review of the previous report.
- Coding standards: [.dev/.guides/CODE-STANDARDS.md](../.guides/CODE-STANDARDS.md).

---

## Codebase Layout — Relevant Components

### What We Are Adding (new files)

```
FDP/Toolkits/FDP.Toolkit.ImGui/
├── Icons/
│   ├── IconAtlas.cs           ← WM-S101
│   └── IconWidgets.cs         ← WM-S102 – WM-S105
└── WindowManager/
    ├── WindowScope.cs         ← WM-S201
    ├── ManagedWindow.cs       ← WM-S201 – WM-S203
    ├── GlobalMenuRegistry.cs  ← WM-S301
    ├── IStatusBarSection.cs   ← WM-S601
    ├── StatusBar.cs           ← WM-S601 – WM-S602
    └── WindowManager.cs       ← WM-S302 – WM-S305, WM-S401, WM-S602

Hrot.Common/ (or FDP.Framework.Runner — confirm with dev lead)
├── Events/
│   └── TogglePerspectiveEvent.cs   ← WM-S701
└── Components/
    └── ActivePerspective.cs        ← WM-S702

Hrot.ClusterRunner/
└── Systems/
    └── PerspectiveCoordinatorSystem.cs  ← WM-S703
```

### What We Are Extending (existing files)

| File | Why touched |
|------|------------|
| `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` | Add docking (WM-S402), expose `WindowManager` (WM-S501), shrink dockspace for status bar (WM-S503) |
| `FDP/Framework/FDP.Framework.Runner/SubsystemConfig.cs` | Add `WindowManager?` property (WM-S501) |
| `Hrot.ClusterRunner/Program.cs` | Subscribe `OnPerspectiveChanged` → publish `TogglePerspectiveEvent` (WM-S502) |

### What We Are Reading (for context — do not modify unless your task requires it)

| Path | Relevance |
|------|-----------|
| `FDP/Toolkits/FDP.Toolkit.ImGui/` | Existing toolkit we are extending; uses `global using Gui = ImGuiNET.ImGui` |
| `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` | Frame loop owner; `_activeMapOwner` / `SwitchMapOwner()` map camera sync |
| `FDP/Toolkits/FDP.Toolkit.Vis2D/MapCanvas.cs` | 2D map surface; `MapCamera.SnapTo()` used by `SwitchMapOwner` |
| `Hrot.ClusterRunner/Services/IgSubsystem.cs` | Implements `IMapCameraProvider`; perspective `"IG"` |
| `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` | Perspective `"SimHost"` |
| `FDP/Toolkits/FDP.Toolkit.ImGui/Utils/ContextMenuBuilder.cs` | Existing entity context menu system — out of scope but related |

---

## How to Build

```powershell
cd D:\Work\IOS-IG-SimHost-FDP
dotnet build IOS-IG-SimHost.sln
```

To build only the toolkit project:

```powershell
dotnet build FDP/Toolkits/FDP.Toolkit.ImGui/FDP.Toolkit.ImGui.csproj
```

Clean build:

```powershell
dotnet build IOS-IG-SimHost.sln --no-incremental
```

---

## Dependencies

The `FDP.Toolkit.ImGui` project already references all packages needed for this work:

- `ImGuiNET` — C# bindings for Dear ImGui
- `rlImGui_cs` — Raylib ↔ ImGui bridge (`rlImGui.Begin/End`)
- `Raylib_cs` — Raylib window, texture, and drawing APIs

No new NuGet packages are required.

---

## Key Architecture Constraints

1. **No ECS or domain references in the toolkit.** `FDP.Toolkit.ImGui.WindowManager` and `FDP.Toolkit.ImGui.Icons` must not reference `Fdp.Kernel`, `FdpEventBus`, or any simulation domain type.

2. **`WindowManager` exposes `OnPerspectiveChanged` (C# event) for external wiring**, not a bus. The composition root bridges this event into `TogglePerspectiveEvent` on the `FdpEventBus`.

3. **`PerspectiveCoordinatorSystem` owns the map-side perspective switch.** Only this system should call `SubsystemOrchestrator.SwitchMapOwner()`. The composition root (`Program.cs`) does NOT call `SwitchMapOwner` directly — it just publishes the event.

4. **Icon widgets use `InvisibleButton` + `ImDrawList`** for all interactive elements — never `ImageButton` for the main hit area (except inside popup grids). This gives pixel-level visual control.

5. **Window names use `"{Title}###{Id}"` format** so ImGui's docking engine uses the stable `###Id` for node matching.

6. **Global windows** (`WindowScope.Global`) bypass perspective filtering entirely. The `IsDebugWindow` bool pattern is replaced by this enum.

7. **Status bar is perspective-agnostic** — it renders regardless of `CurrentPerspective`. Subsystem sections are always shown.
