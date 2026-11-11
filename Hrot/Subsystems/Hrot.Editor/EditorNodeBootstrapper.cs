using System;
using System.Linq;
using Hrot.Common.EntityCreation;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Fbt;
using Fbt.HotReload;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Utils;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Layers;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.CGF;
using Hrot.CGF.Orchestration;
using Hrot.CGF.Systems;
using Hrot.Editor.Windows;
using Hrot.Orchestrator.Panels;
using Hrot.Presentation.Windows;
using Hrot.Common.Diagnostics;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Common.Systems;
using Hrot.Common.Scenario;
using Hrot.Editor;
using Hrot.Editor.Adapters;
using Hrot.UI.Common.Adapters;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.Modules;
using Hrot.Editor.Rendering;
using Hrot.Editor.UI;
using Hrot.Player.Systems;
using Hrot.Player.Modules;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Config;
using Hrot.Map.Common.Services;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Rendering;
using Hrot.ScenarioEditor.Services;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Hrot.Presentation.Map;
using Hrot.Presentation.Facades;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.Core.Network;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.Core.Diagnostics;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
using IMapCameraProvider = Fdp.Toolkit.Runner.IMapCameraProvider;
using FdpEntityInspectorPanel = Fdp.Presentation.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel = Fdp.Presentation.Panels.EventBrowserPanel;
using FdpRepositoryAdapter = Fdp.Presentation.Adapters.RepositoryAdapter;
using FdpInspectorState = Fdp.Presentation.Abstractions.InspectorState;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Interfaces;
using Fdp.Toolkit.Spatial;
using CarKinem.Tkb;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat.Translators;
using Hrot.Common.Events;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Blueprints.Editor.Reload;
using StructEdit.Reflection;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared;

namespace Hrot.Editor;

/// <summary>
/// ⭐⭐⭐ <c>CE-1446</c> B2 (R-1045, R-3074 decisions B and C, <c>DESIGN_Subsystem_Composition_Unification.md</c> §4.1af) — <b>the editor's node build, on the ONE shared bootstrapper.</b>
/// The editor is an OFFLINE ECS node that composes every simulation role (<see cref="EditorCapabilities.DefaultRole"/>) and owns its clock (time <b>Standalone</b>). What
/// <c>EditorSubsystem.Initialize</c> built inline is the base's phases now; this class supplies the editor's INPUTS to them:
/// <list type="bullet">
///   <item><b>No participant, no replication</b> (decision B): <see cref="BuildContext"/> is the plain builder, never <c>BuildReplicatedNode</c> (that would build a NED module with a null participant);
///     the orchestration is <c>NodeBootstrapper.BuildOrchestration(…, offline: true)</c> — a Brain with no DDS participant, a participant is NOT faked.</item>
///   <item>ONE slave — the builder's; it is ticked by the shared frame (<c>HrotNodeContext.Tick</c>, decision C), not as a kernel module.</item>
///   <item>Brain and MuscleGround packs fuse in one world, so the base deduplicates systems by type (<see cref="DeduplicateSystemsByType"/>, CE-165).</item>
/// </list>
/// Host glue (the scenario editor module, gizmos, debuggers, asset catalogue, hot reload) is <c>EditorSubsystem.RegisterHostSystems</c> — the registrar (phase 6d).
/// </summary>
internal sealed class EditorNodeBootstrapper : SharedApplicationBootstrapper
{
    private readonly NodeId _editorNodeId;
    private readonly string _isolatedTempRoot;
    private readonly DiagnosticEventHistoryService _eventHistory;
    private readonly Fdp.Toolkit.Navigation.SwitchableNavmeshProvider _navmesh;
    private readonly BlueprintRegistry _blueprintRegistry;
    private readonly Func<MuscleModuleContext, IReadOnlyList<INodeCapability>>? _muscleCapabilitiesFactory;
    private readonly IReadOnlyList<Hrot.Core.Tkb.TranslatorPlacement>? _translatorPlacements;
    private readonly Hrot.SimHost.NodeBootstrapper _nodeBootstrapper = new();
    private BehaviorRegistry? _behaviorRegistry;
    private ScenarioSerializer? _serializer;

    public EditorNodeBootstrapper(int editorNodeId, string isolatedTempRoot, DiagnosticEventHistoryService eventHistory,
        Fdp.Toolkit.Navigation.SwitchableNavmeshProvider navmesh, BlueprintRegistry blueprintRegistry,
        Func<MuscleModuleContext, IReadOnlyList<INodeCapability>>? muscleCapabilitiesFactory,
        IReadOnlyList<Hrot.Core.Tkb.TranslatorPlacement>? translatorPlacements)
    {
        _editorNodeId = (Fdp.Core.NodeId)(editorNodeId); _isolatedTempRoot = isolatedTempRoot; _eventHistory = eventHistory; _navmesh = navmesh;
        _blueprintRegistry = blueprintRegistry; _muscleCapabilitiesFactory = muscleCapabilitiesFactory; _translatorPlacements = translatorPlacements;
    }

    // ── what the host reads back ─────────────────────────────────────────────────────────────

    public BehaviorRegistry BehaviorRegistry => _behaviorRegistry ?? throw new InvalidOperationException("[EditorNodeBootstrapper] the node is not bootstrapped yet.");
    public ScenarioSerializer ScenarioSerializer => _serializer ?? throw new InvalidOperationException("[EditorNodeBootstrapper] the node is not bootstrapped yet.");
    public EntityLifecycleModule? Elm { get; private set; }
    public PhysicsToolkitModule? PhysicsModule { get; private set; }
    public IReadOnlyList<INodeCapability> ResolvedCapabilities { get; private set; } = Array.Empty<INodeCapability>();
    public Fdp.Toolkit.Navigation.EngineBacked.EngineBackedNavigationModule? NavModule { get; private set; }
    public SimHostCapabilities.NavigationSolver? NavSolverCapability { get; private set; }
    public Hrot.Traffic.TrafficPack? TrafficPack { get; private set; }
    public Hrot.SimHost.NodeBootstrapper Orchestration => _nodeBootstrapper;

    /// <summary>⭐ The node's one trajectory pool (routes written by the solver, read by kinematics), taken from a provider — as on SimHost — so the muscle pack and the solver share it (CE-180: two pools ⇒ routes resolve into memory the kinematics never read).</summary>
    private readonly TrajectoryPoolProvider _trajectoryPool = new();

    protected override IReadOnlyList<string> DeclaredResourceKeys(NodeRole role)
        => _muscleCapabilitiesFactory == null ? new[] { ResourceKeys.TrajectoryPool } : Array.Empty<string>();   // the injected (Stride) muscle arm needs none

    protected override IReadOnlyList<INodeResourceProvider> ResolveResources(HrotNodeContext context, NodeRole role)
        => _muscleCapabilitiesFactory == null ? new INodeResourceProvider[] { _trajectoryPool } : Array.Empty<INodeResourceProvider>();

    protected override Fdp.Toolkit.Time.Controllers.TimeRole TimeRole => Fdp.Toolkit.Time.Controllers.TimeRole.Standalone;
    protected override bool DeduplicateSystemsByType => true;

    // ── phase 1 ──────────────────────────────────────────────────────────────────────────────

    protected override BehaviorRegistry? GetBehaviorRegistry()
    {
        if (_behaviorRegistry != null) return _behaviorRegistry;
        var behaviorRegistry = new BehaviorRegistry();
        // Register Urban Combat behaviors so MissionAdapterSystem can resolve Ambush and InfantryCombat behavior trees when loading UrbanCombatNew scenario files.
        // (CGF behaviors — MoveToLocation, FollowRoute, … — are loaded asynchronously from Hrot.AI.Behaviors.dll by the host's hot-reload coordinator.)
        UrbanCombatNewScenario.RegisterUrbanCombatBehaviors(behaviorRegistry);
        _behaviorRegistry = behaviorRegistry;
        return behaviorRegistry;
    }

    protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
    {
        // ⛔⛔ The plain builder, NOT BuildReplicatedNode: the editor is OFFLINE (decision B). 📐 `Headless = true` here means "skip DDS", not "no window" — no participant, no DDS allocator,
        //   no slave translator. ⭐ The role is what the host DOES (R-4005): the editor composes all four simulation roles, so it tells the roster the same.
        var context = new HrotNodeBuilder(config)
            .WithRole(config.SubsystemName, role)
            // ⭐ Standalone, NOT Master: TimeControllerFactory routes both to MasterSyncController.
            .WithTimeRole(TimeRole)
            // ⭐⭐⭐ CE-203 `E2` — the offline factory supplies the id allocator (CE-516: the factory the composition root gave this node).
            .WithNetworkFactory(networkFactory)
            .Build();

        // ⭐ OrchestrationEventRegistry.RegisterAll already ran inside Build() on this bus; RegisterInternalEvents is Hrot.Orchestrator's own vocabulary and only two hosts want it.
        Hrot.Orchestrator.OrchestratorEventRegistry.RegisterInternalEvents(context.EventBus);

        // Start in Deterministic mode so authoring starts paused (dt == 0 every frame).
        ((MasterSyncController)context.TimeController!).SwitchToDeterministic(new HashSet<int>());

        // ⭐⭐⭐ CE-203 `E2` — THE SHARED ALLOCATOR. `Reset(WorldBase)` makes the first id issued 1000, exactly as ClusterMaster resets it at every scenario load.
        context.IdAllocator!.Reset(Fdp.Toolkit.NetworkSpawning.WorldIdAuthority.WorldBase);
        return context;
    }

    // ── phase 2 / 3 ──────────────────────────────────────────────────────────────────────────

    protected override void RegisterDomainComponents(EntityRepository world)
    {
        PhysicsModule = new PhysicsToolkitModule();
        PhysicsModule.Initialize(world);

        // FdpAutoSerializer compiles property-extraction delegates at Build() time against the current ComponentTypeRegistry, so all types must be registered first.
        SimHostComponentRegistry.RegisterAll(world);
        CgfComponentRegistry.RegisterAll(world);
        // MapDisplayComponent is used by MapLayerAssignmentSystem (UXI-23 S1: routed through the shared map list).
        Hrot.Presentation.Map.MapPresentationRegistry.RegisterAll(world);
        // IG presentation components required by MapCullingModule / StyleResolutionModule.
        world.RegisterComponent<Hrot.Map.Common.Components.CullingState>();
        world.RegisterComponent<Hrot.Map.Common.Components.ResolvedStyle>();
        world.RegisterManagedComponent<Hrot.Map.Common.Components.MapSymbolOverride>();
        // Visual effect components required by EventEffectModule (EventToEffectSystem).
        world.RegisterComponent<VisualEffectState>();
        world.RegisterComponent<TracerTarget>();
    }

    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
    {
        // The strict default policy (a typo fails loudly in the editor and CI); the blueprint registry rides along.
        _serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(registry ?? BehaviorRegistry, _blueprintRegistry);
        return _serializer;
    }

    // ── phase 3c: the capability plan — Brain + MuscleGround in one node ─────────────────────

    protected override IReadOnlyList<INodeCapability> ResolveCapabilities(HrotNodeContext context, NodeRole role)
    {
        var entityMap = context.EntityMap;
        Hrot.SimHost.MuscleRoleInputs.Parts? muscle = null;
        IReadOnlyList<INodeCapability>? injectedMuscleCapabilities = null;

        if (_muscleCapabilitiesFactory == null)
        {
            // ⭐⭐ CE-3017 / CE-4270 B9: the Muscle's parts from the ONE step SimHost and the fused server use. One navmesh is shared by the singleton and the
            //   background solver. The muscle pack and the solver share the node's ONE trajectory pool (two pools ⇒ routes resolve into memory the kinematics
            //   never read, CE-180). The solver gets the live road holder. Each terrain load re-bakes the navmesh.
            muscle = Hrot.SimHost.MuscleRoleInputs.Build(entityMap, default(CarKinem.Road.RoadNetworkBlob), _trajectoryPool, _navmesh, () => _nodeBootstrapper);
            NavModule = muscle.NavigationModule;
            NavSolverCapability = muscle.NavigationSolver;
            _nodeBootstrapper.AttachNavmesh(new Fdp.Toolkit.Navigation.Recast.RecastNavmeshFactory(), _navmesh);   // each terrain load re-bakes it
            TrafficPack = Hrot.Traffic.TrafficPack.Serving();   // ⭐ AQ93 R-4047: the editor is its own traffic node
        }
        else
        {
            injectedMuscleCapabilities = _muscleCapabilitiesFactory(new MuscleModuleContext(context.World, entityMap));
        }

        // ⭐ CE-454 (W3): the one mapper list both Brain hosts share. ⚠ NOT gated on authority (CGF's gate follows a role policy; the editor is offline and has none).
        var mapperRegistry = Hrot.AI.Behaviors.Mappers.DefaultTacticalMappers.Create();
        var cgfLogicPack = new CgfLogicPack(BehaviorRegistry, entityMap, mapperRegistry, _blueprintRegistry, nodeBus: context.EventBus);

        // ⭐⭐⭐ CE-4270 B9 (§4.1ag, R-3077): the ONE role table, the same one CGF and the fused server resolve. The editor's row is the fused set with the network
        //   off and no gameplay server (it serves no players). The injected arm (Stride's mode 1) puts the supplied tier in the Muscle's slot.
        var plan = Hrot.SimHost.RoleCapabilityTable.Plan(new Hrot.SimHost.RoleTableInputs
        {
            Brain          = new Hrot.CGF.CgfCapabilities.Brain(cgfLogicPack),
            Muscle         = muscle,
            TrajectoryPool = muscle != null ? _trajectoryPool : null,
            InjectedMuscle = injectedMuscleCapabilities,
            Traffic        = TrafficPack,
        });

        ResolvedCapabilities = plan.Resolve(EditorCapabilities.DefaultRole);
        return ResolvedCapabilities;
    }

    protected override void PopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> inputSystems, List<IEcsModuleSystem> simSystems, List<IEcsModuleSystem> postSimSystems)
    {
        // Everything the editor runs comes from its capabilities (walked right after this hook, deduplicated by type).
    }

    // ⭐ CE-4270 B9: no GetAdditionalModules. BehaviorDiagnosticsModule comes from the Brain capability, as on CGF and the fused server.

    // ── phase 5: the ONE slave, OFFLINE ──────────────────────────────────────────────────────

    /// <summary>⭐ <c>CE-3354</c> ③ — where this host's 3-D map step polls the map's build from (lazy: the map is attached after the bootstrap; a null result = no map ⇒ the step resolves). The map-in-load setting makes THIS node's load wait for it.</summary>
    public System.Func<Fdp.Toolkit.Orchestration.IMapBuildStatus?>? MapBuildStatus { get; set; }

    /// <summary>⭐ Node Features F-17 — this host supplies a 3-D map status, so it declares `map3d.in-load` (default false here).</summary>
    protected override System.Collections.Generic.IEnumerable<Hrot.Common.Infrastructure.FeatureDeclaration> HostFeatures
        => new[] { Hrot.Common.Infrastructure.Map3dInLoadFeature.Declaration(onByDefault: false) }
           .Concat(Hrot.Common.Infrastructure.HudFeatures.Declarations(playerDefault: false));   // ⭐ CE-4148 — the gameplay HUD aids (hud.*), default ON for the Player only

    protected override ClusterSlave BuildOrchestration(HrotNodeContext context, TogglableSimulationGroup simGroup, TogglablePostSimulationGroup postSimGroup, ScenarioSerializer serializer)
    {
        Elm = context.BaseModules.OfType<EntityLifecycleModule>().FirstOrDefault();

        // ⭐ CE-2056 — the editor HOLDS a behaviour registry, so it passes a remapper built from it (the silent-default rule).
        var behaviorRemapper = Hrot.CGF.Configuration.CgfBehaviorSetup.CreateBehaviorRemapper(BehaviorRegistry);
        var extractor = new StagingEntityExtractor();
        // ⭐⭐⭐ BP-509 — the staging→runtime id table reaches the control-plane bus, wired by the subsystem (not the extractor).
        extractor.OnRemap = map => context.EventBus.PublishManaged(
            new Fdp.Toolkit.Orchestration.StagingRemapPublishedEvent { GuidToRuntime = map, SourceNodeId = _editorNodeId });

        var diagnosticsDump = new DiagnosticsDumpClusterOpHandler(
            _eventHistory,
            new ArchitectureDiagnosticsService(() => context.Kernel),
            new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(context.World, context.EntityMap, serializer),
            new Hrot.Core.Diagnostics.LogArchiveExtractionService(System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"), "Editor", 0),
            new HrotNodeConfig
            {
                NodeId = _editorNodeId, SubsystemName = "Editor", LocalTempRoot = _isolatedTempRoot,
                LogDirectory = System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"),
            });

        // ⭐ CE-4270 B11 — the ONE orchestration step (HostOrchestration). Offline (decision B): no participant, no network half — none is faked.
        return Hrot.EcsNode.Hosting.HostOrchestration.Build(_nodeBootstrapper, BootRole, context,
            new Hrot.EcsNode.Hosting.OrchestrationSpec
            {
                SubsystemName           = "Editor",
                LocalTempRoot           = _isolatedTempRoot,
                Serializer              = serializer,
                ScenarioExtractor       = extractor,
                SimGroup                = simGroup,
                InputGroup              = InputGroup,
                PostSimGroup            = postSimGroup,
                DiagnosticsDump         = diagnosticsDump,
                ScenarioHandlers        = Array.Empty<IClusterStateHandler>(),   // the editor registers no episode handler
                BehaviorRemapper        = behaviorRemapper,
                Offline                 = true,
                DryRunIncludesTransient = true,   // the editor's dry run copied Transient components (HN-016)
                MapBuildStatus          = MapBuildStatus,   // ⭐ CE-3354 ③ — the editor HAS a map (EditorSubsystem wires it)
            });
    }

    // ── phase 6a: the spawning pipeline ──────────────────────────────────────────────────────

    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // ⭐ CE-3141 — the obstacle tool creates through the pack.
        context.Kernel.RegisterModule(new EditorSystemsModule(() => EntityCreation));

        // ⭐ CE-4270 B6 (§4.1ag) — the ONE shared spawning step: the pack, then the capabilities, then the genesis tail. IsBroadcastArbiter
        //   TRUE: the editor is a standalone single-node world (the Brain arbitrates). CE-237 — order-sensitive translator placements when the
        //   host has them, else the stance/animation extra (CE-2121; the pack takes extras OR placements, never both). CE-4024 — offline, so the
        //   attribute compiler is passed here (a networked host takes it from its adapters). Macros S3a ④ — the editor loads scenarios, so it
        //   expands the macro instances it creates (§15 E-4).
        if (Elm == null) throw new InvalidOperationException("[EditorNodeBootstrapper] the lifecycle module is missing.");
        bool placed = _translatorPlacements is { Count: > 0 };
        EntityCreation = Hrot.EcsNode.Hosting.HostSpawning.Register(context, ConfiguredNetworkFactory,
            new Hrot.EcsNode.Hosting.SpawningSpec
            {
                IsBroadcastArbiter    = true,
                TranslatorPlacements  = placed ? _translatorPlacements : null,
                ExtraTranslators      = placed ? null : new Fdp.Interfaces.ITkbEntityTranslator[]
                {
                    new Hrot.MuscleCharacter.Animation.Translators.AnimationTkbTranslator(null),
                },
                JsonAttributeCompiler = Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.Build(),
                Macros = new Hrot.Common.Macros.MacroExpander(
                    Fdp.Toolkit.Scenario.FileMacroLibrary.ForNode(_isolatedTempRoot),
                    new StagingEntityExtractor(reuseStagingWorld: true),   // ⭐ CE-2396 one staging world, emptied per expansion
                    ScenarioSerializer,
                    Hrot.CGF.Configuration.CgfBehaviorSetup.CreateBehaviorRemapper(BehaviorRegistry)),
            },
            () => RegisterCapabilities(context));

        // ⭐⭐ O-7 B (CE-4418, docs/DESIGN_Terrain_Authoring.md §18e-2 B-1/B-4) — the editor shows its terrain blocks and features LIVE: its obstacle bake (the pack's one StaticObstacleBakeSystem) is handed the live overlay.
        //   ⭐ Set here, on the constructed system, rather than through the shared pack: every other host keeps null (a block is inert data there; the compiled overlay reaches them at load) and no host can get one by
        //   forgetting a parameter. The editor's own load merges NO compiled overlay (its staging root has no gateway-written header ⇒ no OverlayHash), so a block is never drawn twice.
        //   📌 Rail: TheEditorShowsItsTerrainBlocksLiveTests (the editor's system has it, a SimHost's does not).
        if (EntityCreation != null) EntityCreation.ObstacleBakeSystem.LiveOverlay = new Hrot.Map.Common.Services.LiveOverlay();

        // BSA-WIRE: the blueprint genesis + event-ingress systems, so InitialBlueprintsIntent (written on scenario load) is consumed in the offline editor as on a CGF node.
        Hrot.SimHost.Systems.BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(context.Kernel, _blueprintRegistry);
        // ⛔ MX2 — the ingress system CONSUMES these two events, but nothing declared them on this world's strict bus: declared HERE, beside the systems that drain them (MX-008).
        context.World.RegisterManagedEvent<Fdp.Toolkit.Blueprints.Events.AttachInstanceBlueprintEvent>();
        context.World.RegisterEvent<Fdp.Toolkit.Blueprints.Events.RemoveInstanceBlueprintEvent>();
    }

    protected override void RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory? configuredFactory)
    {
        // Offline: no translators.
    }

    protected override void PostInitialize(HrotNodeContext context)
    {
        // The navigation module's providers need RegisterSystems to have run (it did, inside Kernel.Initialize) — the same post-Initialize step SimHost takes.
        NavModule?.RegisterProviders(context.World);
    }
}
