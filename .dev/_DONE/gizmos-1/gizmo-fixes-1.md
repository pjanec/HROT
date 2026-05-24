in hrot.editor, entity drag&drop works again. Interaction events shown in the Event browser.
but, in 'clusterrunner -m all', when in ExCon Entity spawner UI  i click ACTIVATE PLACEMENT TOOL, no placement tool appears in the IG perspective.
If in IG's Mini Exconn UI panel i  click "Spawn", the IG map shows the entity newly created, it also shows green filled circle when i click it, but i can not drag & drop it.

This is happening because of two cascading architectural defects in `IgApplication.cs`. The IG's local interaction state is being actively wiped out every frame, and its interaction systems are completely deaf to the UI bus.

Here is exactly what is destroying your IG interaction pipeline:

**1. The Fatal Buffer Wipe (`_ingressTranslator.PollAndApply`)**
In my previous advice, I stated the IG should use `DebugPrimitivesIngressTranslator` to receive remote gizmos. This was fundamentally incorrect. `IgApplication` is a "smart" terminal; it runs its own local gizmo projectors (`IgEntityPresentationGizmo`, `MapOverlayGizmo`, `GlobalGizmoManager`). 
If you look at `DebugPrimitivesIngressTranslator.PollAndApply()`, it begins with `_buffer.Clear()`. Because you call it in `IgApplication.Update()` immediately after `_kernel.Update()`, it instantly destroys the Placement Tool crosshair, the Exclusive Input bindings, and the Drag-and-Drop pick spheres that your kernel just drew, replacing them with SimHost's data.

*(Note: The green selection circle still works because it is drawn by `SelectionRenderSystem`, which uses direct Raylib calls and completely bypasses the `_gizmoBuffer`).*

**2. Module Execution Order & DI Failure**
In `IgApplication.cs`, `_igDataDrivenGizmoSystem` and `StatelessGizmoSystem` are currently registered as **Global Systems** rather than inside the `GizmoInteractionModule`. Because they run globally, they execute *before* `_interactionBus.SwapBuffers()` advances the UI events. Furthermore, `_igDataDrivenGizmoSystem` is instantiated without the `interactionBus` argument, forcing it to fall back to the silent main world bus.

Here is the precise refactoring of `IgApplication.cs` required to restore the UI pipeline.

### 1. Delete the Ingress Translator
Remove the field and its invocation. The IG does not need diagnostic streams from SimHost; it generates its own visuals.

Locate `IgApplication.Update(float dt)` and remove the `PollAndApply` call:
```csharp
// Clear the primitive buffer before backend ECS systems populate it.
_gizmoBuffer?.EndFrame(dt);

_kernel.Update();

// REMOVE THIS LINE ENTIRELY:
// _ingressTranslator?.PollAndApply();
```
*(You should also delete the `private DebugPrimitivesIngressTranslator? _ingressTranslator;` field from the top of the file).*

### 2. Move the Bus Up & Inject Dependencies
Locate `InitializeNetwork` where the gizmos are set up (around line 1234). You must move the `_interactionBus` instantiation up so it can be passed into the Selection and DataDriven systems.

```csharp
// Gizmo subsystem (GZ020) — renders entity-bound diagnostic overlays.
_gizmoBuffer           = new DebugPrimitiveBuffer(capacity: 4096);
_gizmoRegistry         = new GizmoRegistry();
_statelessGizmoRegistry = new StatelessGizmoRegistry();
_gizmoSettingsRegistry  = new GizmoSettingsRegistry();
_gizmoUndoStack        = new GizmoUndoStack();

// 1. INSTANTIATE THE BUS HERE:
_interactionBus = new FdpEventBus();

Hrot.IG.Gizmos.GizmoRegistrar.Register(_gizmoRegistry, _statelessGizmoRegistry, _gizmoSettingsRegistry);

// ... (keep EntityDragGizmoDefinition and MissionPresentationGizmo registrations) ...

// 2. INJECT BUS INTO SELECTION:
_selectionSystem = new SelectionInteractionSystem(_world, _interactionBus);
```

### 3. Consolidate the Execution Pipeline
Scroll down slightly to where `DataDrivenGizmoSystem` is instantiated. Pass the bus to it, remove the late bus instantiation, and move the systems into the `GizmoInteractionModule`.

```csharp
// 3. INJECT BUS INTO DATA-DRIVEN GIZMOS:
_igDataDrivenGizmoSystem = new DataDrivenGizmoSystem(
    _gizmoRegistry!,
    _gizmoBuffer!,
    isSelectedPredicate: null,
    interactionBus: _interactionBus); // <-- CRITICAL FIX

// REMOVE THIS LATE INSTANTIATION:
// _interactionBus = new FdpEventBus();

_globalGizmoManager = new GlobalGizmoManager(_gizmoBuffer!, _interactionBus);
_measureToolGizmoAdapter = new MeasureToolGizmoAdapter(_globalGizmoManager, _gizmoSettingsRegistry);
var gizmoLayer = new DebugGizmoLayer(31, _gizmoBuffer!, _interactionBus, _world);
_gizmoLayer = gizmoLayer;
_canvas.AddLayer(gizmoLayer);
_canvas.DrawBuffer = _gizmoBuffer;

// ... (keep the network adapter gizmo setup) ...

// 4. PUT ALL GIZMO SYSTEMS INSIDE THE MODULE:
_kernel.RegisterModule(new GizmoInteractionModule(
    _interactionBus!,
    contextIngress: null,
    interactionSystems: new IEcsModuleSystem[]
    {
        _globalGizmoManager,
        _igDataDrivenGizmoSystem,                                          // MOVED INSIDE
        new StatelessGizmoSystem(_statelessGizmoRegistry!, _gizmoBuffer!)  // MOVED INSIDE
    },
    gizmoIngress: gizmoIngress,
    gizmoEgress:  gizmoEgress));

_kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, _interactionBus!));

// 5. REMOVE THESE GLOBAL REGISTRATIONS:
// _kernel.RegisterGlobalSystem(_igDataDrivenGizmoSystem);
// _kernel.RegisterGlobalSystem(new StatelessGizmoSystem(_statelessGizmoRegistry!, _gizmoBuffer!));
```

Once applied, the IG will stop destroying its own UI state every frame. `GlobalGizmoManager` will safely project the Placement Tool crosshair, and `DataDrivenGizmoSystem` will finally be listening to the correct `_interactionBus` (with buffers correctly swapped) to capture your drag-and-drop actions.







Regarding the history trails and route network etc, aren't they converted to gizmos yet?


You have identified a lingering architectural debt. While some authoring components have been successfully migrated to the data-driven pipeline, the core navigation graphs and trails have not. 

Route entities (`RoutePlan`) and tactical map overlays (`EditablePolyline`) are fully converted. They are handled by `RouteGizmo` and `MapOverlayGizmo`, which cleanly project their state into the primitive buffer using `IDebugDrawBuilder`. 

However, the fundamental physics and kinematic representations—specifically the `RoadNetworkBlob` and the active paths in the `TrajectoryPoolManager`—are still trapped in legacy presentation-tier classes like `SimHostRoadLayer` and `SimHostTrajectoryLayer`. These legacy layers completely bypass the `IDebugDrawBuilder` abstraction, coupling themselves to the presentation layer by issuing imperative `Raylib.DrawLineEx` calls directly to the screen. 

Because they sidestep the `DebugPrimitiveBuffer`, their visual state never reaches the `DebugPrimitivesBatch` DDS topic. Consequently, your headless `GizmoMap.Viewer` will be completely blind to road graphs, kinematic trajectories, and history trails.



no i do not want a special ECS system for each stuff to draw. We introduced gizmo projector as the proper unit for converting data to a stream of draing primitives. how to do it via gizmos?




You are right: the Gizmo pipeline needs to be the universal funnel for all debug rendering, whether that data lives on an entity or in a global singleton. 

To do this cleanly without breaking the chunk-iteration performance of the existing entity-bound mask evaluations, we simply formalize a parallel contract for global projectors.

Here is how we integrate global gizmos into the pipeline.

### 1. Introduce the Global Projector Contract
We define a new interface in `Fdp.Toolkit.Diagnostics.Gizmos` that drops the `Entity` context and runs exactly once per frame.

```csharp
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public interface IGlobalStatelessGizmo
    {
        void Draw(ISimulationView view, IDebugDrawBuilder drawBuilder);
    }
}
```

### 2. Extend the Stateless Registry
We update `StatelessGizmoRegistry` to manage these global rules separately from the entity-masked rules, preventing iteration pollution.

```csharp
internal struct CompiledGlobalRule
{
    public IGlobalStatelessGizmo Projector;
    public IGizmoVisibilityPolicy VisibilityPolicy;
}

public sealed class StatelessGizmoRegistry
{
    // ... existing entity rules ...
    private readonly List<CompiledGlobalRule> _globalRules = new();
    internal IReadOnlyList<CompiledGlobalRule> GlobalRules => _globalRules;

    // Overload for global projectors
    public void RegisterGlobal(
        IGlobalStatelessGizmo projector, 
        IGizmoVisibilityPolicy? visibilityPolicy = null)
    {
        if (projector == null) throw new ArgumentNullException(nameof(projector));
        
        _globalRules.Add(new CompiledGlobalRule
        {
            Projector = projector,
            VisibilityPolicy = visibilityPolicy ?? AlwaysVisiblePolicy.Instance
        });
    }
}
```

### 3. Evaluate in the Existing System
We modify `StatelessGizmoSystem.Execute` to invoke the global projectors exactly once per frame, before dropping into the heavy entity chunk-iteration loop.

```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    // ... existing repo cast and setup ...

    // 1. Evaluate global gizmos once per frame
    foreach (var rule in _registry.GlobalRules)
    {
        if (rule.VisibilityPolicy.IsGloballyEnabled(view))
        {
            rule.Projector.Draw(view, _drawBuilder);
        }
    }

    // 2. Evaluate entity-bound gizmos via chunk iteration
    // ... existing maxIndex loop ...
}
```

### 4. Update the Roslyn Source Generator
To keep everything automated and DRY, you update `GizmoRegistrarGenerator.cs` to recognize the new interface. If a class has `[GizmoProjector]` (with zero type arguments) and implements `IGlobalStatelessGizmo`, the generator emits a call to `statelessRegistry.RegisterGlobal(...)`.

### 5. Convert the Implementations
Now you can transition both the road network and the spatial grid into pure, clean gizmos, stripping them of their `IEcsModuleSystem` baggage.

**The Road Network Gizmo:**
```csharp
[GizmoProjector]
public sealed class RoadNetworkGizmo : IGlobalStatelessGizmo
{
    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (!view.HasSingleton<ZoneEnvironmentData>()) return;
        
        ref readonly var envData = ref view.GetSingletonUnmanaged<ZoneEnvironmentData>();
        ref readonly var network = ref envData.RoadNetwork;
        
        if (!network.Nodes.IsCreated || !network.Segments.IsCreated) return;

        // Emit your DebugPrimitiveShape.Line primitives into 'draw'
        // ...
    }
}
```

**Refactoring SpatialGridGizmo:**
You strip `[UpdateInPhase(SystemPhase.PostSimulation)]` and `IEcsModuleSystem` from `SpatialGridGizmo`, make it implement `IGlobalStatelessGizmo`, and delete its manual registration from the composition roots.

By executing this, the `ModuleHostKernel` remains strictly reserved for simulation logic, while `StatelessGizmoSystem` handles 100% of the diagnostic rendering projection, respecting the architectural boundary perfectly.





i am now also going to implement the previously discussed global statless not-entity-bound gizmos. has anything changed from the approach your suggested before?


The architectural approach we established previously remains mathematically sound and perfectly aligned with the current state of your codebase. The core principle holds: passing an empty component mask to the existing `IStatelessGizmo` would cause `StatelessGizmoSystem` to match *every* active entity in the chunk iteration loop, duplicating the draw calls $N$ times. A dedicated global path that executes exactly once per frame is required.

Based on the latest sources you provided, here is the exact execution plan to implement global stateless gizmos.

### 1. Introduce the Global Projector Contract
Create `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGlobalStatelessGizmo.cs`:
```csharp
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// A stateless gizmo projector that runs exactly once per frame, independent of any entity.
    /// Used for global diagnostic overlays like grids or road networks.
    /// </summary>
    public interface IGlobalStatelessGizmo
    {
        void Draw(ISimulationView view, IDebugDrawBuilder drawBuilder);
    }
}
```

### 2. Extend the Registry
Modify `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StatelessGizmoRegistry.cs` to store global rules separately from entity-bound rules.

```csharp
internal struct CompiledGlobalRule
{
    public IGlobalStatelessGizmo Projector;
    public IGizmoVisibilityPolicy VisibilityPolicy;
}

public sealed class StatelessGizmoRegistry
{
    private readonly List<CompiledStatelessRule> _rules = new List<CompiledStatelessRule>();
    private readonly List<CompiledGlobalRule> _globalRules = new List<CompiledGlobalRule>();

    internal IReadOnlyList<CompiledStatelessRule> Rules => _rules;
    internal IReadOnlyList<CompiledGlobalRule> GlobalRules => _globalRules;

    // ... existing Register() method ...

    public void RegisterGlobal(
        IGlobalStatelessGizmo projector, 
        IGizmoVisibilityPolicy? visibilityPolicy = null)
    {
        if (projector == null) throw new ArgumentNullException(nameof(projector));
        
        _globalRules.Add(new CompiledGlobalRule
        {
            Projector = projector,
            VisibilityPolicy = visibilityPolicy ?? AlwaysVisiblePolicy.Instance
        });
    }
}
```

### 3. Evaluate Globals in the System
Update `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/StatelessGizmoSystem.cs`. Evaluate the global projectors exactly once per frame before entering the heavy entity-chunk loop.

```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    if (view is not EntityRepository repo)
        throw new InvalidOperationException("...");

    // 1. Evaluate global gizmos once per frame
    var globalRules = _registry.GlobalRules;
    for (int g = 0; g < globalRules.Count; g++)
    {
        var rule = globalRules[g];
        if (rule.VisibilityPolicy.IsGloballyEnabled(view))
        {
            rule.Projector.Draw(view, _drawBuilder);
        }
    }

    // 2. Pre-evaluate global visibility for entity-bound rules
    var rules = _registry.Rules;
    // ... remaining existing chunk iteration logic ...
}
```

### 4. Update the Roslyn Source Generator
Modify `FDP/Toolkits/Fdp.Toolkits.Analyzers/GizmoRegistrarGenerator.cs` to recognize the new interface and emit the correct registration call. If you don't update this, the analyzer will throw `FDP_002` for global gizmos.

Update the symbol resolution:
```csharp
INamedTypeSymbol? statelessGizmoType = compilation.GetTypeByMetadataName("Fdp.Toolkit.Diagnostics.Gizmos.IStatelessGizmo");
INamedTypeSymbol? globalGizmoType = compilation.GetTypeByMetadataName("Fdp.Toolkit.Diagnostics.Gizmos.IGlobalStatelessGizmo");
```

Update the validation:
```csharp
bool isStateless = statelessGizmoType != null && ImplementsInterface(classSymbol, statelessGizmoType);
bool isGlobal = globalGizmoType != null && ImplementsInterface(classSymbol, globalGizmoType);

if (!isStateless && !isGlobal)
{
    context.ReportDiagnostic(Diagnostic.Create(FDP002_NotStateless, cds.GetLocation(), classSymbol.ToDisplayString()));
    continue;
}
```

Add an `IsGlobal` boolean to `GizmoEntry` and update the source emission:
```csharp
foreach (var entry in entries)
{
    string ctorArgs = entry.RequiresSettings ? "settings" : string.Empty;
    
    if (entry.IsGlobal)
    {
        sb.AppendLine($"            statelessRegistry.RegisterGlobal(new {entry.FullTypeName}({ctorArgs}));");
    }
    else
    {
        sb.Append($"            statelessRegistry.Register(new {entry.FullTypeName}({ctorArgs}),");
        sb.AppendLine();
        sb.AppendLine($"                new Type[]");
        sb.AppendLine("                {");
        foreach (var comp in entry.ComponentTypeNames)
            sb.AppendLine($"                    typeof({comp}),");
        sb.AppendLine("                });");
    }
}
```

### 5. Migrate `SpatialGridGizmo`
Your `SpatialGridGizmo` is currently defined as an `IEcsModuleSystem` and is manually registered in `EditorSubsystem.cs`. With the global gizmo pipeline in place, you can finally migrate it to a pure gizmo.

Change the class definition:
```csharp
[GizmoProjector]
public sealed class SpatialGridGizmo : IGlobalStatelessGizmo
```
Remove `[UpdateInPhase(SystemPhase.PostSimulation)]`, strip the `IEcsModuleSystem` interface, and rename `Execute` to `Draw(ISimulationView view, IDebugDrawBuilder draw)`. 

Finally, physically delete the manual registration from `EditorSubsystem.cs`:
```csharp
// REMOVE THIS LINE:
// _kernel.RegisterGlobalSystem(new Hrot.Common.Diagnostics.Gizmos.SpatialGridGizmo(_gizmoBuffer, new GizmoSettingsRegistry()));
```

Because it is decorated with `[GizmoProjector]`, the updated Roslyn generator will automatically pick it up and emit `statelessRegistry.RegisterGlobal(...)` inside `GizmoRegistrar.g.cs`. It will be evaluated perfectly by `StatelessGizmoSystem` alongside all entity-bound gizmos.


how to convert the green selection circle to gizmo as well? And let's make it non-filled as it now completely hides the entity symbol under it.

To fully convert the selection circle into a gizmo and make it non-filled, we need to complete the migration that was already partially started in the codebase. 

Currently, the data-driven `SelectionHighlightGizmo` exists, but the legacy `SelectionRenderSystem` is still actively drawing the filled circles via direct Raylib calls. Furthermore, the gizmo pipeline's `Sphere` primitive currently lacks support for line thickness and size modes (it assumes a filled world-meter sphere).

We will eradicate the legacy presentation layer, expand the `Sphere` gizmo contract to support wireframes and pixel-scaling, and update the stateless gizmo to use it.

### 1. Delete the Legacy Presentation Layer
Physically delete the following legacy files from the codebase:
*   `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/SelectionRenderSystem.cs`
*   `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/SelectionRenderConstants.cs`

Next, remove its registration from **`Hrot.IG/IgApplication.cs`**. Locate `InitializeEcs` (around line 1257) and delete this block:
```csharp
// REMOVE THIS ENTIRE BLOCK
// var selectionQuery  = _world.Query() ... .Build();
// var selectionLayer  = new SelectionRenderSystem(_world, selectionQuery);
// _canvas.AddLayer(selectionLayer);
```

### 2. Extend the Sphere Gizmo Contract
To support outline-only circles that maintain a fixed pixel size regardless of camera zoom, we must add `thickness` and `sizeMode` to the `DrawSphere` signatures.

Update **`FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Abstractions/IDebugDrawBuilder.cs`** and **`IGizmoDrawBuilder.cs`**:
```csharp
void DrawSphere(
    Vector3 center, float radius, Rgba32 color,
    float thickness = 0f, 
    SizeMode sizeMode = SizeMode.WorldMeters,
    PipelineTarget target = PipelineTarget.All,
    byte layer = 0);
```

Update the factory method in **`FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitive.cs`**:
```csharp
public static DebugPrimitive MakeSphere(
    Vector3 center, float radius, Rgba32 color,
    float thickness = 0f,
    SizeMode sizeMode = SizeMode.WorldMeters,
    PipelineTarget target = PipelineTarget.All,
    byte layer = 0)
{
    var p = default(DebugPrimitive);
    p.Shape        = DebugPrimitiveShape.Sphere;
    p.Color        = color;
    p.TargetView   = target;
    p.DebugLayer   = layer;
    p.SphereCenter = center;
    p.SphereRadius = radius;
    p.ThicknessU16 = (ushort)(thickness * 10f); // Map thickness
    p.SizeMode     = sizeMode;                  // Map SizeMode
    return p;
}
```

Update the `DrawSphere` implementation in **both** `DebugPrimitiveBuffer.cs` (FDP/Diagnostics) and `GizmoPrimitiveBuffer.cs` (GizmoMap.Contracts) to pass the new parameters:
```csharp
public void DrawSphere(
    Vector3 center, float radius, Rgba32 color,
    float thickness = 0f,
    SizeMode sizeMode = SizeMode.WorldMeters,
    PipelineTarget target = PipelineTarget.All,
    byte layer = 0)
{
    Append(DebugPrimitive.MakeSphere(center, radius, color, thickness, sizeMode, target, layer));
}
```

### 3. Update the 2D Renderer
Modify **`FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/Rendering/DebugPrimitiveRenderer2D.cs`** to draw a Raylib ring instead of a filled circle when thickness is requested.

```csharp
case DebugPrimitiveShape.Sphere:
{
    var center = new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y);
    float radius = prim.SphereRadius * geomScale;
    
    if (thickness > 0f)
    {
        // Draw outline ring. Math.Max prevents inner radius inversion.
        Raylib.DrawRing(center, MathF.Max(0, radius - thickness), radius, 0f, 360f, 32, color);
    }
    else
    {
        // Default filled
        Raylib.DrawCircleV(center, radius, color);
    }
    break;
}
```

### 4. Update the Selection Gizmo
Finally, rewrite **`Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/SelectionHighlightGizmo.cs`** to act as the sole source of truth for selections. We strip the filled component entirely, leaving only crisp, 2-pixel-thick wireframe circles rendered in screen-pixel size space.

```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    [GizmoProjector(typeof(SelectionState), typeof(SimTransform))]
    public sealed class SelectionHighlightGizmo : IStatelessGizmo
    {
        private const float SelectionRadiusPx = 20f;
        
        private static readonly Rgba32 PrimaryOutline = Rgba32.Green;
        private static readonly Rgba32 Secondary      = Rgba32.Yellow;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<SelectionState>(entity)) return;

            ref readonly var sel = ref view.GetComponentRO<SelectionState>(entity);
            if (!sel.IsSelected) return;

            if (!view.HasComponent<SimTransform>(entity)) return;
            
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            var pos = new Vector3(tf.Position.X, tf.Position.Y, 0f);

            if (sel.IsPrimarySelection)
            {
                draw.DrawSphere(pos, SelectionRadiusPx, PrimaryOutline, thickness: 2.0f, sizeMode: SizeMode.ScreenPixels);
            }
            else
            {
                draw.DrawSphere(pos, SelectionRadiusPx, Secondary, thickness: 2.0f, sizeMode: SizeMode.ScreenPixels);
            }
        }
    }
}
```

By completing this, the UI logic remains completely decoupled from presentation, the cluster runner streams the selection circle blindly over DDS, and the IG renders it dynamically as a perfect wireframe without obscuring the underlying entity symbol.




when i start drawing areas, i am allowed to click multiple time, each time a new vertex is added (small blue circle) and lines connecting the vertices are drawn. If i right click the shape closes, area entity gets created by the authoring mode does not stop. Next left click creates next veretx etc. Is this authoring mode also already converted to gizmos? how to fix the commit?


Yes, the area and route authoring modes have already been successfully migrated to the data-driven interaction pipeline. They both utilize the `PointSequenceGizmo`, which correctly handles the state machine for collecting vertices and drawing the preview lines and spheres.

The issue you are observing is a classic lifecycle mismanagement defect: an execution leak caused by failing to properly unregister the gizmo from the manager upon completion. 

If you examine the `onRemove` closures injected into `PointSequenceGizmo` during instantiation in both `EditorSpawnAdapter` and `IgApplication`, they only null out the local class fields (e.g., `_activeSequenceId = null`). They completely omit the required call to `_globalGizmoManager.Unregister(...)`. 

Consequently, when you right-click to commit, the gizmo successfully fires the `onFinish` delegate and spawns the entity, but because it is never evicted from `GlobalGizmoManager._activeGizmos`, it remains permanently focused and continues to process hardware inputs and emit drawing primitives.

To fix this, you must generate the stable ID *before* instantiating the gizmo, capture that ID inside the `onRemove` closure, and explicitly unregister it. You must apply this fix to both Area and Route authoring in both application shells.

### 1. Fix `EditorSpawnAdapter.cs`
Update `StartAreaAuthoringMode` and `StartRouteAuthoringMode`:

```csharp
public void StartAreaAuthoringMode(string styleOverrideJson = "")
{
    if (_activeSequenceId.HasValue)
    {
        _globalGizmoManager!.Unregister(_activeSequenceId.Value);
        _activeSequenceId = null;
    }

    var styleJson = styleOverrideJson;
    
    // CAPTURE ID EARLY
    long id = GlobalGizmoManager.NewId();

    var gizmo = new PointSequenceGizmo(
        onFinish: points =>
        {
            // ... existing spawn logic ...
        },
        onRemove: () => 
        { 
            // EXPLICITLY UNREGISTER
            _globalGizmoManager!.Unregister(id);
            _activeSequenceId = null; 
        });

    _activeSequenceId = id;
    _globalGizmoManager!.Register(id, gizmo);
}
```

### 2. Fix `IgApplication.cs`
Apply the exact same pattern to `ActivateAreaAuthoringTool` and `ActivateRouteAuthoringTool`:

```csharp
private void ActivateAreaAuthoringTool(Guid requestId, string styleJson = "")
{
    // ... existing guards ...

    long id = GlobalGizmoManager.NewId();

    var areaGizmo = new PointSequenceGizmo(
        onFinish: points =>
        {
            // ... existing spawn logic ...
        },
        onRemove: () =>
        {
            // EXPLICITLY UNREGISTER
            _globalGizmoManager?.Unregister(id);
            _activeSequenceId    = null;
            _activeSequenceGizmo = null;
        });

    _activeSequenceId    = id;
    _activeSequenceGizmo = areaGizmo;
    _globalGizmoManager?.Register(id, areaGizmo);
    
    FdpLog<IgApplication>.Info("[Node-{0}] Area authoring tool activated.", _effectiveInstanceId);
}
```

By explicitly dropping the reference from the `GlobalGizmoManager`, the gizmo will be correctly disposed, relinquishing its input capture and properly terminating the authoring session on the first right-click commit.



in hrot.editor, when i click to en empty map space and drag, i expect the selection "rubber" rectangle to be drawn and when i drop, the entitiies withing the rectangle should get selected and the rubber box disappears. Now it does nothing. How to fix?


The architectural gap preventing rubber-band selection is that the data-driven interaction pipeline currently ignores clicks on empty space. When the operator clicks the canvas, `DebugGizmoLayer.HandleInput` hit-tests existing primitives and returns `false` if nothing is struck. Consequently, the layer never captures the input session, and no interaction events are routed to the `_interactionBus`. 

To fix this natively within the strict ECS boundary, we must extend `DebugGizmoLayer` to capture unhandled left-clicks as a "background drag", project the selection area using a new `IGlobalStatelessGizmo`, and evaluate the AABB spatial query inside the `SelectionInteractionSystem` FSM.

Here is the exact execution to implement this cleanly.

### 1. Enable Background Drag Capture in `DebugGizmoLayer`
Open `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`. Add a tracking flag for background drags:
```csharp
private bool _backgroundDragActive;
```

Update `HandleHover` and `HandleDrag` to respect the new flag:
```csharp
public void HandleHover(Vector2 mouseWorldPos)
{
    // ...
    if (_captureActive || _interactionToken.IsValid || _backgroundDragActive)
    // ...
}

public bool HandleDrag(Vector2 worldPos, Vector2 delta)
{
    if (_interactionToken.IsValid || _backgroundDragActive)
    {
        _interactionDragActive = true;
        return true;
    }
    return false;
}
```

Update the release block in `HandleInput` to process background releases:
```csharp
// Interaction mode: handle release events for commit/cancel.
if (!isPressed && (_interactionToken.IsValid || _backgroundDragActive))
{
    if (button == MapMouseButton.Left)
    {
        if (_interactionDragActive)
        {
            _interactionDragActive = false;
            var token = _interactionToken;
            var space = _interactionSpace;
            _interactionToken = default;
            _backgroundDragActive = false; // Clear flag
            _eventBus.Publish(new GizmoInteractionCommitEvent ...
// ... ensure _backgroundDragActive = false is also added to the Cancel and Right-click branches.
```

Finally, at the very end of `HandleInput`, add the fallback to initiate the background drag instead of returning `false`:
```csharp
    // ... end of hit-testing loop ...

    // Fallback: click on empty space initiates a background rubber-band selection.
    _interactionToken = default; // Target = Entity.Null
    _interactionSpace = CoordinateSpace.World;
    _interactionDragActive = false;
    _backgroundDragActive = true;

    _eventBus.Publish(new GizmoInteractionStartedEvent
    {
        Token    = default,
        WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
    });
    return true; // Consume the click so the camera does not pan
}
```

### 2. Define the Rubber Band State
Create a managed singleton to decouple the interaction FSM from the presentation tier. 
Add `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RubberBandState.cs`:
```csharp
using System.Numerics;

namespace Hrot.ScenarioEditor.Gizmos
{
    public sealed class RubberBandState
    {
        public bool IsActive;
        public Vector2 Start;
        public Vector2 Current;
    }
}
```

### 3. Implement the Rubber Band Gizmo
Create the global stateless projector that emits the primitives. 
Add `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RubberBandGizmo.cs`:
```csharp
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos
{
    [GizmoProjector]
    public sealed class RubberBandGizmo : IGlobalStatelessGizmo
    {
        private static readonly Rgba32 FillColor   = new Rgba32(0, 120, 215, 50);
        private static readonly Rgba32 BorderColor = new Rgba32(0, 120, 215, 200);

        public void Draw(ISimulationView view, IDebugDrawBuilder draw)
        {
            if (!view.HasSingletonManaged<RubberBandState>()) return;

            var state = view.GetSingletonManaged<RubberBandState>();
            if (state == null || !state.IsActive) return;

            float minX = MathF.Min(state.Start.X, state.Current.X);
            float maxX = MathF.Max(state.Start.X, state.Current.X);
            float minY = MathF.Min(state.Start.Y, state.Current.Y);
            float maxY = MathF.Max(state.Start.Y, state.Current.Y);

            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float extX = (maxX - minX) * 0.5f;
            float extY = (maxY - minY) * 0.5f;

            var prim = default(DebugPrimitive);
            prim.Shape      = DebugPrimitiveShape.Box2D;
            prim.Space      = CoordinateSpace.World;
            prim.TargetView = PipelineTarget.Map2D;
            prim.BoxCenterX = cx;
            prim.BoxCenterY = cy;
            prim.BoxExtentX = extX;
            prim.BoxExtentY = extY;
            prim.Color      = FillColor;
            draw.EmitRaw(in prim);

            var p0 = new Vector3(minX, minY, 0f);
            var p1 = new Vector3(maxX, minY, 0f);
            var p2 = new Vector3(maxX, maxY, 0f);
            var p3 = new Vector3(minX, maxY, 0f);

            draw.DrawLine(p0, p1, BorderColor, 1.5f, SizeMode.ScreenPixels);
            draw.DrawLine(p1, p2, BorderColor, 1.5f, SizeMode.ScreenPixels);
            draw.DrawLine(p2, p3, BorderColor, 1.5f, SizeMode.ScreenPixels);
            draw.DrawLine(p3, p0, BorderColor, 1.5f, SizeMode.ScreenPixels);
        }
    }
}
```
*(Because it is decorated with `[GizmoProjector]`, the Roslyn generator will automatically wire this into the `StatelessGizmoRegistry` via `RegisterGlobal`,)*

### 4. Evaluate the Selection FSM
Finally, update `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Systems/SelectionInteractionSystem.cs` to track the background drag and execute the AABB query on commit. 

Add tracking fields:
```csharp
private bool _isBoxSelecting;
private System.Numerics.Vector2 _boxStart;
private System.Numerics.Vector2 _boxCurrent;
```

Update `Tick` to mutate the singleton and process the drag cycle:
```csharp
public void Tick(float dt)
{
    foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionStartedEvent>())
    {
        var entity = evt.Token.Target;
        if (entity.IsNull)
        {
            ClearAllSelections();
            OnSelectionChanged?.Invoke(Entity.Null, evt.WorldPos);
            
            _isBoxSelecting = true;
            _boxStart = new System.Numerics.Vector2(evt.WorldPos.X, evt.WorldPos.Y);
            _boxCurrent = _boxStart;
            _world.SetSingletonManaged(new Gizmos.RubberBandState { IsActive = true, Start = _boxStart, Current = _boxCurrent });
        }
        else // existing entity click logic...
    }

    foreach (ref readonly var evt in _interactionBus.Read<GizmoDragUpdateEvent>())
    {
        if (evt.Token.Target.IsNull && _isBoxSelecting)
        {
            _boxCurrent = new System.Numerics.Vector2(evt.WorldPos.X, evt.WorldPos.Y);
            _world.SetSingletonManaged(new Gizmos.RubberBandState { IsActive = true, Start = _boxStart, Current = _boxCurrent });
        }
    }

    foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionCommitEvent>())
    {
        if (evt.Token.Target.IsNull && _isBoxSelecting)
        {
            _boxCurrent = new System.Numerics.Vector2(evt.WorldPos.X, evt.WorldPos.Y);
            ExecuteBoxSelection();
            _isBoxSelecting = false;
            _world.SetSingletonManaged(new Gizmos.RubberBandState { IsActive = false });
        }
    }

    foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionCancelEvent>())
    {
        if (evt.Token.Target.IsNull && _isBoxSelecting)
        {
            _isBoxSelecting = false;
            _world.SetSingletonManaged(new Gizmos.RubberBandState { IsActive = false });
        }
    }
    
    // ... existing Delete key logic ...
}
```

Add the domain resolution query using the recorded bounds:
```csharp
private void ExecuteBoxSelection()
{
    float minX = MathF.Min(_boxStart.X, _boxCurrent.X);
    float maxX = MathF.Max(_boxStart.X, _boxCurrent.X);
    float minY = MathF.Min(_boxStart.Y, _boxCurrent.Y);
    float maxY = MathF.Max(_boxStart.Y, _boxCurrent.Y);

    // Ignore tiny accidental drags
    if (maxX - minX < 2f && maxY - minY < 2f) return;

    var q = _world.Query().With<SimTransform>().WithLifecycle(EntityLifecycle.All).Build();
    bool first = true;
    Entity primary = Entity.Null;

    foreach (var e in q)
    {
        if (!_world.IsAlive(e)) continue;

        // Only select entities representing map objects (ignore invisible internals)
        if (!_world.HasComponent<NetworkIdentity>(e) && !_world.HasComponent<TkbIdentity>(e))
            continue;

        ref readonly var tf = ref _world.GetComponentRO<SimTransform>(e);
        float px = tf.Position.X;
        float py = tf.Position.Y;

        if (px >= minX && px <= maxX && py >= minY && py <= maxY)
        {
            SetSelected(e, isPrimary: first);
            if (first) primary = e;
            first = false;
        }
    }

    if (!primary.IsNull)
        OnSelectionChanged?.Invoke(primary, new System.Numerics.Vector3(_boxCurrent.X, _boxCurrent.Y, 0f));
}
```

By explicitly routing `Entity.Null` picking via the global interaction bus, the presentation layer retains no UI state, the hardware input remains strictly quarantined, and the multi-selection domain mutations are predictably handled during the synchronous execution phase.






when i right click to an entity or even an ampty map space, i expect i see a context menu drawn. Now nothing like that happens, no context menu. How to fix?



The root cause of this defect is that by eradicating the legacy `StandardInteractionTool`, we severed the bridge that mapped canvas right-clicks to the local presentation layer. 

Currently, `DebugGizmoLayer.HandleRightClick` only hit-tests against `Box2D` primitives, which caters exclusively to the zero-latency DDS menu bindings. It silently ignores `Sphere` primitives (the standard hit-box for entities) and drops clicks on empty map space entirely.

To fix this architecturally, we must fully integrate right-click context requests into the unidirectional data-flow pipeline. We will unify the hit-testing logic in `DebugGizmoLayer`, introduce a `GizmoContextMenuRequestedEvent`, and have the application shells read from the isolated interaction bus to trigger their local ImGui popups.

Here is the exact execution plan.

### 1. Define and Register the Event
Add the new event to `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`:
```csharp
[EventId(8059)]
public struct GizmoContextMenuRequestedEvent
{
    public PickToken Token;
    public Vector2 ScreenPos;
}
```

Register it in `Hrot/Engine/Hrot.Common/Interactions/GizmoInteractionModule.cs` inside the constructor:
```csharp
_interactionBus.Register<GizmoContextMenuRequestedEvent>();
```

### 2. Unify Hit-Testing in `DebugGizmoLayer`
Open `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`. We need to extract the hit-test logic so both left and right clicks evaluate all interactive primitives correctly.

Add the universal hit-test method:
```csharp
private DebugPrimitive? FindTopmostInteractivePrimitive(Vector2 worldPos)
{
    var primitives = _buffer!.GetFrame();
    DebugPrimitive? best = null;

    foreach (ref readonly var prim in primitives)
    {
        if (!prim.GetPickToken().IsValid) continue;
        if (!HitTest(in prim, worldPos, HitRadiusWorld)) continue;

        if (best == null || prim.DebugLayer > best.Value.DebugLayer)
        {
            best = prim;
        }
    }
    return best;
}
```

Update `HandleInput` to process right-clicks on **release** (which `MapCanvas` correctly suppresses if the user was right-dragging to pan the camera) and simplify the left-click press logic:
```csharp
// ... inside HandleInput ...

// Interaction mode: handle release events for commit/cancel.
if (!isPressed && (_interactionToken.IsValid || _backgroundDragActive))
{
    // ... existing left/right commit/cancel logic ...
}

// Right-click release on the canvas (no active drag): trigger context menu
if (!isPressed && button == MapMouseButton.Right && !_interactionDragActive)
{
    return HandleRightClick(worldPos);
}

// Only press events are processed below this point.
if (!isPressed) return false;

// ... existing visibility checks ...

if (button != MapMouseButton.Left) return false;

var best = FindTopmostInteractivePrimitive(worldPos);

if (best.HasValue)
{
    _interactionToken      = best.Value.GetPickToken();
    _interactionSpace      = best.Value.Space;
    _interactionDragActive = false;
    _eventBus.Publish(new GizmoInteractionStartedEvent
    {
        Token    = _interactionToken,
        WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
    });
    return true;
}

// Fallback: click on empty space initiates a background rubber-band selection.
// ...
```

Rewrite `HandleRightClick` to evaluate the unified hit-test and publish the new event as a fallback:
```csharp
private bool HandleRightClick(Vector2 worldPos)
{
    var frame = _buffer!.GetFrame();
    var menuBindings = new Dictionary<long, uint>();
    foreach (ref readonly var prim in frame)
    {
        if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding)
            menuBindings[prim.InspNetworkId] = prim.StringHash;
    }

    var best = FindTopmostInteractivePrimitive(worldPos);
    long entityId = best?.InspNetworkId ?? best?.AnchorIndex ?? 0;

    // Fast-path: Pre-cached DDS menu
    if (entityId != 0 && menuBindings.TryGetValue(entityId, out uint menuHash))
    {
        string? json = _buffer!.InternMap.TryResolve(menuHash);
        if (json != null)
        {
            _contextMenuAdapter.Schedule(entityId, json);
            return true;
        }
    }

    // Fallback: Local UI menu (Editor tools, IG cache-miss)
    _eventBus?.Publish(new GizmoContextMenuRequestedEvent
    {
        Token     = best?.GetPickToken() ?? default,
        ScreenPos = Raylib_cs.Raylib.GetMousePosition()
    });

    return true; // Consume the event
}
```

### 3. Wire the Application Shells
Now we just read the event from the isolated interaction bus during the application update loops (which happen after the kernel has swapped the bus buffers).

**In `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`:**
Promote the interaction bus to a class field `private FdpEventBus? _interactionBus;` and assign it in `Initialize`. Then add to `Update`:
```csharp
foreach (ref readonly var evt in _interactionBus!.Read<GizmoContextMenuRequestedEvent>())
{
    _pendingContextMenuEntity = evt.Token.Target;
    _openContextMenuThisFrame = true;
}
```

**In `Hrot/Subsystems/Hrot.IG/IgApplication.cs`:**
In `Update`:
```csharp
foreach (ref readonly var evt in _interactionBus!.Read<GizmoContextMenuRequestedEvent>())
{
    _contextMenuSystem.RequestOpen(evt.Token.Target, evt.ScreenPos.X, evt.ScreenPos.Y);
}
```

**In `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`:**
In `Update`:
```csharp
foreach (ref readonly var evt in _cgfInteractionBus!.Read<GizmoContextMenuRequestedEvent>())
{
    _pendingContextMenuEntity = evt.Token.Target;
    _openContextMenuThisFrame = true;
}
```

**In `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`:**
Promote the interaction bus to a class field `private FdpEventBus? _interactionBus;`, assign it in `Initialize` (`_interactionBus = interactionBus ?? repo.Bus;`), and add to `Update`:
```csharp
foreach (ref readonly var evt in _interactionBus!.Read<GizmoContextMenuRequestedEvent>())
{
    _pendingMapContextEntity = evt.Token.Target;
    _openMapContextThisFrame = true;
}
```

By pushing right-clicks strictly through `GizmoContextMenuRequestedEvent`, we maintain complete decoupling between input gathering and UI presentation, allowing both empty-space clicks and entity clicks to surface naturally across all application topologies.

