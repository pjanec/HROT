# BATCH-22 Report — GZ059: Eradicate Legacy Rendering Infrastructure

**Batch:** BATCH-22  
**Task:** GZ059  
**Status:** COMPLETE  
**Build result:** 0 errors, 0 warnings related to deleted types

---

## Deletions

| File | Reason |
|------|--------|
| `FDP/Engine/Fdp.Presentation/Vis2D/Adapters/PerspectiveEntityVisualizerBase.cs` | Legacy visualizer base class |
| `FDP/Engine/Fdp.Presentation/Vis2D/Layers/EntityRenderLayer.cs` | Legacy render layer |
| `FDP/Engine/Fdp.Presentation/Vis2D/Defaults/DelegateAdapter.cs` | Legacy delegate adapter |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Layers/EntityRenderLayerTests.cs` | Tests for deleted class |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Defaults/DelegateAdapterTests.cs` | Tests for deleted class |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/SstVisualizerAdapter.cs` | Legacy adapter |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/SstVisualizerAdapterConstants.cs` | Constants for deleted adapter |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/StubVisualizerAdapter.cs` | Legacy stub adapter |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/StubVisualizerConstants.cs` | Constants for deleted adapter |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/RouteRenderLayer.cs` | Legacy render layer |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/MapOverlayRenderLayer.cs` | Legacy render layer |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/MissionRenderLayer.cs` | Legacy render layer |
| `Hrot/Engine/Hrot.Presentation/Adapters/ProjectileLayerFactory.cs` | Legacy factory for deleted render layer |
| `Hrot/Subsystems/Hrot.CGF/CgfDebugVisualizerAdapter.cs` | CGF legacy adapter |
| `Hrot/Subsystems/Hrot.SimHost/Visualization/SimHostVehicleVisualizer.cs` | SimHost legacy visualizer |
| `Hrot/Subsystems/Hrot.Editor/Adapters/EditorPerspectiveVisualizer.cs` | Editor legacy perspective visualizer |
| `Hrot/Subsystems/Hrot.IG/Layers/EffectRenderLayer.cs` | Legacy effect render layer |
| `Hrot/Subsystems/Hrot.IG/Layers/ZoneObstacleRenderLayer.cs` | Legacy zone obstacle render layer |
| `Hrot/Subsystems/Hrot.IG.Tests/NedVisualizerAdapterTests.cs` | Tests for deleted adapter |
| `Hrot/Subsystems/Hrot.IG.Tests/StubVisualizerAdapterTests.cs` | Tests for deleted adapter |
| `Hrot/Subsystems/Hrot.IG.Tests/StandardInteractionToolTests.cs` | Tests covering deleted 4-arg constructor |
| `Hrot/Subsystems/Hrot.IG.Tests/RouteRenderLayerTests.cs` | Tests for deleted RouteRenderLayer (not in instructions; deleted as side-effect) |

---

## Modifications

| File | What Changed |
|------|--------------|
| `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/CoreInterfaces.cs` | Removed `IVisualizerAdapter` interface and its XML doc |
| `FDP/Engine/Fdp.Presentation/Vis2D/Tools/StandardInteractionTool.cs` | Replaced `IVisualizerAdapter adapter` parameter with `Func<Entity, Vector2> getEntityPosition` delegate; removed adapter from hit-testing |
| `FDP/Engine/Fdp.Presentation/Vis2D/Tools/BoxSelectionTool.cs` | Same position-delegate replacement; restored `using Fdp.Toolkit.Vis2D.Abstractions` that was needed for `IMapTool`/`RenderContext` |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/BoxSelectionToolTests.cs` | Updated to new position-delegate API; removed `Mock<IVisualizerAdapter>` |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/StandardInteractionToolTests.cs` | Restored `using Fdp.Toolkit.Vis2D.Abstractions` needed for `Mock<IMapLayer>` |
| `FDP/Examples/Fdp.Examples.CarKinem/CarKinemApp.cs` | Removed `EntityRenderLayer` construction and `_map.AddLayer(vehicleLayer)`; replaced `StandardInteractionTool` call with position-delegate form |
| `FDP/Examples/Fdp.Examples.CarKinem/Visualization/VehicleVisualizer.cs` | Removed `: IVisualizerAdapter` from class declaration; kept `GetPosition`, `GetHitRadius`, `Render` as-is; restored `using Fdp.Toolkit.Vis2D.Abstractions` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/StandardInteractionTool.cs` | Removed `IVisualizerAdapter` parameter; restored `using Fdp.Toolkit.Vis2D.Abstractions` needed for `IMapTool`/`RenderContext` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/SelectionRenderConstants.cs` | Added `SelectionRadiusPx = 20` constant (required for `SelectionRenderSystem` pixel radius used in render) |
| `Hrot/Engine/Hrot.Presentation.Tests/RenderLayerPresenceTests.cs` | Removed two obsolete test methods asserting presence of deleted types; kept `ScenarioEditor_Assembly_ContainsSelectionRenderTypes` and `IG_Assembly_DoesNotContainMovedRenderLayers` |
| `Hrot/Engine/Hrot.Presentation.Tests/WorldResetTests.cs` | Updated `StandardInteractionTool` constructor call from 4-arg (old) to 3-arg (new) |
| `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs` | Removed `_visualizer` field; removed `EntityRenderLayer` construction; replaced `StandardInteractionTool` with position-delegate form; removed `using Hrot.Presentation.Adapters` (deleted namespace) |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Removed `_visualizerAdapter` field references; removed hover-tooltip block in `DrawUI()` that referenced deleted adapter |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | Removed `EntityRenderLayer`, `EffectRenderLayer`, `ZoneObstacleRenderLayer` construction; replaced `StandardInteractionTool` with 3-arg form; restored `using Hrot.ScenarioEditor.Rendering` (needed for `SelectionRenderSystem`) |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Removed `EntityRenderLayer` and related legacy layer registrations; added `DebugGizmoLayer` + gizmo registry setup; restored `using Hrot.Editor.Adapters` (accidentally removed in prior session); removed `using Hrot.Presentation.Adapters` (deleted namespace); added `using Fdp.Toolkit.Diagnostics.Gizmos.Systems` for `StatelessGizmoSystem` |
| `Hrot/Subsystems/Hrot.IG.Tests/IgApplicationPanelTests.cs` | Removed `EntityRenderQuery`-based test methods; fixed orphaned `[F` fragment left by prior edit |
| `Hrot/Subsystems/Hrot.IG.Tests/MapEventTranslatorTests.cs` | Removed `NedVisualizerAdapter` usage; changed `StandardInteractionTool` to 3-arg form |
| `Hrot/Subsystems/Hrot.IG.Tests/TraceLoggingTests.cs` | Removed render-trace log entry from expected list; removed `TryRenderOnce` helper; removed `NullResourceProvider` inner class; removed `using Hrot.ScenarioEditor.Adapters` and `using Fdp.Toolkit.Vis2D.Abstractions` |
| `Hrot/Subsystems/Hrot.IG.Tests/ToolInteractionIntegrationTests.cs` | Removed `EntityRenderLayer` test |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorSubsystemBootTests.cs` | Updated comments |

---

## Build Result

```
dotnet build IOS-IG-SimHost.sln --no-incremental
```

**Result:** 0 errors, 0 warnings related to deleted types.

---

## Test Results

| Project | Passed | Failed | Skipped | Total | Notes |
|---------|--------|--------|---------|-------|-------|
| `GizmoMap.Contracts.Tests` | 6 | 0 | 0 | 6 | |
| `GizmoMap.Network.Tests` | 5 | 0 | 0 | 5 | |
| `GizmoMap.Example.Tests` | 6 | 0 | 0 | 6 | |
| `GizmoMap.Presentation.Tests` | 6 | 0 | 0 | 6 | |
| `Hrot.Network.BDC.Tests` | 8 | 0 | 0 | 8 | |
| `Hrot.Core.Tests` | 126 | 5 | 0 | 131 | Pre-existing |
| `Fdp.Presentation.Tests` | 299 | 3 | 0 | 302 | Pre-existing (EntityInspectorPanel) |
| `Fdp.Network.Cyclone.Tests` | 40 | 0 | 0 | 40 | |
| `Fdp.Toolkits.Tests` | 940 | 27 | 0 | 967 | Pre-existing (~26 per instructions) |
| `Fdp.Examples.Scenarios.Tests` | 47 | 21 | 0 | 68 | Pre-existing |
| `Fdp.ModuleHost.Tests` | 183 | 6 | 0 | 189 | Pre-existing |
| `Fdp.Examples.CarKinem.Tests` | 9 | 0 | 0 | 9 | |
| `Fdp.Examples.UrbanCombat.Tests` | 27 | 2 | 0 | 29 | Pre-existing |
| `Hrot.Network.NED.Tests` | 95 | 0 | 0 | 95 | |
| `Hrot.Orchestrator.Integration.Tests` | 12 | 0 | 0 | 12 | |
| `Hrot.Map.Common.Tests` | 40 | 3 | 0 | 43 | Pre-existing |
| `Hrot.Editor.Tests` | 94 | 0 | 0 | 94 | |
| `Hrot.IG.Tests` | 430 | 15 | 0 | 445 | 4 pre-existing CS011; 11 pre-existing DDS/component issues (see note) |
| `Hrot.Presentation.Tests` | 42 | 0 | 0 | 42 | |
| `Fdp.Core.Tests` | 702 | 5 | 2 | 709 | Pre-existing |
| `Hrot.ExCon.Tests` | 324 | 0 | 0 | 324 | |
| `Hrot.SimHost.Integration.Tests` | 33 | 8 | 0 | 41 | Pre-existing |
| `Hrot.Orchestrator.Tests` | 129 | 2 | 0 | 131 | Pre-existing |
| `Hrot.ClusterRunner.Integration.Tests` | 7 | 25 | 3 | 35 | Pre-existing DDS infrastructure |
| `Hrot.SimHost.Tests` | 559 | 25 | 3 | 587 | Pre-existing (~20 per instructions) |
| `Hrot.ClusterRunner.Tests` | 208 | 15 | 0 | 223 | Pre-existing |

**Note on `Hrot.IG.Tests` (15 failures):**  
The instructions expected ~4 failures (CS011 EntityInfoTranslator). The additional 11 failures all share the root cause `GizmoInteractionBatch does not exhibit expected DDS generated native methods` or `StatelessGizmoRegistry.Register: required component type 'BrainBlackboard' is not registered`. These are infrastructure failures introduced by BATCH-21's `GizmoInteractionBatch` DDS type — not caused by BATCH-22. BATCH-22 added no code that touches `GizmoInteractionBatch`. The tests that fail construct a full `IgApplication` which attempts DDS participant initialization, failing before any legacy-rendering code is reached.

---

## Deviations from Instructions

1. **`SelectionRenderConstants.cs` — added `SelectionRadiusPx`**  
   `SelectionRenderSystem` referenced `SelectionRenderConstants.SelectionRadiusPx` which did not exist. Added `public const float SelectionRadiusPx = 20;` as required to compile.

2. **`RenderLayerPresenceTests.cs` — additional removal needed**  
   Two test methods (`ScenarioEditor_Assembly_ContainsRenderLayers`, `ScenarioEditor_Assembly_ContainsSstVisualizerAdapter`) asserted presence of types deleted in this batch. They were removed. The two surviving tests (`ContainsSelectionRenderTypes`, `IG_Assembly_DoesNotContainMovedRenderLayers`) were verified correct.

3. **`using Fdp.Toolkit.Vis2D.Abstractions` restored in multiple files**  
   Prior-session edits incorrectly removed this using from `BoxSelectionTool.cs`, `StandardInteractionTool.cs` (FDP), and `Hrot.Presentation StandardInteractionTool.cs`. It is not a deleted namespace — it provides `IMapTool`, `RenderContext`, `IMapLayer`. Restored in all three files.

4. **`VehicleVisualizer.cs` and `CarKinemApp.cs` — not in instructions**  
   `VehicleVisualizer` still implemented `IVisualizerAdapter`, and `CarKinemApp` still constructed `EntityRenderLayer`. Fixed both to compile against the new API.

5. **`using Hrot.Editor.Adapters` restored in `EditorSubsystem.cs`**  
   The prior session removed `using Hrot.Editor.Adapters;` (needed for `EditorSpawnAdapter`, `EditorMissionService`, `EditorOrbatAdapter`, etc.) and accidentally left `using Hrot.Presentation.Adapters;` (the deleted namespace). Fixed: added back `Hrot.Editor.Adapters`, removed `Hrot.Presentation.Adapters`.

6. **`using Fdp.Toolkit.Diagnostics.Gizmos.Systems` added to `EditorSubsystem.cs`**  
   The prior session added `StatelessGizmoSystem` usage but not the corresponding using directive. Added `using Fdp.Toolkit.Diagnostics.Gizmos.Systems;`.

7. **`RouteRenderLayerTests.cs` deleted (not in instructions)**  
   `RouteRenderLayer` was deleted by this batch; its test file was not listed in the instructions. Deleted to eliminate compilation errors.

8. **`CgfSubsystem.cs` — hover-tooltip block removal not in instructions**  
   The instruction's Step 8 removed the `_visualizerAdapter` field, but orphaned usages in `DrawUI()` (hover-tooltip UI block) were missed. Removed the entire hover-tooltip block that read `_visualizerAdapter.GetHitRadius` and `_visualizerAdapter.GetPosition`.

9. **`MapEventTranslatorTests.cs`, `WorldResetTests.cs`, `BoxSelectionToolTests.cs` — additional fixes**  
   These test files were not listed in instructions but referenced deleted types or the old 4-arg constructor. Fixed inline with the corresponding source changes.

10. **`Hrot.IG.Tests` — 15 failures vs expected ~4**  
    11 additional failures are pre-existing from BATCH-21 DDS infrastructure (see note above). No new BATCH-22 regressions.
