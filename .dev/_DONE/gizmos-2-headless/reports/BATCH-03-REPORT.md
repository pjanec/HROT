# BATCH-03 Report

## Tasks completed
- [x] DEBT-002 -- GizmoUiStateHub wired to LayerControlGizmo in SimHostApp + EditorSubsystem
- [x] GZH-014 -- IGizmoControllable interface + PerspectiveCoordinatorSystem listener transfer
- [x] GZH-016 -- IsActiveMapOwner input gate (SubsystemConfig delegate + IgApplication + Editor)

## Files modified

### FDP submodule (`FDP/`)
- `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemConfig.cs` -- added `IsActiveMapOwner` property (GZH-016)
- `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemOrchestrator.cs` -- sets `cfg.IsActiveMapOwner` lambda in `Initialize()` loop (GZH-016)

### Hrot (parent repo)
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` -- added `_gizmoUiHub` field, `GizmoUiHub` property, pass hub to `LayerControlGizmo` ctor (DEBT-002); made `GizmoController` public, added `IGizmoControllable` to `SimHostSubsystem` (GZH-014)
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` -- added `_isActiveMapOwner` field, set from `SubsystemConfig.IsActiveMapOwner` in `Initialize()`, gate applied in `Update()` canvas block (GZH-016); made `GizmoController` public (GZH-014)
- `Hrot/Subsystems/Hrot.IG/IgSubsystem.cs` -- added `IGizmoControllable` interface, `GizmoController` property, set `_app.IsActiveMapOwner` from config (GZH-014, GZH-016)
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` -- added `_gizmoUiHub` field, `GizmoUiHub` property, pass hub to `LayerControlGizmo` ctor (DEBT-002); added `IGizmoControllable` interface, made `GizmoController` public (GZH-014); added `_isActiveMapOwner` field, set from config, gate applied in `DrawUI()` canvas block (GZH-016)
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` -- added explicit `IGizmoControllable.GizmoController` implementation delegating to `_cgfGizmoController` (GZH-014)
- `Hrot/Runner/Hrot.ClusterRunner/Systems/PerspectiveCoordinatorSystem.cs` -- added `_gizmoControllables` dictionary and optional 3rd parameter; added listener transfer calls in `ProcessPendingEvents()` (GZH-014)
- `Hrot/Runner/Hrot.ClusterRunner/Program.cs` -- builds `gizmoControllables` map from subsystems implementing `IGizmoControllable` and passes it to `PerspectiveCoordinatorSystem` ctor (GZH-014)

## Files created
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/IGizmoControllable.cs` -- new interface (GZH-014)

## Test results

### GZH-011 + DEBT002_SimHost (Hrot.SimHost.Tests)
```
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 44 ms
```
Tests: `GZH011_1_SchemaHash_MatchesComputedHash`, `GZH011_2_UpdateAndDraw_WithEditing_PublishesOnce_NoDuplicateEcho`, `DEBT002_SimHost_GizmoUiHub_IsNonNull_AfterConstruction`

### GZH-014 (Hrot.ClusterRunner.Tests)
```
Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 11 ms
```
Tests: `GZH014_1_PerspectiveSwitch_TransfersGizmoListenerCount`, `GZH014_2_UnknownNewPerspective_IsIgnored_NoException`

### PerspectiveCoordinatorSystem regression suite (Hrot.ClusterRunner.Tests)
```
Passed!  - Failed: 0, Passed: 11, Skipped: 0, Total: 11, Duration: 25 ms
```
All 9 pre-existing tests + 2 new GZH-014 tests pass.

### GZH-016 (Hrot.IG.Tests)
```
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 5 ms
```
Tests: `GZH016_1_InputGate_MouseCaptured_SuppressesInput`, `GZH016_2_InputGate_InactiveMapOwner_SuppressesInput`, `GZH016_3_InputGate_ActiveOwnerAndMouseFree_AllowsInput`

### DEBT002_Editor (Hrot.Editor.Tests)
```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 11 ms
```
Test: `DEBT002_Editor_GizmoUiHub_IsNonNull_AfterConstruction`

## Issues / deviations

- **DEBT002 test placement**: per instructions' fallback, `DEBT002_SimHost` is placed next to `GZH011_1/GZH011_2` in `Hrot.SimHost.Tests/Gizmos/LayerControlGizmoTests.cs`. `DEBT002_Editor` is placed in `Hrot.Editor.Tests/EditorBootstrapTests.cs` (nearest editor test file), since `Hrot.SimHost.Tests` does not reference `Hrot.Editor`.

- **GZH016_1/2/3 test location**: tests are in `Hrot.IG.Tests/IgApplicationPanelTests.cs` following the pattern of existing `InputGate_WantCaptureMouse*` tests. The ClusterRunner.Tests filter returns 0 matches for GZH016 because the tests live in IG.Tests; this matches the instructions' guidance ("or the nearest existing test file in `Hrot.IG.Tests`").

- **SimHostSubsystem vs SimHostApp for GZH-014**: the `IGizmoControllable` interface is implemented on `SimHostSubsystem` (the `ISubsystem` adapter), not on the inner `SimHostApp`. This is because the subsystem list in `Program.cs` holds `ISubsystem` references and the `PerspectiveCoordinatorSystem` casts them via `OfType<IGizmoControllable>()`. The `GizmoController` property on `SimHostSubsystem` delegates to `_app?.GizmoController`.
