# Fdp.Presentation

**Project path:** `FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj`
**Date:** 2026-05-23

---

## README Validation

**Missing** — No `README.md` file exists in the project folder.

---

## Executive Overview

`Fdp.Presentation` is the **visual/UI runtime layer** of the FDP simulation framework.
It was assembled by merging three previously separate projects (`FDP.Toolkit_Vis2D`,
`FDP.Toolkit_ImGui`, and `Fdp.Raylib`) into a single assembly. The project provides:

- A **Raylib application host** (`FdpApplication`) that manages the window, main loop,
  and the two rendering passes (world geometry + ImGui UI).
- A **2D map canvas** (`MapCanvas`) with a composable layer stack, panning/zooming
  camera, input routing, and gizmo integration.
- A comprehensive **ImGui debug UI** toolkit: entity inspector, event browser, replay
  browser, message log, system profiler, architecture diagnostics, component editor,
  and window management infrastructure.

### Position in the FDP Layering Model

```
+----------------------------------------------+
|        Host Applications (IG, Editor, ...)   |
+----------------------------------------------+
|   Fdp.Presentation  (THIS PROJECT)           |
|   - Raylib app host / main loop              |
|   - 2D map canvas + layers                  |
|   - ImGui panels + window manager           |
+-------------------+------------------+-------+
| Fdp.ModuleHost    |   Fdp.Core        |  ...  |
| (scheduling,      | (ECS kernel,     |       |
|  modules)         |  entities)       |       |
+-------------------+------------------+-------+
```

**Target framework:** .NET 8.0 with `AllowUnsafeBlocks` and nullable reference types.

---

## Architecture

### Design Decisions

1. **Two-namespace assembly**: The project exposes types under both
   `Fdp.Toolkit.Vis2D.*` (2D canvas subsystem) and `Fdp.Presentation.*` (ImGui
   subsystem). This reflects the absorbed project history and allows downstream
   callers to keep their existing `using` directives unchanged.

2. **Abstraction-first for testability**: All input, resource, and selection concerns
   are behind interfaces (`IInputProvider`, `IResourceProvider`, `ISelectionState`).
   Raylib-concrete implementations live in a `Defaults/` subfolder and are swappable
   for test doubles.

3. **Composable layer stack**: `MapCanvas` holds an ordered `List<IMapLayer>`. Layers
   are drawn bottom-to-top and their input handlers are queried top-to-bottom (the
   topmost layer gets first refusal). Layer visibility is controlled by a 32-bit bitmask
   so up to 32 layers can be toggled without any allocation.

4. **ImGui panel/window separation**: Every UI surface is split into a *panel*
   (rendering logic, no window chrome) and a *window* (`ManagedWindow` subclass with
   `DrawClientArea`). This enables embedding panels in custom layouts or test harnesses
   without an active window manager.

5. **Renderer registry (reflection scan)**: `ImGuiRendererRegistry` performs a
   one-time `AppDomain` assembly scan to discover all `IImGuiRenderer` implementations
   annotated with `[ImGuiRenderer(typeof(T))]`. This allows domain assemblies to
   register custom visual renderers without modifying the presentation library.

6. **Zero-allocation hot paths**: `IEntityFilter.IsMatch`, `ChildEnumerator`, and
   `DebugPrimitiveRenderer2D.Render` are designed to be allocation-free on the 60 FPS
   update loop. Expensive setup (filter compilation, atlas UV lookup caches) is
   performed once at construction time.

7. **Perspective-scoped windows**: `WindowScope.PerspectiveBound` windows are only
   rendered when the named perspective is active, allowing the same `WindowManager`
   instance to drive completely different UI layouts (e.g. "IG" vs "ReplayBrowser")
   without rebuilding the window set.

---

## Architecture Diagrams

### Diagram 1: Raylib Application Main Loop

```
+-------------------+
|   FdpApplication  |
|  (abstract)       |
|                   |
|  Run()            |
|  +-----------+    |
|  | InitWindow|    |  Raylib.InitWindow
|  +-----------+    |  rlImGui.Setup
|       |           |
|  +-----------+    |
|  |  OnLoad() |    |  <-- abstract: user creates World, Kernel
|  +-----------+    |
|       |           |
|  +----v------+    |
|  |  Loop     |    |
|  | OnUpdate  |    |  Kernel.Update()
|  | OnDrawWorld    |  MapCanvas.Draw()
|  | OnDrawUI  |    |  WindowManager.Render()
|  +----+------+    |
|       |           |
|  +-----------+    |
|  | OnUnload  |    |  Kernel.Dispose, World.Dispose
|  +-----------+    |
+-------------------+
```

### Diagram 2: 2D Map Canvas Rendering Pipeline

```
+-------------------+      Update(dt)       +-------------------+
|   MapCanvas       |--------------------> |   MapCamera       |
|                   |                      | (pan/zoom/lerp)   |
| ActiveLayerMask   |                      +-------------------+
| List<IMapLayer>   |
|                   |      Draw()
|                   |  Camera.BeginMode()
|                   |  Build RenderContext  +-------------------+
|                   |  +---------------->  |   IMapLayer [ 0 ] |  GridMapLayer
|                   |  |                   +-------------------+
|                   |  |                   |   IMapLayer [ 1 ] |  EntityLayer
|                   |  |                   +-------------------+
|                   |  |                   |   IMapLayer [31 ] |  DebugGizmoLayer
|                   |  +---------------->  +-------------------+
|                   |  Camera.EndMode()
|                   |
|                   |  ProcessInputPipeline
|                   |  (reverse order: top -> bottom)
+-------------------+
        |
        | IResourceProvider.Get<T>()
        v
+-------------------+
|  Resource Bag     |  (MapCamera, ISelectionState, ...)
+-------------------+
```

### Diagram 3: ImGui Window Manager Architecture

```
+-------------------------+
|   WindowManager         |
|                         |
| RegisterWindow(w)       |
| ShowWindow(id)          |
| HideWindow(id)          |
| FocusWindow(id)         |
| SwitchPerspective(name) |
| Render()                |
|   |                     |
|   +-- GlobalMenuRegistry|  trie-based menu items
|   |                     |
|   +-- StatusBarManager  |  sorted section list
|   |                     |
|   +-- Dict<id,Window>   |
|       |                 |
|       | for each open   |
|       v                 |
| +-------------------+   |
| |  ManagedWindow    |   |  abstract base
| |  (PerspectiveBound|   |
| |   or Global)      |   |
| |  DrawClientArea() |   |  <- override in subclass
| +-------------------+   |
+-------------------------+
        |
        | hosts a Panel
        v
+-------------------------+
|   Panel (logic)         |  EntityInspectorPanel
|   DrawContent()         |  EventBrowserPanel
|                         |  ReplayTimelinePanel
|   -> ComponentReflector |  MessageLogPanel
|   -> ImGuiPropertyTree  |  ...
+-------------------------+
```

### Diagram 4: Renderer Registry and Inspector Pipeline

```
  EntityInspectorPanel
         |
         | DrawComponents(session, entity)
         v
  ComponentReflector
         |
         | For each component type T
         |   ImGuiRendererRegistry.GetRenderer(T, contextType)
         |          |
         |   found? |
         |    yes   +-----> IImGuiRenderer.GetSummary(value)  -> header
         |               -> IImGuiRenderer.RenderValue(value) -> detail
         |    no    +-----> ImGuiPropertyTree.Render(value)   -> default tree
         v
  Two-column Property|Value table
```

### Diagram 5: Gizmo and Input Flow

```
  Host Application
       |
       | MapCanvas.Update(dt)
       v
  ProcessInputPipeline
       |
       |  Right-drag? -> MapCamera.HandleInput
       |  Layers (top->bottom):
       |    layer.HandleInput(worldPos, button, isPressed)
       |    layer.HandleHover(mouseWorldPos)
       |    layer.HandleDrag(worldPos, delta)
       |    layer.HandleKeyInput(key)
       v
  DebugGizmoLayer
       |
       | -> GizmoManager.HandleInput
       |    -> IEntityStatefulGizmo.OnMouseEvent
       |    -> IEntityStatefulGizmo.OnKeyEvent
       |
       | DebugPrimitiveRenderer2D.Render(primitives, ctx)
       v
  Raylib draw calls
```

---

## Source Structure

The project root contains 96 C# source files organized into two major subsystems.

### Subsystem A: Raylib Application Host

**Namespace:** `Fdp.Presentation.Raylib`

| File | Type | Responsibility |
|------|------|----------------|
| `Raylib/FdpApplication.cs` | `abstract class FdpApplication : IDisposable` | Main loop host. Manages Raylib window, rlImGui setup, ImGui persistence, and the Load/Update/DrawWorld/DrawUI/Unload lifecycle. Users subclass this. |
| `Raylib/ApplicationConfig.cs` | `struct ApplicationConfig` | Value object carrying window title, dimensions, FPS cap, Raylib config flags, and persistence toggle. |

---

### Subsystem B: 2D Map Canvas (`Fdp.Toolkit.Vis2D`)

**Namespace root:** `Fdp.Toolkit.Vis2D`

#### Abstractions

| File | Type | Responsibility |
|------|------|----------------|
| `Vis2D/Abstractions/CoreInterfaces.cs` | `struct RenderContext`, `interface IMapLayer` | The central rendering contract. `RenderContext` carries zoom, mouse world position, delta time, layer mask, resource provider, and debug draw builder. `IMapLayer` defines the full layer protocol: Update, Draw, HandleInput, HandleHover, HandleDrag, HandleKeyInput, PickEntity. |
| `Vis2D/Abstractions/IInputProvider.cs` | `interface IInputProvider` | Abstracts mouse position, button state, keyboard state, ImGui capture flags. |
| `Vis2D/Abstractions/ISelectionState.cs` | `interface ISelectionState` | Entity selection tracking: selected set, primary selected, hovered entity. |
| `Vis2D/Abstractions/IHierarchyAdapter.cs` | `interface IHierarchyAdapter`, `ref struct ChildEnumerator` | Zero-allocation hierarchy traversal. `ChildEnumerator` walks `VisHierarchyNode` linked lists without allocating. |
| `Vis2D/Abstractions/IEntityFilter.cs` | `interface IEntityFilter`, `interface IEntityFilterFactory` | Per-entity pick predicate and its factory. The filter is compiled once, IsMatch is O(1) allocation-free. |
| `Vis2D/Abstractions/IResourceProvider.cs` | `interface IResourceProvider` | Generic typed-key resource bag (Get<T>, Has<T>). |
| `Vis2D/Abstractions/MapMouseButton.cs` | `enum MapMouseButton` | Left/Right/Middle. Values match Raylib-cs `MouseButton` for direct cast. |
| `Vis2D/Abstractions/MapKeyboardKey.cs` | `enum MapKeyboardKey` | GLFW3 key codes subset (Escape, Enter, Delete, Shift, Control). |

#### Canvas

| File | Type | Responsibility |
|------|------|----------------|
| `Vis2D/MapCanvas.cs` | `class MapCanvas : IResourceProvider` | Core 2D canvas. Owns the `MapCamera`, `Vis2DInputMap`, layer list, layer mask, and resource bag. Drives the Update/Draw/Input pipeline each frame. Right-drag-threshold logic prevents pan-release from firing context menus. |

#### Components

| File | Type | Responsibility |
|------|------|----------------|
| `Vis2D/Components/MapCamera.cs` | `class MapCamera : IMapCameraProvider` | Wraps Raylib `Camera2D`. Supports pan (right-drag), zoom-to-cursor (mouse wheel), optional exponential smoothing via `ZoomDamping`/`PanDamping`. Exposes `ProcessInput` for unit tests. |
| `Vis2D/Components/HierarchyComponents.cs` | `struct VisHierarchyNode`, `struct AggregateState`, `struct AggregateRoot` | ECS components for the ORGBAT tree. `VisHierarchyNode` forms a linked-list tree (Parent/FirstChild/NextSibling). `AggregateState` holds centroid and AABB of children. |
| `Vis2D/Components/MapDisplayComponent.cs` | `struct MapDisplayComponent` | Per-entity layer membership bitmask. Bit N set means the entity appears on layer N. |

#### Input

| File | Type | Responsibility |
|------|------|----------------|
| `Vis2D/Input/Vis2DInputMap.cs` | `class Vis2DInputMap` | Configurable button/key bindings: SelectButton (Left), PanButton (Right), MultiSelectMod (LeftShift), BoxSelectMod (LeftControl). |

#### Layers

| File | Type | Responsibility |
|------|------|----------------|
| `Vis2D/Layers/GridMapLayer.cs` | `sealed class GridMapLayer : IMapLayer` | Adaptive coordinate grid. Auto-scales spacing to keep at most 80 lines visible. Visibility controlled by a `Func<bool>` delegate. `LayerBitIndex = -1` (always on). |
| `Vis2D/Layers/DebugGizmoLayer.cs` | `class DebugGizmoLayer : IMapLayer` | Renders debug primitives from `DebugPrimitiveBuffer`. Delegates interaction to `GizmoMap.Presentation.DebugGizmoLayer`. Bridges FDP input events to the GizmoMap interaction API. |

#### Gizmos

| File | Type | Responsibility |
|------|------|----------------|
| `Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs` | `class DebugPrimitiveRenderer2D` | Thin wrapper over `GizmoMap.Presentation.DebugPrimitiveRenderer2D`. Iterates `DebugPrimitive` spans and delegates to the inner renderer. |
| `Vis2D/Gizmos/PointSequenceGizmo.cs` | `sealed class PointSequenceGizmo : IEntityStatefulGizmo` | Interactive path-collection gizmo. Left-click appends world points; right-click commits and calls `onFinish`; Escape cancels. Draws elastic line to cursor. |
| `Vis2D/Gizmos/EntityPickerGizmo.cs` | `sealed class EntityPickerGizmo : IEntityStatefulGizmo` | Single-entity pick gizmo. Draws a crosshair (amber = waiting, red = valid target). Calls `onPicked` on left-click release; `onCancelled` on right-click or Escape. |
| `Vis2D/Gizmos/FdpLocationPickerGizmo.cs` | `sealed class FdpLocationPickerGizmo : IEntityStatefulGizmo` | World-location pick gizmo. Draws a sky-blue crosshair. Calls `onPicked(Vector2)` on left-click release. |
| `Vis2D/Gizmos/RichTextRenderer.cs` | global type alias | `global using RichTextRenderer = GizmoMap.Presentation.RichTextRenderer;` type forward. |

#### Defaults

| File | Type | Responsibility |
|------|------|----------------|
| `Vis2D/Defaults/RaylibInputProvider.cs` | `class RaylibInputProvider : IInputProvider` | Raylib-backed implementation. Mouse captured = `ImGui.GetIO().WantCaptureMouse`. Keyboard captured = `WantCaptureKeyboard`. Button/key values cast directly from `MapMouseButton`/`MapKeyboardKey` to Raylib enums. |
| `Vis2D/Defaults/DefaultSelectionState.cs` | `class DefaultSelectionState : ISelectionState` | `HashSet<Entity>`-backed selection. Setting `PrimarySelected` clears the set and adds the single entity. Exposes `AddSelection` and `ClearSelection` for multi-select callers. |

#### Systems

| File | Type | Responsibility |
|------|------|----------------|
| `Vis2D/Systems/HierarchyOrderSystem.cs` | `class HierarchyOrderSystem : IEcsModuleSystem, IDisposable` | Maintains a flattened bottom-up `NativeArray<Entity>` of the hierarchy. Post-order traversal with cycle detection. Publishes `SortedHierarchyData` singleton. Dirty-flag optimization skips re-sort when nothing changed. |

---

### Subsystem C: ImGui Debug UI (`Fdp.Presentation`)

**Namespace root:** `Fdp.Presentation`

**Global using:** `global using Gui = ImGuiNET.ImGui;` (declared in `ImGui/GlobalUsings.cs`)

#### Abstractions

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/Abstractions/IInspectableSession.cs` | `interface IInspectableSession` | Uniform read/write interface over an ECS snapshot or live repository. Allows panels to be decoupled from both `EntityRepository` and `ISimulationView`. |
| `ImGui/Abstractions/IInspectorContext.cs` | `interface IInspectorContext`, `class InspectorState` | Selection/hover state shared between map and inspector. `InspectorState` is the default implementation. |
| `ImGui/Abstractions/IFileDialogService.cs` | `interface IFileDialogService` | Async file dialog abstraction: `ShowSaveAsDialogAsync`, `ShowOpenFileDialogAsync`. Returns `Task<string?>`. |
| `ImGui/Abstractions/IEntityContextMenuHandler.cs` | `interface IEntityContextMenuHandler`, `interface IContextMenuBuilder` | Entity right-click context menu protocol. Handlers populate menus via a fluent builder (AddItem, BeginSubmenu/EndSubmenu, AddSeparator). |
| `ImGui/Abstractions/IDerContextMenuHandler.cs` | `interface IDerContextMenuHandler` | Same protocol for DER (Dynamic Entity Repository) entities. |

#### Adapters

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/Adapters/RepositoryAdapter.cs` | `class RepositoryAdapter : IInspectableSession` | Wraps `EntityRepository` as an inspectable session. Exposes all live entities plus a synthetic `SingletonEntity` pseudo-handle for singleton components. Read/write capable. |
| `ImGui/Adapters/SimulationViewAdapter.cs` | `class SimulationViewAdapter : IInspectableSession` | Wraps `ISimulationView` (read-only). Uses cached reflected `MethodInfo` to dispatch `HasComponent<T>` / `GetComponentRO<T>` by runtime type. |

#### Window Manager

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/WindowManager/WindowManager.cs` | `class WindowManager` | Central orchestrator. Manages a `Dictionary<string, ManagedWindow>`, drives perspective switching, renders the global menu bar (from `GlobalMenuRegistry`), the status bar (`StatusBarManager`), and calls `window.Render()` for each open window. |
| `ImGui/WindowManager/ManagedWindow.cs` | `abstract class ManagedWindow` | Base class for all dockable/floating windows. Stable `###Id` ImGui suffix for dock identity. Supports `IsOpen`, `IsPinned`, `TitleBarColor`, `IsVolatile`, `ShowInMenu`. Subclasses override `DrawClientArea()`. |
| `ImGui/WindowManager/WindowScope.cs` | `enum WindowScope` | `PerspectiveBound` — visible only in owning perspective (or when pinned). `Global` — always visible. |
| `ImGui/WindowManager/StatusBarManager.cs` | `class StatusBarManager` | Sorts named sections by `SortOrder` and renders them left-to-right in a bottom-docked ImGui window. Sections can be perspective-filtered or global. |
| `ImGui/WindowManager/GlobalMenuRegistry.cs` | `class GlobalMenuRegistry`, `class MenuItemNode` | Trie-based registry for slash-separated menu paths (e.g. `"Tools/Radar/Show"`). Supports plain actions, checkable items, and separators. |
| `ImGui/WindowManager/MessageLogStatusBarSection.cs` | `sealed class MessageLogStatusBarSection` | Status-bar section with an alert icon. Turns red when `MessageLogWindow.HasUnobservedAttention` is true. Clicking focuses the log window. |

#### Icons

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/Icons/IconAtlas.cs` | `class IconAtlas : IDisposable` | GPU-framework-agnostic atlas. Parses string coordinates like `"b12"` (row='b'=1, col=12) into `(uv0, uv1)` pairs. Does not reference Raylib; caller supplies the texture handle as `IntPtr`. |
| `ImGui/Icons/IconWidgets.cs` | `static class IconWidgets` | Stateless immediate-mode icon widgets: `InlineIcon`, `AbsoluteIcon`, `IconButton`, `ToggleIcon`. Uses `InvisibleButton + ImDrawList` for zero-GC rendering. |
| `ImGui/Icons/EmbeddedAtlasResources.cs` | `static class EmbeddedAtlasResources` | Loads the FamFamFam Silk 16x16 icon atlas PNG from the assembly's embedded resources. Returns raw bytes for the caller to upload to the GPU. |
| `ImGui/Icons/TransportIconRenderer.cs` | `static class TransportIconRenderer`, `enum TransportShape` | Vector-drawn transport controls: Play, Pause, StepFwd, StepBack, Rewind, HistoryBack, HistoryFwd. Pixel-perfect with hover/press state. |

#### Renderers

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/Renderers/IImGuiRenderer.cs` | `interface IImGuiRenderer`, `interface IEntityAwareImGuiRenderer` | Custom visual renderer plugin protocol. `GetSummary` produces a compact inline string. `RenderValue` replaces the detail cell (return true) or falls through (return false). Entity-aware variant receives session and entity context. |
| `ImGui/Renderers/ImGuiRendererAttribute.cs` | `sealed class ImGuiRendererAttribute : Attribute` | `[ImGuiRenderer(typeof(T))]` with optional `onlyInsideType` for context-specific renderers. |
| `ImGui/Renderers/ImGuiRendererRegistry.cs` | `static class ImGuiRendererRegistry` | One-time `AppDomain` assembly scan with double-checked lock. `GetRenderer(targetType, contextType)` returns context match first, then global match. `Register` for manual test registration. |
| `ImGui/Renderers/BuiltinRenderers.cs` | Multiple `sealed class *Renderer` | Built-in renderers: `Vector2Renderer`, `Vector3Renderer`, `Vector4Renderer`, `QuaternionRenderer` (Euler angles in degrees with XYZW tooltip), `EntityRenderer` (falls through to enable drill-down). |
| `ImGui/Renderers/UnitRosterRenderer.cs` | `sealed class UnitRosterRenderer : IImGuiRenderer` | Renders `UnitRoster` (command hierarchy) as a three-column table: index, entity handle, tactical designation. |
| `ImGui/Renderers/SingletonRenderers.cs` | Additional renderer classes | Domain-specific singleton component renderers (details omitted; same pattern). |

#### Utils

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/Utils/ImGuiPropertyTree.cs` | `static class ImGuiPropertyTree` | Renders any CLR object as a two-column hierarchical table tree (Property / Value). Handles primitives, enums, vectors, structs, collections. Consults `ImGuiRendererRegistry`; returning `true` from `RenderValue` replaces the cell. Reports `doubleClickedPath` (JSON path). |
| `ImGui/Utils/ComponentReflector.cs` | `class ComponentReflector` | Draws all ECS components on an entity as collapsible `CollapsingHeader` sections. Supports `ForceExpandAll`/`ForceCollapseAll`, component filter, and double-click-to-edit (opens `ComponentEditWindow`). Wires up `CopyComponentJsonFunc` for context-menu copy. |
| `ImGui/Utils/ContextMenuBuilder.cs` | `sealed class ContextMenuBuilder : IContextMenuBuilder` | ImGui popup backend for the context menu builder protocol. Subclass `SubmenuBuilder` handles nested `BeginMenu/EndMenu` scopes. |
| `ImGui/Utils/EntityHeaderDrawer.cs` | `static class EntityHeaderDrawer` | Shared header row: entity index/generation, optional network ID in violet, Copy JSON button, READ-ONLY badge. |
| `ImGui/Utils/EntityJsonDumper.cs` | Static utility | Serializes all components of an entity to JSON for clipboard copy. |
| `ImGui/Utils/InspectorJsonUtils.cs` | Static utility | Builds component JSON with optional `ScenarioSerializer` integration for custom translator logic. |
| `ImGui/Utils/LambdaEntityContextMenuHandler.cs` | `sealed class LambdaEntityContextMenuHandler` | Wraps a delegate `Action<Entity, IContextMenuBuilder>` as `IEntityContextMenuHandler` for inline lambda registration. |
| `ImGui/Utils/LambdaDerContextMenuHandler.cs` | `sealed class LambdaDerContextMenuHandler` | Same pattern for DER entities. |
| `ImGui/Utils/ReplayBrowser/ImGuiEntityLink.cs` | Static utility | Renders a clickable entity-link button inside ImGui (fires `onClicked` callback). |

#### Editing

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/Editing/ComponentEditWindow.cs` | `sealed class ComponentEditWindow : ManagedWindow` | Volatile floating window for in-place component editing. Opened by `ComponentReflector` on double-click. Self-closes when the target entity is destroyed. Uses `StructEdit` for the edit session. |
| `ImGui/Editing/ComponentEditDrawer.cs` | `sealed class ComponentEditDrawer` | Recursive renderer for `IEditSession` document tree. Draws each `EditNode` as table rows with pickers for entity/location fields. |
| `ImGui/Editing/IImGuiFieldDrawer.cs` | `interface IImGuiFieldDrawer` | Plugin for custom edit widgets per CLR type. Returns `true` if the value changed. |
| `ImGui/Editing/TypeFieldEditor.cs` | `sealed class TypeFieldEditor : ICustomFieldEditor` | StructEdit custom field editor for `System.Type` fields. |
| `ImGui/Editing/PickerAttributes.cs` | `[MapPickableEntity]`, `[MapPickableWorldLocation]` | Markup attributes on struct fields. Instruct the edit drawer to render "Pick Entity" or "Pick Map" buttons linked to the canvas gizmos. |
| `ImGui/Editing/MathFieldEditors.cs` | Various `IImGuiFieldDrawer` | Float/int/Vector drag-input implementations. |
| `ImGui/Editing/QuaternionEulerFieldDrawer.cs` | `class QuaternionEulerFieldDrawer` | Edits quaternions as Euler angles (yaw/pitch/roll sliders). |
| `ImGui/Editing/GuidFieldDrawer.cs` | `class GuidFieldDrawer` | Edits `Guid` fields as text with format validation. |
| `ImGui/Editing/FixedStringFieldEditors.cs` | Various drawers | Edit widgets for FDP fixed-length string types. |
| `ImGui/Editing/PredicateValueFieldEditor.cs` | `class PredicateValueFieldEditor` | Dynamic editor for predicate values in the replay search UI. |
| `ImGui/Editing/BoundingBoxFieldEditor.cs` | `class BoundingBoxFieldEditor` | Two-point AABB editor used in the replay spatial search predicate. |
| `ImGui/Editing/ISpatialPickerContext.cs` | `interface ISpatialPickerContext` | Context for initiating map-based bounding box or location picks from the editor. |
| `ImGui/Editing/IComponentPickerContext.cs` | `interface IComponentPickerContext` | Context for initiating entity picks from the component editor. |

#### Panels

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/Panels/EntityInspectorPanel.cs` | `class EntityInspectorPanel` | Left-hand entity list with search filter + right-hand component tree. Multi-select (Shift/Ctrl+click). Context menus via `IEntityContextMenuHandler` registration. Optional `ChainToMap` to propagate selection to the canvas. |
| `ImGui/Panels/EntityWatchPanel.cs` | `class EntityWatchPanel` | Single-entity component tree for a fixed entity. Intended for volatile watch windows that pin one entity. |
| `ImGui/Panels/EventBrowserPanel.cs` | `class EventBrowserPanel` | Tabbed event log viewer. Per-type filter checkboxes, pause/resume, "current frame only" mode, causality jump. Fires `OnEntityLinkClicked` on entity handle clicks. |
| `ImGui/Panels/MessageLogPanel.cs` | `sealed class MessageLogPanel` | Multi-tab message log. Per-tab severity filters, logger-name filters, timestamp/logger columns, color-coded rows, auto-scroll, per-tab attention badge. |
| `ImGui/Panels/SystemProfilerPanel.cs` | `static class SystemProfilerPanel` | Stateless 4-column table: Module / Frequency / Failures / Status. Status indicator with colored circle. |
| `ImGui/Panels/ArchitectureDiagnosticsPanel.cs` | `sealed class ArchitectureDiagnosticsPanel` | Three collapsible sections (Modules, Systems, Translators) each with a sortable table. Reads `IArchitectureDiagnosticsService` snapshot. |
| `ImGui/Panels/DerEntityInspectorPanel.cs` | `sealed class DerEntityInspectorPanel` | Generic inspector for DER (Dynamic Entity Repository) entities. Left list + right descriptor tree. Context menus via `IDerContextMenuHandler`. |
| `ImGui/Panels/WinFormsFileDialogService.cs` | `sealed class WinFormsFileDialogService : IFileDialogService` | Windows-native file dialogs on a transient STA thread. Persists last-used directories to `%LOCALAPPDATA%/HROT/file_dialogs.json`. Non-Windows: returns null. |
| `ImGui/Panels/ImGuiFileDialogService.cs` | `sealed class ImGuiFileDialogService : IFileDialogService` | Pure-ImGui modal dialog (no native OS dialog). Resolves asynchronously. One-at-a-time; starting a second dialog cancels the previous. |
| `ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs` | `sealed class ReplayTimelinePanel` | Transport control bar: history back/forward, rewind, play/pause, speed combo. Frame info rows. Seek slider. File loader. JSON export expander. |
| `ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs` | `sealed class ReplaySearchPanel` | Multi-mode search (Component, Event, Lifecycle, Spatial, Structural, Compound, BehaviorParam). StructEdit session for predicate editing. Async search with cancellation. Preset clipboard load/save. |
| `ImGui/Panels/ReplayBrowser/ComponentDiffPanel.cs` | `sealed class ComponentDiffPanel` | Per-field change tree between consecutive frames. Epsilon filter, hide-unchanged toggle, type filter popup. Clickable entity-handle links. |
| `ImGui/Panels/ReplayBrowser/Drawers/PropertyPathFieldDrawer.cs` | Drawer | ECS property path picker for search predicates. |
| `ImGui/Panels/ReplayBrowser/Drawers/PredicateValueFieldDrawer.cs` | Drawer | Dynamically-typed value input for predicate comparisons. |
| `ImGui/Panels/ReplayBrowser/Drawers/FilteredTypeComboFieldDrawer.cs` | Drawer | Type combo box with search filter (component type selection). |
| `ImGui/Panels/ReplayBrowser/Drawers/BoundingBoxFieldDrawer.cs` | Drawer | 2D AABB input with optional map pick button. |
| `ImGui/Panels/ReplayBrowser/Drawers/BehaviorHashFieldDrawer.cs` | Drawer | Hash-to-name resolved behavior selector combo. |

#### Windows (ManagedWindow subclasses)

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/Windows/MessageLogWindow.cs` | `sealed class MessageLogWindow : ManagedWindow` | Global-scope window hosting `MessageLogPanel`. Exposes `HasUnobservedAttention` and `FocusFirstAttentionTab` for status bar integration. |
| `ImGui/Windows/ReplayBrowser/ReplayTimelineWindow.cs` | `sealed class ReplayTimelineWindow : ManagedWindow` | Perspective-bound window hosting `ReplayTimelinePanel`. |
| `ImGui/Windows/ReplayBrowser/ReplaySearchWindow.cs` | `sealed class ReplaySearchWindow : ManagedWindow` | Perspective-bound window hosting `ReplaySearchPanel`. |
| `ImGui/Windows/ReplayBrowser/FdpEventBrowserWindow.cs` | `sealed class FdpEventBrowserWindow : ManagedWindow` | Perspective-bound window hosting `EventBrowserPanel` in replay context. |
| `ImGui/Windows/ReplayBrowser/FdpEntityInspectorWindow.cs` | `sealed class FdpEntityInspectorWindow : ManagedWindow` | Perspective-bound window hosting `EntityInspectorPanel`. Factory delegates supply session and state on each frame. |
| `ImGui/Windows/ReplayBrowser/ComponentDiffWindow.cs` | `sealed class ComponentDiffWindow : ManagedWindow` | Perspective-bound window hosting `ComponentDiffPanel`. |

#### Other

| File | Type | Responsibility |
|------|------|----------------|
| `ImGui/IWindowRegistrar.cs` | `interface IWindowRegistrar` | Implemented by subsystems that want to register windows/menus/status-bar sections. Called by `SubsystemOrchestrator` after `Initialize`, omitted in headless mode. |
| `ImGui/GlobalUsings.cs` | global using | `global using Gui = ImGuiNET.ImGui;` applied to the entire project. |

---

## Public API Reference

### Raylib Host

```csharp
// Abstract base — subclass and override lifecycle methods.
public abstract class FdpApplication : IDisposable
{
    // Construction
    public FdpApplication(ApplicationConfig config);

    // Entry point — call from Main()
    public void Run();

    // Request shutdown at end of current frame
    public void Quit();

    // Protected ECS access — initialize in OnLoad()
    protected EntityRepository World { get; set; }
    protected ModuleHostKernel Kernel { get; set; }

    // Lifecycle hooks to override
    protected abstract void OnLoad();
    protected virtual  void OnUpdate(float dt);       // default: Kernel?.Update()
    protected abstract void OnDrawWorld();             // Raylib drawing pass
    protected abstract void OnDrawUI();                // ImGui pass
    protected virtual  void OnUnload();                // default: Kernel?.Dispose(), World?.Dispose()
}

public struct ApplicationConfig
{
    public string      WindowTitle       { get; set; }   // default "FDP Application"
    public int         Width             { get; set; }   // default 1280
    public int         Height            { get; set; }   // default 720
    public int         TargetFPS         { get; set; }   // default 60
    public ConfigFlags Flags             { get; set; }   // ResizableWindow | Msaa4xHint
    public bool        PersistenceEnabled{ get; set; }   // default true
}
```

### Map Canvas

```csharp
public class MapCanvas : IResourceProvider
{
    // Configuration
    public MapCamera      Camera         { get; set; }
    public Vis2DInputMap  InputMap       { get; set; }
    public uint           ActiveLayerMask{ get; set; }   // 32-bit layer visibility bitmask
    public IInputProvider Input          { get; }
    public IDebugDrawBuilder? DrawBuffer { get; set; }   // debug gizmo injection
    public bool           KeyboardConsumedByTool { get; } // gate host keyboard handling

    // Layer management
    public void              AddLayer(IMapLayer layer);
    public void              RemoveLayer(IMapLayer layer);
    public IReadOnlyList<IMapLayer> Layers { get; }

    // Per-frame
    public void Update(float dt);
    public void Draw();

    // Pick
    public Entity? PickTopmostEntity(Vector2 worldPos);

    // Resource bag
    public void AddResource<T>(T resource) where T : class;
    public T?   Get<T>() where T : class;
    public bool Has<T>() where T : class;
}
```

### MapCamera

```csharp
public class MapCamera : IMapCameraProvider
{
    public Camera2D InnerCamera;
    public float  Zoom             { get; set; }
    public Vector2 Target          { get; set; }
    public Vector2 Offset          { get; set; }
    public float  ZoomSpeed        { get; set; }   // default 0.1
    public float  MinZoom          { get; set; }   // default 0.1
    public float  MaxZoom          { get; set; }   // default 10.0
    public bool   EnableSmoothing  { get; set; }   // default false
    public float  ZoomDamping      { get; set; }   // default 15.0
    public float  PanDamping       { get; set; }   // default 20.0

    public virtual void Update(float dt);
    public virtual bool HandleInput(IInputProvider input);
    public bool ProcessInput(float wheelMove, Vector2 mousePos, bool isPanDown, bool isInputCaptured);

    // Coordinate conversion
    public Vector2 ScreenToWorld(Vector2 screenPos);
    public Vector2 WorldToScreen(Vector2 worldPos);
    public void BeginMode();
    public void EndMode();
}
```

### IMapLayer

```csharp
public interface IMapLayer
{
    string Name         { get; }
    int    LayerBitIndex{ get; }   // -1 = always on; 0-31 = bitmask position

    void    Update(float dt);
    void    Draw(RenderContext ctx);
    bool    HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed);
    Entity? PickEntity(Vector2 worldPos);

    // Default implementations (no-op / false):
    void HandleHover(Vector2 mouseWorldPos) { }
    bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;
    bool HandleKeyInput(MapKeyboardKey key) => false;
}
```

### WindowManager

```csharp
public class WindowManager
{
    public WindowManager(IconAtlas atlas);
    public IconAtlas Atlas { get; }

    // Window registration
    public void    RegisterWindow(ManagedWindow window);
    public bool    TryGetWindow(string id, out ManagedWindow? window);

    // Programmatic show/hide/focus
    public void ShowWindow(string id);
    public void HideWindow(string id);
    public void SetWindowPinned(string id, bool isPinned);
    public void FocusWindow(string id);

    // Perspectives
    public string CurrentPerspective { get; }
    public void   SwitchPerspective(string perspective);

    // Sub-services
    public GlobalMenuRegistry MenuRegistry { get; }
    public StatusBarManager   StatusBar    { get; }

    // Per-frame
    public void Render(IFileDialogService? fileDialogService = null);
}
```

### ManagedWindow

```csharp
public abstract class ManagedWindow
{
    protected ManagedWindow(string id, string title,
                            string owningPerspective, WindowScope scope);

    public string      Id                { get; }
    public string      Title             { get; protected set; }
    public string      OwningPerspective { get; }
    public WindowScope Scope             { get; }
    public bool        IsOpen            { get; set; }
    public bool        IsPinned          { get; set; }
    public Vector4?    TitleBarColor     { get; set; }
    public bool        IsVolatile        { get; protected set; }
    public bool        ShowInMenu        { get; protected set; }

    public  void RequestFocus();
    public  void Render(WindowManager wm, string currentPerspective);
    protected abstract void DrawClientArea();
}
```

### ImGuiRendererRegistry

```csharp
public static class ImGuiRendererRegistry
{
    public static IImGuiRenderer? GetRenderer(Type targetType, Type? contextType = null);
    public static void Register(Type targetType, IImGuiRenderer renderer, Type? contextType = null);
}

public interface IImGuiRenderer
{
    string? GetSummary(object value);
    bool    RenderValue(object value);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ImGuiRendererAttribute : Attribute
{
    public Type  TargetType     { get; }
    public Type? OnlyInsideType { get; }
    public ImGuiRendererAttribute(Type targetType, Type? onlyInsideType = null);
}
```

### IconAtlas / EmbeddedAtlasResources

```csharp
public class IconAtlas : IDisposable
{
    public IconAtlas(IntPtr textureId, float atlasWidth, float atlasHeight, float iconSize = 16f);
    public IntPtr   TextureId   { get; }
    public Vector2  IconSizeVec { get; }
    public (Vector2 uv0, Vector2 uv1) GetUvCoordinates(string coordinate);
    // e.g. "b12" => row 'b' (index 1), column 12 (1-based)
}

public static class EmbeddedAtlasResources
{
    // Returns PNG bytes of the embedded FamFamFam Silk atlas.
    public static byte[] GetSilkAtlasPngBytes();
}
```

### Picker Attributes (Component Edit)

```csharp
// Tag a struct field to get a "Pick Entity" button in the component editor.
[MapPickableEntity("filter_preset1", "filter_preset2")]
public Entity TargetUnit;

// Tag a struct field to get a "Pick Location" button in the component editor.
[MapPickableWorldLocation]
public Vector2 SpawnPoint;
```

---

## Dependencies

### Project References

| Referenced Project | Role |
|---|---|
| `ExtDeps/GizmoMap/GizmoMap.Presentation` | Debug primitive rendering, gizmo interaction, RichTextRenderer |
| `Toolkits/Fdp.Toolkits` | Diagnostics (DebugPrimitiveBuffer, FdpEventBus), ReplayBrowser, Behavior, Serialization, Scenario |
| `ExtDeps/StructEdit/src/StructEdit.Core` | Edit session and document model for component editing |
| `ExtDeps/StructEdit/src/StructEdit.Reflection` | Reflection-based binding for StructEdit |

Implicit transitive dependencies (via `Fdp.Toolkits`):
- `Fdp.Core` (ECS kernel, `EntityRepository`, `ISimulationView`)
- `Fdp.ModuleHost` (module lifecycle, `ModuleHostKernel`, `IEcsModuleSystem`)

### NuGet Package References

| Package | Version | Usage |
|---|---|---|
| `Raylib-cs` | 7.0.2 | 2D/3D rendering, window management, input polling, `Camera2D` |
| `rlImGui-cs` | 3.2.0 | ImGui integration layer for Raylib (`rlImGui.Setup`, `rlImGui.Begin/End`) |
| `ImGui.NET` | 1.91.6.1 | ImGui .NET bindings (`ImGuiNET.ImGui`, docking, draw lists) |

### Embedded Resources

| Resource | Logical Name | Usage |
|---|---|---|
| `FDP/Data/Icons/famfamfam-silk.png` | `FDP.Toolkit_ImGui.Icons.famfamfam-silk.png` | 16x16 icon atlas loaded via `EmbeddedAtlasResources.GetSilkAtlasPngBytes()` |

### InternalsVisibleTo

- `FDP.Toolkit_ImGui.Tests`
- `Fdp.Presentation.Tests`

---

## Usage Examples

### Example 1: Minimal Raylib Application

```csharp
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Presentation.Raylib;
using Fdp.Presentation.WindowManager;
using Fdp.Presentation.Icons;
using Raylib_cs;

class MyApp : FdpApplication
{
    private WindowManager _wm = null!;
    private MapCanvas _canvas = null!;

    public MyApp() : base(new ApplicationConfig
    {
        WindowTitle = "My FDP App",
        Width = 1600, Height = 900, TargetFPS = 60
    }) { }

    protected override void OnLoad()
    {
        World  = new EntityRepository();
        Kernel = new ModuleHostKernel(World);

        // Register ECS modules
        Kernel.AddModule<MySimulationModule>();
        Kernel.Initialize();

        // Build 2D canvas
        _canvas = new MapCanvas();
        _canvas.AddLayer(new GridMapLayer(() => true));
        _canvas.AddLayer(new MyEntityLayer(World));

        // Build window manager
        byte[] iconPng = EmbeddedAtlasResources.GetSilkAtlasPngBytes();
        var img = Raylib.LoadImageFromMemory(".png", iconPng);
        var tex = Raylib.LoadTextureFromImage(img);
        var atlas = new IconAtlas((IntPtr)tex.Id, tex.Width, tex.Height);
        _wm = new WindowManager(atlas);

        var inspector = new EntityInspectorPanel();
        _wm.RegisterWindow(new MyInspectorWindow(inspector));
    }

    protected override void OnDrawWorld() => _canvas.Draw();

    protected override void OnDrawUI()   => _wm.Render();

    protected override void OnUpdate(float dt)
    {
        Kernel.Update();
        _canvas.Update(dt);
    }
}

// Entry point
new MyApp().Run();
```

### Example 2: Custom Map Layer

```csharp
using Fdp.Core;
using Fdp.Presentation.Vis2D.Abstractions;
using Raylib_cs;

public class UnitLayer : IMapLayer
{
    private readonly EntityRepository _repo;

    public string Name         => "Units";
    public int    LayerBitIndex => 0;   // toggleable via ActiveLayerMask bit 0

    public UnitLayer(EntityRepository repo) => _repo = repo;

    public void Update(float dt) { }

    public void Draw(RenderContext ctx)
    {
        // Query and draw all entities with a position component
        foreach (var entity in _repo.Query().With<WorldPosition>().Build())
        {
            var pos = _repo.GetComponentRO<WorldPosition>(entity);
            Raylib.DrawCircleV(new System.Numerics.Vector2(pos.X, pos.Y),
                               5f / ctx.Zoom, Color.Green);
        }
    }

    public bool HandleInput(System.Numerics.Vector2 worldPos,
                            MapMouseButton button, bool isPressed)
    {
        if (button == MapMouseButton.Left && isPressed)
        {
            var selection = ctx.Resources.Get<ISelectionState>();
            var entity = PickEntity(worldPos);
            if (entity.HasValue && selection != null)
                selection.PrimarySelected = entity;
            return entity.HasValue; // consume click if we hit something
        }
        return false;
    }

    public Entity? PickEntity(System.Numerics.Vector2 worldPos)
    {
        const float PickRadius = 10f;
        foreach (var entity in _repo.Query().With<WorldPosition>().Build())
        {
            var pos = _repo.GetComponentRO<WorldPosition>(entity);
            var delta = new System.Numerics.Vector2(pos.X - worldPos.X,
                                                    pos.Y - worldPos.Y);
            if (delta.LengthSquared() <= PickRadius * PickRadius)
                return entity;
        }
        return null;
    }
}
```

### Example 3: Custom Component Renderer

```csharp
using Fdp.Core;
using Fdp.Presentation.Renderers;
using ImGuiApi = ImGuiNET.ImGui;

// Auto-discovered by ImGuiRendererRegistry at startup.
[ImGuiRenderer(typeof(HealthPoints))]
public sealed class HealthPointsRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var hp = (HealthPoints)value;
        return $"{hp.Current} / {hp.Maximum}";
    }

    public bool RenderValue(object value)
    {
        var hp = (HealthPoints)value;
        float fraction = hp.Maximum > 0 ? (float)hp.Current / hp.Maximum : 0f;

        // Draw a colored progress bar
        var color = fraction > 0.5f
            ? new System.Numerics.Vector4(0, 1, 0, 1)   // green
            : new System.Numerics.Vector4(1, 0, 0, 1);  // red

        ImGuiApi.TextColored(color, $"{hp.Current} / {hp.Maximum}");
        ImGuiApi.SameLine();
        ImGuiApi.ProgressBar(fraction, new System.Numerics.Vector2(-1, 0));
        return true; // we handled the cell
    }
}
```

### Example 4: Window Manager with Perspectives

```csharp
// Register windows for two perspectives: "Live" and "Replay"
_wm.RegisterWindow(new EntityInspectorWindow("entity_inspector",
    "Entity Inspector", "Live", inspectorPanel));

_wm.RegisterWindow(new ReplayTimelineWindow("rb_timeline",
    "Timeline", "Replay", timelinePanel,
    new System.Numerics.Vector4(0.2f, 0.1f, 0.3f, 1f)));

// Switch between perspectives
_wm.SwitchPerspective("Replay");   // hides "Live" windows, shows "Replay" windows
_wm.SwitchPerspective("Live");     // reverses

// Pin a window across perspectives
_wm.SetWindowPinned("entity_inspector", true);
```

### Example 5: Entity Picker Gizmo from Component Editor

```csharp
// Instruct the component editor to show a "Pick Entity" button
// for fields marked with [MapPickableEntity].
public struct AttackOrder
{
    [MapPickableEntity("units")]   // "units" filter preset
    public Entity TargetEntity;

    [MapPickableWorldLocation]
    public System.Numerics.Vector2 RallyPoint;
}

// Wire the picker context to the inspector at startup:
inspectorPanel.Reflector.EditPickerContext = new MyPickerContext(_canvas);

// MyPickerContext.BeginEntityPick() registers an EntityPickerGizmo with
// GlobalGizmoManager, which then forwards canvas events to the gizmo.
```

### Example 6: Lambda Context Menu

```csharp
// Register a context menu handler without a full class:
inspectorPanel.RegisterContextMenuHandler(
    new LambdaEntityContextMenuHandler((entity, builder) =>
    {
        builder.AddItem("Center Camera", () => _canvas.Camera.Target =
            GetEntityPosition(entity));

        var sub = builder.BeginSubmenu("Debug");
        sub.AddItem("Log Components", () => LogComponents(entity));
        sub.AddItem("Destroy Entity", () => _repo.DestroyEntity(entity),
            enabled: !_session.IsReadOnly);
        sub.EndSubmenu();
    }));
```

---

## Best Practices

### Layer Development

- **Return `true` from `HandleInput`** only when the layer actually consumed the input.
  Returning `true` from higher layers prevents lower layers (and camera panning) from
  processing the same event.
- **Use `LayerBitIndex = -1`** for always-on background layers (grid, background image).
  Use values 0–31 for user-toggleable layers.
- **Keep `Draw` allocation-free** on the hot path. Cache queries, avoid LINQ, and
  use `ReadOnlySpan<T>` from `DebugPrimitiveBuffer.GetFrame()`.

### ImGui Renderers

- **Annotate with `[ImGuiRenderer(typeof(T))]`** and provide a public parameterless
  constructor. The registry discovery is fully automatic.
- **Return `false` from `RenderValue`** when you only want to customize the summary
  string but still want the default tree expansion behavior for child fields.
- **Use `IEntityAwareImGuiRenderer`** when your renderer needs to read sibling ECS
  components (e.g., resolving an entity handle to a name by reading `EntityInfo`).

### Window Management

- **Use `IsVolatile = true`** for dynamically-spawned watch windows that should
  disappear when closed, not linger in the registry.
- **Use `ShowInMenu = false`** for windows that are opened programmatically on demand
  and should not appear in the persistent Windows menu.
- **Use `WindowScope.Global`** for truly cross-cutting tools like the message log.
  Use `WindowScope.PerspectiveBound` for all other panels.

### Component Editing

- **Annotate picakble fields** with `[MapPickableEntity]` or `[MapPickableWorldLocation]`
  to automatically get picker buttons — no extra code in the panel or reflector is
  needed.
- **Wire `EditSessionGetter`** on `ComponentReflector` before opening edit windows.
  The `ComponentEditWindow` uses this to detect entity destruction and self-close.

### Performance

- **`HierarchyOrderSystem` dirty flag**: Only call `MarkDirty()` when hierarchy
  structure actually changes. Do not call it on every frame.
- **`IEntityFilter` lifecycle**: Compile filters via `IEntityFilterFactory.CreateFilter`
  once per pick session, not once per frame. The `IsMatch` hot path must be O(1).
- **`ImGuiPropertyTree` member cache**: The `_memberCache` is process-wide.
  Push a unique `ImGui.PushID` scope before each `Render` call to avoid table
  state collisions when multiple components are expanded simultaneously.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Core` | ECS kernel consumed by both the canvas (entity queries, components) and the inspector (RepositoryAdapter wraps EntityRepository). |
| `Fdp.ModuleHost` | Module scheduling and lifecycle. `FdpApplication.OnUpdate` calls `Kernel.Update()`. `HierarchyOrderSystem` implements `IEcsModuleSystem`. |
| `Fdp.Presentation.Tests` | Unit tests for `MapCamera.ProcessInput`, `HierarchyOrderSystem`, `ImGuiRendererRegistry`, `ComponentEditWindow` logic, and window management. |
| `GizmoMap.Presentation` | External dependency providing `DebugGizmoLayer`, `DebugPrimitiveRenderer2D`, `RichTextRenderer`, and the GizmoMap interaction protocol. |
| `StructEdit.Core` / `StructEdit.Reflection` | External dependency providing the edit session model for in-place component editing. |
| `Fdp.Toolkits` | Diagnostics (gizmos, event bus), ReplayBrowser toolkit, behavior registry, serialization, scenario serialization. All consumed by the panels. |
| `Hrot.IG` / `Hrot.Editor` | Host applications that subclass `FdpApplication`, register layers, register windows via `IWindowRegistrar`, and compose the full UI. |
| `Fdp.Examples.*` | Example applications demonstrating `FdpApplication`, `MapCanvas`, and panel usage patterns. |
