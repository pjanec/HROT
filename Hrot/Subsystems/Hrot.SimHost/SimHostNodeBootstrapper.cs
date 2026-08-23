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
    private IReadOnlyList<ITkbEntityTranslator>? _translators;
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
        _translators = new List<ITkbEntityTranslator>
        {
            new SpatialCoreTkbTranslator(),
            new VehicleKinematicsTkbTranslator(),
            new BehaviorTkbTranslator(),
            new CombatTkbTranslator(),
            new PerceptionTkbTranslator(),
            new Hrot.SimHost.Diagnostics.AiDiagnosticsTkbTranslator(),  // behav-diag-1: auto-enable AI tracing
        }.AsReadOnly();

        var ctx = new HrotNodeBuilder(config)
            .WithRole(config.SubsystemName, role)
            .WithNetworkFactory(networkFactory)
            .WithReplication(role)
            .WithBehaviorRegistry(GetBehaviorRegistry())
            .WithTranslators(_translators)   // TKB-022 -- threads through to NedReplicationModule
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
        // ST-027: the projector-required schema, for uniform gizmo membership. Every host now
        // declares every gizmo family (MapGizmoPack), and StatelessGizmoRegistry throws on a
        // required component it does not know -- so the schema must be in place BEFORE the
        // registrars run. ST-020 is what happens when declaration outruns schema.
        Hrot.Common.Diagnostics.Gizmos.MapSchemaPack.RegisterAll(world);
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

        // elm reference for spawning (BaseModules[0] == EntityLifecycleModule)
        var elm = (EntityLifecycleModule)context.BaseModules[0];
        elm.SetTranslators(_translators!);   // TKB-022: set before kernel Initialize

        var spawningSystem = new NetworkSpawningSystem(
            context.TkbDb!,
            elm,
            context.EntityMap,
            context.IdAllocator!,
            context.NodeId,
            translators:      _translators,       // TKB-022
            onEntitySpawned: (world, entity, isLocalAuthority) =>
            {
                if (isLocalAuthority && world.HasComponent<SimTransform>(entity))
                {
                    world.SetAuthority<SimTransform>(entity, true);
                    if (world.HasComponent<NetworkTransform>(entity))
                        world.SetAuthority<NetworkTransform>(entity, true);
                    if (world.HasComponent<NetworkVelocity>(entity))
                        world.SetAuthority<NetworkVelocity>(entity, true);
                }
            });

        context.Kernel.RegisterModule(new SimHostModule(spawnSystem: spawningSystem));
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
