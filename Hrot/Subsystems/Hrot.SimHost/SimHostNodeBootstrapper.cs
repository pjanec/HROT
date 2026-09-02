using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using CarKinem.Tkb;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Navigation.EngineBacked;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Spatial;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time;
using Fdp.Core.Orchestration;
using Fdp.Core.Diagnostics;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Diagnostics;
using Hrot.Common.EntityCreation;
using Hrot.Common.Infrastructure;
using Hrot.Core.Diagnostics;
using Hrot.Core.Network;
using Hrot.Network.Infrastructure;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Serializers;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost;

/// <summary>
/// Concrete <see cref="SharedApplicationBootstrapper"/> for the SimHost node.
/// Implements all abstract hooks to produce a node with roles:
/// <see cref="NodeRole.MuscleGround"/> | <see cref="NodeRole.Perception"/>.
/// </summary>
public sealed class SimHostNodeBootstrapper : SharedApplicationBootstrapper
{
    private readonly INetworkFactory? _networkFactory;
    private readonly NodeRole _role;
    private readonly string _localTempRoot;
    private readonly IDiagnosticEventHistoryService? _eventHistoryService;
    private readonly HrotNodeConfig _hrotConfig;
    private readonly string? _roadNetworkBlobPath;
    private readonly float _simulationRateHz;

    private NodeBootstrapper? _nodeBootstrapper;
    private ITkbDatabase? _tkbDb;
    private EngineBackedNavigationModule? _navModule;

    /// <summary>
    /// Core simulation systems pack. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public SimHostCoreLogicPack? CoreLogicPack { get; private set; }

    /// <summary>
    /// Slave orchestration translator. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public ISlaveOrchestrationTranslator? SlaveTranslator { get; private set; }

    /// <summary>
    /// Checkpoint I/O worker. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public CheckpointIOWorker? CheckpointWorker { get; private set; }

    /// <summary>
    /// Physics toolkit module. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public PhysicsToolkitModule? PhysicsModule { get; private set; }

    /// <summary>
    /// Perception module. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public CognitiveSpatialModule? PerceptionModule { get; private set; }

    /// <summary>
    /// Behavior registry. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public BehaviorRegistry? BehaviorRegistry { get; private set; }

    /// <summary>Migration services bundle. Valid after BootstrapNode() returns.</summary>
    public MigrationServices? MigrationServices { get; private set; }

    /// <summary>
    /// Loaded road network. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public CarKinem.Road.RoadNetworkBlob? RoadNetwork { get; private set; }

    /// <summary>
    /// Optional callback invoked during Phase 6d (after network translators, before Initialize).
    /// SimHostApp sets this to register gizmo modules and event-history capture systems that must
    /// be part of the initialized kernel topology but are not part of the domain core.
    /// </summary>
    public Action<HrotNodeContext>? ApplicationSystemsRegistrar { get; set; }

    /// <inheritdoc/>
    protected override void RegisterApplicationSystems(HrotNodeContext context)
        => ApplicationSystemsRegistrar?.Invoke(context);

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="EngineBackedNavigationModule.RegisterProviders"/> here (post-Initialize)
    /// because RegisterProviders requires <c>_navmesh</c>/<c>_registry</c> which are
    /// created by <c>RegisterSystems</c> during <c>Kernel.Initialize()</c> (Phase 7).
    /// </remarks>
    protected override void PostInitialize(HrotNodeContext context)
        => _navModule!.RegisterProviders(context.World);

    /// <param name="networkFactory">Optional network factory for DDS setup.</param>
    /// <param name="role">Node role controlling which simulation modules are activated.</param>
    /// <param name="localTempRoot">Root directory for checkpoints and temporary files.</param>
    /// <param name="eventHistoryService">Optional diagnostic event history service.</param>
    /// <param name="hrotConfig">Hrot node configuration.</param>
    /// <param name="roadNetworkBlobPath">Optional path to road network blob file.</param>
    /// <param name="simulationRateHz">Simulation rate in Hz for GlobalTime singleton.</param>
    public SimHostNodeBootstrapper(
        INetworkFactory? networkFactory,
        NodeRole role,
        string localTempRoot,
        IDiagnosticEventHistoryService? eventHistoryService,
        HrotNodeConfig hrotConfig,
        string? roadNetworkBlobPath = null,
        float simulationRateHz = 20.0f)
    {
        _networkFactory = networkFactory;
        _role = role;
        _localTempRoot = localTempRoot;
        _eventHistoryService = eventHistoryService;
        _hrotConfig = hrotConfig;
        _roadNetworkBlobPath = roadNetworkBlobPath;
        _simulationRateHz = simulationRateHz;
    }

    /// <inheritdoc/>
    protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
    {
        // ⭐⭐⭐ CE-140 step 3, host (b) — the translator list is no longer BUILT HERE.
        //    EntityCreationPack owns it (RegisterSpawningPipeline below): it composes
        //    TkbTranslatorSet.Base() + ExtraTranslators, hands that ONE instance to the ELM and to
        //    NetworkSpawningSystem, and GhostPromotionSystem reads it back off the ELM.
        //
        // ⛔ `.WithTranslators(...)` is DELIBERATELY GONE. It fed NedReplicationModule's
        //    `tkbEntityTranslators`, whose ONLY consumer is GhostPromotionSystem — and that system now
        //    falls back to EntityLifecycleModule.Translators when no explicit list is given. Keeping the
        //    call would hand promotion a SECOND, equal-but-distinct list instance, which is exactly the
        //    thing tkb-1/DESIGN.md §6.3 asks not to happen ("identical for all three systems within the
        //    same node"). ⭐ Dropping it makes that true by CONSTRUCTION rather than by two copies
        //    agreeing.
        //
        // 📐 Measured before removing: `_tkbEntityTranslators` has exactly one read site,
        //    NedReplicationModule.cs:413's GhostPromotionSystem construction.
        var ctx = new HrotNodeBuilder(config)
            .WithRole(config.SubsystemName, role)
            .WithNetworkFactory(networkFactory)
            .WithReplication(role)
            .WithBehaviorRegistry(GetBehaviorRegistry())
            .Build();

        _tkbDb = ctx.TkbDb;
        return ctx;
    }

    /// <inheritdoc/>
    protected override BehaviorRegistry? GetBehaviorRegistry()
    {
        BehaviorRegistry ??= new BehaviorRegistry();
        return BehaviorRegistry;
    }

    /// <inheritdoc/>
    protected override void RegisterDomainComponents(EntityRepository world)
    {
        SimHostComponentRegistry.RegisterAll(world);
        world.SetSingletonManaged<ITkbDatabase>(_tkbDb!);  // TKB-015
    }

    /// <inheritdoc/>
    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
    {
        return HrotScenarioSerializerFactory.Build(registry ?? new BehaviorRegistry());
    }

    /// <inheritdoc/>
    protected override void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim)
    {
        // Load road network
        var roadNetwork = SimHostApp.LoadRoadNetwork(_roadNetworkBlobPath, localNodeId: context.NodeId);
        RoadNetwork = roadNetwork;

        CoreLogicPack = new SimHostCoreLogicPack(context.EntityMap, roadNetwork);

        // Configure factory for this node and create attribute update systems
        var nodeFactory = _networkFactory?.ConfigureForNode(context, _role, GetBehaviorRegistry());
        foreach (var sys in nodeFactory?.CreateSimHostAttributeUpdateSystems()
                             ?? System.Linq.Enumerable.Empty<IEcsModuleSystem>())
            input.Add(sys);

        foreach (var s in CoreLogicPack.InputSystems)          input.Add(s);
        foreach (var s in CoreLogicPack.SimulationSystems)     sim.Add(s);
        foreach (var s in CoreLogicPack.PostSimulationSystems) postSim.Add(s);

        // Seed GlobalTime singleton
        context.World.SetSingletonUnmanaged(new GlobalTime
        {
            DeltaTime = 1.0f / _simulationRateHz,
            TimeScale = 1.0f
        });
    }

    /// <inheritdoc/>
    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
    {
        // Create services needed by diagnostics handler
        var archService = new ArchitectureDiagnosticsService(context.Kernel);
        var entityService = new EntityStateExtractionService(context.World, context.EntityMap);
        var logService = new LogArchiveExtractionService(
            string.IsNullOrWhiteSpace(_hrotConfig.LogDirectory)
                ? System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs")
                : _hrotConfig.LogDirectory,
            _hrotConfig.SubsystemName,
            context.NodeId);
        var diagHandler = new DiagnosticsDumpClusterOpHandler(
            _eventHistoryService!, archService, entityService, logService, _hrotConfig);

        var checkpointPath = System.IO.Path.Combine(_localTempRoot, "checkpoints");
        CheckpointWorker = new CheckpointIOWorker(checkpointPath, context.NodeId);

        _nodeBootstrapper = new NodeBootstrapper(_networkFactory);
        MigrationServices = _nodeBootstrapper.RegisterMigrationServices(
            _role,
            writerIdentifier: _role.HasFlag(NodeRole.Brain) ? "Hrot.CGF" : "Hrot.SimHost");
        var slave = _nodeBootstrapper.BuildOrchestration(
            _role, context.Kernel, context.World, context.NodeId,
            participant:          context.Participant,
            subsystemName:        "SimHost",
            eventBus:             context.EventBus,
            scenarioSerializer:   null,
            localTempRoot:        _localTempRoot,
            tkbDb:                _tkbDb,         // TKB-020
            checkpointWorker:     CheckpointWorker,
            simGroup:             simGroup,
            lifecycleGroup:       context.NedReplication?.NetworkLifecycleGroup,
            ghostCreationSystem:  context.GhostCreationSystem,
            eventAccumulator:     context.EventAccumulator,
            afterSeek:            (context.NedReplication as Hrot.Common.Abstractions.INedReplicationModule)?.AfterSeekCallback,
            diagnosticsDumpHandler: diagHandler);

        SlaveTranslator = _nodeBootstrapper.SlaveTranslator;
        return slave;
    }

    /// <inheritdoc/>
    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // Toolkit modules - Physics
        PhysicsModule = new PhysicsToolkitModule();
        PhysicsModule.Initialize(context.World);

        // ⭐⭐⭐ CE-140 step 3, host (b) — THE ENTITY CREATION PACK.
        //    This host used to hand-assemble the spawn path: build the list, call SetTranslators, and
        //    construct NetworkSpawningSystem with `translators:` passed by hand. Every one of those was
        //    an independent chance to get it wrong, and five hosts got it wrong the same way
        //    (S1, CE-137 twice, CE-138, CE-139). ⇒ the pack makes the omission unrepresentable.
        //
        // ⭐⭐ AND IT CLOSES THE SAME QUIETER GAP host (a) had: SimHost had NO CreateEntityRequestSystem,
        //    so nothing could ask it to create an entity — not even itself. Its only creation path was
        //    a raw bus SpawnEntityCommand. 🔒 User ruling 2026-08-31: the shared code "should not
        //    restrict any ecs enabled node from creating own networked entities … not removing
        //    capabilities by design". The pack has no opt-out.
        //
        // ⭐ AiDiagnostics goes through ExtraTranslators rather than being baked into Base(), because it
        //    lives above Hrot.Core — exactly the per-node ADDITION tkb-1/DESIGN.md §6.5 sanctions.
        //    ⛔ ExtraTranslators is ADD-ONLY: there is no way to hand the pack a narrower list. Per
        //    component narrowing is gate 2 (IsComponentTypeRegistered), never the list (§6.5b).
        //
        // 📄 DESIGN_Entity_Creation_Unification.md §3, §3.4 · Architect_Question_65 §0, §4.
        var creation = EntityCreationPack.Build(new EntityCreationContext
        {
            World       = context.World,
            EntityMap   = context.EntityMap,
            TkbDb       = context.TkbDb!,
            IdAllocator = context.IdAllocator!,
            // BaseModules[0] == EntityLifecycleModule. The pack calls SetTranslators on it, which must
            // precede the kernel's Initialize — it does: this runs during RegisterSpawningPipeline.
            Elm         = (EntityLifecycleModule)context.BaseModules[0],
            NodeId      = context.NodeId,

            ExtraTranslators = new ITkbEntityTranslator[]
            {
                new Hrot.SimHost.Diagnostics.AiDiagnosticsTkbTranslator(),
            },

            // ⛔ NOT the cluster's broadcast arbiter — that is CGF, and exactly one node may be it.
            //    ⚠ This does NOT stop SimHost creating entities: a request targeted at this node is
            //    processed regardless of the flag (CreateEntityRequestSystem.cs:151-156, Q65 §1).
            IsBroadcastArbiter = false,
        });

        var spawningSystem = creation.SpawnSystem;

        // ⭐⭐⭐ CE-147 step 4 — THE onEntitySpawned HOOK IS GONE, and nothing replaced it.
        //
        // 📌 It carried three statements. AX-011's shadow attach moved into GeoSpatialEgressTranslator
        //    (§13.7), which makes the invariant true for EVERY owning host instead of this one. The other
        //    two — SetAuthority<SimTransform> and SetAuthority<NetworkVelocity> — were REDUNDANT:
        //    NetworkSpawningSystem executes `metaNS.AuthorityMask = compNS` immediately before invoking
        //    the hook, which already sets the bit for every component present on the entity.
        //
        // ⚠ Redundant is not unread. `CarKinematicsSystem` filters on `.WithOwned<SimTransform>()`, so
        //   that bit is load-bearing — it is simply already set by the line above. The cluster rails that
        //   assert it (TheEgressShadowExistsAtBirthTests, SplitAuthoritySpawnTests) are the proof, not
        //   this comment.

        // ⭐ The HOST schedules; the pack only constructs. NetworkSpawningSystem is BeforeSync and still
        //   goes through SimHostModule exactly as before — composition changed, scheduling did not.
        context.Kernel.RegisterModule(new SimHostModule(spawnSystem: spawningSystem));
        context.Kernel.RegisterGlobalSystem(creation.RequestSystem);        // Input
        context.Kernel.RegisterGlobalSystem(creation.FinalizationSystem);   // PostSimulation

        // ⭐⭐ Make an omission LOUD. Every one of the five defects behind this design was silent, so the
        //   pack reports any piece the host built and then forgot to schedule.
        var unserviceable = creation.Unserviceable(new object[]
        {
            creation.SpawnSystem, creation.RequestSystem, creation.FinalizationSystem,
        });
        if (unserviceable.Length > 0)
            Fdp.Core.Logging.FdpLog<SimHostNodeBootstrapper>.Warn(unserviceable);

        // ⚠ FOLLOW-UP, not a regression: no DDS ingress request source or ACK sink is passed here, so
        //   this node serves LOCAL requests only — same position host (a) is in. Wiring the network half
        //   needs lifecycle adapters on HrotNodeContext, which is a separate change. Strictly better than
        //   before, when this node had no request tier at all.
        context.Kernel.RegisterModule(CoreLogicPack!);
        context.Kernel.RegisterModule(new EqsModule());

        // Register engine-backed navigation module (road-graph + direct-line stubs).
        // RegisterProviders is deferred to PostInitialize (after Kernel.Initialize) because
        // EngineBackedNavigationModule.RegisterProviders requires _navmesh/_registry which
        // are created by RegisterSystems — run during Kernel.Initialize (Phase 7).
        _navModule = new EngineBackedNavigationModule(
            RoadNetwork ?? default(CarKinem.Road.RoadNetworkBlob),
            CoreLogicPack!.TrajectoryPool);
        context.Kernel.RegisterModule(_navModule);

        context.Kernel.RegisterGlobalSystem(new AreaQueryResultMaterializationSystem());

        PerceptionModule = new CognitiveSpatialModule(
            context.World,
            colliderRadiusReader: static (view, e) => view.HasComponent<PhysicsCollider>(e)
                ? view.GetComponentRO<PhysicsCollider>(e).Radius
                : 0f);
        context.Kernel.RegisterModule(PerceptionModule);

        // GenesisMaterializationSystem - Input phase, registered after togglable groups
        context.Kernel.RegisterGlobalSystem(
            new GenesisMaterializationSystem(context.EntityMap));
    }

    /// <inheritdoc/>
    protected override void RegisterNetworkTranslators(
        HrotNodeContext context,
        INetworkFactory? configuredFactory)
    {
        if (context.Participant == null || configuredFactory == null) return;

        configuredFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(context.Kernel);
        configuredFactory.CreateSimHostPerceptionTranslators(context.GhostCreationSystem).RegisterOn(context.Kernel);
        configuredFactory.CreateSimHostPathfindingTranslators(CoreLogicPack!.TrajectoryPool).RegisterOn(context.Kernel);
    }
}
