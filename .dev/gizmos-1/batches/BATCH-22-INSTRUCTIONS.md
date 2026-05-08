# BATCH-22 Instructions — GZ059: Eradicate Legacy Rendering Infrastructure

## Overview

GZ059 removes the legacy `IVisualizerAdapter` / `EntityRenderLayer` rendering stack from
all composition roots (SimHost, CGF, IG, Editor) and deletes every class that implemented
or depended on them.  Entity rendering is now solely driven by the `DebugGizmoLayer` backed
by the `StatelessGizmoSystem` + `DebugPrimitiveBuffer`.

Refer to `.dev/gizmos-1/TASK-DETAIL.md` (task GZ059) and `.dev/gizmos-1/DESIGN.md`
(Phase 16: Eradicate Legacy Rendering Infrastructure) for authoritative requirements.

**Success Conditions (from TASK-DETAIL.md):**
- SC-GZ059-1: Solution compiles cleanly without legacy visualization interfaces.
- SC-GZ059-2: Running the cluster (simhost, cgf, ig) results in a 2-D tactical map driven
  100% by the GizmoMap primitive stream.

---

## Reference: Key Files

| File | Action |
|------|--------|
| `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/CoreInterfaces.cs` | Modify — remove `IVisualizerAdapter` |
| `FDP/Engine/Fdp.Presentation/Vis2D/Tools/StandardInteractionTool.cs` | Modify — replace adapter with position delegate |
| `FDP/Engine/Fdp.Presentation/Vis2D/Tools/BoxSelectionTool.cs` | Modify — replace adapter with position delegate |
| `FDP/Engine/Fdp.Presentation/Vis2D/Adapters/PerspectiveEntityVisualizerBase.cs` | **Delete** |
| `FDP/Engine/Fdp.Presentation/Vis2D/Layers/EntityRenderLayer.cs` | **Delete** |
| `FDP/Engine/Fdp.Presentation/Vis2D/Defaults/DelegateAdapter.cs` | **Delete** |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/SstVisualizerAdapter.cs` | **Delete** |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/SstVisualizerAdapterConstants.cs` | **Delete** |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/StubVisualizerAdapter.cs` | **Delete** |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/StubVisualizerConstants.cs` | **Delete** |
| `Hrot/Subsystems/Hrot.CGF/CgfDebugVisualizerAdapter.cs` | **Delete** |
| `Hrot/Subsystems/Hrot.SimHost/Visualization/SimHostVehicleVisualizer.cs` | **Delete** |
| `Hrot/Subsystems/Hrot.Editor/Adapters/EditorPerspectiveVisualizer.cs` | **Delete** |
| `Hrot/Subsystems/Hrot.IG/Layers/EffectRenderLayer.cs` | **Delete** |
| `Hrot/Subsystems/Hrot.IG/Layers/ZoneObstacleRenderLayer.cs` | **Delete** |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/RouteRenderLayer.cs` | **Delete** |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/MapOverlayRenderLayer.cs` | **Delete** |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/MissionRenderLayer.cs` | **Delete** |
| `Hrot/Engine/Hrot.Presentation/Adapters/ProjectileLayerFactory.cs` | **Delete** |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/StandardInteractionTool.cs` | Modify — remove adapter param |
| `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs` | Modify — remove legacy layers |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | Modify — remove legacy layers |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Modify — remove legacy layers |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Modify — remove legacy layers, add gizmo infra |
| `Hrot/Subsystems/Hrot.IG.Tests/NedVisualizerAdapterTests.cs` | **Delete** |
| `Hrot/Subsystems/Hrot.IG.Tests/StubVisualizerAdapterTests.cs` | **Delete** |
| `Hrot/Subsystems/Hrot.IG.Tests/StandardInteractionToolTests.cs` | **Delete** |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Layers/EntityRenderLayerTests.cs` | **Delete** |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Defaults/DelegateAdapterTests.cs` | **Delete** |
| `Hrot/Subsystems/Hrot.IG.Tests/ToolInteractionIntegrationTests.cs` | Modify — remove EntityRenderLayer test |
| `Hrot/Subsystems/Hrot.IG.Tests/IgApplicationPanelTests.cs` | Modify — remove EntityRenderQuery tests |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorSubsystemBootTests.cs` | Modify — update comments |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/StandardInteractionToolTests.cs` | Modify — remove adapter |

---

## Step 1: Modify FDP Core Tools (remove IVisualizerAdapter)

### 1a. `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/CoreInterfaces.cs`

Delete the `IVisualizerAdapter` interface entirely (lines ~30-55).  Keep `RenderContext`,
`IMapLayer`, and any other content.  The file must not reference `IVisualizerAdapter`
anywhere after this change.

### 1b. `FDP/Engine/Fdp.Presentation/Vis2D/Tools/StandardInteractionTool.cs`

Replace `IVisualizerAdapter adapter` with `Func<Entity, Vector2?>? getEntityPosition = null`.

- Remove the `using Fdp.Toolkit.Vis2D.Abstractions;` import (no longer needed).
- Add `using Fdp.Core;` if not already present (needed for `SimTransform`).
- Change the private field from `private readonly IVisualizerAdapter _adapter;`
  to `private readonly Func<Entity, Vector2?> _getEntityPosition;`.
- Change the constructor to:
  ```csharp
  public StandardInteractionTool(
      ISimulationView view,
      EntityQuery query,
      Func<Entity, Vector2?>? getEntityPosition = null)
  {
      _view  = view;
      _query = query;
      _getEntityPosition = getEntityPosition ?? (e =>
          view.HasComponent<SimTransform>(e)
              ? new Vector2(view.GetComponentRO<SimTransform>(e).Position.X,
                            view.GetComponentRO<SimTransform>(e).Position.Y)
              : (Vector2?)null);
  }
  ```
- In `HandleDrag`, replace:
  ```csharp
  var startPos = _adapter.GetPosition(_view, _potentialTarget) ?? _mouseDownPos;
  ```
  with:
  ```csharp
  var startPos = _getEntityPosition(_potentialTarget) ?? _mouseDownPos;
  ```
- In `HandleDrag`, the `BoxSelectionTool` construction currently passes `_adapter`.
  Replace `_adapter` with `_getEntityPosition` in the `BoxSelectionTool` constructor call.

### 1c. `FDP/Engine/Fdp.Presentation/Vis2D/Tools/BoxSelectionTool.cs`

Replace `IVisualizerAdapter adapter` with `Func<Entity, Vector2?>? getEntityPosition`.

- Remove `using Fdp.Toolkit.Vis2D.Abstractions;`.
- Add `using Fdp.Core;` if not present.
- Change the field from `private readonly IVisualizerAdapter _adapter;`
  to `private readonly Func<Entity, Vector2?> _getEntityPosition;`.
- Change the constructor parameter and assignment:
  ```csharp
  public BoxSelectionTool(
      Vector2 startPos,
      ISimulationView view,
      EntityQuery query,
      Func<Entity, Vector2?>? getEntityPosition,
      Action<List<Entity>> onSelectionComplete,
      Action onCancel)
  {
      _startPos   = startPos;
      _currentPos = startPos;
      _view       = view;
      _query      = query;
      _getEntityPosition = getEntityPosition ?? (e =>
          view.HasComponent<SimTransform>(e)
              ? new Vector2(view.GetComponentRO<SimTransform>(e).Position.X,
                            view.GetComponentRO<SimTransform>(e).Position.Y)
              : (Vector2?)null);
      _onSelectionComplete = onSelectionComplete;
      _onCancel            = onCancel;
      _isActive            = true;
  }
  ```
- In `FinishSelection`, replace:
  ```csharp
  var pos = _adapter.GetPosition(_view, entity);
  ```
  with:
  ```csharp
  var pos = _getEntityPosition(entity);
  ```

---

## Step 2: Delete FDP Legacy Files

Delete these files entirely (use `git rm` or file system delete):

```
FDP/Engine/Fdp.Presentation/Vis2D/Adapters/PerspectiveEntityVisualizerBase.cs
FDP/Engine/Fdp.Presentation/Vis2D/Layers/EntityRenderLayer.cs
FDP/Engine/Fdp.Presentation/Vis2D/Defaults/DelegateAdapter.cs
```

---

## Step 3: Delete Hrot Adapter/Visualizer Files

Delete entirely:

```
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/SstVisualizerAdapter.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/SstVisualizerAdapterConstants.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/StubVisualizerAdapter.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/StubVisualizerConstants.cs
Hrot/Subsystems/Hrot.CGF/CgfDebugVisualizerAdapter.cs
Hrot/Subsystems/Hrot.SimHost/Visualization/SimHostVehicleVisualizer.cs
Hrot/Subsystems/Hrot.Editor/Adapters/EditorPerspectiveVisualizer.cs
```

---

## Step 4: Delete Hrot Legacy Layer Files

Delete entirely:

```
Hrot/Subsystems/Hrot.IG/Layers/EffectRenderLayer.cs
Hrot/Subsystems/Hrot.IG/Layers/ZoneObstacleRenderLayer.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/RouteRenderLayer.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/MapOverlayRenderLayer.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/MissionRenderLayer.cs
Hrot/Engine/Hrot.Presentation/Adapters/ProjectileLayerFactory.cs
```

---

## Step 5: Update Hrot StandardInteractionTool (ScenarioEditor.Tools)

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/StandardInteractionTool.cs`

This is the IG/Editor wrapper around the FDP `StandardInteractionTool`.  It currently takes
`IVisualizerAdapter adapter` as a constructor parameter and forwards it to the inner FDP tool.

Changes:
- Remove `using Fdp.Toolkit.Vis2D.Abstractions;` import.
- Remove the `IVisualizerAdapter adapter` parameter from the constructor.
- Update the constructor body so the inner FDP `StandardInteractionTool` is created without an adapter:
  ```csharp
  public StandardInteractionTool(
      EntityRepository      world,
      EntityQuery           query,
      DefaultSelectionState selection)
  {
      _world     = world;
      _selection = selection;
      _inner = new FdpStandardInteractionTool(world, query);
      _inner.OnEntitySelectRequest += HandleEntitySelectRequest;
      _inner.OnRegionSelected      += HandleRegionSelected;
  }
  ```
  (The FDP tool will default to reading `SimTransform` for entity position.)

---

## Step 6: Update SimHostVisualization.cs

**File:** `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`

Remove the following from `Initialize()`:
- The `_visualizer = new SimHostVehicleVisualizer(...)` line.
- The `new EntityRenderLayer("Vehicles", ...)` block and the `_map.AddLayer(...)` call for it.
- The `_map.AddLayer(ProjectileLayerFactory.CreateLayer(...))` call.
- The `_interactionTool = new StandardInteractionTool(repo, _vehicleQuery, _visualizer);` line —
  replace it with `_interactionTool = new StandardInteractionTool(repo, _vehicleQuery);`
- Remove any `using` directives that are now unused:
  - `using Hrot.SimHost.Visualization;` (SimHostVehicleVisualizer)
  - `using Hrot.Presentation.Adapters;` (ProjectileLayerFactory)
  - `using Fdp.Toolkit.Vis2D.Layers;` if only used for EntityRenderLayer

Also remove the `_visualizer` private field declaration if it was `SimHostVehicleVisualizer?`.

Keep the `DebugGizmoLayer` (already added in BATCH-20). The `_interactionTool` field type
is still `StandardInteractionTool?` — verify the field declaration remains correct.

---

## Step 7: Update IgApplication.cs

**File:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

Remove from `InitializeEcs()` (the early canvas setup section, around line 773):
- `_canvas.AddLayer(new EffectRenderLayer(_world));`
  (This is the duplicate early-registration that predates the NED section.)

Remove from the main NED canvas layer section (around lines 1060-1128):
- `var adapter = new NedVisualizerAdapter();`
- `var layer = new EntityRenderLayer(...) { Canvas = _canvas };`
- `_canvas.AddLayer(layer);`
- The `var overlayLayer = new MapOverlayRenderLayer(...);` and `_canvas.AddLayer(overlayLayer);`
- The `var missionLayer = new MissionRenderLayer(...);` and `_canvas.AddLayer(missionLayer);`
- The `var routeRenderLayer = new RouteRenderLayer(...);` and `_canvas.AddLayer(routeRenderLayer);`
- `_canvas.AddLayer(new Hrot.IG.Layers.EffectRenderLayer(_world));`
- `_canvas.AddLayer(new Hrot.IG.Layers.ZoneObstacleRenderLayer(_world));`

Update the `StandardInteractionTool` construction line from:
```csharp
var interactionTool = new StandardInteractionTool(_world, query, adapter, selection);
```
to:
```csharp
var interactionTool = new StandardInteractionTool(_world, query, selection);
```

Remove `using` directives that are now unused:
- Any `using` for `NedVisualizerAdapter`, `EntityRenderLayer`, `MapOverlayRenderLayer`,
  `RouteRenderLayer`, `MissionRenderLayer`, `EffectRenderLayer`, `ZoneObstacleRenderLayer`.

Keep: `SelectionRenderSystem` layer, `DebugGizmoLayer`, all other canvas logic.

---

## Step 8: Update CgfSubsystem.cs

**File:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`

In the non-headless visualization block, remove:
- `_visualizerAdapter = new CgfDebugVisualizerAdapter(...);`
- `var renderLayer = new EntityRenderLayer(...) { Canvas = _canvas };`
- `_canvas.AddLayer(renderLayer);`
- The `new Hrot.ScenarioEditor.Rendering.MissionRenderLayer(_context.World, _context.GeoTransform!)` AddLayer call.

Update `StandardInteractionTool` construction from:
```csharp
_interactionTool = new StandardInteractionTool(_context.World, _entityQuery, _visualizerAdapter);
```
to:
```csharp
_interactionTool = new StandardInteractionTool(_context.World, _entityQuery);
```

Remove the `_visualizerAdapter` private field declaration (`CgfDebugVisualizerAdapter? _visualizerAdapter;`).

Remove `using` directives that are now unused:
- Any import for `CgfDebugVisualizerAdapter`, `EntityRenderLayer`, `MissionRenderLayer`.

Keep: `cgfGizmoLayer` (DebugGizmoLayer, already added in BATCH-21).

---

## Step 9: Update EditorSubsystem.cs

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

This is the largest change.  The Editor had NO gizmo infrastructure — add it now.

### 9a. Add field declaration

In the "Production visualizer dependencies" fields section (~line 206), add:
```csharp
private DebugPrimitiveBuffer? _gizmoBuffer;
```

### 9b. Add gizmo kernel registration (before `_kernel.Initialize()`)

After step 4f (EventEffectModule registration, around `_kernel.RegisterModule(new EventEffectModule())`)
and before step 5 (`_kernel.Initialize()`), insert:

```csharp
// ── 4g. Gizmo subsystem — local stateless gizmo rendering ─────────────────
// The Editor has no DDS transport; primitives are produced locally and consumed
// by a DebugGizmoLayer on the canvas.
_gizmoBuffer = new DebugPrimitiveBuffer();
var editorStatelessGizmoRegistry = new StatelessGizmoRegistry();
// Auto-register all [GizmoProjector]-decorated gizmos in Hrot.ScenarioEditor.Gizmos
// (IgEntityPresentationGizmo, RouteGizmo, MapOverlayGizmo, EffectPresentationGizmo, ...).
Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll(
    new GizmoRegistry(), editorStatelessGizmoRegistry, new GizmoSettingsRegistry());
// MissionPresentationGizmo requires IGeographicTransform — register manually.
editorStatelessGizmoRegistry.Register(
    new Hrot.ScenarioEditor.Gizmos.MissionPresentationGizmo(geoTransform),
    new[] { typeof(SimTransform), typeof(SelectionState) });
_kernel.RegisterGlobalSystem(new StatelessGizmoSystem(editorStatelessGizmoRegistry, _gizmoBuffer));
```

Required new `using` directives (add at top of file if not already present):
- `using Fdp.Toolkit.Diagnostics.Gizmos;`       (StatelessGizmoRegistry, GizmoRegistry, StatelessGizmoSystem)
- `using Fdp.Toolkit.Diagnostics.Gizmos.Settings;`  (GizmoSettingsRegistry)
- `using Fdp.Toolkit.Diagnostics;`              (DebugPrimitiveBuffer)
- `using Fdp.Toolkit.Vis2D.Layers;`             (DebugGizmoLayer)

### 9c. Remove legacy layers from the non-headless canvas block (step 10)

In the `if (!_headless)` block (around lines 568-695), remove:

1. The `entityQuery` build with `Without<MapOverlayStyle>` / `WithoutManaged<RoutePlan>` exclusions —
   this query is no longer needed (gizmos have their own `[GizmoProjector]`-based queries).
   The entity query was only used for `EntityRenderLayer` and `StandardInteractionTool`.
   Remove the `var entityQuery = ...Build();` block.

2. The `EditorPerspectiveVisualizer` construction block:
   ```csharp
   var visualizerAdapter = new Hrot.Editor.Adapters.EditorPerspectiveVisualizer(
       new Fdp.Toolkit.Vis2D.Shapes.DefaultEntityShapeLibrary());
   var renderLayer = new EntityRenderLayer(...) { Canvas = _canvas };
   _canvas!.AddLayer(renderLayer);
   ```

3. The overlay query + `MapOverlayRenderLayer`:
   ```csharp
   var overlayQuery = _world.Query()...Build();
   _canvas.AddLayer(new MapOverlayRenderLayer(_world, overlayQuery));
   ```

4. The route query + `RouteRenderLayer`:
   ```csharp
   var routeQuery = _world.Query()...Build();
   _canvas.AddLayer(new RouteRenderLayer(_world, routeQuery, _fdpInspectorState));
   ```

5. The `ZoneObstacleRenderLayer`:
   ```csharp
   _canvas.AddLayer(new Hrot.IG.Layers.ZoneObstacleRenderLayer(_world));
   ```

6. The `MissionRenderLayer`:
   ```csharp
   _canvas.AddLayer(new MissionRenderLayer(_world, geoTransform));
   ```

7. The `EffectRenderLayer`:
   ```csharp
   _canvas.AddLayer(new EffectRenderLayer(_world));
   ```

8. The `ProjectileLayerFactory.CreateLayer(...)` AddLayer call.

### 9d. Add DebugGizmoLayer to canvas

After removing the legacy layers, add the gizmo layer in the non-headless block (insert after
step 9c removals, before the interaction tool creation):
```csharp
// Gizmo layer — renders entity presentation primitives produced locally by StatelessGizmoSystem.
_canvas!.AddLayer(new DebugGizmoLayer(31, _gizmoBuffer!, _world.Bus, _canvas, _world));
```

### 9e. Build entity query for interaction tool

The `EditorInteractionTool` (= `StandardInteractionTool`) needs an `EntityQuery` for entity
picking and box selection.  Rebuild a minimal query after the layer removals:
```csharp
var entityQuery = _world.Query()
    .With<NetworkIdentity>()
    .With<SimTransform>()
    .WithLifecycle(EntityLifecycle.All)
    .Build();
```

### 9f. Update StandardInteractionTool construction

Replace:
```csharp
_interactionTool = new EditorInteractionTool(_world, entityQuery, visualizerAdapter, _selectionState);
```
with:
```csharp
_interactionTool = new EditorInteractionTool(_world, entityQuery, _selectionState);
```

(The `EditorInteractionTool` alias resolves to `Hrot.ScenarioEditor.Tools.StandardInteractionTool`
which after Step 5 takes 3 parameters.)

### 9g. Remove now-unused using directives

Remove from `EditorSubsystem.cs` any `using` statements for:
- `Hrot.Editor.Adapters` (EditorPerspectiveVisualizer) — if this was a using directive.
  (May have been referenced as a fully-qualified name; remove the type reference.)
- `Fdp.Toolkit.Vis2D.Shapes` (DefaultEntityShapeLibrary) — if no longer used.
- `Fdp.Toolkit.Vis2D.Layers` — check if still needed; keep if `SelectionRenderSystem` or
  other layers remain.
- Any other imports that only served the deleted types.

The alias `using EditorInteractionTool = Hrot.ScenarioEditor.Tools.StandardInteractionTool;`
at line ~78 should remain — the alias name is still used in the field and construction.

### 9h. Update Shutdown()

Remove `_interactionTool = null;` if the field was removed — but keep it since the field is
still present (just the type is unchanged).  Remove any nulling of `_visualizerAdapter` or
other fields that no longer exist.

---

## Step 10: Delete Test Files

Delete entirely:

```
Hrot/Subsystems/Hrot.IG.Tests/NedVisualizerAdapterTests.cs
Hrot/Subsystems/Hrot.IG.Tests/StubVisualizerAdapterTests.cs
Hrot/Subsystems/Hrot.IG.Tests/StandardInteractionToolTests.cs
FDP/Engine/Fdp.Presentation.Tests/Vis2D/Layers/EntityRenderLayerTests.cs
FDP/Engine/Fdp.Presentation.Tests/Vis2D/Defaults/DelegateAdapterTests.cs
```

---

## Step 11: Update ToolInteractionIntegrationTests.cs

**File:** `Hrot/Subsystems/Hrot.IG.Tests/ToolInteractionIntegrationTests.cs`

### 11a. Delete test that exercises EntityRenderLayer

The test `CreationTool_SpawnAndTag_EntityPickableByRenderLayer` (and its helper)
uses `EntityRenderLayer` and `NedVisualizerAdapter`.  Delete this entire test method.

### 11b. Update StandardInteractionTool_SelectEntity_SetsEcsSelectionStateTrue

This test still exercises valid functionality (ECS SelectionState update on selection).
Update the `StandardInteractionTool` construction to use the new 3-parameter constructor:

Old code:
```csharp
var adapter         = new NedVisualizerAdapter();
var selection       = new DefaultSelectionState();
var pickQuery       = repo.Query().With<SimTransform>().Build();
var interactionTool = new StandardInteractionTool(repo, pickQuery, adapter, selection);
```

New code:
```csharp
var selection       = new DefaultSelectionState();
var pickQuery       = repo.Query().With<SimTransform>().Build();
var interactionTool = new StandardInteractionTool(repo, pickQuery, selection);
```

Remove the `using Hrot.ScenarioEditor.Adapters;` import if it was only needed for
`NedVisualizerAdapter`.  Keep the `using Hrot.ScenarioEditor.Tools;` import.

Also remove any remaining references to `EntityRenderLayer` (e.g., in comments or
the `var layer = ...` helper that was local to the deleted test).

---

## Step 12: Update IgApplicationPanelTests.cs

**File:** `Hrot/Subsystems/Hrot.IG.Tests/IgApplicationPanelTests.cs`

Delete the following two test methods and their shared helper:
- `EntityRenderQuery_MatchesEntityWithNetworkIdentityAndSimTransform`
- `EntityRenderQuery_DoesNotMatchEntityWithoutNetworkIdentity`
- `GetEntityRenderQuery(IgApplication app)` private static helper method
- `QueryContains(EntityQuery query, Entity entity)` private static helper method
  (if it is ONLY used by the two deleted tests; verify this before deleting).

These tests verified the `EntityRenderLayer` query configuration which is now replaced by
the `[GizmoProjector]` attribute on `IgEntityPresentationGizmo` (already tested in
`Hrot.IG.Tests/Gizmos/PresentationGizmoTests.cs`, test SC_GZ057_5).

Remove the `using Fdp.Toolkit.Vis2D.Layers;` import if it was only needed for `EntityRenderLayer`.

---

## Step 13: Update EditorSubsystemBootTests.cs (comments only)

**File:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorSubsystemBootTests.cs`

Update the XML doc comments in:
- `SpawnEntity_WithMapOverlayStyle_UpdateFramesDoNotThrow` — the comment references
  `MapOverlayRenderLayer` and `EntityRenderLayer`.  Update to explain that the BUG11 fix now
  uses `MapOverlayGizmo` via the `StatelessGizmoSystem` instead of a dedicated render layer.
- `SpawnEntity_WithRoutePlan_UpdateFramesDoNotThrow` — similarly update to reference
  `RouteGizmo` instead of `RouteRenderLayer` and `EntityRenderLayer`.

The test logic itself does NOT change (it tests ECS plumbing, not rendering).

---

## Step 14: Update FDP StandardInteractionToolTests.cs

**File:** `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/StandardInteractionToolTests.cs`

Remove `IVisualizerAdapter` from all three test methods.  The FDP `StandardInteractionTool`
constructor no longer takes an adapter.

For each `new StandardInteractionTool(view.Object, query, adapter.Object)` call:
- Remove the `adapter.Object` argument: `new StandardInteractionTool(view.Object, query)`

For `new Mock<IVisualizerAdapter>()` — remove these mock declarations entirely.

Remove:
- `using Fdp.Toolkit.Vis2D.Abstractions;` import.
- Any `adapter.Setup(...)` calls (no longer needed).

The test `FindEntity_SelectsClosest` mocks an `IMapLayer` that returns the correct entity
from `PickEntity`.  The adapter mock was set up with positions but was not the picking mechanism
(picking goes through `canvas.PickTopmostEntity`).  After removing the adapter, verify that
the test still compiles and passes — the mock layer logic is unaffected.

---

## Build Verification

After all changes, build and verify:

```
dotnet build IOS-IG-SimHost.sln --no-incremental
```

Expected: 0 errors, 0 warnings related to deleted types.

Then run tests (excluding known pre-existing failures):
```
dotnet test
```

Expected pre-existing failures (do NOT count against this batch):
- ~26 in `Fdp.Toolkits.Tests`
- ~4 in `Hrot.IG.Tests` (CS011 EntityInfoTranslator)
- ~3 in `Fdp.Presentation.Tests` (EntityInspectorPanelTests)
- ~20 in `Hrot.SimHost.Tests`

---

## Batch Report

After completing the work, write a report to `.dev/gizmos-1/reports/BATCH-22-REPORT.md` with:
- Summary of all deletions (file list)
- Summary of all modifications (file + what changed)
- Build result (0 errors)
- Test counts per project (pass/fail)
- Any deviations from instructions with justification
