# BATCH-27: Phase 4 — Migrate Hrot Editor Picker Tools to IEntityStatefulGizmo

**Batch Number:** BATCH-27
**Phase:** Phase 4 — Migrating Picker Tools
**Priority:** HIGH
**Dependencies:** BATCH-26 (Phase 3) — complete and committed.

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. `.dev/gizmos-1/old-stuff-erradication.md` — Phase 4 section.
2. `.dev/gizmos-1/reviews/BATCH-26-REVIEW.md` — previous batch review.
3. `Hrot/Subsystems/Hrot.Editor/Tools/LocationPickerTool.cs` — tool being replaced (read in full).
4. `Hrot/Subsystems/Hrot.Editor/Tools/ModalBoxSelectionTool.cs` — tool being replaced (read in full).
5. `Hrot/Subsystems/Hrot.Editor/Tools/EntityPickerTool.cs` — dead stub to delete.
6. `Hrot/Subsystems/Hrot.Editor/Adapters/EditorMapPickAdapter.cs` — adapter being updated (read in full).
7. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/PlacementCanvasBridge.cs` — the bridge pattern (already created in Phase 3).
8. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityPlacementGizmo.cs` — reference gizmo implementation.
9. `Hrot/Subsystems/Hrot.Editor.Tests/Adapters/AdapterTests.cs` — find the A004 test class.

### Source Code Locations

- **New gizmo files:** `Hrot/Subsystems/Hrot.Editor/Gizmos/` (same folder as `ObstaclePlacementGizmo`)
- **Adapter being updated:** `Hrot/Subsystems/Hrot.Editor/Adapters/EditorMapPickAdapter.cs`
- **Tests:** `Hrot/Subsystems/Hrot.Editor.Tests/Adapters/AdapterTests.cs`

### What is NOT in scope for this batch

The following use `Fdp.Toolkit.Vis2D.Tools.LocationPickerTool` and `Fdp.Toolkit.Vis2D.Tools.EntityPickerTool` (the FDP production tools, not the Hrot stubs). Do NOT touch them:
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/LocationPickerTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/EntityPickerTool.cs`
- `Hrot/Engine/Hrot.Presentation/Facades/CanvasMapPickAdapter.cs`
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` (uses FDP's LocationPickerTool at import line ~95)
- `EditorMapPickAdapter.PickEntityAsync` (already uses FDP's EntityPickerTool — leave it as-is)

### Build & Test Commands

```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q
dotnet test Hrot/Subsystems/Hrot.Editor.Tests/ --no-build -v q
dotnet test Hrot/Subsystems/Hrot.IG.Tests/ --no-build -v q
```

### Report Submission

Submit your report to: `.dev/gizmos-1/reports/BATCH-27-REPORT.md`

### DO NOT STOP

Implement everything, fix all errors, ensure all tests pass, then write the report.

---

## Context

Phase 4 deletes the three `Hrot.Editor.Tools` picker tools and replaces two of them with `IEntityStatefulGizmo` implementations using the `PlacementCanvasBridge` pattern established in Phase 3.

Key facts:
- `Hrot.Editor.Tools.EntityPickerTool` is a dead stub — it is not instantiated anywhere in production code. Just delete it.
- `Hrot.Editor.Tools.LocationPickerTool` is used only in `EditorMapPickAdapter.PickLocationAsync`. It has WGS-84 geo conversion via `IGeographicTransform`. Replace it with `LocationPickerGizmo`.
- `Hrot.Editor.Tools.ModalBoxSelectionTool` is used only in `EditorMapPickAdapter.PickAreaEntitiesAsync`. Replace it with `ModalBoxSelectionGizmo`.
- `EditorMapPickAdapter.PickEntityAsync` uses `Fdp.Toolkit.Vis2D.Tools.EntityPickerTool` — leave this method and that FDP tool untouched.
- The `CanvasMapPickAdapter` uses FDP tools only — leave it untouched.

---

## Architecture

### LocationPickerGizmo

- Namespace: `Hrot.Editor.Gizmos`
- File: `Hrot/Subsystems/Hrot.Editor/Gizmos/LocationPickerGizmo.cs`
- Implements: `IEntityStatefulGizmo`
- `RequiresExclusiveFocus => true`
- Constructor: `(IGeographicTransform geoTransform, Action<GeoPoint> onLocationPicked, Action? onRemove = null)`
- Fields: `_geoTransform`, `_onLocationPicked`, `_onRemove`, `_cursorWorld: Vector3`
- `UpdateAndDraw`: draws the same crosshair as `LocationPickerTool.Draw`. Copy the crosshair drawing logic (DrawLine x4 + DrawSphere for center) from `Hrot.Editor.Tools.LocationPickerTool.Draw`. Use `IDebugDrawBuilder` methods. Constants (`CrosshairHalfSize`, `CrosshairThickness`, `CrosshairGapRadius`) from `LocationPickerTool` copied as `private const`.
  - Note: `LocationPickerTool.Draw` scales by `ctx.Zoom`. `UpdateAndDraw` receives no context/zoom. Use the constants unscaled (zoom scaling is a canvas-layer concern, not the gizmo's).
- `OnDragUpdate(Vector3 worldPos)`: `_cursorWorld = worldPos`
- `OnMouseEvent(Left, isPressed=false, pos)`: geo-convert `pos` → fire `_onLocationPicked(geo)` → call `_onRemove()`
- `OnMouseEvent(Right, isPressed=true, pos)`: call `_onRemove()` (cancellation, no callback fired)
- `OnKeyEvent(Escape, pressed)`: call `_onRemove()`
- Unused methods: empty body
- `Dispose()`: empty body

Geo conversion:
```csharp
var (lat, lon, alt) = _geoTransform.ToGeodetic(worldPos);
var geo = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };
_onLocationPicked(geo);
```

### ModalBoxSelectionGizmo

- Namespace: `Hrot.Editor.Gizmos`
- File: `Hrot/Subsystems/Hrot.Editor/Gizmos/ModalBoxSelectionGizmo.cs`
- Implements: `IEntityStatefulGizmo`
- `RequiresExclusiveFocus => true`
- Constructor: `(Action<IReadOnlyList<int>> onSelectionComplete, Action? onRemove = null)`
- `UpdateAndDraw`: empty (no visual)
- `OnDragUpdate`: empty
- `OnMouseEvent(Left, false, pos)`: fire `_onSelectionComplete(Array.Empty<int>())` then `_onRemove()`
- `OnMouseEvent(Right, true, pos)`: call `_onRemove()` (no callback)
- `OnKeyEvent(Escape, true)`: call `_onRemove()`
- Unused methods: empty body
- `Dispose()`: empty body

---

## Tasks

### Task 1: Delete three legacy picker tool files

Delete:
1. `Hrot/Subsystems/Hrot.Editor/Tools/LocationPickerTool.cs`
2. `Hrot/Subsystems/Hrot.Editor/Tools/ModalBoxSelectionTool.cs`
3. `Hrot/Subsystems/Hrot.Editor/Tools/EntityPickerTool.cs`

---

### Task 2: Create `LocationPickerGizmo`

**File:** `Hrot/Subsystems/Hrot.Editor/Gizmos/LocationPickerGizmo.cs` (NEW)

Implement as described in the Architecture section. Use `System.Numerics.Vector3` for cursor position.

The crosshair draw logic (adapted from `LocationPickerTool.Draw`, unscaled):
```csharp
public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
{
    var drawColor = new Rgba32(102, 191, 255, 255);
    var pos = _cursorWorld;

    draw.DrawLine(new Vector3(pos.X - CrosshairHalfSize, pos.Y, 0f), new Vector3(pos.X - CrosshairGapRadius, pos.Y, 0f), drawColor, CrosshairThickness);
    draw.DrawLine(new Vector3(pos.X + CrosshairGapRadius, pos.Y, 0f), new Vector3(pos.X + CrosshairHalfSize, pos.Y, 0f), drawColor, CrosshairThickness);
    draw.DrawLine(new Vector3(pos.X, pos.Y - CrosshairHalfSize, 0f), new Vector3(pos.X, pos.Y - CrosshairGapRadius, 0f), drawColor, CrosshairThickness);
    draw.DrawLine(new Vector3(pos.X, pos.Y + CrosshairGapRadius, 0f), new Vector3(pos.X, pos.Y + CrosshairHalfSize, 0f), drawColor, CrosshairThickness);
    draw.DrawSphere(new Vector3(pos.X, pos.Y, 0f), CrosshairGapRadius, drawColor);
}
```

---

### Task 3: Create `ModalBoxSelectionGizmo`

**File:** `Hrot/Subsystems/Hrot.Editor/Gizmos/ModalBoxSelectionGizmo.cs` (NEW)

Implement as described in the Architecture section.

---

### Task 4: Update `EditorMapPickAdapter`

**File:** `Hrot/Subsystems/Hrot.Editor/Adapters/EditorMapPickAdapter.cs` (MODIFY)

**`PickLocationAsync` method** — replace `LocationPickerTool` with gizmo+bridge:

```csharp
public Task<Hrot.Core.Mission.GeoPoint> PickLocationAsync(CancellationToken ct = default)
{
    var tcs = new TaskCompletionSource<Hrot.Core.Mission.GeoPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
    PlacementCanvasBridge? bridge = null;
    var gizmo = new LocationPickerGizmo(
        _geoTransform,
        geo => tcs.TrySetResult(new Hrot.Core.Mission.GeoPoint(geo.Latitude, geo.Longitude, geo.Altitude)),
        onRemove: () => bridge?.RequestPop());
    bridge = new PlacementCanvasBridge(gizmo);

    ct.Register(() =>
    {
        if (_canvas.ActiveTool == bridge)
            bridge.RequestPop();
        tcs.TrySetCanceled(ct);
    });

    _canvas.PushTool(bridge);
    return tcs.Task;
}
```

**`PickAreaEntitiesAsync` method** — replace `ModalBoxSelectionTool` with gizmo+bridge:

```csharp
public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(
    string[]? filterPresets = null,
    CancellationToken ct    = default)
{
    var tcs = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
    PlacementCanvasBridge? bridge = null;
    var gizmo = new ModalBoxSelectionGizmo(
        list => tcs.TrySetResult(list),
        onRemove: () => bridge?.RequestPop());
    bridge = new PlacementCanvasBridge(gizmo);

    ct.Register(() =>
    {
        if (_canvas.ActiveTool == bridge)
            bridge.RequestPop();
        tcs.TrySetCanceled(ct);
    });

    _canvas.PushTool(bridge);
    return tcs.Task;
}
```

**Usings to remove:** `using Hrot.Editor.Tools;` (if nothing else in the file needs it)
**Usings to add:** `using Hrot.Editor.Gizmos;`, `using Hrot.ScenarioEditor.Gizmos;`

Keep `PickEntityAsync` unchanged.

---

### Task 5: Update `AdapterTests` A004

**File:** `Hrot/Subsystems/Hrot.Editor.Tests/Adapters/AdapterTests.cs` (MODIFY)

Find the `EditorMapPickAdapterTests` class. Update the three tests in it:

**`PickLocationAsync_ToolFires_TaskCompletesWithGeoPoint`:**
```csharp
[Fact]
public async Task PickLocationAsync_ToolFires_TaskCompletesWithGeoPoint()
{
    var adapter = new EditorMapPickAdapter(_canvas, HrotEnvironment.CreateGeoTransform());
    Task<Hrot.Core.Mission.GeoPoint> task = adapter.PickLocationAsync();

    // Simulate the operator left-clicking.
    var bridge = Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
    bridge.HandleClick(new Vector2(0f, 0f), MapMouseButton.Left);

    var result = await task;
    // The task should complete (the exact geo values depend on the transform).
    Assert.True(task.IsCompleted);
    Assert.False(task.IsFaulted);
    Assert.False(task.IsCanceled);
}
```

**`PickLocationAsync_CancellationToken_TaskCancelled`** — this test does NOT click the tool; it just cancels via CT. The check `if (_canvas.ActiveTool == bridge)` guards the pop. Update the test to use the new type, but the cancellation flow is unchanged:
```csharp
[Fact]
public async Task PickLocationAsync_CancellationToken_TaskCancelled()
{
    var cts     = new CancellationTokenSource();
    var adapter = new EditorMapPickAdapter(_canvas, HrotEnvironment.CreateGeoTransform());
    Task<Hrot.Core.Mission.GeoPoint> task = adapter.PickLocationAsync(cts.Token);

    // Verify the bridge is the active tool before cancellation.
    Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);

    cts.Cancel();

    await Assert.ThrowsAsync<TaskCanceledException>(() => task);
}
```

**`PickAreaEntitiesAsync_ToolFires_TaskCompletesWithList`:**
```csharp
[Fact]
public async Task PickAreaEntitiesAsync_ToolFires_TaskCompletesWithList()
{
    var adapter = new EditorMapPickAdapter(_canvas, HrotEnvironment.CreateGeoTransform());
    Task<IReadOnlyList<int>> task = adapter.PickAreaEntitiesAsync();

    // Simulate a left-click to trigger the gizmo's selection complete callback.
    var bridge = Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
    bridge.HandleClick(new Vector2(0f, 0f), MapMouseButton.Left);

    var result = await task;
    // Placeholder implementation returns empty list.
    Assert.NotNull(result);
}
```

Remove the old `tool.OnLocationPicked?.Invoke(...)` / `tool.OnSelectionComplete?.Invoke(...)` direct invocations — replace with `bridge.HandleClick(...)` as shown.

Add `using Hrot.ScenarioEditor.Gizmos;` at the top. Remove `using Hrot.Editor.Tools;` if it's only needed for the deleted picker tools (check first — `LocationPickerTool` and `ModalBoxSelectionTool` were from `Hrot.Editor.Tools`, but there may be other things that need it).

---

### Task 6: Build and Verify

```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q
dotnet test Hrot/Subsystems/Hrot.Editor.Tests/ --no-build -v q
dotnet test Hrot/Subsystems/Hrot.IG.Tests/ --no-build -v q
dotnet test Hrot/Engine/Hrot.Presentation.Tests/ --no-build -v q
```

Must be 0 build errors. `Hrot.Editor.Tests` and `Hrot.Presentation.Tests` must all pass. `Hrot.IG.Tests` must have no new failures beyond the 68-failure baseline.

---

## Pass Conditions

| Condition | Status |
|-----------|--------|
| `Hrot.Editor.Tools.LocationPickerTool` physically deleted | |
| `Hrot.Editor.Tools.ModalBoxSelectionTool` physically deleted | |
| `Hrot.Editor.Tools.EntityPickerTool` physically deleted | |
| `LocationPickerGizmo` implements `IEntityStatefulGizmo`, `RequiresExclusiveFocus = true` | |
| `ModalBoxSelectionGizmo` implements `IEntityStatefulGizmo`, `RequiresExclusiveFocus = true` | |
| `EditorMapPickAdapter.PickLocationAsync` uses bridge+gizmo | |
| `EditorMapPickAdapter.PickAreaEntitiesAsync` uses bridge+gizmo | |
| `EditorMapPickAdapter.PickEntityAsync` unchanged (still uses FDP's EntityPickerTool) | |
| `CanvasMapPickAdapter` unchanged | |
| Solution builds 0 errors | |
| `Hrot.Editor.Tests` all pass | |
| `Hrot.IG.Tests` no new failures vs 68-failure baseline | |
| `Hrot.Presentation.Tests` all pass | |

---

## Report Requirements

Submit `.dev/gizmos-1/reports/BATCH-27-REPORT.md` containing:
- Pass condition table (filled in)
- Test result counts for each test project
- Files created / modified / deleted
- Issues encountered and how resolved
