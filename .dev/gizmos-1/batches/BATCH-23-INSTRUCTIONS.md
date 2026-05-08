# BATCH-23 Instructions -- GZ060, GZ061, GZ062, GZ063

**Tasks:** GZ060, GZ061, GZ062, GZ063
**Design references:** `.dev/gizmos-1/TASK-DETAIL.md` (lines 1572-1630), `.dev/gizmos-1/DESIGN.md`

---

## Overview

This batch decouples all interactive map tools from `Raylib_cs`. The work has four parts:

1. **GZ060** -- Replace `Raylib_cs.Camera2D Camera` in `RenderContext` with `float Zoom` and
   `IDebugDrawBuilder? DrawBuilder`. Also define FDP-specific `MapMouseButton` and `MapKeyboardKey`
   enums so the `Fdp.Toolkit.Vis2D.Abstractions` namespace has zero Raylib references.

2. **GZ061** -- Convert `MeasureTool.Draw`, `CreationTool.Draw`, and `ObstaclePlacementTool.Draw`
   from `Raylib.*` calls to `ctx.DrawBuilder.*` calls.

3. **GZ062** -- Convert `EntityRotationTool.Draw` from `Raylib.*` calls to `ctx.DrawBuilder.*`.

4. **GZ063** -- Convert `EditTool.Draw` and `RouteEditTool.Draw` from `Raylib.*` calls to
   `ctx.DrawBuilder.*`.

Build after ALL steps and fix any errors before writing the report.

---

## Step 1 -- Create `MapMouseButton.cs` (new file)

Create `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/MapMouseButton.cs`:

```csharp
namespace Fdp.Toolkit.Vis2D.Abstractions;

/// <summary>
/// Mouse button identifiers for <see cref="IMapTool"/> and <see cref="IMapLayer"/>
/// input handling. Values match the Raylib-cs MouseButton enum so that a direct
/// cast is safe (no conversion table needed in RaylibInputProvider).
/// </summary>
public enum MapMouseButton
{
    Left   = 0,
    Right  = 1,
    Middle = 2,
}
```

---

## Step 2 -- Create `MapKeyboardKey.cs` (new file)

Create `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/MapKeyboardKey.cs`:

```csharp
namespace Fdp.Toolkit.Vis2D.Abstractions;

/// <summary>
/// Keyboard key identifiers for <see cref="IMapTool.HandleKeyPressed"/>.
/// Values match the Raylib-cs / GLFW3 keyboard scan codes so that a direct
/// cast from the raw int returned by <c>IInputProvider.GetKeyPressed()</c> is safe.
/// Only the subset used by tools is enumerated; all other keys arrive as
/// unnamed (but valid) enum values.
/// </summary>
public enum MapKeyboardKey
{
    Unknown = 0,
    Enter   = 257,
    Escape  = 256,
    Delete  = 261,
}
```

---

## Step 3 -- Modify `CoreInterfaces.cs`

File: `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/CoreInterfaces.cs`

a) Remove `using Raylib_cs;`

b) Remove the `Camera2D Camera` field from `RenderContext`. Replace `float Zoom => Camera.Zoom;`
   with a plain field:
   ```csharp
   public float Zoom;
   ```

c) Add `DrawBuilder` field to `RenderContext`:
   ```csharp
   /// <summary>
   /// Debug primitive builder injected by <see cref="MapCanvas.Draw"/>.
   /// Tools use this to emit backend-neutral draw primitives instead of calling Raylib directly.
   /// May be null in headless test contexts.
   /// </summary>
   public Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder? DrawBuilder;
   ```

d) Update `IMapLayer.HandleInput` signature -- replace `MouseButton` with `MapMouseButton`:
   ```csharp
   bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed);
   ```

The final `RenderContext` struct should look like:
```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Vis2D.Abstractions;

/// <summary>
/// Context passed to rendering layers and tools.
/// </summary>
public struct RenderContext
{
    public float Zoom;
    public Vector2 MouseWorldPos;
    public float DeltaTime;

    /// <summary>
    /// The mask of layers currently enabled by the user (32-bit bitmask).
    /// </summary>
    public uint VisibleLayersMask;

    /// <summary>
    /// Access to global resources.
    /// </summary>
    public IResourceProvider Resources;

    /// <summary>
    /// Debug primitive builder injected by <see cref="MapCanvas.Draw"/>.
    /// Tools use this to emit backend-neutral draw primitives instead of calling Raylib directly.
    /// May be null in headless test contexts.
    /// </summary>
    public Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder? DrawBuilder;
}
```

---

## Step 4 -- Modify `IMapTool.cs`

File: `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/IMapTool.cs`

a) Remove `using Raylib_cs;`

b) Replace `MouseButton` with `MapMouseButton` in `HandleClick` and `HandlePress`:
   ```csharp
   bool HandleClick(Vector2 worldPos, MapMouseButton button);
   // ...
   bool HandlePress(Vector2 worldPos, MapMouseButton button) => false;
   ```

c) Replace `KeyboardKey` with `MapKeyboardKey` in `HandleKeyPressed`:
   ```csharp
   bool HandleKeyPressed(MapKeyboardKey key) => false;
   ```

---

## Step 5 -- Modify `IInputProvider.cs`

File: `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/IInputProvider.cs`

a) Remove `using Raylib_cs;`

b) Replace `MouseButton` with `MapMouseButton` in mouse-button query methods.

c) Replace `KeyboardKey` with `MapKeyboardKey` in key query methods.
   Update the comment on `GetKeyPressed()` to say "Cast the return value to
   `MapKeyboardKey`." instead of referencing `KeyboardKey`.

---

## Step 6 -- Modify `RaylibInputProvider.cs`

File: `FDP/Engine/Fdp.Presentation/Vis2D/Defaults/RaylibInputProvider.cs`

Update the interface implementation to convert `MapMouseButton` / `MapKeyboardKey` to their
Raylib counterparts using direct casts (values are identical):

```csharp
public bool IsMouseButtonPressed(MapMouseButton button) =>
    Raylib.IsMouseButtonPressed((Raylib_cs.MouseButton)(int)button);

public bool IsMouseButtonDown(MapMouseButton button) =>
    Raylib.IsMouseButtonDown((Raylib_cs.MouseButton)(int)button);

public bool IsMouseButtonReleased(MapMouseButton button) =>
    Raylib.IsMouseButtonReleased((Raylib_cs.MouseButton)(int)button);

public bool IsKeyPressed(MapKeyboardKey key) =>
    Raylib.IsKeyPressed((Raylib_cs.KeyboardKey)(int)key);

public bool IsKeyDown(MapKeyboardKey key) =>
    Raylib.IsKeyDown((Raylib_cs.KeyboardKey)(int)key);

public bool IsKeyReleased(MapKeyboardKey key) =>
    Raylib.IsKeyReleased((Raylib_cs.KeyboardKey)(int)key);
```

---

## Step 7 -- Modify `MapCanvas.cs`

File: `FDP/Engine/Fdp.Presentation/Vis2D/MapCanvas.cs`

a) Add a `DrawBuffer` property near the other public properties (after `ActiveLayerMask`):
   ```csharp
   /// <summary>
   /// Debug primitive builder injected into every <see cref="RenderContext"/> during
   /// <see cref="Draw"/>. Set by the hosting application after creating the canvas and
   /// before the first draw call. May be null until set.
   /// </summary>
   public Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder? DrawBuffer { get; set; }
   ```

b) In `Draw()`, replace:
   ```csharp
   var ctx = new RenderContext
   {
       Camera = Camera.InnerCamera,
       MouseWorldPos = Camera.ScreenToWorld(GetMousePosition()),
       DeltaTime = GetFrameTime(),
       VisibleLayersMask = ActiveLayerMask,
       Resources = this
   };
   ```
   With:
   ```csharp
   var ctx = new RenderContext
   {
       Zoom             = Camera.Zoom,
       MouseWorldPos    = Camera.ScreenToWorld(GetMousePosition()),
       DeltaTime        = GetFrameTime(),
       VisibleLayersMask = ActiveLayerMask,
       Resources        = this,
       DrawBuilder      = DrawBuffer
   };
   ```

c) Update `ProcessInputPipeline()` -- replace all `MouseButton.Left`, `MouseButton.Right` with
   `MapMouseButton.Left`, `MapMouseButton.Right`. The layer `HandleInput` call also takes
   `MapMouseButton`. The keyboard key cast:
   ```csharp
   // BEFORE:
   if (ActiveTool.HandleKeyPressed((KeyboardKey)rawKey))
   // AFTER:
   if (ActiveTool.HandleKeyPressed((MapKeyboardKey)rawKey))
   ```

---

## Step 8 -- Update all `IMapLayer` implementations

Update `HandleInput(Vector2 worldPos, MouseButton button, bool isPressed)` to
`HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed)` in:

- `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Layers/GridMapLayer.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/SelectionRenderSystem.cs`
- `Hrot/Subsystems/Hrot.SimHost/Visualization/SimHostTrajectoryLayer.cs`
- `Hrot/Subsystems/Hrot.SimHost/Visualization/SimHostRoadLayer.cs`
- `Hrot/Subsystems/Hrot.Editor/Rendering/PerceptionMapLayer.cs`
- `FDP/Examples/Fdp.Examples.CarKinem/Visualization/TrajectoryMapLayer.cs`
- `FDP/Examples/Fdp.Examples.CarKinem/Visualization/RoadMapLayer.cs`

Also remove `using Raylib_cs;` from any of these files that only needed it for `MouseButton`.

For `DebugGizmoLayer.cs`: it uses `MouseButton.Left` inside `HandleInput`. Change it to
`MapMouseButton.Left`. Remove `using Raylib_cs;` ONLY if no other Raylib types are used there.

For `SimHostRoadLayer.cs`: it uses the fully-qualified `Raylib_cs.MouseButton` -- replace with
`MapMouseButton`.

---

## Step 9 -- Update all `IMapTool` implementations

For each of the following, change `MouseButton` → `MapMouseButton` in `HandleClick`/`HandlePress`,
and `KeyboardKey` → `MapKeyboardKey` in `HandleKeyPressed`. Remove `using Raylib_cs;` if no
longer needed.

- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/StandardInteractionTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/BoxSelectionTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/EntityPickerTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/LocationPickerTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/EntityDragTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/PointSequenceTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/StandardInteractionTool.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/MeasureTool.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/CreationTool.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EntityRotationTool.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EditTool.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/RouteEditTool.cs`
- `Hrot/Subsystems/Hrot.Editor/Tools/ObstaclePlacementTool.cs`
- `Hrot/Subsystems/Hrot.Editor/Tools/RoutePlacementTool.cs`
- `Hrot/Subsystems/Hrot.Editor/Tools/ModalBoxSelectionTool.cs`
- `Hrot/Subsystems/Hrot.Editor/Tools/LocationPickerTool.cs`
- `Hrot/Subsystems/Hrot.Editor/Tools/EntityPickerTool.cs`
- `Hrot/Subsystems/Hrot.Editor/Tools/AreaPlacementTool.cs`

Note: `GizmoInteractionProxyTool.cs` uses `KeyboardKey.Escape` in `HandleKeyPressed` --
replace with `MapKeyboardKey.Escape`.

---

## Step 10 -- Convert `MeasureTool.Draw` (GZ061)

File: `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/MeasureTool.cs`

a) Remove `using Raylib_cs;`

b) Add `using Fdp.Toolkit.Diagnostics.Gizmos;` (for `Rgba32`, `IDebugDrawBuilder`)

c) In `Draw()`, for the **crosshair** (when no start point set):
   Replace the 4 `Raylib.DrawLineEx` calls and 1 `Raylib.DrawCircleLinesV` call with
   `ctx.DrawBuilder` equivalents:
   ```csharp
   if (ctx.DrawBuilder != null)
   {
       var color = new Rgba32(0, 255, 255, 255); // cyan
       float z   = 0f;
       ctx.DrawBuilder.DrawLine(
           new System.Numerics.Vector3(pos.X - size, pos.Y, z),
           new System.Numerics.Vector3(pos.X - gap, pos.Y, z), color, thick);
       ctx.DrawBuilder.DrawLine(
           new System.Numerics.Vector3(pos.X + gap, pos.Y, z),
           new System.Numerics.Vector3(pos.X + size, pos.Y, z), color, thick);
       ctx.DrawBuilder.DrawLine(
           new System.Numerics.Vector3(pos.X, pos.Y - size, z),
           new System.Numerics.Vector3(pos.X, pos.Y - gap, z), color, thick);
       ctx.DrawBuilder.DrawLine(
           new System.Numerics.Vector3(pos.X, pos.Y + gap, z),
           new System.Numerics.Vector3(pos.X, pos.Y + size, z), color, thick);
       ctx.DrawBuilder.DrawSphere(
           new System.Numerics.Vector3(pos.X, pos.Y, z), gap, color);
   }
   ```
   Keep `TestHook_LineDrawCount += 4;` and `TestHook_CircleDrawCount++;` unconditionally
   (not inside the `ctx.DrawBuilder != null` guard) so test counters still increment even
   when `DrawBuilder` is null in tests that only pass `new RenderContext { Zoom = 1f }`.

d) For the **measuring line** (when start point is set):
   Replace `Raylib.DrawLineEx(start, end, ...)` with:
   ```csharp
   ctx.DrawBuilder?.DrawLine(
       new System.Numerics.Vector3(start.X, start.Y, 0f),
       new System.Numerics.Vector3(end.X, end.Y, 0f),
       new Rgba32(0, 255, 255, 255), MeasureToolConstants.LineThickness);
   ```
   Replace `Raylib.DrawText(label, ...)` with:
   ```csharp
   ctx.DrawBuilder?.DrawTextLong(midpoint.X, midpoint.Y + MeasureToolConstants.LabelOffsetY,
       label, Rgba32.White);
   ```

e) Update `MeasureToolConstants.cs` -- change `LineColor` from `Raylib_cs.Color` to `Rgba32`:
   ```csharp
   // Remove: public static readonly Raylib_cs.Color LineColor = ...;
   // It is no longer needed -- color is inlined in MeasureTool.Draw.
   ```
   (Delete the `LineColor` constant entirely; the cyan value is now inlined above.)

---

## Step 11 -- Convert `CreationTool.Draw` (GZ061)

File: `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/CreationTool.cs`

a) Remove `using Raylib_cs;`

b) Add `using Fdp.Toolkit.Diagnostics.Gizmos;`

c) Change `GetAffiliationColor` to return `Rgba32` instead of `Color`:
   ```csharp
   private static Rgba32 GetAffiliationColor(ForceId affiliation) =>
       affiliation switch
       {
           ForceId.Friend  => new Rgba32(0,   0,   255, 255), // blue
           ForceId.Hostile => Rgba32.Red,
           ForceId.Neutral => Rgba32.Green,
           _               => Rgba32.White,
       };
   ```

d) In `Draw()`, change color alpha modification to work with `Rgba32`:
   ```csharp
   var ghostColor = GetAffiliationColor(_affiliationForDisplay);
   ghostColor.A = CreationToolConstants.GhostAlpha;
   ```
   This works because `Rgba32.A` is a `byte`.

e) Replace the `Raylib.DrawCircle` call with:
   ```csharp
   ctx.DrawBuilder?.DrawSphere(
       new System.Numerics.Vector3(_currentMouseWorld.X, _currentMouseWorld.Y, 0f),
       CreationToolConstants.GhostRadiusPx,
       ghostColor);
   ```

f) Replace the `Raylib.DrawText` call with:
   ```csharp
   ctx.DrawBuilder?.DrawTextLong(
       _currentMouseWorld.X,
       _currentMouseWorld.Y + CreationToolConstants.GhostLabelOffsetY,
       _tkbType.ToString(),
       Rgba32.White);
   ```

---

## Step 12 -- Convert `ObstaclePlacementTool.Draw` (GZ061)

File: `Hrot/Subsystems/Hrot.Editor/Tools/ObstaclePlacementTool.cs`

a) Remove `using Raylib_cs;`

b) Add `using Fdp.Toolkit.Diagnostics.Gizmos;`

c) In `Draw()`, replace the `Raylib_cs.Raylib.DrawCircleLinesV(...)` call with:
   ```csharp
   ctx.DrawBuilder?.DrawSphere(
       new System.Numerics.Vector3(_currentMousePos.X, _currentMousePos.Y, 0f),
       _radius,
       Rgba32.Red);
   ```

---

## Step 13 -- Convert `EntityRotationTool.Draw` (GZ062)

File: `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EntityRotationTool.cs`

a) Remove `using Raylib_cs;`

b) Add `using Fdp.Toolkit.Diagnostics.Gizmos;`

c) In `Draw()`, replace `Raylib.DrawLineEx(origin, _currentPoint, ...)` with:
   ```csharp
   ctx.DrawBuilder?.DrawLine(
       new System.Numerics.Vector3(origin.X, origin.Y, 0f),
       new System.Numerics.Vector3(_currentPoint.X, _currentPoint.Y, 0f),
       EntityRotationToolConstants.LineColor,
       EntityRotationToolConstants.LineThickness);
   ```

d) Replace `Raylib.DrawText(label, ...)` with:
   ```csharp
   ctx.DrawBuilder?.DrawTextLong(midpoint.X,
       midpoint.Y + EntityRotationToolConstants.LabelOffsetY,
       label,
       Rgba32.White);
   ```

e) Update `EntityRotationToolConstants.LineColor` to be `Rgba32` instead of
   `Raylib_cs.Color`. The color is orange (255, 128, 0, 255). Change the constant:
   ```csharp
   // In EntityRotationTool.cs (constants class at top of file), change:
   public static readonly Rgba32 LineColor = new Rgba32(255, 128, 0, 255); // orange
   ```
   Add `using Fdp.Toolkit.Diagnostics.Gizmos;` to the file header.

---

## Step 14 -- Convert `EditTool.Draw` (GZ063)

File: `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EditTool.cs`

a) Remove `using Raylib_cs;`

b) Add `using Fdp.Toolkit.Diagnostics.Gizmos;`

c) In `Draw()`, replace the ghost polyline edge drawing:
   ```csharp
   // BEFORE:
   Raylib.DrawLineEx(p1, p2, EditToolConstants.VertexHandleRadiusWorldUnits, Color.Yellow);
   // AFTER:
   ctx.DrawBuilder?.DrawLine(
       new System.Numerics.Vector3(p1.X, p1.Y, 0f),
       new System.Numerics.Vector3(p2.X, p2.Y, 0f),
       Rgba32.Yellow,
       EditToolConstants.VertexHandleRadiusWorldUnits);
   ```

d) Replace vertex circle drawing:
   ```csharp
   // BEFORE:
   Color col  = sel ? Color.Red : Color.White;
   Raylib.DrawCircleV(pos, r, col);
   // AFTER:
   Rgba32 col  = sel ? Rgba32.Red : Rgba32.White;
   ctx.DrawBuilder?.DrawSphere(
       new System.Numerics.Vector3(pos.X, pos.Y, 0f), r, col);
   ```

---

## Step 15 -- Convert `RouteEditTool.Draw` (GZ063)

File: `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/RouteEditTool.cs`

a) Remove `using Raylib_cs;`

b) Add `using Fdp.Toolkit.Diagnostics.Gizmos;`

c) In `Draw()`, replace ghost polyline edge drawing:
   ```csharp
   // BEFORE:
   Raylib.DrawLineEx(a, b, 2f, Color.Yellow);
   // AFTER:
   ctx.DrawBuilder?.DrawLine(
       new System.Numerics.Vector3(a.X, a.Y, 0f),
       new System.Numerics.Vector3(b.X, b.Y, 0f),
       Rgba32.Yellow, 2f);
   ```

d) Replace vertex circle drawing:
   ```csharp
   // BEFORE:
   Raylib.DrawCircleV(pos, sel ? ... : ..., sel ? Color.Red : Color.White);
   // AFTER:
   ctx.DrawBuilder?.DrawSphere(
       new System.Numerics.Vector3(pos.X, pos.Y, 0f),
       sel ? RouteEditToolConstants.SelectedHandleRadius : RouteEditToolConstants.HandleRadius,
       sel ? Rgba32.Red : Rgba32.White);
   ```

---

## Step 16 -- Set `DrawBuffer` in hosting sites

For each canvas that has a `DebugPrimitiveBuffer`, set it on the canvas:

**`Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`:**
After `_gizmoBuffer = gizmoBuffer ?? new DebugPrimitiveBuffer();`, add:
```csharp
_map.DrawBuffer = _gizmoBuffer;
```

**`Hrot/Subsystems/Hrot.IG/IgApplication.cs`:**
After `_gizmoBuffer` is created (find the existing `_gizmoBuffer = new DebugPrimitiveBuffer();`
line) and after `_canvas` is initialized, add:
```csharp
_canvas.DrawBuffer = _gizmoBuffer;
```
(Note: the canvas `_canvas` must exist before this line.)

**`Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`:**
After `_gizmoBuffer = new DebugPrimitiveBuffer();` (find by searching for `cgfGizmoLayer`
or `DebugPrimitiveBuffer` construction), add:
```csharp
_context.Canvas.DrawBuffer = _gizmoBuffer;
```
(Adjust the canvas field name to whatever CgfSubsystem uses.)

**`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`:**
After `_gizmoBuffer = new DebugPrimitiveBuffer();` (step 4g from BATCH-22), add:
```csharp
if (_canvas != null)
    _canvas.DrawBuffer = _gizmoBuffer;
```
(EditorSubsystem may create the canvas conditionally for headless mode.)

---

## Step 17 -- Update test files

### `FDP/Engine/Fdp.Presentation.Tests/Vis2D/MapCanvasTests.cs`

The stub `IInputProvider` in this test file uses `MouseButton` parameter types.
Update `IsMouseButtonPressed(MouseButton)`, `IsMouseButtonDown(MouseButton)`,
`IsMouseButtonReleased(MouseButton)` to use `MapMouseButton`. Also update
`Mock<IMapLayer>` setup that passes `MouseButton.Left` to pass `MapMouseButton.Left`.
Remove `using Raylib_cs;` from the test file if the only Raylib usage was `MouseButton`.

### `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Layers/DebugGizmoLayerHitTests.cs`

Uses `layer.HandleInput(..., MouseButton.Left, ...)` -- change to `MapMouseButton.Left`.
Also creates `RenderContext { Camera = new Camera2D { Zoom = zoom, ... } }` -- change to
`RenderContext { Zoom = zoom }`.
Remove `using Raylib_cs;`.

### `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Layers/DebugGizmoLayerActivationTests.cs`

Same pattern -- `MouseButton.Left` → `MapMouseButton.Left`.

### `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/DebugGizmoLayerGizmoTests.cs`

Same pattern -- `MouseButton.Left` → `MapMouseButton.Left`.

### `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/GizmoInteractionProxyToolClickAwayTests.cs`

The inner `PressingRecorderTool : IMapTool` uses `MouseButton` and `KeyboardKey` --
update to `MapMouseButton` and `MapKeyboardKey`. `GizmoInteractionProxyTool` now also uses
`MapMouseButton.Left` / `MapKeyboardKey.Escape` -- update the tests that call
`tool.HandlePress(Vector2.Zero, MouseButton.Left)` to `MapMouseButton.Left`.

### `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/StandardInteractionToolTests.cs`

`tool.HandleClick(..., MouseButton.Left)` -- change to `MapMouseButton.Left`.
Remove `using Raylib_cs;`.

### `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/EntityPickerToolTests.cs`

`tool.Draw(new RenderContext { Camera = new Raylib_cs.Camera2D { Zoom = 1f } })` --
change to `new RenderContext { Zoom = 1f }`.
Remove `using Raylib_cs;`.

### `Hrot/Subsystems/Hrot.IG.Tests/MeasureToolTests.cs`

The tests `Draw_NoStartPoint_DoesNotThrow` and `Draw_NoStartPoint_DrawsCrosshair` use:
- `tool.TestHook_SkipRaylibCalls = true;` -- keep (the field still guards the if-block)

Actually, after Step 10, `TestHook_SkipRaylibCalls` can be REMOVED since we no longer
call Raylib at all. Update the tests as follows:

**`Draw_NoStartPoint_DoesNotThrow`:**
```csharp
public void Draw_NoStartPoint_DoesNotThrow()
{
    var tool = new MeasureTool();
    var ex = Record.Exception(() => tool.Draw(new RenderContext { Zoom = 1f }));
    Assert.Null(ex);
}
```

**`Draw_NoStartPoint_DrawsCrosshair`:**
Replace `TestHook_SkipRaylibCalls` / `TestHook_LineDrawCount` / `TestHook_CircleDrawCount`
approach with `FullCapturingDrawBuilder` (already exists in `Hrot.IG.Tests.Gizmos`):
```csharp
public void Draw_NoStartPoint_DrawsCrosshair()
{
    var spy  = new Hrot.IG.Tests.Gizmos.FullCapturingDrawBuilder();
    var tool = new MeasureTool();
    tool.Draw(new RenderContext { Zoom = 1f, DrawBuilder = spy });
    Assert.Equal(4, spy.LineCalls.Count);
    Assert.Equal(1, spy.SphereCalls.Count);
}
```

Remove `TestHook_SkipRaylibCalls`, `TestHook_LineDrawCount`, `TestHook_CircleDrawCount` fields
from `MeasureTool.cs` and the corresponding usages.

Also update the `Draw_NoStartPoint_DoesNotThrow` and `Draw_NoStartPoint_DrawsCrosshair` RenderContext
construction in the test to use `Zoom = 1f` instead of `Camera = new Camera2D { Zoom = 1f }`.
Remove `using Raylib_cs;` from the test file.

### `ExtDeps/GizmoMap/GizmoMap.Presentation.Tests/GizmoPresentationTests.cs`

This test uses `MouseButton.Left` in `HandlePress(Vector2.Zero, MouseButton.Left)`.
Update to `MapMouseButton.Left`. It also uses `Camera2D` -- check and update.

### Other test files that use `MouseButton` or `KeyboardKey`:

Do a grep for `MouseButton\.|KeyboardKey\.` in `**/*Tests*.cs` and update any remaining
occurrences to `MapMouseButton.` / `MapKeyboardKey.` with `using Fdp.Toolkit.Vis2D.Abstractions;`.

---

## Step 18 -- Handle `CarKinemApp.cs` and `VehicleVisualizer.cs`

`FDP/Examples/Fdp.Examples.CarKinem/CarKinemApp.cs` uses `Raylib.IsKeyPressed(KeyboardKey.Delete)`
etc. directly -- these are NOT going through the IMapTool interface, they are direct Raylib calls
in the hosting app and are OUT OF SCOPE for GZ060 (those Raylib calls are in the outer Raylib
window loop, not in Vis2D abstractions). Leave them unchanged.

---

## Step 19 -- Handle `EntityShapeCondition` and `SelectionRenderSystem` if they use Raylib

Check `SelectionRenderSystem.cs` for Raylib usage:
</s> It uses `MouseButton` in `HandleInput`. Already handled in Step 8. Also check for any `Raylib.Draw*`
calls in `SelectionRenderSystem.cs` -- those should NOT be changed (they are correct Raylib render
calls in the non-abstracted rendering path). Only the `HandleInput` signature needs updating.

---

## Step 20 -- Build verification

```
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-incremental
```

Fix all errors. Key things that commonly cause errors:
- Missing `using Fdp.Toolkit.Vis2D.Abstractions;` after removing Raylib usings (needed for
  `MapMouseButton`/`MapKeyboardKey`)
- Forgetting to update `HandleKeyPressed` in tool implementations that previously had
  `if (key == KeyboardKey.Escape)` -- must become `if (key == MapKeyboardKey.Escape)`
- `Camera2D` constructor in test `RenderContext` construction -- change to `Zoom = 1f`
- Any file that used `Color.Yellow`, `Color.Red`, `Color.White` (Raylib colors) in draw calls --
  change to `Rgba32.Yellow`, `Rgba32.Red`, `Rgba32.White`

---

## Step 21 -- Run tests and check counts

```
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln
```

Expected: all previously-passing tests continue to pass. The draw tests for MeasureTool should
now count `FullCapturingDrawBuilder.LineCalls` and `SphereCalls` instead of TestHook counters.

---

## AGENTS.md Invariants (Non-Negotiable)

- Preserve all existing comments exactly (except ones that reference removed types).
- Only change lines required for the functional fix.
- Do NOT use unicode characters in comments or string literals.
- Before finishing, make sure the solution builds without errors.

---

## Report

Write results to `.dev/gizmos-1/reports/BATCH-23-REPORT.md` with:
- List of new files created
- List of modified files with summary of changes
- Build result (error count)
- Test results (pass/fail counts per project)
- Any deviations from instructions with justification
