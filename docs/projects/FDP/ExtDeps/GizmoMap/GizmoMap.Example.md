# GizmoMap.Example

| Field     | Value                                                                  |
|-----------|------------------------------------------------------------------------|
| Project   | GizmoMap.Example                                                       |
| Path      | `FDP/ExtDeps/GizmoMap/GizmoMap.Example/GizmoMap.Example.csproj`       |
| Namespace | `GizmoMap.Example`                                                     |
| Target    | `net8.0`                                                               |
| Output    | Executable (`<OutputType>Exe</OutputType>`)                            |
| Date      | 2026-05-23                                                             |

---

## README Validation

**Status: Missing** -- No `README.md` exists in `GizmoMap.Example/` or in the GizmoMap
root folder. All findings are derived directly from source code and inline comments.

---

## Executive Overview

`GizmoMap.Example` is the reference application for the GizmoMap subsystem. It serves two
purposes:

1. **Integration test harness.** It exercises every primitive shape, all interaction
   patterns (drag, context menu, main menu, StructInspector, exclusive-focus raw-input),
   and both transport modes (local in-process and live CycloneDDS).

2. **Developer onboarding sample.** Reading this project end-to-end demonstrates how to
   wire `GizmoPrimitiveBuffer`, `IGizmoTransport`, `GizmoInteractionManager`,
   `IStatefulGizmo`, and `GizmoViewerFrontend` together in a self-contained application.

The example has two execution modes selected via `--mode`:

- `local` (default): the producer and consumer buffers are bridged by a
  `LocalGizmoTransport` that copies primitives in-process. No DDS participant is created.
- `dds`: a `DdsGizmoTransport` opens a real CycloneDDS participant, publishes to the
  standard GizmoMap topics, and subscribes to its own stream. This proves out the full
  serialization and socket layer.

A `--headless` flag runs 30 frames without opening a Raylib window, enabling CI
validation.

---

## Architecture

### Class Map

```
+-------------------------------------------------------------+
|  Program.cs (entry point)                                   |
|                                                             |
|  Selects transport mode:                                    |
|  +----------------------+    +---------------------------+  |
|  | LocalGizmoTransport  |    | DdsGizmoTransport         |  |
|  | (in-process copy)    |    | (live CycloneDDS)         |  |
|  +----------------------+    +---------------------------+  |
|                                                             |
|  GizmoPrimitiveBuffer (producer)                            |
|  GizmoPrimitiveBuffer (consumer)                            |
|  DemoSceneGenerator                                         |
|       |                                                     |
|       v                                                     |
|  GizmoViewerFrontend.Run (from GizmoMap.Presentation)       |
+-------------------------------------------------------------+

DemoSceneGenerator
+------------------------------------------+
|  IGizmoSource.Emit (per-frame)           |
|    - _manager.Emit (all registered gizmos)
|    - EmitScene (static + dynamic shapes) |
|                                          |
|  GizmoInteractionManager (_manager)      |
|    - VertexEditGizmo (polygon 1)         |
|    - VertexEditGizmo (polygon 2)         |
|    - LayerControlGizmo (layer mask)      |
|    - EntityRotatorGizmo (on-demand)      |
+------------------------------------------+

Gizmos:
+---------------------+  +---------------------+  +-----------------------+
| VertexEditGizmo     |  | EntityRotatorGizmo  |  | LayerControlGizmo     |
| RequiresExclusive=F |  | RequiresExclusive=T |  | RequiresExclusive=F   |
| Draws vertex handles|  | Exclusive raw-input |  | Emits LayerControlMask|
| Multi-handle polygon|  | Heading edit via    |  | StructInspector panel |
| editor              |  | mouse movement      |  | Main menu injection   |
+---------------------+  +---------------------+  +-----------------------+

Transport:
+---------------------------+    +-------------------------------+
| LocalGizmoTransport       |    | DdsGizmoTransport             |
| PublishPrimitives: copy   |    | PublishPrimitives: DDS write  |
| PollAndApply:     copy    |    | PollAndApply:     DDS read    |
+---------------------------+    +-------------------------------+
```

### Per-Frame Data Flow

```
+------------------------------------------------+
|  onUpdateTick(dt)                              |
|                                                |
|  producer.Clear()                              |
|  builder = new LocalDrawBuilder(producer)      |
|  gen.Emit(dt, builder)    --> fills producer   |
|                                                |
|  transport.PublishPrimitives(producer.GetFrame,|
|                              producer.InternMap)|
|                                                |
|  consumer.Clear()                              |
|  transport.PollAndApply(consumer)              |
|    --> copies primitives into consumer buffer  |
+------------------------------------------------+
              |
              v
  GizmoViewerFrontend renders consumer buffer
```

---

## Source Structure

```
GizmoMap.Example/
+-- GizmoMap.Example.csproj
+-- AssemblyInfo.cs             [assembly:InternalsVisibleTo] declarations
+-- GizmoInteractionManager.cs  Focus-arbitrating gizmo lifecycle manager
+-- LocalDrawBuilder.cs         IGizmoDrawBuilder adapter over GizmoPrimitiveBuffer
+-- Program.cs                  Entry point; wires transport + frontend
|
+-- Gizmos/
|   +-- EntityRotatorGizmo.cs   Exclusive-focus heading editor
|   +-- LayerControlGizmo.cs    StructInspector-driven layer mask gizmo
|   +-- VertexEditGizmo.cs      Multi-handle polygon vertex editor
|
+-- Scenarios/
|   +-- DemoSceneGenerator.cs   Per-frame scene: all shape types + interaction state
|
+-- Transport/
    +-- DdsGizmoTransport.cs    Live CycloneDDS transport (IGizmoTransport)
    +-- LocalGizmoTransport.cs  In-process copy transport (IGizmoTransport)
```

---

## Public API Reference

### Entry Point (`Program.cs`)

Command-line arguments:

| Argument          | Default   | Description                                          |
|-------------------|-----------|------------------------------------------------------|
| `--mode local`    | (default) | Use in-process `LocalGizmoTransport`                 |
| `--mode dds`      | --        | Use live `DdsGizmoTransport` (requires CycloneDDS)   |
| `--headless`      | false     | Run 30 frames without Raylib; exit                   |

---

### `GizmoInteractionManager` (sealed class, `IGizmoSource`)

ECS-agnostic lifecycle manager and focus arbitrator for `IStatefulGizmo` instances.
The registry is a `Dictionary<long, IStatefulGizmo>` keyed by stable `AnchorId`.

| Member                                  | Description                                                   |
|-----------------------------------------|---------------------------------------------------------------|
| `bool HasTool(long anchorId)`           | Returns true if a tool is registered under `anchorId`         |
| `void AddTool(long, IStatefulGizmo)`    | Register a tool; grant exclusive focus if it requests it      |
| `void RemoveTool(long)`                 | Dispose tool and release focus lock if it held it             |
| `void DispatchEvent(token, kind, pos, actionId, stateFlags, payloadJson?)` | Route interaction event to the tool registered under `token.AnchorId` |
| `void Emit(float dt, IGizmoDrawBuilder)` (IGizmoSource) | Call `UpdateAndDraw` on all tools; emit `InputCaptureBinding` for exclusive-focus holder |

**Focus arbitration rules:**
- At most one tool holds the exclusive-focus slot at any time.
- When `AddTool` is called with `tool.RequiresExclusiveFocus == true` and no other tool
  holds the slot, the new tool is granted focus and `SetFocus(true)` is called.
- When `RemoveTool` releases the exclusive-focus holder, `SetFocus(false)` is called and
  the slot is cleared. The next `AddTool` with `RequiresExclusiveFocus` will claim it.
- The manager emits `InputCaptureBinding(Exclusive=true)` on behalf of the focus holder.
  Tools do not emit this primitive themselves.

---

### `DemoSceneGenerator` (sealed class, `IGizmoSource`)

Produces a complete demonstration scene covering all GizmoMap shape types and interaction
patterns. All state is mutable and accumulates over the session lifetime.

| Member                              | Description                                                        |
|-------------------------------------|--------------------------------------------------------------------|
| Constructor                         | Registers two `VertexEditGizmo` and one `LayerControlGizmo`        |
| `void Emit(float dt, IGizmoDrawBuilder)` | Per-frame scene emission (requires `LocalDrawBuilder`)        |
| `void Emit(float dt, LocalDrawBuilder)` | Overload for tests bypassing the interface                    |
| `void TriggerRotator()`             | Instantiate `EntityRotatorGizmo`; no-op if already active          |
| `void OnGizmoInteraction(token, kind, pos, actionId, flags, payloadJson?)` | Route interaction events |
| `void OnMenuAction(token, int)`     | Handle context menu and main menu actions                          |
| `IComponentEditService EditService` | Exposed for `schemaRegistry` registration in Program.cs            |
| `static EditDocument BuildMockDocument()` | Builds a mock StructEdit document for stub panels         |

**Scene content per frame:**

| Item | Shape              | Key features                                                    |
|------|--------------------|-----------------------------------------------------------------|
| 1    | `SpatialAnchor`    | Orbiting entity; position and heading computed from elapsed time |
| 2    | `SemanticShape`    | APC silhouette attached to the anchor; toggles `Damaged` flag   |
| 3    | `Sphere`           | Sensor ring; `EntityLocal`, `WorldMeters`, sensors layer         |
| 4    | `Text`             | Short label attached to entity; `EntityLocal`                    |
| 5    | `Arrow`            | Velocity vector; `EntityLocal`                                   |
| 6    | `MilStd2525`       | Static NATO symbol at a fixed world position                     |
| 7    | `Box2D`            | Draggable box with `ContextMenuBinding`; cycles 3 menu JSON sets |
| 8    | `Line`             | Gradient line from world origin                                  |
| 9    | `Icon`             | Atlas icon at a fixed position                                   |
| 10   | `StructInspector`  | Static stub panel at a fixed screen position                     |

---

### `LocalDrawBuilder` (sealed class, `IGizmoDrawBuilder`)

Minimal adapter that forwards all `IGizmoDrawBuilder` calls to a `GizmoPrimitiveBuffer`.
Also exposes `EmitRaw(in DebugPrimitive)` and the underlying `Buffer` property.

| Member                            | Description                                       |
|-----------------------------------|---------------------------------------------------|
| `LocalDrawBuilder(GizmoPrimitiveBuffer)` | Constructor                               |
| `GizmoPrimitiveBuffer Buffer`     | Direct buffer access for advanced callers         |
| All `IGizmoDrawBuilder` methods   | Delegated to `_buffer`                            |

---

### `LocalGizmoTransport` (sealed class, `IGizmoTransport`)

In-process transport that copies primitives between producer and consumer buffers.
No DDS participant is created. Suitable for unit tests and local CI runs.

| Member                                             | Description                        |
|----------------------------------------------------|------------------------------------|
| `void PublishPrimitives(ReadOnlySpan<DebugPrimitive>, StringInternMap?)` | Copy to pending array |
| `void PollAndApply(GizmoPrimitiveBuffer)`          | Drain pending array into target    |
| `void Dispose()`                                   | No-op                              |

---

### `DdsGizmoTransport` (sealed class, `IGizmoTransport`)

Production DDS transport. Creates a real `DdsParticipant`, writes
`DebugPrimitivesBatch` and `StringInternEntry` topics on publish, and polls them on
`PollAndApply`. Also exposes an internal test constructor that accepts pre-built
`IDdsWriter<T>` / `IDdsReader<T>` adapters.

| Member                                                        | Description                    |
|---------------------------------------------------------------|--------------------------------|
| `DdsGizmoTransport(uint domainId, byte nodeId)`               | Production constructor         |
| Internal ctor (writer/reader adapters)                        | Test injection constructor     |
| `void PublishPrimitives(ReadOnlySpan<DebugPrimitive>, StringInternMap?)` | Publish via DDS  |
| `void PollAndApply(GizmoPrimitiveBuffer)`                     | Drain DDS samples into buffer  |
| `void Dispose()`                                              | Disposes participant and readers |

---

### Gizmos

#### `VertexEditGizmo` (sealed class, `IStatefulGizmo`)

Shared-focus (non-exclusive) polygon vertex editor. Multiple instances can coexist.

| Member                        | Description                                              |
|-------------------------------|----------------------------------------------------------|
| Constructor `(anchorId, Vector2[])` | Takes stable ID and mutable vertex array           |
| `bool RequiresExclusiveFocus` | `false`                                                  |
| `UpdateAndDraw`               | Emit polygon edges (lines) and vertex handles (Box2D)    |
| `OnInteractionStarted`        | Identify active vertex via `token.SubElementId`          |
| `OnDragUpdate`                | Move active vertex to world position                     |
| `OnCommit`                    | Finalize vertex position                                 |
| `OnCancel`                    | Restore saved position                                   |

Each vertex handle emits a `Box2D` with:
- `SubElementId = i + 1` (1-based; 0 reserved for non-interactive)
- `BoxAnchorId = _anchorId` (routes `Started` back to this gizmo)

---

#### `EntityRotatorGizmo` (sealed class, `IStatefulGizmo`)

Exclusive-focus heading editor. Activated by `DemoSceneGenerator.TriggerRotator()`.

| Member                        | Description                                              |
|-------------------------------|----------------------------------------------------------|
| Constructor `(entityPos, initialYawRad, onCommit, onRemove)` | All state at construction |
| `bool RequiresExclusiveFocus` | `true`                                                   |
| `bool WantsRawInput`          | `true`                                                   |
| `UpdateAndDraw`               | Draw yellow line from entity center toward cursor heading |
| `OnDragUpdate`                | Recompute yaw from cursor world position                 |
| `OnMouseEvent`                | Left release = commit + call `onRemove`; Right = cancel  |
| `OnKeyEvent`                  | Escape = cancel + call `onRemove`                        |

The gizmo calls `onRemove()` from within `OnMouseEvent`/`OnKeyEvent` to request its
own removal from the manager. The host (DemoSceneGenerator) then calls
`_manager.RemoveTool(RotatorAnchorId)` in response.

---

#### `LayerControlGizmo` (sealed class, `IStatefulGizmo`)

Drives 256-bit layer visibility from a StructEdit-backed inspector panel. Always
registered; never removed.

| Member                    | Description                                                   |
|---------------------------|---------------------------------------------------------------|
| `const long AnchorId`     | `9999L` -- stable registration key                           |
| `const uint SchemaHash`   | `0x8899AABB` -- registered in GizmoSchemaRegistry            |
| `const int OpenActionId`  | `250` -- menu action ID for "Tactical Map Layers..."          |
| `void ToggleEditor()`     | Show/hide the StructInspector panel                           |
| `OnStructUpdate(json)`    | Parse JSON DTO, update `_activeLayers`, emit new mask         |
| `UpdateAndDraw`           | Always emit `LayerControlMask` + `MainMenuBinding`; optionally emit `StructInspector` |

Per frame it emits:
1. `LayerControlMask` primitive with the current `_activeLayers` bitmask.
2. `MainMenuBinding` primitive with a "View > Tactical Map Layers..." JSON entry.
3. `StructInspector` primitive (when `_isEditing == true`).

---

## Dependencies

```
+-------------------------------+
|  GizmoMap.Example             |
|  (Executable)                 |
|                               |
|  Project references:          |
|    GizmoMap.Contracts         |
|    GizmoMap.Network           |
|    GizmoMap.Presentation      |
|    StructEdit.Core            |
|    StructEdit.Json            |
|    StructEdit.Reflection      |
|                               |
|  Package references:          |
|    CycloneDDS.NET   0.2.2     |
|    rlImgui-cs       3.2.0     |
+-------------------------------+
```

`StructEdit.Reflection` is used only by `DemoSceneGenerator` to build the
`ComponentEditService` that backs the `LayerControlGizmo` inspector. It is not referenced
by any other GizmoMap assembly.

---

## Usage Examples

### Example 1: Running in Local Mode

```
GizmoMap.Example.exe --mode local
```

Opens a 640x480 window. The scene shows:
- An APC silhouette orbiting the center, with a sensor ring and velocity arrow.
- A NATO symbol at a fixed position.
- An orange draggable box in the center (left-drag to move it).
- Two green polygon outlines with draggable vertex handles.
- A layer control panel in the main menu ("View > Tactical Map Layers...").
- Press `R` to activate the entity rotator gizmo on the static entity.

### Example 2: Running in DDS Mode

```
# Terminal 1 - start the example as a publisher:
GizmoMap.Example.exe --mode dds

# Terminal 2 - start the standalone viewer to consume the stream:
GizmoMap.Viewer.exe --domain 0 --node-id 1
```

The viewer window shows the same scene rendered from data received over DDS. Interactions
in the viewer window are forwarded back to the example application over the
`GizmoInteractionBatch` topic.

### Example 3: Headless CI Run

```
GizmoMap.Example.exe --mode local --headless
```

Runs 30 frames in `local` mode without opening a Raylib window. The first frame prints
the number of primitives received in the consumer buffer, verifying end-to-end round-trip.
Exit code is 0 on success.

### Example 4: Adding a Custom Gizmo to the Demo Scene

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using System.Numerics;

// Minimal stateful gizmo: draws a crosshair and logs position on commit.
public sealed class CrosshairGizmo : IStatefulGizmo
{
    private Vector2 _pos;
    public bool RequiresExclusiveFocus => false;
    public bool IsFocused { get; private set; }
    public void SetFocus(bool v) => IsFocused = v;

    public CrosshairGizmo(Vector2 initialPos) => _pos = initialPos;

    public void UpdateAndDraw(float dt, IGizmoDrawBuilder draw)
    {
        const float Size = 15f;
        var center = new Vector3(_pos.X, _pos.Y, 0f);
        draw.DrawLine(center - new Vector3(Size, 0, 0),
                      center + new Vector3(Size, 0, 0), Rgba32.White);
        draw.DrawLine(center - new Vector3(0, Size, 0),
                      center + new Vector3(0, Size, 0), Rgba32.White);

        var box = default(DebugPrimitive);
        box.Shape       = DebugPrimitiveShape.Box2D;
        box.Space       = CoordinateSpace.World;
        box.TargetView  = PipelineTarget.Map2D;
        box.BoxCenterX  = _pos.X;
        box.BoxCenterY  = _pos.Y;
        box.BoxExtentX  = Size;
        box.BoxExtentY  = Size;
        box.Color       = Rgba32.Yellow;
        box.SubElementId = 1;
        box.BoxAnchorId  = 12345L; // stable ID
        draw.EmitRaw(in box);
    }

    public void OnInteractionStarted(GizmoPickToken t, Vector3 pos) { }
    public void OnDragUpdate(Vector3 pos) => _pos = new Vector2(pos.X, pos.Y);
    public void OnCommit(Vector3 pos)
    {
        _pos = new Vector2(pos.X, pos.Y);
        System.Console.WriteLine($"Crosshair moved to ({pos.X:F1}, {pos.Y:F1})");
    }
    public void OnCancel() { }
    public void OnMenuAction(int id) { }
    public void OnMouseEvent(MapMouseButton b, bool p, Vector3 pos) { }
    public void OnKeyEvent(MapKeyboardKey k, bool p) { }
    public void Dispose() { }
}

// Register in DemoSceneGenerator constructor or in Program.cs:
// gen.Manager.AddTool(12345L, new CrosshairGizmo(new Vector2(100f, 100f)));
```

### Example 5: Testing the Interactive Drag Box Round-Trip with Local Transport

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Example;

// Verify that the interactive box starts at (0,0) and moves on DragUpdate.
void TestInteractiveBoxDrag()
{
    var gen       = new DemoSceneGenerator();
    var transport = new LocalGizmoTransport();
    var producer  = new GizmoPrimitiveBuffer(capacity: 512);
    var consumer  = new GizmoPrimitiveBuffer(capacity: 512);

    // Emit one frame.
    producer.Clear();
    gen.Emit(1f / 30f, new LocalDrawBuilder(producer));
    transport.PublishPrimitives(producer.GetFrame(), producer.InternMap);
    consumer.Clear();
    transport.PollAndApply(consumer);

    // Find the interactive box (BoxAnchorId == 1).
    int foundCount = 0;
    foreach (ref readonly var p in consumer.GetFrame())
    {
        if (p.Shape == DebugPrimitiveShape.Box2D && p.BoxAnchorId == 1L)
        {
            System.Console.WriteLine($"Box at ({p.BoxCenterX}, {p.BoxCenterY})");
            foundCount++;
        }
    }
    System.Console.WriteLine($"Found {foundCount} interactive box primitive(s).");
}
```

---

## Best Practices

1. **Implement `IStatefulGizmo.OnCancel` to always restore the pre-drag state.** Every
   mutable field written during a drag must be saved at `OnInteractionStarted` and restored
   in `OnCancel`. The `VertexEditGizmo` demonstrates this with `_savedPos`.

2. **Call `onRemove()` from within `OnMouseEvent` or `OnKeyEvent`, not from
   `UpdateAndDraw`.** Self-removal during `UpdateAndDraw` would mutate the manager's
   dictionary while iterating it. The host must act on the removal request after
   `DispatchEvent` returns.

3. **Use `SubElementId` to multiplex multiple handles on a single gizmo AnchorId.**
   SubElementId 0 is reserved for non-interactive primitives. Sub-elements 1 through
   65535 are valid interactive handles. The terminal picks the topmost hit and includes the
   SubElementId in `GizmoPickToken`; the gizmo uses it to identify which handle was hit.

4. **Prefer `LocalGizmoTransport` in unit tests.** It eliminates DDS networking, making
   tests deterministic and fast. Reserve `DdsGizmoTransport` for integration tests that
   specifically verify the serialization and DDS QoS behavior.

5. **Always emit `SpatialAnchor` before `EntityLocal` primitives in the same frame.**
   The renderer's Pass 1 builds the anchor cache before Pass 2 resolves EntityLocal
   coordinates. If the anchor arrives in a later batch, the EntityLocal primitives for
   that frame are silently skipped.

6. **Use `DemoSceneGenerator.BuildMockDocument()` only for stub panels.** The mock
   document does not reflect any real struct layout. In production code, use
   `StructEdit.Reflection.ComponentEditServiceBuilder` to generate a real schema from
   your DTO type.

7. **Emit `LayerControlMask` every frame.** The renderer treats each frame as authoritative.
   If `LayerControlMask` is absent, the renderer defaults to all layers visible (all 256
   bits set). This is intentional: the backend asserts visibility; absence means
   "show everything."

---

## Related Projects

| Project                  | Relationship                                                          |
|--------------------------|-----------------------------------------------------------------------|
| `GizmoMap.Contracts`     | Provides shared types; the gizmos are built on top of its interfaces  |
| `GizmoMap.Network`       | `DdsGizmoTransport` wraps the network adapters from this assembly     |
| `GizmoMap.Presentation`  | `GizmoViewerFrontend` is the rendering entry point used by this app   |
| `GizmoMap.Viewer`        | Companion viewer; can be pointed at the DDS stream produced by this app |
| `StructEdit.Reflection`  | Builds `ComponentEditService` from DTO types for `LayerControlGizmo`  |
| `StructEdit.Core`        | `EditDocument` / `IComponentEditService` contracts                    |
