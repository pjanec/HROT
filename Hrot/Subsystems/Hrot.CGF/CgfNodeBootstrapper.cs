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
    private readonly Action<IReadOnlyDictionary<long, long>> _onStagingRemap;

    private BehaviorRegistry? _behaviorRegistry;
    private BlueprintRegistry? _blueprintRegistry;
    private ScenarioSerializer? _serializer;
    private readonly Hrot.SimHost.NodeBootstrapper _nodeBootstrapper;

    public CgfNodeBootstrapper(INetworkFactory? networkFactory, string isolatedTempRoot, DiagnosticEventHistoryService eventHistory,
                               Action<IReadOnlyDictionary<long, long>> onStagingRemap)
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

    /// <summary>⭐ Decision D (R-3074): CGF builds NO post-simulation group; a capability that contributes post-sim systems throws at boot instead of being dropped.</summary>
    protected override bool BuildsPostSimGroup => false;

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

    protected override void RegisterDomainComponents(EntityRepository world) => CgfComponentRegistry.RegisterAll(world);

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
        var cgfLogicPack = new CgfLogicPack(BehaviorRegistry, context.EntityMap, mapperRegistry, BlueprintRegistry, vehicleApi: null, gateOnAuthority: true);

        // ⭐⭐ CE-2144 (R-2008) — the plan starts from the ONE shared head: HealthApplication first, so damage is applied before the cognitive runtime.
        ResolvedCapabilities = CoreInfrastructureCapabilities.LiveSimulationPlan(CgfSubsystem.DefaultRole)
            .Capability(CgfSubsystem.DefaultRole, new CgfCapabilities.Brain(cgfLogicPack))
            // ⭐ CE-221 — cross-role infrastructure, declared LAST so it keeps its tail-of-Simulation position (Resolve de-duplicates by Key).
            .Capability(CgfSubsystem.DefaultRole, new CoreInfrastructureCapabilities.UnitHierarchy())
            .Capability(CgfSubsystem.DefaultRole, new Hrot.SimHost.EqsResultUpdateCapability())
            // ⭐ CE-3110 (R-221/R-224) — interactions (doors, embarkation) are cross-role too.
            .Capability(CgfSubsystem.DefaultRole, new CoreInfrastructureCapabilities.Interactions())
            // ⭐ AQ93 §12 N6 (R-4056) — SUMO cars from every traffic node, mirrored (vicinity replicas the brains see); idle without a net.
            .Capability(CgfSubsystem.DefaultRole, Hrot.Traffic.TrafficPack.Mirroring())
            .Resolve(CgfSubsystem.DefaultRole);
        return ResolvedCapabilities;
    }

    protected override void PopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> inputSystems, List<IEcsModuleSystem> simSystems, List<IEcsModuleSystem> postSimSystems)
    {
        // Everything CGF runs comes from its capabilities (the base walks them right after this hook) and the base's own owner-side edit-request handlers.
    }

    // ── phase 5: the ONE slave, the shared handler composition ───────────────────────────────

    /// <summary>⭐ <c>CE-3354</c> ③ — where this host's 3-D map step polls the map's build from (lazy: the map is attached after the bootstrap; a null result = no map ⇒ the step resolves). The map-in-load setting makes THIS node's load wait for it.</summary>
    public System.Func<Fdp.Toolkit.Orchestration.IMapBuildStatus?>? MapBuildStatus { get; set; }

    protected override ClusterSlave BuildOrchestration(HrotNodeContext context, TogglableSimulationGroup simGroup, TogglablePostSimulationGroup postSimGroup, ScenarioSerializer serializer)
    {
        ToggleSim = simGroup;

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
            context.EventBus.PublishManaged(new StagingRemapPublishedEvent { StagingToRuntime = map, SourceNodeId = context.NodeId });
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

        return _nodeBootstrapper.BuildOrchestration(
            BootRole, context.Kernel, context.World, context.NodeId,
            participant:          context.Participant,
            subsystemName:        "CGF",
            eventBus:             context.EventBus,
            scenarioSerializer:   serializer,
            scenarioExtractor:    extractor,
            scenarioSource:       context.World.Bus,
            scenarioIdAllocator:  context.IdAllocator,
            localTempRoot:        _isolatedTempRoot,
            tkbDb:                context.TkbDb,
            simGroup:             simGroup,
            inputGroup:           InputGroup,
            postSimGroup:         null,   // decision D: none
            // ⭐ Decision A: the replay load gates the network lifecycle systems and bypasses ghost lifecycle, as on SimHost.
            lifecycleGroup:       context.NedReplication?.NetworkLifecycleGroup,
            ghostCreationSystem:  context.GhostCreationSystem,
            eventAccumulator:     context.EventAccumulator,
            afterSeek:            context.NedReplication?.AfterSeekCallback,
            diagnosticsDumpHandler: diagnosticsDump,
            elm:                  context.BaseModules?.OfType<EntityLifecycleModule>().FirstOrDefault(),
            terrainContent:       context.TerrainContent,
            // ⭐ ONE slave, ONE slave translator — the builder's (the second `new ClusterSlave` and the context swap are gone).
            clusterSlave:         context.ClusterSlave,
            slaveTranslator:      context.SlaveTranslator,
            scenarioHandlers:     new IClusterStateHandler[] { episodeHandler },
            behaviorRemapper:     behaviorRemapper,
            entityMap:            context.EntityMap,   // CE-2101 — the world boundary clears it
            mapBuildStatus:       MapBuildStatus);     // ⭐ CE-3354 ③ — this host HAS a map (CgfSubsystem attaches it), so it passes it
    }

    // ── phase 6a: the spawning pipeline ──────────────────────────────────────────────────────

    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // Allocate RaycastBatchData so Action_QueryRaycast can enqueue/query requests on CGF.
        PhysicsModule = new PhysicsToolkitModule();
        PhysicsModule.Initialize(context.World);

        // ⭐⭐⭐ CE-140 step 3, host (d) — CGF's entity-creation tier, BUILT AS A UNIT by the shared pack. CGF is the cluster's broadcast arbiter
        //   (`IsBroadcastArbiter: true` — exactly one node may set it); it is NOT an authority gate, it only decides who intercepts Owner == 0 broadcasts.
        var creation = EntityCreationPack.Build(new EntityCreationContext
        {
            World       = context.World,
            EntityMap   = context.EntityMap,
            TkbDb       = context.TkbDb!,
            IdAllocator = context.IdAllocator!,
            Elm         = (EntityLifecycleModule)context.BaseModules.First(m => m is EntityLifecycleModule),
            NodeId      = context.NodeId,
            // ⭐⭐ S2b — the SAME network adapters every ECS host passes (request source, ack sink, JSON compiler, ownership strategy, forwarding egress).
            NetworkAdapters = ConfiguredNetworkFactory?.CreateCgfEntityLifecycleAdapters(),
            // ⭐⭐⭐ CE-291 (piece C) — the reliable-init wait-set provider, sourced from the shared NED replication module.
            ExpectedPeers = context.NedReplication?.ExpectedPeers,
            // ⭐ CE-138's list: the ONE base set plus AiDiagnostics (it lives above Hrot.Core). ⛔ Never subtract to narrow — gate ② narrows per component.
            ExtraTranslators = new ITkbEntityTranslator[]
            {
                new Hrot.SimHost.Diagnostics.AiDiagnosticsTkbTranslator(),
                // ⭐ CE-2121 — stance (and the animation runtime pair) from the TKB's CharacterAnimationDefDto.
                new Hrot.MuscleCharacter.Animation.Translators.AnimationTkbTranslator(null),
            },
            IsBroadcastArbiter = true,
        });
        EntityCreation = creation;   // CE-515

        // ⭐ CE-4013 D6 — every piece of the pack scheduled by the ONE shared registration, unconditionally, online and offline (Q65 §0).
        creation.RegisterOn(context.Kernel);

        // ⭐ CE-4013 — the capabilities' Register hook, which CGF never asked before (none in its plan implements it today).
        RegisterCapabilities(context);

        Hrot.SimHost.Systems.GenesisTail.RegisterOn(context.Kernel, context.EntityMap);   // ⭐ CE-4013 D11 — the shared genesis tail
        Hrot.SimHost.Systems.BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(context.Kernel, BlueprintRegistry);
    }

    // ── phase 6b: translators ────────────────────────────────────────────────────────────────

    protected override void RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory? configuredFactory)
    {
        // Auxiliary translators (time-sync, combat, mission-control) via the injected factory — the SimHost pattern.
        configuredFactory?.CreateSimHostAuxiliaryTranslators()?.RegisterOn(context.Kernel);
        configuredFactory?.CreateSimHostPerceptionTranslators()?.RegisterOn(context.Kernel);
        configuredFactory?.CreateSimHostPathfindingTranslators()?.RegisterOn(context.Kernel);
        // ⭐ CE-2121 slice ② — the Brain's half of the stance wire: its request out, the body's report in.
        Hrot.Animation.Replication.AnimationReplicationModule.RegisterStanceOn(context.Kernel, context.Participant, context.EntityMap, BootRole);
    }
}
