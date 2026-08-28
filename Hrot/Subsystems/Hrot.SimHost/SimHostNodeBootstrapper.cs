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
            // ⭐⭐⭐ UXI-23 S1 — the sixth translator this list was missing. It writes VisualData
            //    (and EntityInfo.ForceId) from the TKB's VisualDefinitionDto. Without it SimHost's
            //    entities had no VisualData, so the shared entity gizmos drew nothing: measured
            //    2026-08-28 as 3 non-Line primitives against the Scenario perspective's 69.
            // ⚠ It early-returns when VisualData is unregistered, so this line is only half the
            //    fix — SimHostComponentRegistry must register the components, which it now does.
            new Hrot.Map.Definitions.Tkb.PresentationTkbTranslator(),
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

                    // ⭐⭐⭐ AX-011 — ATTACH the egress shadow at birth, on the node that OWNS SimTransform.
                    //
                    // 🔴 THE BUG, measured `2026-08-26`: `GeoSpatialEgressTranslator.ScanAndPublish` queries
                    //    `SimTransform` + `NetworkTransform` + `NetworkIdentity`. ⛔ The production TKB catalog
                    //    (`NedTkbBuilder.DefineVehicle`) never declares `NetworkTransform`, and nothing on the
                    //    owner side attached it ⇒ 📐 that query matched **0** entities (drop the clause and it
                    //    matches 1) ⇒ SimHost published **no `WorldPos` at all**. The IG ghost therefore never
                    //    received `SimTransform`, which is a **HARD** mandatory component, so
                    //    `GhostPromotionSystem` correctly declined to promote it — forever. 📄 tracker `AX-009`.
                    //
                    // ⭐⭐⭐ THIS HOOK WAS ALREADY WRITTEN FOR IT. The two lines below used to read
                    //    `if (world.HasComponent<NetworkTransform>(entity)) SetAuthority(...)` — an authority
                    //    grant for a component **nothing ever attached**, so the branch was dead. 📌 The same
                    //    dead-affordance shape this programme keeps finding: the guard was correct, and the
                    //    thing it guarded never arrived. ⇒ ⭐ attach it, and the grant becomes live.
                    //
                    // ⭐⭐ WHY HERE rather than in `NetworkSpawningSystem`. 📐 Measured: a bare
                    //    `AddComponent` there **throws** `"Component NetworkTransform is not registered"` —
                    //    the engine-level spawn system imposes a registration contract, and **37** worlds
                    //    register `TkbIdentity` while only `HrotSharedComponentRegistry` registers
                    //    `NetworkTransform`. ⇒ the engine-level attach would have needed 37 registry edits
                    //    (two of them FDP example scenarios). ⭐ This hook already runs on the one host that
                    //    owns `SimTransform`, in an assembly where the component IS registered.
                    //    ⚠ Cost stated honestly: it is per-host, so a FUTURE owning host must wire the same
                    //      hook. The rail below asserts the invariant on a real spawn rather than trusting it.
                    //
                    // ⚠ NON-OWNERS get nothing here, and need nothing: a replica's shadow is written by
                    //   `GeoSpatialIngressTranslator` on first receipt via `SetComponent` (upsert) — which
                    //   `GeoSpatialTranslatorTests` already asserts happens "even for freshly created ghost
                    //   entities".
                    //
                    // ⚠⚠ SEEDED TO `default` — ZEROS — DELIBERATELY. The translator publishes only when the
                    //   live pose differs from this shadow, or when the salted heartbeat fires at `% 600`
                    //   ticks. ⛔ Seeding from the entity's CURRENT `SimTransform` would make the first
                    //   comparison say "has not moved", leaving a stationary spawned entity INVISIBLE to
                    //   every other node for up to 600 ticks — 10 s at 60 Hz. Zeros force a first publish.
                    if (!world.HasComponent<NetworkTransform>(entity))
                        world.AddComponent(entity, default(NetworkTransform));
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
