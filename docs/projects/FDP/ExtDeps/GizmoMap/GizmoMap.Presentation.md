# GizmoMap.Presentation

| Field     | Value                                                                           |
|-----------|---------------------------------------------------------------------------------|
| Project   | GizmoMap.Presentation                                                           |
| Path      | `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/GizmoMap.Presentation.csproj`      |
| Namespace | `GizmoMap.Presentation` / `GizmoMap.Presentation.Shapes`                       |
| Target    | `net8.0`                                                                        |
| Date      | 2026-05-23                                                                      |

---

## README Validation

**Status: Missing** -- No `README.md` exists in `GizmoMap.Presentation/` or the GizmoMap
root folder. All findings are derived directly from source code and inline comments.

---

## Executive Overview

`GizmoMap.Presentation` is the rendering and user-interaction layer of the GizmoMap
subsystem. It receives a `GizmoPrimitiveBuffer` (from either a local simulation or a DDS
subscriber) and:

1. Renders all visible primitives onto a Raylib 2D canvas using `DebugPrimitiveRenderer2D`.
2. Performs hit-testing on interactive `Box2D` primitives and routes mouse/keyboard events
   to an interaction callback via `DebugGizmoLayer`.
3. Draws JSON-defined context menus and the aggregated main menu bar via ImGui.
4. Schedules `StructInspector` property panels for StructEdit-backed primitives via
   `ImGuiPropertyTreeAdapter`.

`GizmoViewerFrontend` wraps the Raylib+ImGui application loop so that both
`GizmoMap.Viewer` and `GizmoMap.Example` can share the same frame-driven rendering
scaffold without code duplication.

The project has no dependency on ECS, DDS internals, or simulation-specific types. Its
only dependencies are Raylib-cs, rlImgui-cs, ImGui.NET, and the StructEdit libraries.

---

## Architecture

### Component Hierarchy

```
+------------------------------------------------------------------+
|  GizmoViewerFrontend (static class)                              |
|  Raylib window + ImGui application loop scaffold                 |
|                                                                  |
|  +----------------------------+  +----------------------------+  |
|  | DebugGizmoLayer            |  | ImGuiPropertyTreeAdapter   |  |
|  | (input routing + render    |  | (StructInspector panels)   |  |
|  |  orchestration)            |  +----------------------------+  |
|  |                            |                                  |
|  | +------------------------+ |  +----------------------------+  |
|  | | DebugPrimitiveRenderer2D| |  | ContextMenuAdapter         |  |
|  | | (2D shape dispatch)    | |  | (JSON -> ImGui popup)      |  |
|  | +----+-------------------+ |  +----------------------------+  |
|  |      |                     |  +----------------------------+  |
|  |      |  shape dispatch     |  | MainMenuAdapter            |  |
|  |      v                     |  | (binding merger + sorter)  |  |
|  | +----+-------------------+ |  +----------------------------+  |
|  | | MilStd2525Renderer     | |                                  |
|  | | SemanticShapeRenderer  | |  +----------------------------+  |
|  | | RichTextRenderer       | |  | GizmoInteractionProxyTool  |  |
|  | | IconAtlasAdapter       | |  | (drag state machine)       |  |
|  | | PerspectiveShapeRenderer| |  +----------------------------+  |
|  | +------------------------+ |                                  |
|  +----------------------------+  +----------------------------+  |
|                                  | GizmoUndoStack             |  |
|                                  | (generic undo records)     |  |
|                                  +----------------------------+  |
+------------------------------------------------------------------+
```

### Two-Pass Rendering Algorithm

The renderer must resolve `CoordinateSpace.EntityLocal` primitives (emitted relative to a
moving entity) into absolute world coordinates before drawing. This is done via a
`SpatialAnchor` companion primitive that carries the entity's world position and
orientation. The algorithm:

```
+---------------------------------------------------+
|  DebugPrimitiveRenderer2D.Render(span, camera)    |
|                                                   |
|  Pass 1: Sweep entire span                        |
|  +-- SpatialAnchor?  --> store in anchors[]       |
|  +-- LayerControlMask? --> update activeLayers    |
|                                                   |
|  Pass 2: Sweep entire span again                  |
|  for each primitive:                              |
|    - Skip meta/control shapes                     |
|    - Filter: TargetView must include Map2D        |
|    - Filter: activeLayers.IsSet(DebugLayer)       |
|    - Filter: LOD zoom culling                     |
|    - if Space == EntityLocal:                     |
|        Look up anchors[AnchorIndex]               |
|        Transform local coords -> world coords     |
|        Set resolved.Space = World                 |
|    - Append to sortBuffer                         |
|                                                   |
|  Sort sortBuffer by (DebugLayer, ZIndex)          |
|  For each: DispatchShape(primitive, camera, zoom) |
+---------------------------------------------------+
```

---

## Source Structure

```
GizmoMap.Presentation/
+-- GizmoMap.Presentation.csproj
+-- GizmoViewerFrontend.cs          Raylib+ImGui application loop scaffold
+-- GizmoUndoStack.cs               Generic undo stack (no ECS)
|
+-- Gizmos/
|   +-- GizmoInteractionProxyTool.cs Drag state machine (Started->DragUpdate->Commit/Cancel)
|
+-- Layers/
|   +-- DebugGizmoLayer.cs          Orchestrates render + input for a primitive span
|
+-- Rendering/
|   +-- DebugPrimitiveRenderer2D.cs Two-pass sort-and-dispatch renderer
|   +-- MilStd2525Renderer.cs       NATO SIDC symbol stub renderer
|   +-- PresentationMath.cs         Camera/world conversion helpers
|   +-- RichTextRenderer.cs         Inline rich-text tag parser and renderer
|   +-- SemanticShapeRenderer.cs    DIS-profile silhouette renderer
|
+-- Shapes/
|   +-- DefaultEntityShapeLibrary.cs Built-in shape profiles (fallback)
|   +-- EntityShapeCondition.cs      Condition flags (Damaged, Firing, ...)
|   +-- EntityShapeProfile.cs        Named polyline-based shape descriptor
|   +-- IEntityShapeLibrary.cs       Shape profile lookup contract
|   +-- PerspectiveShapeRenderer.cs  Polyline renderer with depth exaggeration
|   +-- PolylineDefinition.cs        Vertex list + display conditions
|
+-- UI/
    +-- ContextMenuAdapter.cs        JSON-defined context menu via ImGui popups
    +-- GizmoSchemaRegistry.cs       schemaHash -> EditDocument registry
    +-- IconAtlasAdapter.cs          Atlas-coord icon renderer (stub/real)
    +-- ImGuiMenuRenderer.cs         Recursive ImGui main-menu renderer
    +-- ImGuiPropertyTreeAdapter.cs  StructInspector panel scheduler + renderer
    +-- MainMenuAdapter.cs           MainMenuBinding merger and priority sorter
```

---

## Public API Reference

### `GizmoViewerFrontend` (static class)

The primary entry point for embedding the GizmoMap visualization loop into an application.

```csharp
public static void Run(
    string windowTitle,
    GizmoPrimitiveBuffer renderBuffer,
    GizmoSchemaRegistry schemaRegistry,
    Action<float> onUpdateTick,
    Action<GizmoPickToken, GizmoInteractionEventKind, Vector3, int, byte, string?> onInteraction,
    Action<GizmoPickToken, int> onMenuAction,
    Action? onCustomInput = null,
    ImGuiPropertyTreeAdapter? externalAdapter = null)
```

**Callback signatures:**

| Parameter         | Signature                                                             | Description                             |
|-------------------|-----------------------------------------------------------------------|-----------------------------------------|
| `onUpdateTick`    | `Action<float dt>`                                                    | Called each frame before rendering      |
| `onInteraction`   | `Action<token, kind, pos, actionId, stateFlags, payloadJson>`         | All interaction events                  |
| `onMenuAction`    | `Action<token, actionId>`                                             | Context menu item clicked               |
| `onCustomInput`   | `Action?`                                                             | Additional Raylib key polling each frame |
| `externalAdapter` | `ImGuiPropertyTreeAdapter?`                                           | Override the default property adapter   |

`Run` initializes a 640x480 Raylib window, sets 30 FPS, and enters the loop. Per frame:
1. Calls `onUpdateTick` (caller fills `renderBuffer`).
2. Calls `DebugGizmoLayer.HandleInput` for mouse/keyboard routing.
3. Calls `onCustomInput` for additional key bindings.
4. Raylib 2D draw: `DebugGizmoLayer.Render`.
5. `ExtractMetaPrimitives` collects `MainMenuBinding` primitives.
6. ImGui frame: main menu bar, context menus, property panels.

---

### `DebugGizmoLayer` (sealed class)

Stateful orchestrator for a single `GizmoPrimitiveBuffer`. Owns the active drag tool,
the context menu state, and the main menu aggregator.

| Member                                                                                 | Description                                    |
|----------------------------------------------------------------------------------------|------------------------------------------------|
| `DebugGizmoLayer(DebugPrimitiveRenderer2D)`                                            | Constructor                                    |
| `void Render(ReadOnlySpan<DebugPrimitive>, Camera2D, float zoom)`                      | Delegate to renderer                           |
| `void ExtractMetaPrimitives(ReadOnlySpan<DebugPrimitive>, StringInternMap)`            | Parse `MainMenuBinding` / `ContextMenuBinding` |
| `void HandleInput(span, internMap, Camera2D, Action<token,kind,pos,actionId,flags>?)`  | Mouse/keyboard routing                         |
| `void DrawMainMenu(Action<int>?)`                                                      | Render aggregated main menu bar (ImGui)        |
| `void DrawContextMenu(Action<GizmoPickToken, int>?)`                                   | Render pending context menu popup (ImGui)      |

**Input routing logic (HandleInput):**

1. If an `InputCaptureBinding` with exclusive mode is present in the frame, all raw HW
   events (mouse press, key press) are forwarded directly to `onInteraction` with
   `GizmoInteractionEventKind.RawInput` and the capturing token. Normal spatial
   hit-testing is suppressed.
2. On left mouse press: iterate the primitive span in reverse to find the topmost
   interactive `Box2D` (non-zero `BoxAnchorId`) and create a `GizmoInteractionProxyTool`.
3. The active proxy tool handles `DragUpdate`, `Commit`, and `Cancel` until released.
4. On right mouse press+release (without drag): detect `ContextMenuBinding` for the
   hit entity and schedule a context menu popup.
5. ImGui cursor-capture state is respected: when `ImGui.GetIO().WantCaptureMouse` is true,
   spatial hit-testing is suppressed.

---

### `DebugPrimitiveRenderer2D` (class)

| Member                                                              | Description                                     |
|---------------------------------------------------------------------|-------------------------------------------------|
| `DebugPrimitiveRenderer2D(IEntityShapeLibrary?, ImGuiPropertyTreeAdapter?)` | Constructor            |
| `void Render(ReadOnlySpan<DebugPrimitive>, Camera2D, float zoom)`  | Two-pass render (see above)                     |
| `virtual void DispatchShape(in DebugPrimitive, Camera2D, float zoom)` | Override in tests to capture dispatched shapes |

`DispatchShape` is a virtual dispatch point. Each `DebugPrimitiveShape` value maps to a
specific Raylib or helper call:

| Shape              | Renderer call                                               |
|--------------------|-------------------------------------------------------------|
| `Line`             | `Raylib.DrawLineEx` or `DrawLineBezier` for dashed/dotted   |
| `Sphere`           | `Raylib.DrawCircleLines` / `DrawCircle` (with fill)         |
| `Box2D`            | `Raylib.DrawRectanglePro` with rotation                     |
| `Arrow`            | `Raylib.DrawLineEx` + filled triangle arrowhead             |
| `Text`             | `Raylib.DrawText` or RichTextRenderer                       |
| `EntityBadge`      | `RichTextRenderer` at entity position                       |
| `Icon`             | `IconAtlasAdapter.Draw`                                     |
| `MilStd2525`       | `MilStd2525Renderer.Draw`                                   |
| `SemanticShape`    | `SemanticShapeRenderer.Draw` or `PerspectiveShapeRenderer`  |
| `StructInspector`  | `ImGuiPropertyTreeAdapter.Schedule` (deferred to ImGui pass)|

---

### `GizmoInteractionProxyTool` (sealed class)

Short-lived tool created when the user left-clicks an interactive primitive.

| Member                          | Description                                          |
|---------------------------------|------------------------------------------------------|
| Constructor                     | Fires `Started` event immediately                    |
| `bool HandlePress(pos, button)` | Activates drag on left button                        |
| `bool HandleDrag(pos, delta)`   | Fires `DragUpdate` events while dragging             |
| `bool HandleClick(pos, button)` | Fires `Commit` on left release, `Cancel` on right    |
| `bool HandleKey(key)`           | Fires `Cancel` on Escape                             |

---

### UI Components

#### `ContextMenuAdapter` (sealed class)

Schedule + render pattern for JSON-defined context menus via ImGui.

| Member                             | Description                                 |
|------------------------------------|---------------------------------------------|
| `void Schedule(long anchorId, string menuJson)` | Record a right-click; stores pending state |
| `void DrawScheduled(Action<long, int>?)`        | Must be called inside rlImGui Begin/End     |

Menu JSON format:
```json
[
  { "id": 1, "label": "Move", "shortcut": "M" },
  { "separator": true },
  { "label": "Orders", "children": [
    { "id": 10, "label": "Attack" },
    { "id": 11, "label": "Defend" }
  ]},
  { "id": 99, "label": "Delete", "style": "destructive" }
]
```

---

#### `MainMenuAdapter` (sealed class)

Aggregates `MainMenuBinding` primitives from multiple backend gizmos and merges them
into a unified menu tree sorted by priority.

| Member                          | Description                                           |
|---------------------------------|-------------------------------------------------------|
| `void Schedule(string menuJson)`| Parse and merge a JSON menu array                     |
| `IReadOnlyList<ContextMenuItemDto> ConsumeItems()` | Return sorted items; clears state  |

Merging rules: items sharing the same top-level `Label` have their `Children` arrays
combined; the minimum `Priority` value is kept.

---

#### `GizmoSchemaRegistry` (sealed class)

Maps FNV-1a schema hash to a StructEdit `EditDocument`.

| Member                                 | Description                               |
|----------------------------------------|-------------------------------------------|
| `void Register(uint schemaHash, EditDocument)` | Register a document for a schema hash |
| `bool TryGet(uint schemaHash, out EditDocument?)` | Look up a document              |

---

#### `ImGuiPropertyTreeAdapter` (sealed class)

Schedules and renders `StructInspector` panels. Two-pass design: `Schedule` is called from
the Raylib draw pass; `DrawScheduled` is called from the ImGui pass.

| Member                                                                          | Description                              |
|---------------------------------------------------------------------------------|------------------------------------------|
| `void Schedule(long networkId, uint schemaHash, uint gizmoTypeId, ScreenAnchor, float, float, SizeMode, bool)` | Queue a panel for this frame |
| `void Schedule(long, uint, uint, float, float, bool)` (legacy)                  | Legacy overload (TopLeft, ScreenPixels) |
| `void DrawScheduled(Action<long, uint, string>? onStructUpdate)`                | Render all pending panels via ImGui      |

Window titles use the ImGui `###` stable-ID syntax so the visible title can change without
losing window position/size state.

---

### Shape Library

#### `IEntityShapeLibrary` (interface)

| Method                                                          | Description                              |
|-----------------------------------------------------------------|------------------------------------------|
| `EntityShapeProfile GetShape(string? shapeName, ulong fallbackDisType)` | Profile lookup with DIS fallback |

#### `EntityShapeProfile` (sealed class)

| Property   | Type                             | Description                       |
|------------|----------------------------------|-----------------------------------|
| `Name`     | `string`                         | Profile identifier                |
| `Elements` | `IReadOnlyList<PolylineDefinition>` | Polyline elements of the shape |

#### `PerspectiveShapeRenderer` (static class)

Renders a polyline-based shape profile with optional depth-exaggeration (pseudo-3D). Uses
`stackalloc` for vertex buffers up to 64 points to minimize heap pressure.

| Parameter                 | Description                                         |
|---------------------------|-----------------------------------------------------|
| `shape`                   | `EntityShapeProfile` to render                      |
| `worldPos`                | 2D world-space center                               |
| `rotation`                | `Quaternion` (heading/pitch/roll from SpatialAnchor)|
| `lengthMeters`            | Platform bounding length                            |
| `widthMeters`             | Platform bounding width                             |
| `exaggerationCoefficient` | Z-depth to screen-scale factor (default 0.05)       |

#### `SemanticShapeRenderer` (sealed class)

Profile-based renderer with a `ISemanticShapeProfileRegistry` lookup. Falls back to a
magenta outline circle when no profile is found. Renders a red X overlay when
`conditionMask` bit 0 (`Damaged`) is set.

---

### Rendering Utilities

#### `MilStd2525Renderer` (static class)

Stub renderer for NATO MIL-STD-2525 symbols. Reads the second SIDC character for
affiliation (F/A/D/J = blue, H/S = red, N/L = yellow, else green) and draws a filled
circle with a 4-character label.

#### `RichTextRenderer`

Parses inline rich-text tags (e.g. `<color=#RRGGBB>`) from a string and renders styled
text segments via Raylib.

#### `IconAtlasAdapter`

Resolves icon atlas coordinates via `IIconAtlas`. Falls back to a yellow dot when no atlas
is configured or the coordinate is not found.

---

### Undo Stack

#### `GizmoUndoStack` (sealed class)

Minimal `Stack<IGizmoUndoRecord>` wrapper.

| Member                                      | Description              |
|---------------------------------------------|--------------------------|
| `void Push(IGizmoUndoRecord)`               | Record an undo action    |
| `bool TryUndo(out IGizmoUndoRecord?)`       | Pop and return if available |

Implement `IGizmoUndoRecord.Undo()` to define the rollback logic for a specific edit.

---

## Dependencies

```
+-------------------------------+
|  GizmoMap.Presentation        |
|                               |
|  Project references:          |
|    GizmoMap.Contracts         |
|    GizmoMap.Network           |
|    StructEdit.Core            |
|    StructEdit.Json            |
|                               |
|  Package references:          |
|    Raylib-cs        7.0.2     |
|    rlImgui-cs       3.2.0     |
|    ImGui.NET        1.91.6.1  |
+-------------------------------+
        |
        v
+-------------------------------+
|  GizmoMap.Network             |
|  (GizmoInteractionBatch,      |
|   GizmoInteractionEventKind,  |
|   GizmoUiState, ...)          |
+-------------------------------+
        |
        v
+-------------------------------+
|  GizmoMap.Contracts           |
|  (DebugPrimitive, ...)        |
+-------------------------------+
        |
        v
+-------------------------------+
|  StructEdit.Core / .Json      |
|  (EditDocument, JsonDeserialize|
+-------------------------------+
```

---

## Usage Examples

### Example 1: Embedding the Viewer Loop in a Host Application

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;
using GizmoMap.Presentation;
using System.Numerics;

// Setup
var renderBuffer   = new GizmoPrimitiveBuffer(capacity: 4096);
var schemaRegistry = new GizmoSchemaRegistry();

// Register StructEdit documents for any StructInspector panels you emit.
// schemaRegistry.Register(mySchemaHash, myEditDocument);

GizmoViewerFrontend.Run(
    windowTitle: "My App - GizmoMap",
    renderBuffer: renderBuffer,
    schemaRegistry: schemaRegistry,
    onUpdateTick: dt =>
    {
        // 1. Clear previous frame.
        renderBuffer.Clear();
        // 2. Fill renderBuffer with the current frame's primitives.
        //    Either from local simulation or DDS subscriber.
        MySimulation.DrawGizmos(dt, renderBuffer);
    },
    onInteraction: (token, kind, pos, actionId, flags, payloadJson) =>
    {
        // Route interaction back to simulation or undo stack.
        MySimulation.HandleGizmoEvent(token, kind, pos);
    },
    onMenuAction: (token, actionId) =>
    {
        MySimulation.HandleMenuAction(token.AnchorId, actionId);
    });
```

### Example 2: Standalone Renderer Usage (Headless Test)

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Presentation;
using System.Collections.Generic;

// Subclass DebugPrimitiveRenderer2D to capture shapes without Raylib.
sealed class TestRenderer : DebugPrimitiveRenderer2D
{
    public readonly List<DebugPrimitive> Dispatched = new();

    protected override void DispatchShape(
        in DebugPrimitive prim,
        Raylib_cs.Camera2D camera,
        float zoom)
    {
        Dispatched.Add(prim);
    }
}

void TestTwoPasses()
{
    var renderer = new TestRenderer();
    var buffer   = new GizmoPrimitiveBuffer(capacity: 64);

    // Emit a SpatialAnchor at (10, 20).
    buffer.EmitRaw(DebugPrimitive.MakeSpatialAnchor(
        networkId: 5L,
        worldX: 10f, worldY: 20f, worldZ: 0f,
        heading: 45f, pitch: 0f, roll: 0f));

    // Emit an EntityLocal line that references anchor #5.
    var prim = DebugPrimitive.MakeLine(
        from: new System.Numerics.Vector3(0, 0, 0),
        to:   new System.Numerics.Vector3(5, 0, 0),
        color: Rgba32.Green);
    prim.Space       = CoordinateSpace.EntityLocal;
    prim.AnchorIndex = 5;   // treated as NetworkId in standalone viewer
    prim.TargetView  = PipelineTarget.Map2D;
    buffer.AppendRaw(in prim);

    // Render: pass 1 caches anchor; pass 2 resolves EntityLocal -> World.
    renderer.Render(buffer.GetFrame(),
        new Raylib_cs.Camera2D { Zoom = 1f },
        zoom: 1f);

    System.Console.WriteLine($"Dispatched: {renderer.Dispatched.Count} primitive(s).");
    // Expected: 1 (the line; SpatialAnchor itself is never dispatched)
}
```

### Example 3: JSON Context Menu with ContextMenuAdapter

```csharp
using GizmoMap.Presentation;
using GizmoMap.Network;

var ctxMenu = new ContextMenuAdapter();

// From input handling code (outside ImGui):
ctxMenu.Schedule(anchorId: 42L, menuJson: """
[
  { "id": 1, "label": "Move", "shortcut": "M" },
  { "separator": true },
  { "id": 99, "label": "Destroy", "style": "destructive" }
]
""");

// From within rlImGui.Begin() / rlImGui.End() each frame:
ctxMenu.DrawScheduled((anchorId, actionId) =>
{
    System.Console.WriteLine($"Entity {anchorId}: action {actionId}");
});
```

### Example 4: Using the Undo Stack

```csharp
using GizmoMap.Presentation;
using System.Numerics;

var undoStack = new GizmoUndoStack();

// When the user commits a vertex drag:
Vector2 prevPos = new Vector2(10f, 20f);
Vector2 newPos  = new Vector2(30f, 40f);

undoStack.Push(new LambdaUndoRecord(() =>
{
    // Restore vertex to prevPos.
    polygon[0] = prevPos;
}));

// On Ctrl+Z:
if (undoStack.TryUndo(out var record))
    record!.Undo();
```

---

## Best Practices

1. **Always call `ExtractMetaPrimitives` before calling `DrawMainMenu`.** The frame must be
   scanned for `MainMenuBinding` primitives before the ImGui menu bar is rendered. Call
   `ExtractMetaPrimitives` at the end of the Raylib 2D pass, before `rlImGui.Begin`.

2. **Separate Raylib draw calls from ImGui draw calls.** `DebugGizmoLayer.Render` must run
   inside `Raylib.BeginMode2D` / `Raylib.EndMode2D`. `ContextMenuAdapter.DrawScheduled`
   and `ImGuiPropertyTreeAdapter.DrawScheduled` must run inside `rlImGui.Begin` /
   `rlImGui.End`. Mixing them causes undefined rendering behavior.

3. **Check `ImGui.GetIO().WantCaptureMouse` before processing Raylib mouse events.**
   `DebugGizmoLayer.HandleInput` already does this, but any custom input handler added via
   `onCustomInput` must also check it to prevent click-through into UI elements.

4. **Register StructEdit documents in `GizmoSchemaRegistry` before the first frame.**
   If a `StructInspector` primitive arrives before its schema is registered, the adapter
   renders a stub label. Documents registered mid-session take effect on the next frame.

5. **Use `PerspectiveShapeRenderer` for profiles with fewer than 64 vertices per element.**
   The renderer uses `stackalloc` for vertex buffers up to 64 points. Profiles with more
   points per polyline element will fall back to heap allocation.

6. **Override `DispatchShape` for headless testing.** Subclassing
   `DebugPrimitiveRenderer2D` and overriding `DispatchShape` allows testing the entire
   two-pass resolution pipeline (including SpatialAnchor lookups and LOD filtering) without
   initializing Raylib.

7. **Use the `externalAdapter` parameter for shared StructInspector state.** Pass a
   pre-configured `ImGuiPropertyTreeAdapter` to `GizmoViewerFrontend.Run` when you need
   to access inspector state (e.g. to handle `StructUpdate` callbacks) outside the
   standard `onInteraction` callback.

---

## Related Projects

| Project               | Relationship                                                         |
|-----------------------|----------------------------------------------------------------------|
| `GizmoMap.Contracts`  | Upstream; provides all shared types (`DebugPrimitive`, `IGizmoDrawBuilder`, ...) |
| `GizmoMap.Network`    | Upstream; provides `GizmoInteractionEventKind` and `GizmoUiState`   |
| `GizmoMap.Viewer`     | Application that uses `GizmoViewerFrontend` with DDS transport       |
| `GizmoMap.Example`    | Reference app showing `GizmoViewerFrontend` in both local and DDS modes |
| `StructEdit.Core`     | Provides `EditDocument` used by `GizmoSchemaRegistry` and `ImGuiPropertyTreeAdapter` |
| `StructEdit.Json`     | JSON serialization for StructEdit documents                          |
| `Fdp.Presentation`    | Sister library in Fdp; shares design patterns with this assembly but adds IMapLayer and FdpEventBus |
