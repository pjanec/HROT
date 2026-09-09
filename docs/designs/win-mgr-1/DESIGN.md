# Window Manager & Icon System — Design Document

**Project:** `win-mgr-1`  
**Status:** Design phase  
**Source:** [design_talk.md](../design_talk.md)

---

## 1. Overview

This document describes the design for four closely related enhancements:

1. **Icon System** (`FDP.Toolkit.ImGui.Icons`) — A texture-atlas-based colored icon framework providing reusable immediate-mode icon widgets.
2. **Window Manager** (`FDP.Toolkit.ImGui.WindowManager`) — A generic, perspective-aware window management system for multi-subsystem FDP applications.
3. **Status Bar** (`FDP.Toolkit.ImGui.WindowManager`) — A persistent bottom bar where subsystems register independent sections to display status icons and interactive controls.
4. **Background Map Perspective Manager** (`FDP.Framework.Runner` / `Hrot.ClusterRunner`) — The complementary world-space rendering perspective system that runs in parallel with the Window Manager and must be synchronized with it.

Features 1–3 are implemented entirely within `FDP/Toolkits/FDP.Toolkit.ImGui/` and remain completely decoupled from any domain or ECS logic. Feature 4 lives in the runner layer and connects the two worlds via a standard C# event, keeping the dependency arrow pointing inward toward the toolkit.

> **Note on entity context menus:** The design talk does not cover context-menu-to-entity-item sharing. That capability is already implemented by the existing `ContextMenuBuilder` / `IEntityContextMenuHandler` infrastructure in `FDP.Toolkit.ImGui` and is out of scope for this workstream.

---

## 2. Context & Motivation

### 2.1 Existing Infrastructure

The current `FDP.Toolkit.ImGui` library (at `FDP/Toolkits/FDP.Toolkit.ImGui/`) provides:
- `IImGuiRenderer` / `ImGuiRendererRegistry` — pluggable per-type cell renderers
- `EntityInspectorPanel`, `EventBrowserPanel`, `SystemProfilerPanel` — standalone panels
- Utility helpers: `ImGuiPropertyTree`, `ContextMenuBuilder`

There are **no** icon, atlas, window lifecycle, or perspective-aware facilities today.

The **multi-subsystem rendering loop** is owned by `SubsystemOrchestrator` (`FDP/Framework/FDP.Framework.Runner/`). Each `ISubsystem` gets `DrawWorld()` and `DrawUI()` calls per frame. Perspective switching at the _map level_ is done by `SubsystemOrchestrator.SwitchMapOwner()` with `MapCamera.SnapTo()` synchronisation. The Window Manager will introduce a parallel _UI panel perspective_ concept that can optionally be synchronised with the map perspective via an event.

### 2.2 Motivation

- Applications built on the FDP runner (e.g. `Hrot.ClusterRunner`) need a consistent, branded icon vocabulary (status indicators, pin/close controls, toolbar buttons, status bar icons).
- Multiple subsystems (IG, SimHost, ExCon…) each contribute ImGui panels. There is currently no uniform way to show/hide panels, group them by active context, or pin cross-context panels.
- The pin/close controls needed in the window title bars directly require the icon widgets, making icon and window features naturally sequenced.
- Subsystems also contribute world-space 2D map content (grids, icons, polygons) via `MapCanvas`/`IMapLayer`. This background rendering has its own perspective concept — parallel to the window perspective — and the two must stay in sync. Other than `SubsystemOrchestrator.SwitchMapOwner()`, no formal coordination layer exists today.
- A persistent status bar is needed so subsystems can register independent status sections without coupling to each other, reusing the same icon widget vocabulary.

---

## 3. Feature A: Icon System

**Target namespace:** `FDP.Toolkit.ImGui.Icons`  
**Target project:** `FDP/Toolkits/FDP.Toolkit.ImGui/`  
**New folder:** `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/`

### 3.1 IconAtlas

`IconAtlas` owns a single Raylib `Texture2D` that is a fixed-cell sprite sheet (checkerboard layout). The canonical atlas is the `famfamfam-silk` 16×16 icon set but the class is generic. It is `IDisposable` and calls `Raylib.UnloadTexture` on disposal.

**Coordinate syntax:** Icons are addressed by a human-readable string coordinate such as `"b12"`:
- The first character (letter) identifies the **row**: `'a'` = row 0, `'b'` = row 1, …
- The remaining digits (1-based) identify the **column**: `"12"` → column index 11.

The `GetUvCoordinates(string coordinate)` method parses this string into two `Vector2` values (`uv0` top-left, `uv1` bottom-right) computed from the icon cell dimensions divided by the atlas texture dimensions. UV computation is done **once per call** and callers are responsible for caching if needed.

**API:**
```
public class IconAtlas : IDisposable
{
    public IntPtr   TextureId   { get; }
    public Vector2  IconSizeVec { get; }
    public (Vector2 uv0, Vector2 uv1) GetUvCoordinates(string coordinate)
    public void Dispose()
}
```

### 3.2 IconWidgets

`IconWidgets` is a **static, stateless** class. All methods accept an `IconAtlas` reference. No class-level state.

The fundamental pattern for interactive widgets is:
1. **`InvisibleButton`** → allocate space + route input → query `IsItemHovered()` / `IsItemActive()`.
2. **`ImDrawList`** → layer background → layer image (±1px pressed shift) → layer hover border.

This gives pixel-perfect control and zero GC allocations on the 60 FPS render path.

#### 3.2.1 Stateless rendering

| Method | Description |
|---------|-------------|
| `InlineIcon(atlas, coordinate)` | Draws icon at current layout cursor. Calls `Gui.SameLine()` afterward so the next widget follows inline. |
| `AbsoluteIcon(atlas, coordinate, screenPos)` | Draws icon via `ImDrawList.AddImage` at an absolute screen coordinate. Does not affect the layout cursor. |

#### 3.2.2 Interactive widgets

All interactive widgets use the `InvisibleButton` + `ImDrawList` pattern so that visual states are fully custom:

| State | Rendering rule |
|-------|----------------|
| **Normal** | Icon drawn at `screenPos`, no border, no background. |
| **Hovered** | Bright rectangle border drawn around the icon bounds. |
| **Pressed (held)** | Icon image drawn at `screenPos + (1,1)`. |
| **Toggled** | Subtle filled background rectangle (gray) behind the icon. |

| Method | Signature | Description |
|---------|-----------|-------------|
| `IconButton` | `(atlas, id, coordinate) → bool` | Returns `true` on click. Hover border + 1px press shift. |
| `ToggleIcon` | `(atlas, id, coordinate, ref bool isToggled) → bool` | Changes `isToggled` on click. Filled background when toggled. Returns `true` on click. |
| `AlternatingFaceToggleIcon` | `(atlas, id, trueCoordinate, falseCoordinate, ref bool isToggled) → bool` | Like `ToggleIcon` but swaps the icon face based on state (no background fill). Returns `true` on click. |
| `DropdownFaceIcon` | `(atlas, id, IReadOnlyList<string> coordinates, ref int selectedIndex) → bool` | Click opens an `ImGui.BeginPopup` grid of icons. Selecting one sets `selectedIndex` and closes the popup. Returns `true` when a new selection is made. |

The `DropdownFaceIcon` grid uses a configurable `iconsPerRow = 4` layout and uses `Gui.PushID(i)` to prevent ImGui ID collisions inside the loop.

---

## 4. Feature B: Window Manager

**Target namespace:** `FDP.Toolkit.ImGui.WindowManager`  
**Target project:** `FDP/Toolkits/FDP.Toolkit.ImGui/`  
**New folder:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/`

### 4.1 Core Concepts

#### 4.1.1 Window Scope

```csharp
public enum WindowScope
{
    PerspectiveBound,  // Only shown when its owning perspective is active, or when pinned
    Global             // Always shown when IsOpen=true, regardless of perspective
}
```

`WindowScope.Global` replaces the previous `IsDebugWindow` flag. Any subsystem can register a global window (diagnostic dashboards, floating toolboxes, etc.).

#### 4.1.2 Perspectives

A **perspective** is a named UI mode (e.g. `"IG"`, `"SimHost"`, `"ExCon"`). Each `ManagedWindow` with `PerspectiveBound` scope is associated with exactly one perspective via `OwningPerspective`. Only windows belonging to the active perspective are rendered (unless pinned). The active perspective is a piece of internal `WindowManager` state.

Perspectives are discovered lazily from the registered windows — there is no separate perspective registration step.

#### 4.1.3 Pinning

A pinned window is shown regardless of the currently active perspective. Pinning is modelled as the `bool IsPinned` property on `ManagedWindow`. Pinning rules:

- When a window is opened via the **Windows menu** while its owning perspective is **not** the current perspective → `IsPinned` is automatically set to `true`.
- When a window is programmatically shown via `WindowManager.ShowWindow()` while its perspective is not active → `IsPinned` is automatically set to `true`.
- When a window's close button is clicked (or `HideWindow` is called) → `IsPinned` is reset to `false`.
- When the user manually unpins a visible window while its perspective is inactive → the window immediately hides. A tooltip on the pin button warns: _"Unpinning will hide this window in the current perspective."_

#### 4.1.4 Docking

ImGui docking is enabled at `SubsystemOrchestrator` initialization via `ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable`. A fullscreen dockspace is created at the start of each frame. Because all `ManagedWindow` instances use the `"{Title}###{Id}"` name format, ImGui uses only the stable `###Id` suffix for internal ID computation, ensuring the dock node graph survives title changes. No manual dock-node management is required; ImGui serializes the layout into `imgui.ini` natively.

### 4.2 ManagedWindow

`ManagedWindow` is an abstract base class. Subsystems subclass it to provide a window.

```csharp
public abstract class ManagedWindow
{
    public string      Id                 { get; }
    public string      Title              { get; }
    public string      OwningPerspective  { get; }
    public WindowScope Scope              { get; }
    public bool        IsOpen             { get; set; }
    public bool        IsPinned           { get; set; }

    protected virtual bool HasMenuBar => false;

    public    void Render(string currentPerspective, IconAtlas atlas)
    internal  void RequestFocus()

    protected virtual  void DrawLocalMenuBar()   { }
    protected abstract void DrawClientArea()
}
```

**Render lifecycle per frame:**

1. If `!IsOpen` → return immediately.
2. Evaluate `isVisible`: `Scope == Global` **or** `IsPinned` **or** `OwningPerspective == currentPerspective`.
3. If `!isVisible` → return.
4. If `_focusRequested` → call `Gui.SetWindowFocus(windowInternalName)` and clear flag.
5. Call `Gui.Begin(windowInternalName, ref IsOpen, flags)`.
6. Render custom title bar controls (pin + close icons).
7. If `HasMenuBar` → `Gui.BeginMenuBar()` → `DrawLocalMenuBar()` → `Gui.EndMenuBar()`.
8. Call `DrawClientArea()`.
9. `Gui.End()`.

**Custom title bar controls** are rendered inline using `Gui.SameLine(Gui.GetWindowWidth() - offset)`:
- **Pin icon** (`AlternatingFaceToggleIcon`): only shown for `PerspectiveBound` windows. The `"pin_on"` / `"pin_off"` icon atlas coordinates are used. Tooltip shown on hover when unpinning would hide the window.
- **Close icon** (`IconButton` with `"cross"` coordinate): sets `IsOpen = false` and `IsPinned = false`.

**Optional local menu bar:** Override `HasMenuBar => true` and implement `DrawLocalMenuBar()` to inject window-specific menus without polluting the global menu bar.

### 4.3 GlobalMenuRegistry

`GlobalMenuRegistry` maintains a tree of named menu items addressable via `"a/b/c/leaf"` slash-delimited paths. Internal nodes become `BeginMenu`/`EndMenu` sections; leaf nodes become `MenuItem` calls.

```csharp
public class MenuItemNode
{
    public string                       Name             { get; set; }
    public Action                       OnClick          { get; set; }
    public Func<bool>                   GetCheckedState  { get; set; }
    public Action<bool>                 OnCheckedChanged { get; set; }
    public bool                         IsSeparator      { get; set; }
    public Dictionary<string, MenuItemNode> Children     { get; }
}

public class GlobalMenuRegistry
{
    public MenuItemNode Root { get; }

    public void RegisterItem(string path, Action onClick)
    public void RegisterCheckableItem(string path, Func<bool> getChecked, Action<bool> onChanged)
    public void RegisterSeparator(string path)
}
```

Registration must happen before the first `Render()` call. Rendering is delegated to `WindowManager.RenderGlobalMenu(node)` which recursively issues `BeginMenu` / `MenuItem` / `EndMenu` calls.

### 4.4 WindowManager

`WindowManager` is the central orchestrator. It owns the window registry, the menu registry, the current perspective state, and the imgui settings handler for persistence.

```csharp
public class WindowManager
{
    // State
    public string CurrentPerspective { get; private set; }
    
    // Registration
    public void RegisterWindow(ManagedWindow window)
    public GlobalMenuRegistry GlobalMenu { get; }

    // Programmatic API
    public void    ShowWindow(string id)
    public void    HideWindow(string id)
    public void    SetWindowPinned(string id, bool isPinned)
    public void    FocusWindow(string id)
    public bool    TryGetWindow(string id, out ManagedWindow window)
    
    // Perspective
    public void                  SwitchPerspective(string newPerspective)
    public event Action<string, string>? OnPerspectiveChanged   // (old, new)

    // Frame entry point
    public void Render()
}
```

#### 4.4.1 Render() structure

```
Render():
    BeginMainMenuBar()
        RenderGlobalMenu()          // Dynamic subsystem-injected items (never perspective-filtered)
        RenderFixedWindowsMenu()    // "Windows" pulldown — grouped by perspective + auto-pin
        RenderPerspectiveSwitcher() // Radio buttons for each registered perspective
        RenderFixedHelpMenu()       // "Help → Debug" (global windows) + "About"
    EndMainMenuBar()
    foreach window in _windows.Values:
        window.Render(CurrentPerspective, _iconAtlas)
    _statusBar.Render(_iconAtlas)   // Always rendered last, anchored to bottom of viewport
```

`WindowManager` exposes `public StatusBar StatusBar { get; }` (initialized in constructor) so subsystems can register sections during `Initialize` via `SubsystemConfig.WindowManager`.

#### 4.4.2 Windows menu

The **Windows** menu groups `PerspectiveBound` windows by their `OwningPerspective` as sub-menus. `Global` windows are listed under a `"Global"` sub-menu. Each entry is a checkable `MenuItem` reflecting `IsOpen`. Opening a cross-perspective window triggers the auto-pin rule.

#### 4.4.3 Perspective switcher

Radio buttons are rendered in the menu bar for each distinct `OwningPerspective` value found among registered `PerspectiveBound` windows, sorted alphabetically. Clicking a radio button calls `SwitchPerspective(p)`.

#### 4.4.4 Help menu

Fixed structure:
```
Help
  └─ Debug
       ├─ <Global window 1>
       ├─ <Global window 2>
       └─ ...
  └─ About   (shows version string)
```

#### 4.4.5 Programmatic API rules

| Method | Behaviour |
|--------|-----------|
| `ShowWindow(id)` | Sets `IsOpen = true`. If `PerspectiveBound` and perspective not active → sets `IsPinned = true`. |
| `HideWindow(id)` | Sets `IsOpen = false`, `IsPinned = false`. |
| `SetWindowPinned(id, bool)` | Sets `IsPinned` only for `PerspectiveBound` windows. No-op for `Global`. |
| `FocusWindow(id)` | Calls `ShowWindow(id)` logic, then `win.RequestFocus()` to queue `Gui.SetWindowFocus` on next frame. |
| `SwitchPerspective(p)` | Updates `CurrentPerspective`, fires `OnPerspectiveChanged`. No-op if same. |

#### 4.4.6 Persistence

A custom ImGui settings handler (`AddSettingsHandler`) is registered during `WindowManager` construction. It hooks into the `imgui.ini` lifecycle:

- **Write** (`WriteAllFn`): For each window, serialises `id=IsOpen,IsPinned` under a `[FDP_WindowManager]` section.
- **Read** (`ReadLineFn`): Parses `id=IsOpen,IsPinned` lines and restores state.

This complements the native docking layout persisted by ImGui itself.

### 4.5 Perspective Synchronization with Map Layer

The `WindowManager` has **no knowledge** of the ECS, `SubsystemOrchestrator`, or `MapCanvas`. It fires `OnPerspectiveChanged` and nothing more. See [§6 Background Map Perspective Manager](#6-background-map-perspective-manager) for the full design of the parallel world-space perspective system, and tasks WM-S502 / WM-S701–703 for what needs to be built.

---

## 5. Feature C: Status Bar

**Target namespace:** `FDP.Toolkit.ImGui.WindowManager`  
**Target project:** `FDP/Toolkits/FDP.Toolkit.ImGui/`  
**New file:** `StatusBarManager.cs`

### 5.1 Purpose

The status bar is a persistent, fixed-height horizontal bar anchored at the bottom of the application viewport. It is always visible regardless of the active perspective. Subsystems register independent render delegates during their `Initialize` call; the `StatusBarManager` orchestrates the physical layout and yields control to each delegate to render whatever it wishes — text, `InlineIcon`, `IconButton`, `ToggleIcon`, colored text, etc.

Design talk quote: _"subsystems will be able to effortlessly render custom colored icons and respond to user clicks within their reserved status bar sections."_

### 5.2 Delegate-Based Registration

The status bar uses a **registry of plain `Action` delegates**. This is the idiomatic immediate-mode GUI pattern: no interface, no virtual dispatch, no allocation on the hot path. The delegate is allocated exactly once at initialization; the render loop just invokes it.

```csharp
public class StatusBarManager
{
    private struct Section
    {
        public string Id;
        public int    SortOrder;   // lower = rendered closer to left
        public Action RenderDelegate;
    }

    private readonly List<Section> _sections   = new();
    private          bool          _needsSort  = false;
    public           float         Height      { get; } // computed from ImGui frame height + padding

    public void RegisterSection(string id, int sortOrder, Action renderDelegate)
    public void Render()
}
```

`RegisterSection` appends to `_sections` and sets `_needsSort = true`. On first `Render()` after registration, the list is sorted once by `SortOrder`, then left stable.

### 5.3 Render() Implementation

```
Render():
    Sort sections by SortOrder if dirty
    Compute height = ImGui.GetFrameHeight() + Style.WindowPadding.Y * 2
    SetNextWindowPos(viewport.WorkPos.X, viewport.WorkPos.Y + viewport.WorkSize.Y - height)
    SetNextWindowSize(viewport.WorkSize.X, height)
    Begin("##GlobalStatusBar", NoDecoration | NoDocking | NoSavedSettings | NoFocusOnAppearing | NoNav)
    for i in sections:
        sections[i].RenderDelegate()           // subsystem renders whatever it wants
        if not last:
            SameLine(); SeparatorEx(Vertical); SameLine()
    End()
```

The horizontal spacer / right-anchoring problem: the design talk's first-class model is left-to-right sorted sections. Right-anchoring can be achieved by the delegate itself calling `Gui.SetCursorPosX(rightEdge)` before rendering, or by using a high `SortOrder` value. This is intentionally left to the registering subsystem rather than prescribing a `Left/Right` enum the framework has to manage.

### 5.4 Subsystem Usage Example

```csharp
// In RadarSubsystem.Initialize():
statusBar.RegisterSection("radar_status", sortOrder: 100, renderDelegate: () =>
{
    Gui.Text("Radar:");
    Gui.SameLine();
    if (IconWidgets.ToggleIcon(atlas, "radar_emit_toggle", "antenna_on", "antenna_off", ref _radarEmitting))
        HandleEmissionToggle();
    Gui.SameLine();
    Gui.TextColored(_radarEmitting ? Green : Red, _radarEmitting ? "ACTIVE" : "SILENT");
});
```

The subsystem's business state (`_radarEmitting`) is captured by closure — completely encapsulated, zero coupling to the framework.

### 5.5 WindowManager Integration

`WindowManager` owns `private readonly StatusBarManager _statusBar = new()` and exposes `public StatusBarManager StatusBar { get; }` (initialized in constructor). `Render()` calls `_statusBar.Render()` as its final step. The `SubsystemOrchestrator` dockspace height is reduced by `_statusBar.Height` so docked windows do not overlap the bar (see WM-S503).

---

## 5b. Context Menu Architecture (Out of Scope — Existing Infrastructure)

> **No new tasks.** This section documents how context menus work so developers know what to reach for; the infrastructure already exists.

### Local Window Context Menus

Inside a concrete `ManagedWindow` subclass, right-click context menus are handled directly with native ImGui calls (`ImGui.BeginPopupContextItem()`, `ImGui.BeginPopupContextWindow()`). No injection mechanism is needed or desired. High cohesion: the window owns its state and its menus.

### Entity Context Menus (Shared Between Map and Windows)

When the same domain entity (e.g., a vehicle unit) appears in both the 2D map canvas and an `EntityInspectorPanel`, right-clicking it must produce the same core menu actions. The framework solves this with a **multi-handler pipeline**: all registered handlers receive the entity and a shared `IContextMenuBuilder`, appending items in registration order separated by visual dividers. Sources:

- `IEntityContextMenuHandler` / `IDerContextMenuHandler` — interface for global domain handlers
- `LambdaEntityContextMenuHandler` / `LambdaDerContextMenuHandler` — adapter for inline lambdas, allowing a specific window or map view to append **local, context-specific** items (e.g., "Center camera here", "Select on map") alongside the shared domain actions

Both the `EntityInspectorPanel` (UI window) and the `MapCanvas` hit-test handler invoke the same pipeline for the same entity type, guaranteeing feature parity with zero duplication.

---

## 5c. Future Consideration: Unified Undo/Redo (Out of Scope)

The design talk identified the need for a unified user-action history for scenario editors. This is **not** in scope for `win-mgr-1` but should be tracked as a future workstream:

- A new `FDP.Toolkit.UserActions` library with `IUserAction { Execute(); Undo(); }` and a central `ActionHistoryManager` (Ctrl+Z / Ctrl+Y stacks).
- Subsystems instantiate domain-specific action objects (`MoveWaypointAction`, `ChangeAffiliationAction`) and push them to the shared history.
- `CompositeAction` wraps bulk operations into a single undoable unit.
- The `WindowManager` global menu can expose Undo/Redo items bound to the `ActionHistoryManager`.

---

### 6.1 Concept

The application viewport hosts two independent rendering layers:

- **ImGui layer** — managed windows, menus, status bar (governed by `WindowManager`).
- **World-space layer** — 2D map content drawn by Raylib (`MapCanvas.Draw()`, called from `ISubsystem.DrawWorld()`).

Both layers have a perspective concept. They must switch in sync but are completely decoupled. Design principle: **two parallel managers synchronized at the composition root via a plain C# event — no shared state, no cross-layer coupling.**

### 6.2 Current State

`SubsystemOrchestrator` already provides the foundation:
- `_activeMapOwner` — the subsystem whose `DrawWorld()` is called each frame.
- `SwitchMapOwner(IMapCameraProvider newOwner)` — snaps the incoming camera to the outgoing camera via `MapCamera.SnapTo()`, then updates `_activeMapOwner`.

What is **missing:**
1. A domain event (`TogglePerspectiveEvent`) on `FdpEventBus` so ECS systems can react to perspective changes without direct coupling to `SubsystemOrchestrator`.
2. A `PerspectiveCoordinatorSystem` that is the single ECS-side consumer of that event, delegates to `SwitchMapOwner`, and writes the `ActivePerspective` singleton.
3. An `ActivePerspective` singleton ECS component that individual map layer systems can query to gate their `Draw()` calls — useful when multiple subsystems contribute independent map layers to the same canvas.

### 6.3 TogglePerspectiveEvent

```csharp
// In Hrot.Common (or FDP.Framework.Runner — confirm with dev lead)
public record TogglePerspectiveEvent(string OldPerspective, string NewPerspective);
```

Published by the composition root (`Program.cs`) when `WindowManager.OnPerspectiveChanged` fires.

### 6.4 PerspectiveCoordinatorSystem

An ECS system in `Hrot.ClusterRunner` that:
1. Subscribes to `TogglePerspectiveEvent` on the event bus.
2. Maps `NewPerspective` string to the appropriate `IMapCameraProvider` subsystem instance.
3. Calls `orchestrator.SwitchMapOwner(subsystem)`.
4. Updates `world.Set(new ActivePerspective { Name = event.NewPerspective })`.

This replaces the direct `SwitchMapOwner` call that was previously wired inline in the composition root, allowing other domain systems to also subscribe to perspective changes.

### 6.5 ActivePerspective Singleton ECS Component

```csharp
// In Hrot.Common (or FDP.Framework.Runner)
public struct ActivePerspective
{
    public string Name; // matches WindowManager.CurrentPerspective value
}
```

Written by `PerspectiveCoordinatorSystem` after each switch. Read by individual map layer ECS systems to gate their rendering without coupling to the window manager.

### 6.6 Synchronization Flow

```
User clicks perspective radio button (or WindowManager.SwitchPerspective() called)
         │
         ▼
  WindowManager.CurrentPerspective updated
  WindowManager.OnPerspectiveChanged(old, new) fired   [C# event — toolkit layer]
         │
         ▼  (composition root handler in Program.cs)
  FdpEventBus.Publish(new TogglePerspectiveEvent(old, new))   [domain event — runner layer]
         │
         ├──▶ PerspectiveCoordinatorSystem
         │         ├── SubsystemOrchestrator.SwitchMapOwner()   [camera snap]
         │         └── world.Set(new ActivePerspective { Name = new })
         │
         └──▶ (any other ECS system subscribing to TogglePerspectiveEvent)
```

---

## 7. Implementation Phases & Tasks

### Phase 1 — Icon System Foundation

**Goal:** Deliver a complete, tested icon atlas and widget library.

| ID | Task |
|----|------|
| WM-S101 | `IconAtlas` — resource loading, UV parsing, disposal |
| WM-S102 | `IconWidgets` — `InlineIcon` and `AbsoluteIcon` |
| WM-S103 | `IconWidgets` — `IconButton` and `ToggleIcon` (InvisibleButton + ImDrawList visual states) |
| WM-S104 | `IconWidgets` — `AlternatingFaceToggleIcon` |
| WM-S105 | `IconWidgets` — `DropdownFaceIcon` |

### Phase 2 — Window Manager: ManagedWindow Base

**Goal:** Define the window lifecycle and visibility contract.

| ID | Task |
|----|------|
| WM-S201 | `WindowScope` enum + `ManagedWindow` abstract base (visibility rules, render lifecycle) |
| WM-S202 | `ManagedWindow` custom title bar (pin AlternatingFaceToggleIcon + close IconButton + unpin tooltip) |
| WM-S203 | `ManagedWindow` optional local menu bar (`HasMenuBar` + `DrawLocalMenuBar()`) |

### Phase 3 — Window Manager: Menu Registry & Orchestrator

**Goal:** Complete the `GlobalMenuRegistry` and core `WindowManager` wiring.

| ID | Task |
|----|------|
| WM-S301 | `GlobalMenuRegistry` — trie data structure + registration API |
| WM-S302 | `WindowManager` — dictionary-based window registry + programmatic API (`ShowWindow`, `HideWindow`, `SetWindowPinned`, `FocusWindow`, `TryGetWindow`) |
| WM-S303 | `WindowManager.Render()` — global menu tree + Windows pulldown + auto-pin logic |
| WM-S304 | `WindowManager.Render()` — perspective switcher radio buttons + `SwitchPerspective` + `OnPerspectiveChanged` event |
| WM-S305 | `WindowManager.Render()` — Help/Debug menu |

### Phase 4 — Persistence & Docking

**Goal:** Ensure layout and state survive application restarts.

| ID | Task |
|----|------|
| WM-S401 | ImGui custom settings handler — persist `IsOpen` and `IsPinned` in `imgui.ini` |
| WM-S402 | ImGui docking integration — `DockingEnable` flag + fullscreen `DockSpace` creation |

### Phase 5 — Framework Integration

**Goal:** Wire the Window Manager and Status Bar into the FDP runner.

| ID | Task |
|----|------|
| WM-S501 | `SubsystemOrchestrator` integration — create and expose `WindowManager`; allow subsystems to register windows, menu items, and status bar sections during `Initialize` |
| WM-S502 | Composition root — `OnPerspectiveChanged` → publish `TogglePerspectiveEvent` on event bus |
| WM-S503 | `SubsystemOrchestrator` dockspace height — shrink dockspace by `StatusBar.Height` so docked windows do not overlap the bar |

### Phase 6 — Status Bar

**Goal:** Implement the subsystem-injectable persistent status bar using a delegate registry.

| ID | Task |
|----|------|
| WM-S601 | `StatusBarManager` class — delegate registry + sorted render loop |
| WM-S602 | `WindowManager.StatusBar` property + `StatusBarManager.Render()` called from `WindowManager.Render()` |
| WM-S603 | Reference section registration — demonstrate status bar in `Hrot.ClusterRunner` composition root |

### Phase 7 — Background Map Perspective Manager

**Goal:** Build the ECS-side coordination layer so world-space rendering perspective stays in sync with the window perspective.

| ID | Task |
|----|------|
| WM-S701 | `TogglePerspectiveEvent` record — define in `Hrot.Common` (or `FDP.Framework.Runner`) |
| WM-S702 | `ActivePerspective` singleton ECS component |
| WM-S703 | `PerspectiveCoordinatorSystem` — subscribes to event, calls `SwitchMapOwner`, writes `ActivePerspective` |

---

## 8. Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| `InvisibleButton` + `ImDrawList` for interactive icons | Gives pixel-perfect control over hover border, pressed-shift, and toggle background without fighting ImGui's internal styling. Zero GC allocations per frame. |
| `"###Id"` ImGui window name format | ImGui uses only the `###` suffix for internal ID computation, so stable IDs survive title renames and ensure docking persistence. |
| `WindowScope` enum instead of `IsDebugWindow` flag | Generalises the "always visible" concept so any subsystem can provide global windows, not just the runner framework. |
| `WindowManager` is toolkit-internal; no ECS dependency | Keeps the toolkit reusable outside of FDP domain applications. External synchronization (map switch) is done at the composition root via `OnPerspectiveChanged`. |
| `imgui.ini` settings handler for custom state | Reuses ImGui's existing persistence lifecycle. `IsOpen` and `IsPinned` are written alongside the native docking layout in one file. |
| Perspectives discovered from registered windows | No separate registration step. Alphabetical sort keeps the perspective switcher stable. |
| `Action` delegate for status bar sections, not an interface | The design talk explicitly chose the delegate pattern: sections are allocated once at init, invoked each frame at zero overhead. The delegate closure captures all necessary subsystem state, achieving high cohesion with zero coupling to the framework. No `IStatusBarSection` interface is needed. |
| `TogglePerspectiveEvent` on event bus, not direct `SwitchMapOwner` call | Decouples `Program.cs` from `SubsystemOrchestrator` and allows any ECS system to react to perspective changes. Also makes the change testable as a domain event without spinning up the window manager. |
| `ActivePerspective` as ECS singleton component | Map layer systems can query it during `OnUpdate`/`Draw` without coupling to `WindowManager` or `SubsystemOrchestrator`. Follows the existing ECS singleton pattern used elsewhere in the project. |

---

## 9. File Layout (After Implementation)

```
FDP/Toolkits/FDP.Toolkit.ImGui/
├── Icons/
│   ├── IconAtlas.cs
│   └── IconWidgets.cs
└── WindowManager/
    ├── WindowScope.cs
    ├── ManagedWindow.cs
    ├── GlobalMenuRegistry.cs
    ├── StatusBarManager.cs
    └── WindowManager.cs

Hrot.Common/          (or FDP.Framework.Runner — confirm with dev lead)
├── Events/
│   └── TogglePerspectiveEvent.cs
└── Components/
    └── ActivePerspective.cs

Hrot.ClusterRunner/
├── Systems/
│   └── PerspectiveCoordinatorSystem.cs
└── Program.cs        (updated: OnPerspectiveChanged → publish event)
```

The `FDP.Toolkit.ImGui.csproj` already references `ImGuiNET`, `rlImGui_cs`, and `Raylib_cs`, so no new package dependencies are required for the toolkit. `TogglePerspectiveEvent` and `ActivePerspective` must be placed in a project that both `Hrot.ClusterRunner` and any future map-layer systems can reference (confirm the correct project with the dev lead).
