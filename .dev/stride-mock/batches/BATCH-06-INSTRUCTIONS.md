# BATCH-06 — SM-010: Refactor IgApplication to Use SharedApplicationBootstrapper

## Scope

Implement **SM-010** from [TASK-DETAILS.md](../TASK-DETAILS.md#sm-010--refactor-igapplication-to-use-sharedapplicationbootstrapper)
and [DESIGN.md §10.2](../DESIGN.md#102-igapplication).

**One file to create:** `Hrot\Subsystems\Hrot.IG\IgNodeBootstrapper.cs`  
**One file to modify heavily:** `Hrot\Subsystems\Hrot.IG\IgApplication.cs`  
**One test file to create:** `Hrot\Subsystems\Hrot.IG.Tests\IgNodeBootstrapperTests.cs`

Do NOT modify any other file unless a compile error requires it.

---

## Success Conditions (copy from TASK-DETAILS.md)

- **SC_SM010_1** — All tests in `Hrot.IG.Tests` that currently pass (313 out of 381) still pass
  after the refactor. Do not break any previously-passing test.
- **SC_SM010_2** — IG presentation modules (`MapLayerModule`, `MapCullingModule`,
  `StyleResolutionModule`, `EventEffectModule`, `HistoryTrailModule`) are registered via the
  `GetAdditionalModules()` hook. A new test verifies this.
- **SC_SM010_3** — `SharedApplicationBootstrapper` phase ordering (phases 1–7) applies to IG init.
- **SC_SM010_4** — No orchestration setup duplicated between `IgApplication` and
  `IgNodeBootstrapper`.

---

## Context — What You Are Migrating

`IgApplication` currently initializes itself via two private methods:

- `InitializeEcs()` — builds the `HrotNodeContext`, registers ECS components.
- `InitializeNetwork(bool enableNetwork, ...)` — registers modules and DDS translators,
  creates `ClusterSlave`, calls `_kernel.Initialize()`.

After this batch, both methods are REPLACED by a single `BootstrapNode()` call on
`IgNodeBootstrapper`. The private methods `InitializeEcs()` and `InitializeNetwork()`
are deleted from `IgApplication`.

`IgApplication.InitializeEmbedded()` becomes the composition root: it creates all
prerequisite objects, then calls `_igBootstrapper.BootstrapNode()`, then extracts
context fields from the returned `HrotNodeContext` and from `_igBootstrapper`
public properties.

---

## Reference: Phase Order in SharedApplicationBootstrapper

BEFORE you write any code, read `SharedApplicationBootstrapper.BootstrapNode()` at:
`Hrot\Engine\Hrot.Common\Infrastructure\SharedApplicationBootstrapper.cs`

The phases run in this exact order inside `BootstrapNode()`:
1. `HrotNodeBuilder.Build()` + `ConfigureForNode()` + `CreateReplicationModule()`
2. `RegisterDomainComponents(world)` — abstract hook
3. `BuildSerializer(registry)` — abstract hook
4a. `PopulateSystems(context, input, sim, postSim)` + create TogglableGroups + register
4b. `GetAdditionalModules()` — virtual hook, each module registered via `RegisterModule()`
5. `BuildOrchestration(context, simGroup, postSimGroup, serializer)` — abstract hook
6a. BaseModules registered + `RegisterSpawningPipeline(context)` — abstract hook
6a+. **BASE CLASS registers `context.NedReplication`** (DO NOT re-register in any hook)
6b. `RegisterNetworkTranslators(context, configuredFactory)` — abstract hook
6c. **BASE CLASS registers TimeNetworkModule translators** (DO NOT add them in 6b)
6d. `RegisterApplicationSystems(context)` — virtual hook (override to delegate to callback)
7. `context.Kernel.Initialize()` — ALWAYS LAST

---

## Critical Rules (from SM-009 lessons learned)

1. **Do NOT double-register NedReplicationModule.** Phase 6a+ registers it. Your
   `ApplicationSystemsRegistrar` callback MUST NOT call `ctx.Kernel.RegisterModule(ctx.NedReplication)`.
   The old IgApplication code does call it — you are REMOVING that call.

2. **Do NOT double-register TimeNetworkModule translators.** Phase 6c registers them.
   Your `RegisterNetworkTranslators` MUST NOT create or register any of these:
   `TimeNetworkModule.CreateDescriptorTranslator`, `CreateSlaveTimeSyncTranslator`,
   `CreateSlaveLockstepTranslator`. The old IgApplication code does add them — you are
   REMOVING those.

3. **Everything registered on the kernel must happen before Phase 7 (`Initialize()`).** The
   `ApplicationSystemsRegistrar` callback runs in Phase 6d — before `Initialize()`. This is
   safe. Do NOT register gizmo systems, `SlaveSyncController`, or `EventHistoryCaptureSystem`
   AFTER `BootstrapNode()` returns.

4. **Null factory must work.** `RegisterNetworkTranslators` receives `null` when no factory
   is available (headless tests). Guard: `if (configuredFactory == null || context.Participant == null) return;`

5. **`_world.Bus` vs `_context.EventBus`.** The kernel swaps `_world.Bus` on every tick.
   `SlaveSyncController` and `EventHistoryCaptureSystem("Orchestration")` must use
   `ctx.EventBus` (the stable orchestration bus), not `ctx.World.Bus`.

---

## File 1: Create `IgNodeBootstrapper.cs`

**Path:** `Hrot\Subsystems\Hrot.IG\IgNodeBootstrapper.cs`

`IgNodeBootstrapper` is `internal sealed` and extends `SharedApplicationBootstrapper`.

### Constructor signature

```csharp
internal IgNodeBootstrapper(
    Hrot.Core.Network.INetworkFactory? networkFactory,
    int effectiveInstanceId,
    bool headless,
    Hrot.Core.Network.IIgTranslators? igTranslatorsProvider,
    Hrot.IG.Map.MapUserConfig userConfig,
    Hrot.IG.Map.MapCameraViewport cameraViewport,
    Fdp.ModuleHost.Diagnostics.IDiagnosticEventHistoryService? eventHistoryService,
    Hrot.Common.Infrastructure.HrotNodeConfig hrotConfig)
```

Store all parameters as `private readonly` fields. `hrotConfig` is used in
`BuildOrchestration` for storage paths and subsystem name.

### Public properties (read after BootstrapNode() returns)

```csharp
public bool NetworkEnabled { get; private set; }
public Hrot.Core.Network.IIgNetworkAdapter? NetworkAdapter { get; private set; }
public Fdp.Core.Orchestration.ICommandGateway? CommandGateway { get; private set; }
public Fdp.Core.FdpEventBus? OrchestrationBus { get; private set; }
public Hrot.Common.Orchestration.NodeOpSlaveTranslator? IgSlaveTranslator { get; private set; }
public Action<Hrot.Common.Infrastructure.HrotNodeContext>? ApplicationSystemsRegistrar { get; set; }
```

### Hook implementations

#### Phase 2: `RegisterDomainComponents(EntityRepository world)`

Move ALL component registrations from `IgApplication.InitializeEcs()` here.
Specifically:
- `var tkb = HrotEnvironment.CreateTkb(); world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);`
- `HrotSharedComponentRegistry.RegisterAll(world);`
- All `world.RegisterComponent<X>()` and `world.RegisterManagedComponent<X>()` calls
  that appear in `InitializeEcs()` after `HrotSharedComponentRegistry.RegisterAll()`.

#### Phase 3: `BuildSerializer(BehaviorRegistry? registry)`

```csharp
protected override Fdp.Toolkit.Scenario.ScenarioSerializer BuildSerializer(
    Fdp.Toolkit.Behavior.BehaviorRegistry? registry)
    => Hrot.Common.Scenario.HrotScenarioSerializerFactory.Build(registry ?? new Fdp.Toolkit.Behavior.BehaviorRegistry());
```

#### Phase 4a: `PopulateSystems(...)`

IG is a visualization-only node. Add only the systems that belong in the togglable
simulation groups. At minimum add nothing (empty body) — IG's real ECS processing is
done by the modules registered in phases 4b, 6a, and 6b.

#### Phase 4b: `GetAdditionalModules()` — CRITICAL for SC_SM010_2

```csharp
protected override System.Collections.Generic.IEnumerable<Fdp.ModuleHost.Abstractions.IEcsModule> GetAdditionalModules()
{
    yield return new Hrot.IG.Modules.StyleResolutionModule(_userConfig, _effectiveInstanceId);
    yield return new Hrot.IG.Modules.MapCullingModule(_cameraViewport);
    yield return new Hrot.IG.Modules.MapLayerModule();
    yield return new Hrot.IG.Modules.HistoryTrailModule();
    if (!_headless)
        yield return new Hrot.IG.Modules.EventEffectModule();
}
```

Do NOT add these modules anywhere else (not in `PopulateSystems`, not in
`RegisterSpawningPipeline`, not in `RegisterApplicationSystems`).

#### Phase 5: `BuildOrchestration(...)`

Migrate the ClusterSlave creation from `IgApplication.InitializeNetwork()`.
The code is currently at approximately lines 920–1000 inside the
`if (enableNetwork && participant != null)` block.

The method must:
1. Create `_igOrchestrationBus = new Fdp.Core.FdpEventBus()` and assign `OrchestrationBus`.
2. Create `NodeOpSlaveTranslator` and assign `IgSlaveTranslator`.
3. Create `ClusterSlave` for `_effectiveInstanceId` with subsystem name "IG".
4. Register all cluster operation handlers exactly as they appear in `InitializeNetwork()`:
   - `ReferenceReplayLoadHandler`
   - `ReferenceLiveLoadHandler`
   - `IgZoneDummyHandler`
   - `ReferencePrefetchHandler`
   - `ReferencePreviewHandler`
   - `DiagnosticsDumpClusterOpHandler` (uses `_hrotConfig` fields for storage paths,
     subsystem name, log directory)
5. Return the `ClusterSlave`.

When `context.Participant` is null (headless), still create and return a headless
`ClusterSlave` — look at how `NodeBootstrapper.BuildOrchestration` handles null participant
to see the pattern.

Store `_fdpEntityInspector.ExtractionService` assignment is done AFTER BootstrapNode()
returns in IgApplication — do NOT do it here.

NOTE: Pass `lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup` to
`ReferenceReplayLoadHandler` as the abstract hook docs require.

#### Phase 6a: `RegisterSpawningPipeline(HrotNodeContext context)`

```csharp
protected override void RegisterSpawningPipeline(Hrot.Common.Infrastructure.HrotNodeContext context)
{
    // B. Ghost destruction - replaces SpawningModule so IG does not duplicate entities.
    context.Kernel.RegisterGlobalSystem(new Hrot.IG.Systems.GhostDestructionSystem(context.EntityMap));

    // UnitHierarchySystem - maintains ECS commander-subordinate hierarchy on the IG node (CS016).
    context.Kernel.RegisterModule(new Hrot.IG.Modules.IgUnitHierarchyModule(new Hrot.Core.Systems.UnitHierarchySystem()));
}
```

#### Phase 6b: `RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory? configuredFactory)`

```csharp
protected override void RegisterNetworkTranslators(
    Hrot.Common.Infrastructure.HrotNodeContext context,
    Hrot.Core.Network.INetworkFactory? configuredFactory)
{
    if (configuredFactory == null || context.Participant == null)
        return;

    // Use raw _networkFactory for methods that require a participant directly.
    NetworkAdapter = _networkFactory != null
        ? _networkFactory.CreateIgNetworkAdapter(context.Participant, _effectiveInstanceId)
        : Hrot.Core.Network.NullIgNetworkAdapter.Instance;
    CommandGateway = NetworkAdapter?.CommandGateway;

    var translators = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();

    // IG-specific ingress translators (entity context-actions, combat, etc.)
    // DO NOT add TimeNetworkModule translators here - base class Phase 6c handles them.
    if (_igTranslatorsProvider != null)
    {
        foreach (var t in _igTranslatorsProvider.GetTranslators(
            context.Participant,
            context.EntityMap,
            context.World.Bus,
            context.GhostCreationSystem,
            _effectiveInstanceId,
            _headless))
        {
            translators.Add(t);
        }
    }

    // D005: ACL egress translators convert bus events back to DDS.
    if (_networkFactory != null)
    {
        foreach (var t in _networkFactory.CreateIgEgressTranslators(
            context.Participant, context.World.Bus, context.GeoTransform!, _effectiveInstanceId))
        {
            translators.Add(t);
        }
    }

    if (translators.Count > 0)
    {
        context.Kernel.RegisterGlobalSystem(
            new Fdp.Network.Cyclone.Systems.CycloneNetworkIngressSystem(translators.ToArray()));
        context.Kernel.RegisterGlobalSystem(
            new Fdp.Network.Cyclone.Systems.CycloneEgressSystem(translators.ToArray()));
        context.Kernel.RegisterGlobalSystem(
            new Fdp.Network.Cyclone.Systems.CycloneNetworkCleanupSystem(
                translators.OfType<Fdp.Interfaces.IDescriptorTranslator>()));
    }

    NetworkEnabled = true;
}
```

#### Phase 6d: `RegisterApplicationSystems(HrotNodeContext context)`

```csharp
protected override void RegisterApplicationSystems(Hrot.Common.Infrastructure.HrotNodeContext context)
    => ApplicationSystemsRegistrar?.Invoke(context);
```

---

## File 2: Modify `IgApplication.cs`

### Step 2a — Add field for bootstrapper

Add this field declaration near the other private fields:
```csharp
private IgNodeBootstrapper? _igBootstrapper;
```

### Step 2b — Replace InitializeEmbedded() body

The method signature stays unchanged. Replace the body as follows.
Keep the existing camera setup, canvas setup, and field assignments at the top.

After assigning `_igTranslatorsProvider`, `_networkFactory`, camera and canvas:

1. **Create participant** (move from `InitializeEcs()`):
   ```csharp
   var shellParticipant = _networkFactory?.Participant;
   if (shellParticipant == null)
   {
       int igDomainId = _domainOverride ?? IgNetworkConstants.DdsDomain;
       shellParticipant = HrotEnvironment.CreateParticipant(igDomainId);
       shellParticipant.EnableSenderTracking(new SenderIdentityConfig
       {
           AppDomainId   = igDomainId,
           AppInstanceId = _effectiveInstanceId,
       });
   }
   ```

2. **Create igConfig** (move from `InitializeEcs()`):
   ```csharp
   var igConfig = new HrotNodeConfig
   {
       DomainId              = _domainOverride ?? IgNetworkConstants.DdsDomain,
       NodeId                = _effectiveInstanceId,
       Headless              = false,
       ExternalParticipant   = shellParticipant,
       SubsystemName         = "IgApplication",
       SkipAllocatorRouting  = true,
   };
   ```

3. **Create objects that DO NOT need a world** (move from `InitializeEcs()` and place
   here so the `ApplicationSystemsRegistrar` lambda can capture them):
   ```csharp
   _userConfig          = new MapUserConfig();
   _cameraViewport      = new MapCameraViewport();
   _debugPanelState     = new DebugPanelState(_userConfig);
   _debugPanel          = new IgDebugPanel(_debugPanelState);
   _inspectorState      = new EntityInspectorState();
   _inspectorPanel      = new EntityInspectorPanel(_inspectorState);
   _waypointEditorPanel = new WaypointEditorPanel(() => RouteWaypointGizmo.Current);
   _miniIosState        = new MiniExConPanelState(_effectiveInstanceId);
   _performanceMetrics  = new PerformanceMetrics();
   _performanceOverlay  = new PerformanceOverlay(_performanceMetrics);
   _contextMenuSystem   = new ContextMenuSystem();
   _fdpEventBrowser     = new FdpEventBrowserPanel(_fdpEventHistory);
   _edgeCompiler        = new JsonToRecordCompilerBuilder()
       .Register("Name",                  AttributeIds.Name,        AttributeValueKind.String)
       .Register("Affiliation",           AttributeIds.Affiliation,  AttributeValueKind.String)
       .Register("GeoPosition.Latitude",  AttributeIds.GeoLat,      AttributeValueKind.Float64)
       .Register("GeoPosition.Longitude", AttributeIds.GeoLon,      AttributeValueKind.Float64)
       .Register("GeoPosition.Altitude",  AttributeIds.GeoAlt,      AttributeValueKind.Float64)
       .Build();
   ```
   Preserve the existing comments from `InitializeEcs()` when moving code.

4. **Create IgNodeBootstrapper**:
   ```csharp
   _igBootstrapper = new IgNodeBootstrapper(
       _networkFactory,
       _effectiveInstanceId,
       _headless,
       _igTranslatorsProvider,
       _userConfig,
       _cameraViewport,
       _fdpEventHistory,
       igConfig);
   ```

5. **Set the ApplicationSystemsRegistrar callback**. This lambda captures `this` (all
   IgApplication instance fields). Inside the lambda, `ctx` is the `HrotNodeContext`
   built during BootstrapNode. Move the following code from `InitializeEcs()` and
   `InitializeNetwork()` into this callback:

   ```csharp
   _igBootstrapper.ApplicationSystemsRegistrar = ctx =>
   {
       // EventHistoryCaptureSystem (moved from InitializeEcs)
       ctx.Kernel.RegisterGlobalSystem(
           new EventHistoryCaptureSystem("World", _fdpEventHistory, ctx.World.Bus));
       ctx.Kernel.RegisterGlobalSystem(
           new EventHistoryCaptureSystem("Orchestration", _fdpEventHistory, ctx.EventBus));

       // Map context entity (moved from InitializeEcs)
       _mapContextEntity = ctx.World.CreateEntity();
       ctx.World.AddComponent(_mapContextEntity, new NetworkIdentity(0));

       // GeoTransform is pure math; set miniIos now so SendGeoSpatialUpdate works in tests
       _miniIosState.SetGeoTransform(ctx.GeoTransform!);

       // E. SlaveSyncController - unified slave that handles Continuous/Stepping transitions.
       // Must use ctx.EventBus (the same bus as the time translators),
       // NOT ctx.World.Bus which is swapped internally by the kernel.
       var timeController = new SlaveSyncController(ctx.EventBus, _effectiveInstanceId);
       ctx.Kernel.SetTimeController(timeController);

       // DO NOT call ctx.Kernel.RegisterModule(ctx.NedReplication) here.
       // Phase 6a+ already registered it. Double-registration corrupts the system schedule.

       ctx.Kernel.RegisterGlobalSystem(_contextMenuSystem);

       // Gizmo subsystem (moved from InitializeNetwork)
       _gizmoBuffer            = new DebugPrimitiveBuffer(capacity: 4096);
       _gizmoRegistry          = new GizmoRegistry();
       _statelessGizmoRegistry = new StatelessGizmoRegistry();
       _gizmoSettingsRegistry  = new GizmoSettingsRegistry();
       _gizmoUndoStack         = new GizmoUndoStack();
       _interactionBus         = new FdpEventBus();
       Hrot.IG.Gizmos.GizmoRegistrar.Register(_gizmoRegistry, _statelessGizmoRegistry, _gizmoSettingsRegistry);
       Hrot.Presentation.Gizmos.GizmoRegistrar.RegisterAll(_gizmoRegistry, _statelessGizmoRegistry, _gizmoSettingsRegistry);
       if (_igBootstrapper!.NetworkEnabled)
       {
           _gizmoRegistry!.Register(
               new EntityDragGizmoDefinition(onDragCommitted: (entity, worldPos) =>
               {
                   _lastDragWorldPos = worldPos;
                   OnEntityDragEnded(entity);
               }));
       }
       else
       {
           _gizmoRegistry!.Register(new EntityDragGizmoDefinition());
       }
       _statelessGizmoRegistry.Register(
           new Hrot.ScenarioEditor.Gizmos.MissionPresentationGizmo(ctx.GeoTransform!),
           new[] { typeof(SimTransform), typeof(SelectionState) });
       _selectionStateQuery = ctx.World.Query()
           .With<SelectionState>()
           .WithLifecycle(EntityLifecycle.All)
           .Build();

       _selectionSystem = new SelectionInteractionSystem(ctx.World, _interactionBus!);
       if (_igBootstrapper!.NetworkEnabled)
       {
           _selectionSystem.OnSelectionChanged += (entity, worldPos) =>
           {
               OnCanvasClicked(new System.Numerics.Vector2(worldPos.X, worldPos.Y),
                   MapMouseButton.Left, false, false, entity, updateSelection: true);
           };
           _miniIosPanel!.SetGateway(_igBootstrapper.CommandGateway);
       }
       ctx.Kernel.RegisterGlobalSystem(new SelectionInteractionSystemAdapter(_selectionSystem));

       // MapCommandController - created here so _globalGizmoManager is already set above
       if (_igBootstrapper!.NetworkEnabled && ctx.Participant != null)
       {
           _contextMenuSystem.SetCacheMissWriter(
               (reqId, mapId, sel) => _networkAdapter?.WriteContextMenuRequest(reqId, mapId, sel),
               _effectiveInstanceId);
           _mapCommandController = new MapCommandController(
               _canvas,
               ctx.World.Bus,
               dto => _networkAdapter?.WriteMapCommandAck(dto),
               _effectiveInstanceId,
               globalGizmoManager: _globalGizmoManager);
       }

       _igDataDrivenGizmoSystem = new DataDrivenGizmoSystem(
           _gizmoRegistry!,
           _gizmoBuffer!,
           isSelectedPredicate: null,
           interactionBus: _interactionBus);

       _globalGizmoManager = new GlobalGizmoManager(_gizmoBuffer!, _interactionBus);
       _measureToolGizmoAdapter = new MeasureToolGizmoAdapter(_globalGizmoManager, _gizmoSettingsRegistry);

       var schemaRegistry = new GizmoMap.Presentation.GizmoSchemaRegistry();
       var layerControlEditService = new StructEdit.Reflection.ComponentEditServiceBuilder().Build();
       using var layerControlSchemaSession = layerControlEditService.Open(
           new Hrot.Common.Diagnostics.Gizmos.LayerControlDto
           {
               Entities = true,
               Perception = true,
               AiHelpers = true
           },
           typeof(Hrot.Common.Diagnostics.Gizmos.LayerControlDto));
       schemaRegistry.Register(
           Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo.SchemaHash,
           layerControlSchemaSession.Document);
       var gizmoLayer = new DebugGizmoLayer(
           31,
           _gizmoBuffer!,
           _interactionBus,
           ctx.World,
           _canvas.Camera,
           new GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary(),
           schemaRegistry);
       _gizmoLayer = gizmoLayer;
       _canvas.AddLayer(gizmoLayer);
       _canvas.DrawBuffer = _gizmoBuffer;

       CycloneNetworkIngressSystem? gizmoIngress = null;
       CycloneEgressSystem? gizmoEgress = null;
       if (_networkFactory != null)
       {
           var gizmoTranslators = _networkFactory.CreateGizmoTranslators(_interactionBus!, _effectiveInstanceId, _headless);
           var ingressList = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
           var egressList  = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
           foreach (var t in gizmoTranslators)
           {
               if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Ingress) != 0) ingressList.Add(t);
               if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Egress)  != 0) egressList.Add(t);
           }
           if (ingressList.Count > 0)
               gizmoIngress = new CycloneNetworkIngressSystem(ingressList.ToArray());
           if (egressList.Count > 0)
               gizmoEgress = new CycloneEgressSystem(egressList.ToArray());
           var publisherSystem = _networkFactory.CreateGizmoPublisherSystem(_gizmoBuffer!, _effectiveInstanceId);
           if (publisherSystem != null)
               ctx.Kernel.RegisterGlobalSystem(publisherSystem);
       }
       var gizmoGroup = new TogglablePostSimulationGroup("GizmoExecution",
           _globalGizmoManager,
           _igDataDrivenGizmoSystem,
           new StatelessGizmoSystem(_statelessGizmoRegistry!, _gizmoBuffer!));
       gizmoGroup.Enabled = true;
       _gizmoController = new GizmoExecutionController(gizmoGroup, _globalGizmoManager, _igDataDrivenGizmoSystem);
       ctx.Kernel.RegisterModule(new GizmoInteractionModule(
           _interactionBus!,
           contextIngress: null,
           interactionSystems: new Fdp.ModuleHost.Abstractions.IEcsModuleSystem[] { gizmoGroup },
           gizmoIngress: gizmoIngress,
           gizmoEgress:  gizmoEgress));
       ctx.Kernel.RegisterGlobalSystem(
           new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, _interactionBus!));
       ctx.Kernel.RegisterGlobalSystem(new Hrot.Presentation.Systems.CanvasMenuUpdateSystem());
   };
   ```

   **IMPORTANT**: Preserve ALL existing comments from the original code verbatim when
   moving them into this lambda.

6. **Call BootstrapNode and extract fields**:
   ```csharp
   _context = _igBootstrapper.BootstrapNode(igConfig, NodeRole.ImageGenerator, _networkFactory);

   _world     = _context.World;
   _entityMap = _context.EntityMap;
   _kernel    = _context.Kernel;
   _geoTransform = _context.GeoTransform;
   _ghostCreationSystem = _context.GhostCreationSystem;

   _networkEnabled      = _igBootstrapper.NetworkEnabled;
   _networkAdapter      = _igBootstrapper.NetworkAdapter;
   _commandGateway      = _igBootstrapper.CommandGateway;
   _clusterSlave        = _context.ClusterSlave;
   _igSlaveTranslator   = _igBootstrapper.IgSlaveTranslator;
   _igOrchestrationBus  = _igBootstrapper.OrchestrationBus;

   _miniIosPanel = new MiniExConPanel(_miniIosState, _world.Bus);
   _entityFilterFactory = new HrotEntityFilterFactory(_world);
   _selectionStateQuery = _world.Query().With<SelectionState>().WithLifecycle(EntityLifecycle.All).Build();

   var igEntityService = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_world, _entityMap);
   _fdpEntityInspector.ExtractionService = igEntityService;

   if (_networkEnabled)
       IgCapabilitiesPublisher.Publish(_networkAdapter, _effectiveInstanceId);
   ```

   NOTE: `_selectionStateQuery` is created here for post-BootstrapNode availability. If
   it was also built inside the `ApplicationSystemsRegistrar` callback, remove the
   duplicate in the callback (keep only one).

### Step 2c — Delete the old private methods

Delete the entire `InitializeEcs()` method body and its XML doc comment.
Delete the entire `InitializeNetwork(bool enableNetwork, int? domainIdOverride)` method
body and its XML doc comment.

You may keep the method declarations as empty private stubs temporarily if needed for
compilation, then delete them entirely once the migration compiles.

---

## File 3: Create `IgNodeBootstrapperTests.cs` (SC_SM010_2)

**Path:** `Hrot\Subsystems\Hrot.IG.Tests\IgNodeBootstrapperTests.cs`

Write tests that verify SC_SM010_2 by calling `GetAdditionalModules()` via reflection.
`IgNodeBootstrapper` is `internal` and `Hrot.IG` already has `InternalsVisibleTo("Hrot.IG.Tests")`.

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.ModuleHost.Abstractions;
using Hrot.IG.Modules;
using Hrot.IG.Map;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Verifies SC_SM010_2: IG presentation modules are registered via
/// <see cref="IgNodeBootstrapper.GetAdditionalModules"/> and not flattened
/// into PopulateSystems system lists.
/// </summary>
public class IgNodeBootstrapperTests
{
    private static IgNodeBootstrapper CreateHeadlessBootstrapper(bool headless = true)
    {
        return new IgNodeBootstrapper(
            networkFactory:        null,
            effectiveInstanceId:   300,
            headless:              headless,
            igTranslatorsProvider: null,
            userConfig:            new MapUserConfig(),
            cameraViewport:        new MapCameraViewport(),
            eventHistoryService:   null,
            hrotConfig:            new Hrot.Common.Infrastructure.HrotNodeConfig
            {
                NodeId        = 300,
                SubsystemName = "IG",
                LocalTempRoot = System.IO.Path.GetTempPath(),
                LogDirectory  = System.IO.Path.GetTempPath(),
            });
    }

    private static List<IEcsModule> InvokeGetAdditionalModules(IgNodeBootstrapper bootstrapper)
    {
        var method = typeof(IgNodeBootstrapper).GetMethod(
            "GetAdditionalModules",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return ((IEnumerable<IEcsModule>)method!.Invoke(bootstrapper, null)!).ToList();
    }

    // SC_SM010_2 — presentation modules present in headless bootstrapper

    [Theory]
    [InlineData(typeof(StyleResolutionModule))]
    [InlineData(typeof(MapCullingModule))]
    [InlineData(typeof(MapLayerModule))]
    [InlineData(typeof(HistoryTrailModule))]
    public void GetAdditionalModules_Headless_ContainsPresentationModule(System.Type moduleType)
    {
        var bootstrapper = CreateHeadlessBootstrapper(headless: true);
        var modules = InvokeGetAdditionalModules(bootstrapper);
        Assert.Contains(modules, m => m.GetType() == moduleType);
    }

    [Fact]
    public void GetAdditionalModules_Headless_OmitsEventEffectModule()
    {
        // EventEffectModule requires graphics; must not be present in headless mode.
        var bootstrapper = CreateHeadlessBootstrapper(headless: true);
        var modules = InvokeGetAdditionalModules(bootstrapper);
        Assert.DoesNotContain(modules, m => m is EventEffectModule);
    }

    [Fact]
    public void GetAdditionalModules_NonHeadless_IncludesEventEffectModule()
    {
        var bootstrapper = CreateHeadlessBootstrapper(headless: false);
        var modules = InvokeGetAdditionalModules(bootstrapper);
        Assert.Contains(modules, m => m is EventEffectModule);
    }
}
```

---

## Build and Test Validation

After implementation, run in order:

```
cd Hrot\Subsystems\Hrot.IG
dotnet build Hrot.IG.csproj --no-incremental
```

If build passes:
```
dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --logger "console;verbosity=normal" 2>&1
```

Expected: at least 313 tests pass (same set as before). The 68 pre-existing failures
must remain the same set — do not introduce new failures.

Also run:
```
dotnet test Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj
```
Expected: 41/41 pass.

---

## Traps — Things That Previously Caused Regressions

These issues caused 6 test failures in BATCH-05 (SM-009). Do not repeat them:

1. **Calling `BootstrapNode` with a non-nullable factory when factory can be null** — accept
   `INetworkFactory?` (already nullable from prior batch; just use it correctly).

2. **Registering NedReplication in the callback** — It IS in the old code at line ~1121.
   REMOVE it from the callback. The base class handles it.

3. **Registering TimeNetworkModule translators in RegisterNetworkTranslators** — They ARE in
   the old `InitializeNetwork()` at lines ~877-886. REMOVE them. The base class handles them.

4. **Creating the HrotNodeBuilder manually in InitializeEmbedded** — Do NOT call
   `new HrotNodeBuilder(igConfig).Build()` directly anywhere. `BootstrapNode()` does Phase 1
   internally.

---

## What NOT to Change

- Do NOT modify `SharedApplicationBootstrapper.cs`.
- Do NOT modify `SimHostNodeBootstrapper.cs` or `SimHostApp.cs`.
- Do NOT modify `StrideNodeBootstrapper.cs`.
- Do NOT modify `SharedApplicationBootstrapperTests.cs`.
- Do NOT change `IgApplication.InitializeEmbedded()` method signature.
- Do NOT remove any existing IG test. Do not change any test that currently passes.
