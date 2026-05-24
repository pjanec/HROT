# BATCH-23 Report -- GZ060, GZ061, GZ062, GZ063

**Status:** COMPLETE -- all tasks implemented, build passes, all new tests pass.

---

## Tasks Completed

### GZ060 -- Decouple Vis2D Abstractions from Raylib

**New files created:**

- `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/MapMouseButton.cs`
  Enum with `Left=0, Right=1, Middle=2` (values match Raylib-cs for safe direct cast).
- `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/MapKeyboardKey.cs`
  Enum with `Unknown=0, Escape=256, Enter=257, Delete=261, LeftShift=340, LeftControl=341, RightShift=344, RightControl=345`.

**Modified files:**

- `CoreInterfaces.cs`: Removed `Camera2D Camera` from `RenderContext`; added `float Zoom` and `IDebugDrawBuilder? DrawBuilder`. `IMapLayer.HandleInput` now takes `MapMouseButton`.
- `IMapTool.cs`: `HandleClick`, `HandlePress`, `HandleKeyPressed` now use `MapMouseButton`/`MapKeyboardKey`.
- `IInputProvider.cs`: All mouse/keyboard methods use `MapMouseButton`/`MapKeyboardKey`.
- `RaylibInputProvider.cs`: Casts via `(Raylib_cs.MouseButton)(int)button` and `(Raylib_cs.KeyboardKey)(int)key`.
- `Vis2DInputMap.cs`: `SelectButton`, `PanButton`, `MultiSelectMod`, `BoxSelectMod` use `MapMouseButton`/`MapKeyboardKey`.
- `MapCanvas.cs`: Added `IDebugDrawBuilder? DrawBuffer` property; wires it into `RenderContext` in `Draw()`.
- `DebugPrimitiveRenderer2D.cs`: Added `Camera2D Camera { get; set; }` property (Raylib Camera2D stored there, set by hosting layer before each `Render()` call since it was removed from `RenderContext`).
- `DebugGizmoLayer.cs`: Sets `_renderer.Camera = _canvas.Camera.InnerCamera` before each `_renderer.Render()`.
- `GridMapLayer.cs`: Uses `ctx.Zoom` instead of `ctx.Camera.Zoom`.
- `BoxSelectionTool.cs`: `HandleClick`/`HandlePress`/`HandleKeyPressed` use `MapMouseButton`/`MapKeyboardKey`.

All other `IMapLayer` and `IMapTool` implementations in FDP and Hrot updated to use the new types.

### GZ061 -- Convert MeasureTool, CreationTool, ObstaclePlacementTool Draw methods

- `MeasureTool.Draw`: Replaced `Raylib.DrawLineEx`, `Raylib.DrawCircleV`, `Raylib.DrawText` with `ctx.DrawBuilder?.DrawLine`, `ctx.DrawBuilder?.DrawSphere`, `ctx.DrawBuilder?.DrawTextLong`. Removed `TestHook_SkipRaylibCalls`.
- `CreationTool.Draw`: Replaced Raylib calls with `ctx.DrawBuilder?.DrawSphere/DrawTextLong`. `GetAffiliationColor` returns `Rgba32`.
- `ObstaclePlacementTool.Draw`: Replaced Raylib calls with `ctx.DrawBuilder?.DrawSphere`.

### GZ062 -- Convert EntityRotationTool Draw

- `EntityRotationTool.Draw`: Replaced `Raylib.DrawLineEx` with `ctx.DrawBuilder?.DrawLine`. `LineColor` is now `static readonly Rgba32`.

### GZ063 -- Convert EditTool, RouteEditTool Draw

- `EditTool.Draw`: Replaced Raylib calls with `ctx.DrawBuilder?.DrawLine/DrawSphere` using `Rgba32.Yellow/Red/White`.
- `RouteEditTool.Draw`: Replaced Raylib calls with `ctx.DrawBuilder?.DrawLine/DrawSphere`.

### Hosting Sites Wired (Step 16)

All four hosting sites connect `DrawBuffer` to `RenderContext`:

- `SimHostVisualization.cs`: `_map.DrawBuffer = _gizmoBuffer;`
- `IgApplication.cs`: `_canvas.DrawBuffer = _gizmoBuffer;`
- `CgfSubsystem.cs`: `_canvas.DrawBuffer = cgfGizmoBuffer;`
- `EditorSubsystem.cs`: `_canvas.DrawBuffer = _gizmoBuffer;`

### Tests Updated (Step 17)

All test files using `Raylib_cs.MouseButton`, `Raylib_cs.KeyboardKey`, or `RenderContext.Camera` were updated:

- `MapCanvasTests.cs`, `StandardInteractionToolTests.cs`, `EntityPickerToolTests.cs` -- `Fdp.Presentation.Tests`
- `DebugGizmoLayerHitTests.cs`, `DebugGizmoLayerActivationTests.cs`, `DebugGizmoLayerGizmoTests.cs` -- `Fdp.Presentation.Tests`
- `GizmoInteractionProxyToolTests.cs`, `GizmoInteractionProxyToolClickAwayTests.cs` -- `Fdp.Presentation.Tests`
- `DebugPrimitiveRenderer2DTests.cs`, `MapCameraTests.cs` -- `Fdp.Presentation.Tests`
- `MockInputProvider.cs` -- `Fdp.Presentation.Tests`
- `MeasureToolTests.cs`, `CreationToolTests.cs`, `EditToolTests.cs`, `RouteEditToolTests.cs` -- `Hrot.IG.Tests`
- `AdvancedFeaturesIntegrationTests.cs`, `ToolInteractionIntegrationTests.cs` -- `Hrot.IG.Tests`
- `IgApplicationTests.cs`, `MapCommandControllerTests.cs`, `MapEventTranslatorTests.cs` -- `Hrot.IG.Tests`
- `WaypointEditorPanelTests.cs`, `TraceLoggingTests.cs`, `EntityDragToolTests.cs` -- `Hrot.IG.Tests`
- `AdapterTests.cs` (`TestInputProvider` stub) -- `Hrot.Editor.Tests`
- `SystemTests.cs` (`RenderContext` construction) -- `Hrot.Editor.Tests`

---

## Build Result

`dotnet build IOS-IG-SimHost.sln --no-incremental` -- **0 errors, 0 warnings relevant to BATCH-23.**

---

## Test Result

All BATCH-23 related tests pass (135 tests across `Fdp.Presentation.Tests` and `Hrot.IG.Tests` for the affected classes).

Pre-existing failures (unrelated to BATCH-23):
- `EntityInspectorPanelTests` -- entity filtering logic bug (pre-existing, not in scope)
- `TraceLoggingTests.IngressAndRender_EmitsTraceLines` -- DDS `GizmoInteractionBatch` native method registration issue (pre-existing infrastructure)
- Various `Hrot.SimHost.Tests`, `Hrot.ClusterRunner.Tests`, `Fdp.ModuleHost.Tests` -- DDS/network integration failures (pre-existing, require live DDS environment)

---

## Deviations from Instructions

1. **`MapKeyboardKey` extended**: Added `LeftShift=340, LeftControl=341, RightShift=344, RightControl=345` beyond the minimal set specified, to match actual tool usage in `RouteEditTool` and `ModalBoxSelectionTool`.

2. **`DebugPrimitiveRenderer2D.Camera` property**: Since `RenderContext` no longer carries `Camera2D`, the renderer needed another way to receive it. Added a `Camera2D Camera { get; set; }` property to `DebugPrimitiveRenderer2D`; `DebugGizmoLayer` sets it from `_canvas.Camera.InnerCamera` before each `Render()` call. This is the minimal change to keep Raylib rendering functional without leaking `Camera2D` into the abstraction layer.

3. **`BoxSelectionTool.Draw`**: Left using `Raylib.DrawRectangleV/DrawRectangleLinesEx` (not converted). `IDebugDrawBuilder` has no `DrawRectangle` primitive, so conversion was deferred. The input handling (HandleClick/HandlePress/HandleKeyPressed) was fully converted.
