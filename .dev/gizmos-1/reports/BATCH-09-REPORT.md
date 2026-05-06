# BATCH-09 Report — GZ021 Map Measure Tool Gizmo

**Date:** 2026-05-06
**Batch:** BATCH-09-INSTRUCTIONS.md
**Task reference:** TASK-GZ021 (sub-item: measure tool gizmo activation)

---

## Build Result

**0 errors** across all three build targets:

```
dotnet build Hrot\Engine\Hrot.Presentation\Hrot.Presentation.csproj  -> OK
dotnet build Hrot\Subsystems\Hrot.IG\Hrot.IG.csproj                  -> OK
dotnet build Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj      -> OK
```

---

## Test Results

Filter: `FullyQualifiedName~Gizmo`

```
Passed!  - Failed: 0, Passed: 36, Skipped: 0, Total: 36
```

- **New tests added:** 8 (`MeasureToolGizmoAdapterTests`, class `SC_GZ021_MT_1` through `SC_GZ021_MT_8`)
- **Pre-existing gizmo tests:** 28 (all still passing)

---

## Files Created

| File | Description |
|------|-------------|
| `Hrot/Subsystems/Hrot.IG/Gizmos/MeasureToolGizmoSettings.cs` | Settings constants and registration (`Active` bool, `Units` int) |
| `Hrot/Subsystems/Hrot.IG/Gizmos/MeasureToolGizmoAdapter.cs` | Bridges GizmoSettingsRegistry to MapCanvas tool stack |
| `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/MeasureToolGizmoAdapterTests.cs` | 8 unit tests for the adapter |

---

## Files Modified

| File | Change |
|------|--------|
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/MeasureTool.cs` | Added `MeasureDisplayUnits` enum (before class), `DisplayUnits` property, updated label formatting to switch between m/km |
| `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` | Added `MeasureToolGizmoSettings.Register(settings)` call |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | Added `using` directives, `_gizmoSettingsRegistry` and `_measureToolGizmoAdapter` fields, wiring in `InitializeNetwork`, `_measureToolGizmoAdapter?.Update()` call before `_canvas.Update(dt)` in `Update()` |

---

## Deviations from Instructions

### 1. Wiring location: `InitializeNetwork` not `InitializeEcs`

The batch instructions say to wire `_measureToolGizmoAdapter` in `InitializeEcs()`. However, `_gizmoBuffer` and `_gizmoRegistry` are created in `InitializeNetwork()` (line 1121), not in `InitializeEcs()`. The adapter depends on the settings registry being created alongside the gizmo registry, so the wiring was placed in `InitializeNetwork()` immediately after:

```csharp
_gizmoSettingsRegistry   = new GizmoSettingsRegistry();
GizmoRegistrar.Register(_gizmoRegistry, _gizmoSettingsRegistry);
_measureToolGizmoAdapter = new MeasureToolGizmoAdapter(_canvas, _gizmoSettingsRegistry);
```

### 2. `GizmoSettingsRegistry` field added to `IgApplication`

The batch instructions focused on the local variable `gizmoSettingsRegistry`. Since no such local existed in `InitializeNetwork()`, a new field `_gizmoSettingsRegistry` was added alongside `_gizmoBuffer` and `_gizmoRegistry`. This also makes the registry accessible for future extensions (e.g., serialization, debug UI).

### 3. `GizmoRegistrar.Register` was not previously called from IgApplication

The batch instructions assumed `GizmoRegistrar.Register` was already wired in production. It was only called from tests. This batch adds the production wiring alongside the adapter creation, which is the natural composition root for both operations.

### 4. MapCanvas testability

MapCanvas can be constructed without a Raylib window (its constructor only stores `IInputProvider`; no Raylib calls are made until `Update()` or `Draw()`). `PushTool`, `PopTool`, and `ActiveTool` are pure stack operations with no Raylib calls. Therefore, adapter tests use real `MapCanvas` instances and assert on `canvas.ActiveTool`, which is more complete than the fallback approach described in the instructions.

---

## Debt Tracker

Added **D-007** (spatial grid gizmo deferred — SpatialHashGrid not exposed via service interface).
