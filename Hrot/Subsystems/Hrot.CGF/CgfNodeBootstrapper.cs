using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Configuration;
using Hrot.Common;
using Hrot.Common.Diagnostics;
using Hrot.Common.EntityCreation;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.Network.Infrastructure;
using Hrot.SimHost.Serializers;

namespace Hrot.CGF;

/// <summary>
/// ⭐⭐⭐ <c>CE-1446</c> B1 (R-1045, R-3074, <c>DESIGN_Subsystem_Composition_Unification.md</c> §4.1af) — <b>CGF's node build, on the ONE shared bootstrapper.</b>
/// What <c>CgfSubsystem.Initialize</c> built inline (the builder call, the factory, replication, components, the capability walk, the entity-creation pack, the
/// translators, the orchestration, kernel initialisation, the join gate) is the base's phases now; this class only supplies CGF's INPUTS to them.
/// <list type="bullet">
///   <item>Time <b>Slave</b> (the Orchestrator owns the clock) — the base default; <see cref="BuildsPostSimGroup"/> is <b>false</b> (decision D): CGF builds an input and a simulation group only.</item>
///   <item>ONE <c>ClusterSlave</c> — the builder's (<c>context.ClusterSlave</c>), handed to <c>NodeBootstrapper.BuildOrchestration</c>; the second slave CGF used to make, and the context swap, are gone.</item>
///   <item>⛔ The participant stays CALLER-side (<c>CgfSubsystem</c>), as on SimHost and IG; host glue (gizmos, debuggers, scenario editor, event history) is the <see cref="SharedApplicationBootstrapper.ApplicationSystemsRegistrar"/>.</item>
/// </list>
/// </summary>
internal sealed class CgfNodeBootstrapper : SharedApplicationBootstrapper
{
    private readonly string _isolatedTempRoot;
    private readonly DiagnosticEventHistoryService _eventHistory;
    private readonly Action<IReadOnlyDictionary<Guid, int>> _onStagingRemap;

    private BehaviorRegistry? _behaviorRegistry;
    private BlueprintRegistry? _blueprintRegistry;
    private ScenarioSerializer? _serializer;
    private readonly Hrot.SimHost.NodeBootstrapper _nodeBootstrapper;

    public CgfNodeBootstrapper(INetworkFactory? networkFactory, string isolatedTempRoot, DiagnosticEventHistoryService eventHistory,
                               Action<IReadOnlyDictionary<Guid, int>> onStagingRemap)
    {
        _isolatedTempRoot = isolatedTempRoot;
        _eventHistory     = eventHistory;
        _onStagingRemap   = onStagingRemap;
        _nodeBootstrapper = new Hrot.SimHost.NodeBootstrapper(networkFactory);
    }

    // ── what the host reads back ─────────────────────────────────────────────────────────────

    public BehaviorRegistry BehaviorRegistry => _behaviorRegistry ?? throw new InvalidOperationException("[CgfNodeBootstrapper] the node is not bootstrapped yet.");
    public BlueprintRegistry BlueprintRegistry => _blueprintRegistry ?? throw new InvalidOperationException("[CgfNodeBootstrapper] the node is not bootstrapped yet.");
    public ScenarioSerializer ScenarioSerializer => _serializer ?? throw new InvalidOperationException("[CgfNodeBootstrapper] the node is not bootstrapped yet.");
    public TogglableInputGroup? ToggleInput => InputGroup;
    public Fdp.Toolkit.Diagnostics.EntityStateExtractionService? EntityStateExtraction { get; private set; }
    public TogglableSimulationGroup? ToggleSim { get; private set; }
    public PhysicsToolkitModule? PhysicsModule { get; private set; }
    public IReadOnlyList<INodeCapability> ResolvedCapabilities { get; private set; } = Array.Empty<INodeCapability>();
    public Hrot.Map.Common.Services.TerrainResidency? TerrainResidency => _nodeBootstrapper.TerrainResidency;
    public Fdp.Core.Serialization.Migrations.MigrationServices? MigrationServices { get; private set; }

    /// <summary>⭐ Decision D (R-3074): CGF builds NO post-simulation group; a capability that contributes post-sim systems throws at boot instead of being dropped.
    /// ⭐ CE-4270 B7b — a role that composes the Muscle (the fused server) builds it: the muscle pack has post-simulation systems (as on SimHost).</summary>
    protected override bool BuildsPostSimGroup => Hrot.SimHost.MuscleRoleInputs.ComposesMuscle(BootRole);

    // ── ⭐⭐⭐ CE-4270 B7b (§4.1ag, R-3077) — the Muscle role's inputs, by the DECLARED role. Booted as the Brain alone none of this runs (S0 cgf.json identical);
    //   booted as the fused server (Brain | MuscleGround | Perception | NavigationSolver) this node is the brain AND the muscle in one world — the editor's union,
    //   network ON, slave time. Every Muscle input comes from the ONE shared step SimHost uses (Hrot.SimHost.MuscleRoleInputs), never a copy. ──

    private bool ComposesMuscle => Hrot.SimHost.MuscleRoleInputs.ComposesMuscle(BootRole);

    /// <summary>The node's one trajectory pool when the role composes the Muscle (routes written by the solver, read by kinematics — CE-180). Made on first use:
    /// a Brain-only boot never allocates one.</summary>
    public Hrot.Common.Infrastructure.TrajectoryPoolProvider TrajectoryPool => _trajectoryPool ??= new();
    private Hrot.Common.Infrastructure.TrajectoryPoolProvider? _trajectoryPool;

    /// <summary>⭐ W6 — THE node's navmesh when it composes the Muscle (the solver's, and what a terrain commit bakes into). Made on first use.</summary>
    public Fdp.Toolkit.Navigation.SwitchableNavmeshProvider Navmesh => _navmesh ??= new();
    private Fdp.Toolkit.Navigation.SwitchableNavmeshProvider? _navmesh;

    /// <summary>⭐ W6 — bakes the navmesh from each loaded terrain, as on SimHost (silent-default rule: a node that composes the solver must bake). Null ⇒ no bake.</summary>
    public Fdp.Toolkit.Navigation.INavmeshFactory? NavmeshFactory { get; set; } = new Fdp.Toolkit.Navigation.Recast.RecastNavmeshFactory();

    /// <summary>The Muscle role's parts (the core logic pack, the navigation module, the solver factory); null for a Brain-only boot.</summary>
    public Hrot.SimHost.MuscleRoleInputs.Parts? Muscle { get; private set; }

    /// <summary>The Muscle's GlobalTime step (SimHost's NodeConfiguration default).</summary>
    private static readonly float MuscleSimulationRateHz = new Hrot.SimHost.NodeConfiguration().SimulationRateHz;

    protected override IReadOnlyList<string> DeclaredResourceKeys(NodeRole role) => Hrot.SimHost.MuscleRoleInputs.DeclaredResourceKeys(role);

    protected override IReadOnlyList<INodeResourceProvider> ResolveResources(HrotNodeContext context, NodeRole role)
        => Hrot.SimHost.MuscleRoleInputs.ComposesMuscle(role)
            ? Hrot.SimHost.MuscleRoleInputs.ResolveResources(role, TrajectoryPool)
            : Array.Empty<INodeResourceProvider>();

    /// <summary>⭐ CE-165 (the editor's rule) — the Brain and the Muscle packs in one world share systems; each runs once (the first in plan order wins).</summary>
    protected override bool DeduplicateSystemsByType => ComposesMuscle;

    // ── the registries come first: BuildContext hands the behaviour registry to the replication module ──

    protected override BehaviorRegistry? GetBehaviorRegistry()
    {
        if (_behaviorRegistry != null) return _behaviorRegistry;

        var behaviorRegistry = new BehaviorRegistry();
        // Blueprint registry (shared by materialization system and serializers). Created here so the single attribute-driven
        // registration pass below can populate both registries at once.
        var blueprintRegistry = new BlueprintRegistry();

        // Single self-registration pass: every [BlueprintRegistrar] in the AI behaviors assembly (the generated CuratedBehaviorRegistrar + the per-asset
        // registrars) registers each behavior under its own name; the scanner injects an ActionRegistry populated from the assembly's [FbtRegistrar].
        CgfBehaviorSetup.LoadFromAiAssembly(behaviorRegistry, blueprintRegistry);

        // Expose the registry to the diagnostic renderers so the entity inspector can project BrainBlackboard memory and visualize the BTree state.
        Hrot.Presentation.Renderers.BlueprintBlackboardRenderers.BehaviorRegistry = behaviorRegistry;
        Hrot.Presentation.Renderers.BTreeVisualizerRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.BTreeTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.HsmTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        // The FDP-layer trace emitter → the NLog-backed BehaviorLog (behav-diag-1). Idempotent: safe to overwrite on hot-reload.
        Fdp.Toolkit.Behavior.Diagnostics.BehaviorTraceLog.Instance = new Hrot.AI.Behaviors.Logging.BehaviorTraceLogEmitter();
        // ⭐ O3a / B3: ONE static for the whole renderer family.
        Hrot.Presentation.Renderers.BlueprintBlackboardRenderers.Registry = blueprintRegistry;

        _blueprintRegistry = blueprintRegistry;
        _behaviorRegistry  = behaviorRegistry;
        return behaviorRegistry;
    }

    // ── phase 1–3 ────────────────────────────────────────────────────────────────────────────

    protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
        => HrotNodeBuilderReplicationExtensions.BuildReplicatedNode(config, role, networkFactory, GetBehaviorRegistry());   // time Slave (the default)

    protected override void RegisterDomainComponents(EntityRepository world)
    {
        // ⭐ CE-4270 B7b — a role with the Muscle registers the Muscle's set too, in the editor's order (SimHost's, then CGF's).
        if (ComposesMuscle) Hrot.SimHost.SimHostComponentRegistry.RegisterAll(world);
        CgfComponentRegistry.RegisterAll(world);
    }

    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
    {
        // ⭐ A live-cluster node loads LENIENTLY: a component the registry cannot resolve is skipped with a warning, never a hard failure
        //   (measured 2026-09-28: P4 retired BrainBlackboard without a migrator and every shipped scenario became unloadable). The editor and CI keep
        //   the strict default so a typo still fails loudly. See UnknownComponentPolicy.
        _serializer = HrotScenarioSerializerFactory.Build(registry ?? BehaviorRegistry, unknownComponentPolicy: UnknownComponentPolicy.WarnAndSkip);
        return _serializer;
    }

    // ── phase 3c / 4a: the capability plan (registration order is EXECUTION order — see CgfCapabilities) ──

    protected override IReadOnlyList<INodeCapability> ResolveCapabilities(HrotNodeContext context, NodeRole role)
    {
        // ⭐ CE-454 (W3): the one mapper list both Brain hosts share.
        var mapperRegistry = Hrot.AI.Behaviors.Mappers.DefaultTacticalMappers.Create();
        // P3 step 3b's EXECUTION GATE (docs/DESIGN_Role_Affinity_Ownership.md §3.5, §6f, §6i): turned on HERE and only here — it follows the role POLICY,
        // which this host has; the editor is offline, receives no ghosts, and for it the gate would only subtract.
        var cgfLogicPack = new CgfLogicPack(BehaviorRegistry, context.EntityMap, mapperRegistry, BlueprintRegistry, vehicleApi: null, gateOnAuthority: true, nodeBus: context.EventBus);

        // ⭐ CE-4270 B7b — the Muscle role's parts, built only when the declared role composes it (dormant for the Brain alone).
        Muscle = ComposesMuscle
            ? Hrot.SimHost.MuscleRoleInputs.Build(context.EntityMap, default(CarKinem.Road.RoadNetworkBlob), TrajectoryPool, Navmesh, () => _nodeBootstrapper)
            : null;

        // ⭐⭐⭐ CE-4270 B7a / B9 (§4.1ag, R-3077): the ONE role table (RoleCapabilityTable) resolved for the DECLARED role (`role`, what the subsystem handed
        //   BootstrapNode), never a constant. Booted as the Brain, this is exactly CGF's set as before (S0 cgf.json identical).
        var plan = Hrot.SimHost.RoleCapabilityTable.Plan(new Hrot.SimHost.RoleTableInputs
        {
            Brain          = new CgfCapabilities.Brain(cgfLogicPack),
            Muscle         = Muscle,
            TrajectoryPool = Muscle != null ? TrajectoryPool : null,
            // ⭐ AQ93 §12 N6 (R-4056): a Brain mirrors every traffic node's cars. ⭐ CE-4270 D3: the fused server (Brain + Muscle in one world) SERVES them itself, as the editor does.
            Traffic        = Muscle != null ? Hrot.Traffic.TrafficPack.Serving() : Hrot.Traffic.TrafficPack.Mirroring(),
            // ⭐ Player Join D-2 / §7c (R-4115): the gameplay deploy SERVER, composed by default. Its feature `gameplay.deploy-server` (ON by default) gates it. One server
            //   per cluster: the lowest node id with the feature on (the cluster cache this node's replication module reads).
            GameplayServer = new Hrot.Gameplay.GameplayServerPack(ctx =>
                (ctx.NedReplication as Hrot.Network.Replication.NedReplicationModule)?.ClusterCache is { } cache
                    ? new Hrot.Gameplay.DeployServerElection(cache.AllNodeIds, cache.Supports, cache.FeatureOn) : null, creation: () => EntityCreation),   // ⭐ S2: the node's creation pack, resolved lazily (built after the plan resolves)
        });
        ResolvedCapabilities = plan
            .Resolve(role);
        return ResolvedCapabilities;
    }

    protected override void PopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> inputSystems, List<IEcsModuleSystem> simSystems, List<IEcsModuleSystem> postSimSystems)
    {
        // Everything CGF runs comes from its capabilities (the base walks them right after this hook) and the base's own owner-side edit-request handlers.
        // ⭐ CE-4270 B7b — a role with the Muscle starts with SimHost's GlobalTime seed (the ONE shared step).
        if (ComposesMuscle) Hrot.SimHost.MuscleRoleInputs.SeedGlobalTime(context.World, MuscleSimulationRateHz);
    }

    // ── phase 5: the ONE slave, the shared handler composition ───────────────────────────────

    /// <summary>⭐ <c>CE-3354</c> ③ — where this host's 3-D map step polls the map's build from (lazy: the map is attached after the bootstrap; a null result = no map ⇒ the step resolves). The map-in-load setting makes THIS node's load wait for it.</summary>
    public System.Func<Fdp.Toolkit.Orchestration.IMapBuildStatus?>? MapBuildStatus { get; set; }

    /// <summary>⭐ Node Features F-17 — this host supplies a 3-D map status, so it declares `map3d.in-load` (default false here).</summary>
    protected override System.Collections.Generic.IEnumerable<Hrot.Common.Infrastructure.FeatureDeclaration> HostFeatures
        => new[] { Hrot.Common.Infrastructure.Map3dInLoadFeature.Declaration(onByDefault: false) }
           .Concat(Hrot.Common.Infrastructure.HudFeatures.Declarations(playerDefault: false));   // ⭐ CE-4148 — the gameplay HUD aids (hud.*), default ON for the Player only

    protected override ClusterSlave BuildOrchestration(HrotNodeContext context, TogglableSimulationGroup simGroup, TogglablePostSimulationGroup postSimGroup, ScenarioSerializer serializer)
    {
        ToggleSim = simGroup;

        // ⭐ CE-4270 B7b (W6) — a role with the solver bakes each loaded terrain into the node's navmesh, as SimHost does (silent-default rule: we hold it, so pass it).
        if (ComposesMuscle && NavmeshFactory != null) _nodeBootstrapper.AttachNavmesh(NavmeshFactory, Navmesh);

        // ⭐ Decision A (R-3074): the migration services every Brain host builds (SimHost does, writer identifier "Hrot.CGF" for a Brain).
        MigrationServices = _nodeBootstrapper.RegisterMigrationServices(BootRole, writerIdentifier: "Hrot.CGF");

        var scenarioLoader = new Hrot.Common.Scenario.HrotScenarioLoader(
            new LocalDiskStorageProvider(_isolatedTempRoot), serializer.SubsystemType);
        // ⭐ CE-2054 — with the registry, a blueprint behaviour's params are remapped by their own contract type.
        var behaviorRemapper = CgfBehaviorSetup.CreateBehaviorRemapper(BehaviorRegistry);
        var extractor        = new Hrot.CGF.Orchestration.StagingEntityExtractor();

        // ⭐⭐⭐ BP-509 — the staging→runtime id table reaches the control-plane bus; the sink lives on the extractor and THE SUBSYSTEM wires it (R-79: the
        //   extractor never learns about a bus). `context.EventBus` is the node's own orchestration bus — the one its ClusterSlave drains.
        extractor.OnRemap = map =>
        {
            _onStagingRemap(map);
            context.EventBus.PublishManaged(new StagingRemapPublishedEvent { GuidToRuntime = map, SourceNodeId = context.NodeId });
        };

        // CGF's own episode handler (the default ReferenceEpisodeLoadHandler would be a second one) — HN-037: authored ids come from the ONE allocator,
        // the node's `context.IdAllocator`.
        var episodeHandler = new Hrot.CGF.Orchestration.Handlers.CgfEpisodeLoadHandler(
            serializer, scenarioLoader, extractor, context.IdAllocator!, context.World, behaviorRemapper);

        var archService   = new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(context.Kernel);
        var entityService = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(context.World, context.EntityMap, serializer);
        EntityStateExtraction = entityService;   // the inspector's extraction is this same instance (as before)
        var logService    = new Hrot.Core.Diagnostics.LogArchiveExtractionService(
            string.IsNullOrWhiteSpace(BootConfig!.LogDirectory) ? System.IO.Path.Combine(AppContext.BaseDirectory, "logs") : BootConfig.LogDirectory,
            BootConfig.SubsystemName, BootConfig.NodeId);
        var diagnosticsDump = new DiagnosticsDumpClusterOpHandler(_eventHistory, archService, entityService, logService, BootConfig);

        // ⭐ CE-4270 B11 — the ONE orchestration step (HostOrchestration).
        return Hrot.EcsNode.Hosting.HostOrchestration.Build(_nodeBootstrapper, BootRole, context,
            new Hrot.EcsNode.Hosting.OrchestrationSpec
            {
                SubsystemName     = "CGF",
                LocalTempRoot     = _isolatedTempRoot,
                Serializer        = serializer,
                ScenarioExtractor = extractor,
                SimGroup          = simGroup,
                InputGroup        = InputGroup,
                PostSimGroup      = null,   // decision D: none
                DiagnosticsDump   = diagnosticsDump,
                ScenarioHandlers  = new IClusterStateHandler[] { episodeHandler },
                BehaviorRemapper  = behaviorRemapper,
                MapBuildStatus    = MapBuildStatus,   // ⭐ CE-3354 ③ — this host HAS a map (CgfSubsystem attaches it)
            });
    }

    /// <summary>⭐ CE-4270 B7b — after Kernel.Initialize: the navigation module's providers, when the role composed the solver (the ONE shared step, CE-197 guard).</summary>
    protected override void PostInitialize(HrotNodeContext context)
    {
        if (Muscle != null) Hrot.SimHost.MuscleRoleInputs.RegisterNavigationProviders(Capabilities, Muscle.NavigationModule, context.World);
    }

    // ── phase 6a: the spawning pipeline ──────────────────────────────────────────────────────

    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // Allocate RaycastBatchData so Action_QueryRaycast can enqueue/query requests on CGF.
        PhysicsModule = new PhysicsToolkitModule();
        PhysicsModule.Initialize(context.World);

        // ⭐ CE-4270 B5 (§4.1ag) — the ONE shared spawning step: the pack, then the capabilities, then the genesis tail. CGF is the cluster's
        //   broadcast arbiter (exactly one node; it only decides who intercepts Owner == 0 broadcasts — not an authority gate); the ONE base
        //   translator set plus AI diagnostics and the stance/animation pair (CE-138, CE-2121; never subtract to narrow); the Brain loads
        //   scenarios, so it expands the macro instances it creates (Macros S3a ④, §15 E-4).
        EntityCreation = Hrot.EcsNode.Hosting.HostSpawning.Register(context, ConfiguredNetworkFactory,
            new Hrot.EcsNode.Hosting.SpawningSpec
            {
                IsBroadcastArbiter = true,
                ExtraTranslators   = new ITkbEntityTranslator[]
                {
                    new Hrot.SimHost.Diagnostics.AiDiagnosticsTkbTranslator(),
                    new Hrot.MuscleCharacter.Animation.Translators.AnimationTkbTranslator(null),
                },
                Macros = new Hrot.Common.Macros.MacroExpander(
                    Fdp.Toolkit.Scenario.FileMacroLibrary.ForNode(_isolatedTempRoot),
                    new Hrot.CGF.Orchestration.StagingEntityExtractor(reuseStagingWorld: true),
                    ScenarioSerializer,
                    Hrot.CGF.Configuration.CgfBehaviorSetup.CreateBehaviorRemapper(BehaviorRegistry)),
            },
            () => RegisterCapabilities(context));

        // The Brain's blueprint genesis, after the shared tail (as before).
        Hrot.SimHost.Systems.BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(context.Kernel, BlueprintRegistry);
    }

    // ── phase 6b: translators ────────────────────────────────────────────────────────────────

    protected override void RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory? configuredFactory)
    {
        // ⭐ CE-4270 B7b — a role with the Muscle registers the ONE SimHost translator set (perception over the ghost system, pathfinding over the node's
        //   trajectory pool — each pack picks its Brain and its solver halves from the node's role); the stance wire keeps the Brain's half (BootRole).
        if (Muscle != null)
        {
            Hrot.SimHost.MuscleRoleInputs.RegisterTranslators(context, configuredFactory, Muscle.CoreLogicPack.TrajectoryPool, BootRole);
            return;
        }
        // Auxiliary translators (time-sync, combat, mission-control) via the injected factory — the SimHost pattern.
        configuredFactory?.CreateSimHostAuxiliaryTranslators()?.RegisterOn(context.Kernel);
        configuredFactory?.CreateSimHostPerceptionTranslators()?.RegisterOn(context.Kernel);
        configuredFactory?.CreateSimHostPathfindingTranslators()?.RegisterOn(context.Kernel);
        // ⭐ CE-2121 slice ② — the Brain's half of the stance wire: its request out, the body's report in.
        Hrot.Animation.Replication.AnimationReplicationModule.RegisterStanceOn(context.Kernel, context.Participant, context.EntityMap, BootRole);
    }
}
