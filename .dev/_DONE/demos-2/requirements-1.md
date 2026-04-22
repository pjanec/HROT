I do not se the new doctrine FireAtTarget_BT in the list of doctrines when editing the missions on ExCon. Pls add it there.

------------


I would also like to replace the stub visualization with the production version. how to do this?

Replacing the `StubVisualizerAdapter` with the production version requires more than simply swapping the class instantiation. The FDP engine enforces a strict data-driven presentation layer. The production visualizer, `NedVisualizerAdapter` (which superseded the initial `SstVisualizerAdapter` implementation), does not calculate its own state. It strictly gates visibility and rendering on the presence of computed ECS components like `CullingState` and `ResolvedStyle`.

If you merely swap the adapter without provisioning the systems that compute these components, all entities will instantly disappear from your offline editor because `NedVisualizerAdapter.GetPosition` returns `null` if `CullingState` is missing.

To properly integrate the production visualizer into the offline `EditorSubsystem`, you must satisfy its architectural dependencies by executing the following steps.

1\. Register the Presentation Components

The ECS world must be made aware of the IG-tier presentation components. During `EditorSubsystem.Initialize`, alongside your existing registrations, add the required types:

```
_world.RegisterComponent<ResolvedStyle>(); [4]
_world.RegisterComponent<CullingState>(); [5]
_world.RegisterComponent<VisualData>(); [6]
_world.RegisterManagedComponent<IgSymbolOverride>(); [7]
```

2\. Provision the Configuration Singletons

The culling and style resolution systems require shared configuration objects that act as Layer-3 overrides and viewport boundaries. Instantiate these as fields in `EditorSubsystem`:

```
private readonly MapUserConfig _userConfig = new(); [9]
private readonly MapCameraViewport _cameraViewport = new(); [11]
```

3\. Register the IG Compute Modules

You must inject the systems that actually compute the presentation state into the `ModuleHostKernel`. Before calling `_kernel.Initialize()`, register the IG modules:

```
// Computes CullingState.IsVisible and LodLevel based on the camera viewport [12].
_kernel.RegisterModule(new MapCullingModule(_cameraViewport)); [13]

// Evaluates the 3-layer style merge (TKB -> Network -> User Config) to output ResolvedStyle [10].
_kernel.RegisterModule(new StyleResolutionModule(_userConfig, localNodeId: 0)); [14]
```

4\. Swap the Adapter

You can now safely replace the stub in `EditorSubsystem.cs`:

```
// Replace: var visualizerAdapter = new StubVisualizerAdapter(); [15]
var visualizerAdapter = new NedVisualizerAdapter(localNodeId: 0); [16]

var renderLayer = new EntityRenderLayer(
    "Entities", layerBitIndex: -1,
    _world, entityQuery, visualizerAdapter, _selectionState)
{
    Canvas = _canvas
};
```

5\. Feed the Viewport Pipeline

Because `MapCullingSystem` relies on `MapCameraViewport` to determine which entities are on-screen, you must update the viewport state every frame _before_ the kernel ticks. In `EditorSubsystem.Update`, add the following projection logic right after `_canvas?.Update(deltaTime)`:

```
if (_camera != null && _canvas != null)
{
    var topLeft = _camera.ScreenToWorld(Vector2.Zero); [17]
    var bottomRight = _camera.ScreenToWorld(new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight())); [17]

    _cameraViewport.WorldMinX = MathF.Min(topLeft.X, bottomRight.X); [17]
    _cameraViewport.WorldMaxX = MathF.Max(topLeft.X, bottomRight.X); [17]
    _cameraViewport.WorldMinY = MathF.Min(topLeft.Y, bottomRight.Y); [17]
    _cameraViewport.WorldMaxY = MathF.Max(topLeft.Y, bottomRight.Y); [17]
    _cameraViewport.Zoom = _camera.Zoom; [17]
}

_kernel?.Update(); [18]
```

Architectural Critique on TKB Definitions

If your offline TKB database (`tkbDb`) only registers physical attributes, the entities will still render as fallback circles because `StyleResolutionSystem` will not find `VisualData` at spawn time. You must ensure that your `TkbTemplate` definitions in the editor include the `WithVisual` configuration block (e.g., setting `SymbolCode` and `ModelPath`), mirroring how `NedTkbCatalog` provisions production entities. Without the foundational TKB data, the style resolver has nothing to compute.

regarding all the features and tasks needed to implement, pls enumerate them and group into implementation stages

Here is the objective implementation roadmap. To maintain strict architectural boundaries, the work must be sequenced to update the core execution mechanics before layering on the cognitive behaviors and the UI.

Stage 1: Core Combat Primitive Refactoring

To safely process continuous-time floating-point values deterministically, we must adapt the unmanaged structs within the combat pipeline.

-   **Task 1.1:** Modify the `WeaponState` ECS component to replace `int CooldownTicksRemaining` with `float CooldownSecondsRemaining`.-   **Task 1.2:** Update the `AimAndFireParams` struct that is packed into the `WeaponChannel.Params` memory block, replacing `int CooldownTicks` with `float CooldownSeconds`.-   **Task 1.3:** Refactor `AimAndFireExecutor.Execute` to accept and subtract the continuous `dt` delta rather than performing a fixed discrete integer decrement.-   **Task 1.4:** Locate and update existing AI nodes (such as `InsurgentNodes.Action_AimAndFire`) to initialize `CooldownSeconds = 0f` to prevent compilation breaks.-   **Task 1.5:** Increment the Flight Recorder binary format version. Mutating a Tier 1 component like `WeaponState` breaks binary backward compatibility for older replay files.

Stage 2: Cognitive & Behavior Implementation

With the pipeline adapted, we define the new doctrine relying entirely on the `BrainBlackboard` for state memory.

-   **Task 2.1:** Create a `FireAtTargetParamsJsonDto` class containing `TargetNetworkId` (long), `MaxRounds` (int), and `CooldownSeconds` (float).-   **Task 2.2:** Write a parser delegate `ParseFireAtTargetParams` that deserializes the DTO, resolves the target entity via `NetworkEntityMap`, and strictly packs the layout into `BrainBlackboard.Memory`.-   **Task 2.3:** Implement `Condition_IsTargetValidAndVisible` to query the entity's `TargetMemory`. It must evaluate threat scores to guarantee the target remains organically visible.-   **Task 2.4:** Implement `Action_FireWithLimit` to evaluate the internal rounds-fired counter, pack `AimAndFireParams`, and increment the `WeaponChannel.ActionInstanceId` to trigger the execution pipeline.-   **Task 2.5:** Define a unique constant in `CgfDoctrineIds` and register the `FireAtTarget_BT` doctrine definition in `CgfDoctrineSetup.RegisterAll`.

Stage 3: Editor UI Integration

The mission editing surface must ergonomically expose the new parameters while safely delegating map-based interactions.

-   **Task 3.1:** Extend `MissionPanel.DrawContent` to explicitly intercept the `FireAtTarget` behavior ID.-   **Task 3.2:** Implement a dedicated rendering function leveraging `IMapPickService.PickEntityAsync` for visual target selection.-   **Task 3.3:** Render a standard ImGui multiline text block to allow the operator to manually view and override the raw JSON parameters, preserving existing patterns.

Stage 4: Presentation Layer Upgrade

To replace the offline editor's fallback circles with production-grade symbology, we must satisfy the strict data-driven dependencies of the presentation tier.

-   **Task 4.1:** Register the required IG-tier presentation components in `EditorSubsystem.Initialize`: `ResolvedStyle`, `CullingState`, `VisualData`, and `IgSymbolOverride`.-   **Task 4.2:** Provision and inject the presentation configuration singletons, specifically `MapUserConfig` and `MapCameraViewport`.-   **Task 4.3:** Register the IG compute modules (`MapCullingModule` and `StyleResolutionModule`) into the `ModuleHostKernel`.-   **Task 4.4:** Replace the instantiation of `StubVisualizerAdapter` with `NedVisualizerAdapter`.-   **Task 4.5:** Update `EditorSubsystem.Update` to extract current camera boundaries from the `MapCanvas` and feed them into `MapCameraViewport` strictly _before_ calling `_kernel.Update`.-   **Task 4.6:** Ensure the TKB offline definitions are provisioned via `NedTkbBuilder.WithVisual` so that the base models and symbol codes are present to compute `ResolvedStyle`.

oh ok, i did not mean the new FireAtTarget and the production visualization, consider this task done, it has been dispatched to the developer.

I meant all the stuff mentioned earlier.

Stage 1: Offline Engine Capability Alignment

To execute the Grand Integration Demo scenario within the offline Editor, the application's composition root must be brought to parity with the live simulation nodes. The lack of proper schema registration currently causes the Editor to fall back to default states upon deserialization.

-   **Task 1.1: TKB Registration:** Provision the Transient Knowledge Base by invoking `DemoTkbSetup.RegisterAll(tkb)` during `EditorSubsystem.Initialize`. This guarantees that the entity blueprints for `MilitaryApc` (2001), `InfantrySoldier` (2002), and `Insurgent` (2003) are resolvable when the `ScenarioSerializer` reads the file.-   **Task 1.2: Doctrine Registration:** Invoke `UrbanCombatNewScenario.RegisterUrbanCombatDoctrines` against the Editor's `DoctrineRegistry` during kernel bootstrap. This satisfies the behavior tree definitions so the `MissionAdapterSystem` can correctly assign the `Ambush` and `InfantryCombat` behaviors rather than safely defaulting to "Idle".

Stage 2: Offline Presentation Parity

The offline Editor must visualize the same kinematic and combat events as the networked Image Generator (IG) without duplicating presentation logic.

-   **Task 2.1: Effect System Integration:** Register the `EventEffectModule` within the Editor's kernel bootstrap (e.g., inside `EditorSystemsModule`). This module provisions the `EventToEffectSystem` and `VisualEffectCleanupSystem`, which are required to consume `FireInteractionEvent`s and spawn ephemeral `EffectType.Explosion` and `EffectType.Tracer` entities.-   **Task 2.2: Cognitive State Tooltips:** Integrate the `CgfDebugVisualizerAdapter` into the Editor's `EntityRenderLayer` setup. Because the Editor process locally hosts both the Brain and presentation tiers, this adapter can safely query `TargetMemory`, `WeaponChannel`, and `ActorCapabilityState` to render rich diagnostic tooltips on mouse hover.

Stage 3: Mission Authoring UI

To support user-friendly scenario editing, the presentation tier must decouple the domain logic from raw string manipulation.

-   **Task 3.1: ImGui Parameter Widgets:** Extend the `MissionPanel.DrawContent` method to explicitly intercept the Urban Combat doctrine IDs. Implement dedicated rendering functions that parse the underlying parameter schemas and expose native ImGui controls (sliders, checkboxes, combo boxes) to replace the fallback raw JSON multiline buffer.

After the hexagon architecture refactor the visualization is missing also any representation of entity's mission; for moveToLocation tasks there used to be an orange line drawn to the waypoints. how to return this back? to both the editor and to the all-networked-subsystems-in-one-process mode.

To restore the mission visualization across both the offline Editor and the networked cluster modes, we must rectify several structural flaws introduced during the architecture refactor.

Objectively, the `MissionRenderLayer` currently exists in the codebase but suffers from layer boundary violations, tight geographic coupling, and incomplete composition root registrations.

Here is the architectural remediation required to solve this.

1\. Correct the Architectural Boundary

The `MissionRenderLayer` currently resides in the `Hrot.ScenarioEditor.Rendering` namespace. However, it is being instantiated directly by `IgApplication`. This violates dependency direction, as the IG tier should not depend on the Scenario Editor.

**Action:** Move `MissionRenderLayer.cs` into the `Hrot.IG.Layers` namespace and assembly so both the IG and Editor can safely consume it.

2\. Remove Strict Geographic Coupling

The current implementation of `MissionRenderLayer` blindly assumes that `_geoTransform` is instantiated and that the mission JSON strictly provides `targetLat` and `targetLon`. In the offline Editor mode, `IGeographicTransform` is null and coordinates are purely Cartesian (`X` and `Y`). If executed in the Editor today, this layer will throw a `NullReferenceException` when attempting to call `_geoTransform.ToCartesian()`.

**Action:** Refactor `MissionRenderLayer.Draw` to gracefully handle flat Cartesian fallbacks.

```
using System.Numerics;
using System.Text.Json;
using Fdp.Toolkit.Behavior.Components;
using Hrot.IG.Components;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;
using Raylib_cs;

namespace Hrot.IG.Layers; // Corrected namespace

public class MissionRenderLayer : IMapLayer
{
    public const string LayerName = "MissionRoutes";
    public const int MissionRouteseLayerBitIndex = 4;

    private readonly ISimulationView _view;
    private readonly EntityQuery _query;
    private readonly IGeographicTransform? _geoTransform; // Nullable for offline editor

    public string Name => LayerName;
    public int LayerBitIndex => MissionRouteseLayerBitIndex;

    public MissionRenderLayer(ISimulationView repo, IGeographicTransform? geoTransform)
    {
        _view = repo;
        _query = repo.Query()
            .WithManaged<ActiveMissionPlan>()
            .With<SimTransform>()
            .With<SelectionState>()
            .Build();
        _geoTransform = geoTransform;
    }

    public void Update(float dt) { }

    public void Draw(RenderContext ctx)
    {
        foreach (var entity in _query)
        {
            // Note: The orange line is ONLY drawn for actively selected entities.
            if (!_view.GetComponentRO<SelectionState>(entity).IsSelected) continue;

            var activePlan = _view.GetManagedComponentRO<ActiveMissionPlan>(entity);
            if (activePlan?.Plan?.Tasks == null) continue;

            ref readonly var simTr = ref _view.GetComponentRO<SimTransform>(entity);
            var currentPos = new Vector2(simTr.Position.X, simTr.Position.Y);
            Vector2 lastPos = currentPos;

            foreach (var task in activePlan.Plan.Tasks)
            {
                if (string.IsNullOrEmpty(task.BehaviorParams)) continue;

                float targetX = float.NaN;
                float targetY = float.NaN;

                try
                {
                    using var doc = JsonDocument.Parse(task.BehaviorParams);
                    var root = doc.RootElement;

                    // 1. Attempt to read Cartesian X/Y directly (Offline Editor format)
                    if (root.TryGetProperty("X", out var xEl) && root.TryGetProperty("Y", out var yEl))
                    {
                        targetX = xEl.GetSingle();
                        targetY = yEl.GetSingle();
                    }
                    // 2. Fallback to GeoLat/GeoLon and convert if Transform is available (Networked format)
                    else if (root.TryGetProperty("targetLat", out var latEl) && root.TryGetProperty("targetLon", out var lonEl))
                    {
                        if (_geoTransform != null)
                        {
                            var cartesian = _geoTransform.ToCartesian(latEl.GetSingle(), lonEl.GetSingle(), 0.0);
                            targetX = (float)cartesian.X;
                            targetY = (float)cartesian.Y;
                        }
                    }
                }
                catch { /* Suppress parsing failures */ }

                if (!float.IsNaN(targetX) && !float.IsNaN(targetY))
                {
                    var targetPos = new Vector2(targetX, targetY);
                    float thickness = 2.0f / (ctx.Zoom > 0 ? ctx.Zoom : 1f);

                    Raylib.DrawLineEx(lastPos, targetPos, thickness, Color.Orange);

                    float pointSize = 6.0f / (ctx.Zoom > 0 ? ctx.Zoom : 1f);
                    Raylib.DrawCircleV(targetPos, pointSize, Color.DarkBlue);
                    Raylib.DrawCircleLinesV(targetPos, pointSize, Color.SkyBlue);

                    lastPos = targetPos;
                }
            }
        }
    }

    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
```

3\. Register in the Editor Composition Root

While `IgApplication.InitializeEmbedded` currently adds `MissionRenderLayer` to its canvas, the offline `EditorSubsystem` entirely omits it.

**Action:** In `EditorSubsystem.cs`, locate the `Initialize` method where layers are appended to `_canvas`. Add the refactored layer, passing `null` for the geographic transform since the editor operates strictly in Cartesian space.

```
// Inside EditorSubsystem.Initialize(SubsystemConfig config)
var routeRenderLayer = new RouteRenderLayer(_world, routeQuery, _fdpInspectorState);
_canvas.AddLayer(routeRenderLayer);

// ADD THIS:
var missionRenderLayer = new Hrot.IG.Layers.MissionRenderLayer(_world, geoTransform: null);
_canvas.AddLayer(missionRenderLayer);
```

By ensuring the presentation logic correctly respects the data boundaries (Cartesian vs Geodetic) and registering it uniformly across both composition roots, you will restore the visualization deterministically for both execution paths. Remember that the layer explicitly checks `SelectionState.IsSelected`, so the orange lines will purposefully only appear when the operator clicks the entity.

the missing geoTransform in the editor is a bad choice; being all-in-one system has nothing to do with using geodetic coordinates. Let's instantiate the geo translator there so that we don;t need to do any fallbacks. All parameters need to be crystal clear - either they are always cartesian or always geodetic. In this case always geodetic is probbaly beter choice.

You are absolutely correct. The architectural separation between an offline tool and a networked cluster does not justify polluting the domain model with ambiguous coordinate systems. Mission parameters should be strictly and consistently geodetic (`targetLat` and `targetLon`), relying on the ECS simulation layer to project them to Cartesian via `IGeographicTransform` at execution time.

By removing the Cartesian fallbacks and provisioning the `IGeographicTransform` directly in the `EditorSubsystem`, we enforce a single source of truth for the coordinate space.

Here is the architectural remediation required to implement this strict geodetic consistency:

1\. Provision `IGeographicTransform` in the Editor Composition Root

In `EditorSubsystem.Initialize`, instantiate the standard WGS-84 transform using `HrotEnvironment` and inject it into the layers and adapters that were previously receiving `null`.

```
// Inside EditorSubsystem.Initialize(SubsystemConfig config)

// ── 3. Shared services ────────────────────────────────────────────
// ADD THIS: Instantiate the standard geodetic transform
var geoTransform     = HrotEnvironment.CreateGeoTransform(); 
var entityMap        = new NetworkEntityMap();
var doctrineRegistry = new DoctrineRegistry();
// ... 

// ── 10. Canvas-dependent adapters, layers, and interaction tool ───
if (!_headless)
{
    _mapViewConfig    = new MapViewConfig();
    
    // INJECT geoTransform
    _mapPickAdapter   = new EditorMapPickAdapter(_canvas!, geoTransform); 

    // INJECT geoTransform to remove the null fallback
    var jsonCompiler  = Hrot.SimHost.AttributeCompilerFactory.Build(geoTransform);
    _spawnAdapter     = new EditorSpawnAdapter(_canvas!, _world.Bus, jsonCompiler, tkbDb);
    // ...

    // INJECT geoTransform to the mission render layer
    var missionRenderLayer = new Hrot.IG.Layers.MissionRenderLayer(_world, geoTransform);
    _canvas.AddLayer(missionRenderLayer);
// ...
```

2\. Enforce Geodetic Coordinates in the Map Picking Adapter

Currently, `LocationPickerTool` lazily maps Cartesian `X` to Longitude and `Y` to Latitude. This must be structurally corrected to output true WGS-84 geodetic coordinates using the transform.

Update `EditorMapPickAdapter` to accept and pass the transform:

```
// In Hrot.Editor.Adapters.EditorMapPickAdapter.cs
public sealed class EditorMapPickAdapter : IMapPickService
{
    private readonly MapCanvas _canvas;
    private readonly IGeographicTransform _geoTransform;

    public EditorMapPickAdapter(MapCanvas canvas, IGeographicTransform geoTransform)
    {
        _canvas = canvas;
        _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
    }

    public Task<Hrot.Core.Mission.GeoPoint> PickLocationAsync(CancellationToken ct = default)
    {
        var tcs  = new TaskCompletionSource<Hrot.Core.Mission.GeoPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        // INJECT geoTransform into the tool
        var tool = new LocationPickerTool(_geoTransform); 
        // ...
```

Update `LocationPickerTool` to apply the transform on click:

```
// In Hrot.Editor.Tools.LocationPickerTool.cs
public sealed class LocationPickerTool : IMapTool
{
    public string Name => "LocationPicker";
    public Action<GeoPoint>? OnLocationPicked;
    public Action? OnCancelled;

    private MapCanvas? _canvas;
    private readonly IGeographicTransform _geoTransform;

    public LocationPickerTool(IGeographicTransform geoTransform)
    {
        _geoTransform = geoTransform;
    }

    // ...

    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            // Convert flat map Cartesian X/Y into proper WGS-84 coordinates
            var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(worldPos.X, worldPos.Y, 0f));
            var geo = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };
            
            OnLocationPicked?.Invoke(geo);
            OnCancelled?.Invoke();
            _canvas?.PopTool();
            return true;
        }
        return false;
    }
}
```

3\. Strip the Cartesian Fallback from `MissionRenderLayer`

Now that we guarantee `targetLat` and `targetLon` are populated with true geodetic values and the `_geoTransform` is never null, you must remove the dirty `X`/`Y` Cartesian fallback from `MissionRenderLayer.Draw`.

```
// In Hrot.IG.Layers.MissionRenderLayer.cs (after moving it to Hrot.IG as discussed earlier)
public void Draw(RenderContext ctx)
{
    foreach (var entity in _query)
    {
        // ... selection and plan checks ...

        foreach (var task in activePlan.Plan.Tasks)
        {
            if (string.IsNullOrEmpty(task.BehaviorParams)) continue;

            float targetLat = float.NaN;
            float targetLon = float.NaN;

            try
            {
                using var doc = JsonDocument.Parse(task.BehaviorParams);
                var root = doc.RootElement;

                // STRICTLY Geodetic. No fallback to X/Y.
                if (root.TryGetProperty("targetLat", out var latEl) && root.TryGetProperty("targetLon", out var lonEl))
                {
                    targetLat = latEl.GetSingle();
                    targetLon = lonEl.GetSingle();
                }
            }
            catch { /* Suppress parsing failures */ }

            if (!float.IsNaN(targetLat) && !float.IsNaN(targetLon))
            {
                // Guaranteed projection via _geoTransform
                var cartesian = _geoTransform.ToCartesian(targetLat, targetLon, 0.0);
                var targetPos = new Vector2((float)cartesian.X, (float)cartesian.Y);
                
                float thickness = 2.0f / (ctx.Zoom > 0 ? ctx.Zoom : 1f);
                Raylib.DrawLineEx(lastPos, targetPos, thickness, Color.Orange);

                float pointSize = 6.0f / (ctx.Zoom > 0 ? ctx.Zoom : 1f);
                Raylib.DrawCircleV(targetPos, pointSize, Color.DarkBlue);
                Raylib.DrawCircleLinesV(targetPos, pointSize, Color.SkyBlue);

                lastPos = targetPos;
            }
        }
    }
}
```

This strict approach guarantees that all UI parameter text boxes (`targetLat` / `targetLon`) and JSON scenario files persistently reflect true world coordinates, while internal execution systems (like `MoveToExecutor`) securely handle Cartesian physics projection without risking fallback misalignment.

ok pls let's now think how to visualizae the fires and detonations. I have never seen this. But you mentioned there is something prepared in the codebase. Again i need this in both the editor and the networked versions, sharing same code as much as possible

The codebase contains the `EventEffectModule` which provisions the `EventToEffectSystem` and `VisualEffectCleanupSystem`. This pipeline successfully consumes combat events to generate and age ephemeral ECS entities carrying the `VisualEffectState` component.

However, your assumption that this feature is fully prepared is architecturally incorrect. There are two significant structural gaps:

-   **Missing Presentation Layer:** There is no `IMapLayer` implementation in the codebase that actually issues Raylib draw calls for the `VisualEffectState` components. The engine spawns the data, but the canvas never renders it.-   **Event Impedance Mismatch:** `EventToEffectSystem` currently listens for a legacy `FireInteractionEvent`, completely ignoring the modern `WeaponFireNotification` and `DetonationNotification` events published by the `FDP.Toolkit.Combat` pipeline.

To achieve DRY visualization across both the offline Editor and the networked cluster, we must refactor the event consumption, implement the presentation layer, and unify the composition roots.

1\. Refactor `EventToEffectSystem` (Domain Layer)

You must update the system to consume the standard Combat toolkit events. This ensures that whether the system runs in the offline Editor (where Combat systems run locally) or the IG (where DDS translators inject the events), the pipeline is identical.

```
[UpdateInPhase(SystemPhase.Simulation)]
public class EventToEffectSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        var cmd = World.GetCommandBuffer();

        // 1. Process Explosions from DetonationNotification [7]
        var detonations = World.Bus.Read<DetonationNotification>();
        foreach (ref readonly var evt in detonations)
        {
            SpawnExplosion(cmd, evt.HitX, evt.HitY);
        }

        // 2. Process Tracers from WeaponFireNotification [8]
        var weaponFires = World.Bus.Read<WeaponFireNotification>();
        foreach (ref readonly var evt in weaponFires)
        {
            // Resolve positions. If entities are dead, skip tracer.
            if (!World.IsAlive(evt.Shooter) || !World.IsAlive(evt.Target)) continue;
            if (!World.HasComponent<SimTransform>(evt.Shooter) || !World.HasComponent<SimTransform>(evt.Target)) continue;

            var shooterPos = World.GetComponent<SimTransform>(evt.Shooter).Position;
            var targetPos = World.GetComponent<SimTransform>(evt.Target).Position;

            SpawnTracer(cmd, shooterPos.X, shooterPos.Y, targetPos.X, targetPos.Y);
        }
    }

    // ... Keep existing SpawnExplosion and SpawnTracer helpers [9, 10]
}
```

2\. Implement `EffectRenderLayer` (Presentation Layer)

You must create a new `IMapLayer` in the `Hrot.IG.Layers` assembly (making it available to both the IG and the Editor) to actually render the ECS data. It will read `VisualEffectState` and `TracerTarget`.

```
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.IG.Components;
using Raylib_cs;

namespace Hrot.IG.Layers;

public sealed class EffectRenderLayer : IMapLayer
{
    public string Name => "Visual Effects";
    public int LayerBitIndex => -1; // Always visible

    private readonly EntityRepository _world;
    private readonly EntityQuery _query;

    public EffectRenderLayer(EntityRepository world)
    {
        _world = world;
        _query = world.Query()
            .With<SimTransform>()
            .With<VisualEffectState>()
            .Build();
    }

    public void Update(float dt) { }

    public void Draw(RenderContext ctx)
    {
        foreach (var entity in _query)
        {
            ref readonly var tf = ref _world.GetComponentRO<SimTransform>(entity);
            ref readonly var effect = ref _world.GetComponentRO<VisualEffectState>(entity);

            var color = new Color(effect.ColorR, effect.ColorG, effect.ColorB, (byte)(effect.ColorA * effect.Alpha));
            var screenPos = Raylib.GetWorldToScreen2D(new Vector2(tf.Position.X, tf.Position.Y), ctx.Camera);

            if (effect.Type == EffectType.Explosion)
            {
                // Scale explosion radius by camera zoom
                float radius = effect.Scale * ctx.Zoom;
                Raylib.DrawCircleV(screenPos, radius, color);
            }
            else if (effect.Type == EffectType.Tracer && _world.HasComponent<TracerTarget>(entity))
            {
                ref readonly var target = ref _world.GetComponentRO<TracerTarget>(entity);
                var targetScreenPos = Raylib.GetWorldToScreen2D(new Vector2(target.EndX, target.EndY), ctx.Camera);
                
                Raylib.DrawLineEx(screenPos, targetScreenPos, VisualEffectStateConstants.EffectLineWidthPx, color);
            }
        }
    }

    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
```

3\. Unify Composition Roots (Wiring)

To adhere to DRY, both the offline Editor and the networked IG must register the exact same module and layer.

**For the Offline Editor (****EditorSubsystem.cs****):**

-   Add `_kernel.RegisterModule(new EventEffectModule());` before `_kernel.Initialize();`. Because the Editor already runs the full FDP Combat toolkit, it naturally produces the correct notifications.-   Add `_canvas.AddLayer(new EffectRenderLayer(_world));` where the other layers are registered.

**For the Networked IG (****IgApplication.cs****):**

-   `EventEffectModule` is already registered.-   Add `_canvas.AddLayer(new EffectRenderLayer(_world));` to the canvas setup.-   _Network Data Gap:_ The IG does not inherently know about `DetonationNotification`. You must provision a `MunitionDetonationIngressTranslator` in `EntityStatesIngressPack` (similar to how SimHost uses it) to consume `MunitionDetonation` DDS messages and publish local `DetonationNotification` events on the IG's event bus.-   Update `WeaponFireIngressTranslator` to publish `WeaponFireNotification` instead of the deprecated `IgWeaponFireEvent`. The translator must resolve the DDS Network IDs to local IG ghost entities using the `NetworkEntityMap` before publishing.

By driving visuals entirely off the `VisualEffectState` ECS component and standardizing the event bus payload, you guarantee bit-for-bit rendering parity between the standalone scenario editor and the distributed cluster.

regarding the display of mission/brain related stuff (mission waypoints, hovering tooltips with brain details etc) - i need this primarily on the CGF map as a debug feature. IG does not need to display brain details - no extra brain state publishing necessary at this point.

Constraining cognitive visualization to the CGF node is the correct architectural decision. It prevents unnecessary network congestion and strictly adheres to the Bounded Context pattern by isolating decision-making state from the pure-presentation IG tier.

Here is the objective assessment of the current state and the exact steps required to expose these debug visualisations on the CGF map.

1\. Hover Tooltips with Brain Details (Already Implemented)

The codebase already contains a dedicated presentation adapter for the Brain node: the `CgfDebugVisualizerAdapter`.

Because the CGF node natively owns the cognitive ECS components, this adapter directly queries `DoctrineState`, `MissionPlanQueue`, `LocomotionChannel`, `WeaponChannel`, `TargetMemory`, and `ActorCapabilityState` to build a rich multi-line diagnostic tooltip on mouse hover. Furthermore, it colour-codes the entity symbols based on the active doctrine's tier (e.g., Blue for BTree, Teal for HSM).

This adapter is currently wired into `CgfSubsystem.Initialize` when running in non-headless mode. You do not need to implement any additional logic or publishing mechanisms to see these tooltips on the CGF map; they will appear automatically when hovering over entities.

2\. Mission Waypoint Lines (Requires Registration)

To display the orange routing lines connecting the entity to its mission waypoints, we will reuse the `MissionRenderLayer` that we previously refactored to rely strictly on geodetic coordinates.

Because the CGF node owns the `ActiveMissionPlan` managed component (which holds the decoded `BehaviorParams` JSON), the layer can render the paths locally without network synchronization. You simply need to register the layer into the CGF's `MapCanvas`.

In `Hrot.CGF/CgfSubsystem.cs`, locate the non-headless initialisation block where layers are added to the canvas, and inject the `MissionRenderLayer` using the environment's geodetic transform:

```
// Inside CgfSubsystem.Initialize(SubsystemConfig config), non-headless block:

var renderLayer = new EntityRenderLayer(
    "CGF Entities", -1, _context.World, _entityQuery, _visualizerAdapter, _selectionState)
{ Canvas = _canvas };
_canvas.AddLayer(renderLayer);

// ADD THIS: Inject the MissionRenderLayer to draw ActiveMissionPlan waypoints
var missionRenderLayer = new Hrot.IG.Layers.MissionRenderLayer(_context.World, _context.GeoTransform);
_canvas.AddLayer(missionRenderLayer);

_interactionTool = new StandardInteractionTool(_context.World, _entityQuery, _visualizerAdapter);
// ...
```

By keeping the presentation layers data-driven and wiring them directly into the local `MapCanvas` of the CGF node, you achieve full diagnostic visibility of the AI state machine while maintaining a pristine, low-bandwidth boundary between the Brain and the networked cluster.

