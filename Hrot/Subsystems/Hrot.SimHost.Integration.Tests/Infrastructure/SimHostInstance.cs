using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Fbt;
using Fbt.Runtime;
using Hrot.Core.Mission;
using NedMissionPlan = Hrot.NED.Descriptors.MissionPlan;
using NedMissionTask = Hrot.NED.Descriptors.MissionTask;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Fdp.Toolkit.Perception.Components;
using Hrot.AI.Behaviors;
using Hrot.CGF;
using Hrot.CGF.Configuration;
using Hrot.CGF.Systems;
using Hrot.Common.Systems;   // Q65 obstacle 1: the request tier moved here
using Hrot.Common.EntityCreation;   // CE-160: the harness composes through the shared pack
using Fdp.Toolkit.Replication.Attributes;   // PRE-EXISTING BREAK: AttributeCompilerFactory was never imported
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Trajectory;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;
using CarKinem.Tkb;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Spatial;

using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.SimHost.Integration.Tests.Infrastructure
{
    // â”€â”€ Stubs (DDS-free test doubles) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// In-memory request source: push <see cref="EntityCreationRequest"/> messages
    /// synchronously for deterministic testing.
    /// </summary>
    public sealed class StubRequestSource : IEntityCreationRequestSource
    {
        private readonly List<EntityCreationRequest> _pending = new();

        /// <summary>Returns <c>true</c> when at least one request is queued and not yet consumed.</summary>
        public bool HasPendingRequests => _pending.Count > 0;

        public void Enqueue(EntityCreationRequest r) => _pending.Add(r);

        public void ProcessRequests(Action<EntityCreationRequest> handler)
        {
            foreach (var req in _pending)
                handler(req);
            _pending.Clear();
        }

        public void Dispose() { }
    }

    /// <summary>
    /// In-memory ACK sink: records acknowledgements written by
    /// <see cref="CreateEntityRequestSystem"/>.
    /// Stores records internally as <see cref="CreateUpdateDeleteEntityAck"/> for
    /// compatibility with existing test assertions.
    /// </summary>
    public sealed class StubAckSink : IEntityAckSink
    {
        private readonly List<CreateUpdateDeleteEntityAck> _written = new();

        public IReadOnlyList<CreateUpdateDeleteEntityAck> WrittenAcks => _written;

        public void WriteAck(Guid requestId, long entityId, EntityOperationStatus status)
            => _written.Add(new CreateUpdateDeleteEntityAck
            {
                RequestId  = requestId,
                EntityId   = (int)entityId,
                StatusCode = (int)status,
            });

        public CreateUpdateDeleteEntityAck? TryGetAck(Guid requestId)
        {
            foreach (var a in _written)
                if (a.RequestId == requestId)
                    return a;
            return null;
        }

        /// <summary>
        /// Returns the first terminal (non-InProgress) ACK matching <paramref name="requestId"/>,
        /// or <c>null</c> if no such ACK has been written yet.
        /// </summary>
        public CreateUpdateDeleteEntityAck? TryGetTerminalAck(Guid requestId)
        {
            foreach (var a in _written)
                if (a.RequestId == requestId && a.StatusCode != (int)EntityOperationStatus.InProgress)
                    return a;
            return null;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Monotonically increasing ID allocator with no DDS dependency.
    /// </summary>
    public sealed class StubIdAllocator : INetworkIdAllocator
    {
        private long _next;
        public long LastAllocatedId { get; private set; }

        public StubIdAllocator(long startId = 1000) => _next = startId;

        public long AllocateId() { LastAllocatedId = _next; return _next++; }
        public void Reset(long startId = 0) { _next = startId; LastAllocatedId = 0; }
        public void Dispose() { }
    }

    // â”€â”€ Simple ISystemRegistry adapter around SystemList â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Collects <see cref="IEcsModuleSystem"/> instances registered via
    /// <see cref="IEcsModule.RegisterSystems"/> for manual per-frame execution.
    /// </summary>
    internal sealed class SystemList : ISystemRegistry
    {
        private readonly List<IEcsModuleSystem> _systems = new();
        public IReadOnlyList<IEcsModuleSystem> Systems => _systems;

        public void RegisterSystem<T>(T system) where T : IEcsModuleSystem
            => _systems.Add(system);

        public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
        {
            _systems.Add(system);
            return system;
        }

        public void ExecuteAll(ISimulationView view, float dt)
        {
            foreach (var s in _systems)
                s.Execute(view, dt);
        }
    }

    // â”€â”€ Performance metrics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Captured frame-rate statistics from a performance run.</summary>
    public sealed class PerformanceMetrics
    {
        public float AverageFPS { get; init; }
        public float MinFPS     { get; init; }
        public float MaxFPS     { get; init; }
        public int   FrameCount { get; init; }
    }

    // â”€â”€ SimHostInstance â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Deterministic, DDS-free test harness that wires the complete SimHost pipeline
    /// (entity creation, mission dispatch, vehicle physics, geographic egress) into a
    /// single in-process simulation that can be driven tick-by-tick from xUnit tests.
    ///
    /// Usage pattern:
    /// <code>
    /// using var host = new SimHostInstance();
    /// var ack  = host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
    /// host.PublishEntityMission(mission);
    /// host.RunForSeconds(10);
    /// var geo  = host.ReadGeoSpatial(ack.NewEntityId);
    /// </code>
    /// </summary>
    public sealed class SimHostInstance : IDisposable
    {
        // â”€â”€ ECS world â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private readonly EntityRepository _world;

        // â”€â”€ Infrastructure â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private readonly WGS84Transform   _wgs84;
        private readonly NetworkEntityMap _entityMap;
        private readonly TkbDatabase      _tkbDb;
        private readonly BehaviorRegistry _behaviorRegistry;
        private readonly EntityLifecycleModule _elm;

        // â”€â”€ Public world accessors (accessible to MockExConClient) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public EntityRepository World    => _world;
        public NetworkEntityMap EntityMap => _entityMap;

        // â”€â”€ Public stubs (accessible to MockExConClient) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public readonly StubRequestSource RequestSource = new();
        public readonly StubAckSink       AckSink       = new();
        public readonly StubIdAllocator   IdAllocator   = new(startId: 1000);

        /// <summary>
        /// ⭐ <c>CE-160</c> — the node id this harness runs as. A request whose
        /// <c>OwnerAppInstanceId</c> does not match it is not targeted at this node and is dropped,
        /// so tests must build requests against THIS constant rather than a repeated literal
        /// (three test methods carried a bare <c>1</c> and a comment explaining it).
        /// </summary>
        public const int LocalNodeId = 1;

        /// <summary>
        /// ⭐⭐ <c>CE-160</c> — the translator list the pack composed, exposed so a rail can assert
        /// the harness projects the SAME component set as production. ⛔ Before the pack adoption
        /// this harness hand-rolled FIVE translators where <c>TkbTranslatorSet.Base()</c> carries
        /// SIX; nothing could see the drift because nothing could see the list.
        /// </summary>
        public IReadOnlyList<ITkbEntityTranslator> Translators { get; private set; } =
            System.Array.Empty<ITkbEntityTranslator>();

        // â”€â”€ Systems: IEcsModuleSystem-based (executed manually each tick) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private readonly CreateEntityRequestSystem      _requestSystem;
        private readonly NetworkSpawningSystem          _spawnSystem;
        private readonly EntityRequestFinalizationSystem   _finalizationSystem;
        private readonly SystemList                     _elmSystems  = new();
        private readonly SystemList                     _geoSystems  = new();

        // â"€â"€ Simulation system lists (executed manually via IEcsModuleSystem.Execute) â"€â"€â"€â"€â"€â"€â"€
        private readonly IReadOnlyList<IEcsModuleSystem> _inputSystems;
        private readonly IReadOnlyList<IEcsModuleSystem> _simSystems;
        private readonly IReadOnlyList<IEcsModuleSystem> _postSimSystems;

        // ── Diagnostic seams ──────────────────────────────────────────────────────
        // ⭐ Read-only views of what this harness actually SCHEDULES, and of the behaviour
        //   registry it resolves names against. A test that fails "the entity did not move"
        //   cannot distinguish "the system is absent" from "the system ran and did nothing";
        //   these two make that distinction measurable instead of inferred.
        public IReadOnlyList<IEcsModuleSystem> TestHook_InputSystems      => _inputSystems;
        public IReadOnlyList<IEcsModuleSystem> TestHook_SimulationSystems => _simSystems;
        public IReadOnlyList<IEcsModuleSystem> TestHook_PostSimSystems    => _postSimSystems;
        public BehaviorRegistry                TestHook_BehaviorRegistry  => _behaviorRegistry;

        // â”€â”€ Performance metrics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private bool               _metricsEnabled;
        private readonly List<float> _frameTimes = new();

        // â”€â”€ Disposal flag â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private bool _disposed;

        // â”€â”€ Constructor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public SimHostInstance()
        {
            // 1. World â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            _world = BuildWorld();

            // 2. Infrastructure â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            _wgs84 = new WGS84Transform();
            _wgs84.SetOrigin(32.0853, 34.7818, 10.0);   // Tel-Aviv origin (same as config.json)

            // 🔴🔴 PUBLISH IT AS THE WORLD SINGLETON — the harness HELD this transform and did not
            //   pass it, which is the SILENT-DEFAULT shape in its purest form.
            //
            // 📐 Measured 2026-09-01: every production host does this —
            //   SimHostApp.cs:509 · CgfSubsystem.cs:544 · EditorSubsystem.cs:1025 — because behaviour
            //   PARAMETER RESOLVERS reach the transform through the world, not through a captured
            //   closure: CgfNodes.ResolveMoveToParams (:163) reads
            //   HasSingletonManaged<IGeographicTransform>() and passes null when absent.
            //
            // ⛔ And a null transform does NOT fail loudly. CgfNodes.cs:205 guards the geo branch as
            //      if ((dto.TargetLat != 0 || dto.TargetLon != 0) && geoTransform != null)
            //   with Speed and ArrivalRadius assigned ABOVE it. ⇒ a MoveToLocation mission produced a
            //   NavigationIntent with Mode=DirectPoint, TargetSpeed=15, ArrivalRadius=5 and
            //   FinalDestination=(0,0) — an order to drive to where the vehicle already stands. The
            //   rail reported "moved 0.0m", which reads as broken physics and was nothing of the sort.
            //   📄 WhyDoesTheMissionNotMoveProbe carries the full per-hop measurement.
            _world.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(_wgs84);

            _entityMap        = new NetworkEntityMap();
            _tkbDb            = BuildTkbDatabase();
            _behaviorRegistry = BuildBehaviorRegistry(_wgs84, _entityMap);

            // 3-4. THE ENTITY-CREATION TIER -- built by the SHARED PACK, exactly as every
            //   production host builds it.
            //
            // CE-160: this harness used to hand-assemble the tier -- its own translator list, its
            //   own ELM wiring, and its own three `new` calls. That made it a SEVENTH composition
            //   root for entity creation, and it had already DRIFTED: the hand-rolled list carried
            //   FIVE translators where TkbTranslatorSet.Base() carries SIX (PresentationTkbTranslator
            //   was missing), so every test below projected a different component set than production.
            //
            //   The drift is the point. A test harness that assembles the pipeline itself cannot
            //   detect a defect in how the pipeline is assembled -- it can only ever prove that the
            //   SYSTEMS work, never that the PACK composes them correctly. Going through
            //   EntityCreationPack.Build makes the list correct BY CONSTRUCTION and turns the seven
            //   flow tests below into real coverage of the unification.
            //
            //   IsBroadcastArbiter is FALSE, matching CreateEntityRequestSystem's own default, which
            //   is what this harness passed implicitly before. The tests target their requests at
            //   OwnerAppInstanceId == NodeId, so the broadcast tiebreaker never applies.
            var jsonAttributeCompiler = AttributeCompilerFactory.Build(_wgs84);

            _elm = new EntityLifecycleModule(_tkbDb, new List<int>());

            var creation = EntityCreationPack.Build(new EntityCreationContext
            {
                World                 = _world,
                EntityMap             = _entityMap,
                TkbDb                 = _tkbDb,
                IdAllocator           = IdAllocator,
                Elm                   = _elm,
                NodeId                = LocalNodeId,
                NetworkRequestSource  = RequestSource,
                AckSink               = AckSink,
                JsonAttributeCompiler = jsonAttributeCompiler,
                IsBroadcastArbiter    = false,
            });

            // The pack calls Elm.SetTranslators; RegisterSystems follows it, as on every host.
            _elm.RegisterSystems(_elmSystems);

            _requestSystem      = creation.RequestSystem;
            _spawnSystem        = creation.SpawnSystem;
            _finalizationSystem = creation.FinalizationSystem;
            Translators         = creation.Translators;

            // The pack builds every piece; this harness schedules all three below in Tick(), so
            // nothing may be unserviceable. Asserting here rather than logging keeps the harness
            // honest about the thing it now exists to prove.
            var unserviceable = creation.Unserviceable(new object[]
                { _requestSystem, _spawnSystem, _finalizationSystem });
            if (unserviceable.Length > 0)
                throw new InvalidOperationException(
                    "SimHostInstance did not schedule every EntityCreationPack piece: " + unserviceable);

            // 5. Geographic systems â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            new GeographicModule(_wgs84).RegisterSystems(_geoSystems);

            // 6. Simulation-logic system lists ------------------------------------
            var roadNetwork    = new RoadNetworkBuilder().Build(10f, 100, 100);
            var trajectoryPool = new TrajectoryPoolManager();

            // Use dedicated packs instead of SimulationLogicModule to get IEcsModuleSystem lists.
            var musclePack = new SimHostCoreLogicPack(_entityMap, roadNetwork, trajectoryPool);
            var mapperRegistry = new TacticalIntentMapperRegistry();
            mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.DefendAreaMapper());
            mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.HullDownAttackMapper());
            var brainPack  = new CgfLogicPack(_behaviorRegistry, _entityMap,
                new ScenarioEntityCreationRequestSource(),
                mapperRegistry);

            var inputList   = new List<IEcsModuleSystem>();
            var simList     = new List<IEcsModuleSystem>();
            var postSimList = new List<IEcsModuleSystem>();

            foreach (var s in brainPack.InputSystems)       inputList.Add(s);
            foreach (var s in musclePack.InputSystems)      inputList.Add(s);

            foreach (var s in brainPack.SimulationSystems)  simList.Add(s);
            foreach (var s in musclePack.SimulationSystems) simList.Add(s);
            // MissionAdapterSystem bridges ActiveMissionPlan BehaviorParams into BrainBlackboard,
            // enabling end-to-end mission execution tests without a live CGF node.
            simList.Add(new MissionAdapterSystem());

            foreach (var s in musclePack.PostSimulationSystems) postSimList.Add(s);

            _inputSystems   = inputList;
            _simSystems     = simList;
            _postSimSystems = postSimList;

            var physicsModule = new PhysicsToolkitModule();
            physicsModule.Initialize(_world);

            // 7. Seed GlobalTime â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            const float dt = 1f / 60f;
            _world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = dt, TimeScale = 1.0f });
        }

        // â”€â”€ Public API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Creates a single entity synchronously.  Runs enough ticks to:
        /// (a) process the <see cref="CreateEntityRequest"/>,
        /// (b) spawn the ECS entity via <see cref="NetworkSpawningSystem"/>, and
        /// (c) confirm the entity is fully active in the world.
        /// </summary>
        /// <param name="tkbType">Entity template type (e.g. <see cref="TkbEntityTypes.Tank_M1Abrams"/>).</param>
        /// <param name="position">Cartesian spawn position (ENU metres from origin).</param>
        /// <returns>The <see cref="CreateUpdateDeleteEntityAck"/> produced by <see cref="CreateEntityRequestSystem"/>.</returns>
        public CreateUpdateDeleteEntityAck CreateEntity(long tkbType, Vector2 position = default)
        {
            var requestId = Guid.NewGuid();

            var request = new EntityCreationRequest
            {
                RequestId          = requestId,
                OwnerAppInstanceId = 1,
                TkbType            = tkbType,
                DisType            = 0,
            };

            RequestSource.Enqueue(request);

            // The restructured Tick() runs simulation first, then spawn.
            // Tick 1: CreateEntityRequestSystem fires (in spawn phase) â†’ SpawnEntityCommand
            //         published + ACK written; entity reaches Active within this same tick.
            Tick(1f / 60f);

            // Ticks 2-5: extra simulation ticks to let physics/behavior settle.
            // Entity is already Active after Tick 1; these ticks are safety margin.
            for (int i = 0; i < 4; i++) Tick(1f / 60f);

            var ack = AckSink.TryGetTerminalAck(requestId)
                ?? throw new InvalidOperationException($"No terminal ACK received for request {requestId}");

            return ack;
        }

        /// <summary>
        /// Assigns an <see cref="EntityMission"/> to the specified entity by writing
        /// a <see cref="MissionPlanQueue"/> component (bypasses DDS ingress translator
        /// for deterministic testing).
        /// </summary>
        public void PublishEntityMission(EntityMission mission)
        {
            if (!_entityMap.TryGetEntity(mission.EntityId, out var entity))
                throw new InvalidOperationException(
                    $"Entity with network-id {mission.EntityId} not found in entity map.");

            var queue = BuildQueue(mission.Plan);
            _world.SetComponent(entity, queue);
            _world.SetManagedComponent(entity, MapToActiveMissionPlan(mission));
        }

        private MissionPlanQueue BuildQueue(NedMissionPlan plan)
        {
            var queue = new MissionPlanQueue
            {
                CurrentPhase = 0,
                PhaseElapsedSeconds = 0f,
                PhaseCount = (byte)Math.Min(plan.Tasks?.Count ?? 0, MissionPlanQueue.MaxPhases)
            };

            var tasks = plan.Tasks ?? new List<NedMissionTask>();
            int count = Math.Min(tasks.Count, MissionPlanQueue.MaxPhases);

            for (int i = 0; i < count; i++)
            {
                var task = tasks[i];
                int behaviorId = ResolveBehaviorId(task.BehaviorId);
                var (trigger, param) = ResolveTrigger(task.Triggers);
                
                System.Console.WriteLine($"[SimHostInstance] task {i}: trigger={trigger}, param={param}");

                queue.Phases[i] = new MissionPhase
                {
                    BehaviorId   = behaviorId,
                    Trigger      = trigger,
                    TriggerParam = param
                };
            }

            queue.PhaseCount = (byte)count;
            return queue;
        }

        private int ResolveBehaviorId(string? behaviorName)
        {
            if (string.IsNullOrWhiteSpace(behaviorName))
                return 0;

            if (_behaviorRegistry.TryGetId(behaviorName, out int behaviorId))
                return behaviorId;

            return 0;
        }

        private static ActiveMissionPlan MapToActiveMissionPlan(EntityMission mission)
        {
            var plan = new DomainMissionPlan
            {
                ActiveTaskId = mission.Plan.ActiveTaskId,
                Tasks        = mission.Plan.Tasks?.ConvertAll(t => new DomainMissionTask
                {
                    TaskId          = t.TaskId,
                    ExecutingEngine = t.ExecutingEngine ?? string.Empty,
                    BehaviorName      = t.BehaviorId      ?? string.Empty,
                    BehaviorParams  = t.BehaviorParams  ?? string.Empty,
                }) ?? new List<DomainMissionTask>()
            };
            return new ActiveMissionPlan { Plan = plan };
        }

        private static (Fdp.Toolkit.Behavior.Components.MissionTrigger Trigger, float Param) ResolveTrigger(
            List<Hrot.NED.Descriptors.MissionTrigger>? triggers)
        {
            if (triggers == null || triggers.Count == 0)
                return (Fdp.Toolkit.Behavior.Components.MissionTrigger.TimerElapsed, float.MaxValue);

            var trigger = triggers[0];
            var type = trigger.Type ?? string.Empty;

            return type switch
            {
                "TimerElapsed"       => (Fdp.Toolkit.Behavior.Components.MissionTrigger.TimerElapsed, ParseTriggerParam(trigger.Params)),
                "ReachedDestination" => (Fdp.Toolkit.Behavior.Components.MissionTrigger.BehaviorFinished, 0f),
                "HealthCritical"     => (Fdp.Toolkit.Behavior.Components.MissionTrigger.HealthCritical, ParseTriggerParam(trigger.Params)),
                _                    => (Fdp.Toolkit.Behavior.Components.MissionTrigger.TimerElapsed, float.MaxValue)
            };
        }

        private static float ParseTriggerParam(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0f;

            return float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : 0f;
        }

        /// <summary>
        /// Runs the simulation loop for <paramref name="seconds"/> wall-clock-equivalent seconds
        /// at 60 Hz (1/60 s per tick).
        /// </summary>
        public void RunForSeconds(float seconds)
        {
            int ticks = Math.Max(1, (int)(seconds * 60f));
            RunForTicks(ticks);
        }

        /// <summary>Runs exactly <paramref name="ticks"/> simulation ticks at 1/60 s each.</summary>
        public void RunForTicks(int ticks)
        {
            float dt = 1f / 60f;
            for (int i = 0; i < ticks; i++)
            {
                if (_metricsEnabled)
                {
                    var sw = Stopwatch.StartNew();
                    Tick(dt);
                    sw.Stop();
                    float frameTimeSec = (float)sw.Elapsed.TotalSeconds;
                    _frameTimes.Add(frameTimeSec > 0f ? 1f / frameTimeSec : 60f);
                }
                else
                {
                    Tick(dt);
                }
            }
        }

        /// <summary>
        /// Reads the latest <see cref="WorldPos"/> state of the entity identified by
        /// <paramref name="networkId"/> by querying its <see cref="SimTransform"/> ECS
        /// component and converting to geodetic coordinates on-the-fly.
        /// Returns <c>null</c> when no such entity is found.
        /// </summary>
        public WorldPos? ReadGeoSpatial(int networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return null;

            if (!_world.HasComponent<SimTransform>(entity))
                return null;

            ref readonly var simTf = ref _world.GetComponentRO<SimTransform>(entity);
            var (lat, lon, alt) = _wgs84.ToGeodetic(simTf.Position);

            return new WorldPos
            {
                EntityId = networkId,
                Time     = DateTime.UtcNow,
                Pos      = new Hrot.NED.Common.GeoPoint
                {
                    Latitude  = lat,
                    Longitude = lon,
                    Altitude  = alt,
                }
            };
        }

        /// <summary>
        /// Converts a <see cref="GeoPoint"/> back to a local Cartesian
        /// <see cref="Vector2"/> (X = East, Y = North) using the same WGS-84 origin as
        /// the simulation.
        /// </summary>
        public Vector2 GeoToCartesian(GeoPoint geoPos)
        {
            var cart = _wgs84.ToCartesian(geoPos.Latitude, geoPos.Longitude, geoPos.Altitude);
            return new Vector2(cart.X, cart.Y);
        }

        /// <summary>Overload that accepts the NED protocol <see cref="Hrot.NED.Common.GeoPoint"/>.</summary>
        public Vector2 GeoToCartesian(Hrot.NED.Common.GeoPoint geoPos)
            => GeoToCartesian(new GeoPoint { Latitude = geoPos.Latitude, Longitude = geoPos.Longitude, Altitude = geoPos.Altitude });

        public GeoPoint CartesianToGeo(System.Numerics.Vector3 cartesian)
        {
            var geo = _wgs84.ToGeodetic(cartesian);
            return new GeoPoint { Latitude = geo.lat, Longitude = geo.lon, Altitude = geo.alt };
        }

        /// <summary>
        /// Enables frame-time collection.  Must be called before
        /// <see cref="RunForSeconds"/> / <see cref="RunForTicks"/>.
        /// </summary>
        public void EnablePerformanceMetrics() => _metricsEnabled = true;

        /// <summary>
        /// Returns the FPS statistics collected since <see cref="EnablePerformanceMetrics"/>
        /// was called.
        /// </summary>
        public PerformanceMetrics GetPerformanceMetrics()
        {
            if (_frameTimes.Count == 0)
                return new PerformanceMetrics { AverageFPS = 60f, MinFPS = 60f, MaxFPS = 60f, FrameCount = 0 };

            float sum = 0f, min = float.MaxValue, max = float.MinValue;
            foreach (var fps in _frameTimes)
            {
                sum += fps;
                if (fps < min) min = fps;
                if (fps > max) max = fps;
            }

            return new PerformanceMetrics
            {
                AverageFPS = sum / _frameTimes.Count,
                MinFPS     = min,
                MaxFPS     = max,
                FrameCount = _frameTimes.Count,
            };
        }

        // â”€â”€ IDisposable â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_world.HasSingleton<RaycastBatchData>())
            {
                ref var batch = ref _world.GetSingleton<RaycastBatchData>();
                if (batch.Hits.IsCreated) batch.Hits.Dispose();
            }
            _world.Dispose();
        }

        // â”€â”€ Internal tick loop â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Executes one simulation tick in the correct system-phase order,
        /// aligned with <c>SimHostApp.OnUpdate()</c>: simulation logic runs first
        /// (on the previous tick's read buffer), then entity spawn and lifecycle
        /// processing runs last.
        ///
        /// <para>
        /// When no spawn request is pending the lifecycle sub-swaps are skipped,
        /// so the single end-of-tick <see cref="EntityEventBus.SwapBuffers"/> makes
        /// this tick's simulation events (e.g. <c>AssignBehaviorEvent</c>) visible
        /// on the read buffer for the next tick's input group â€” exactly mirroring
        /// the production <c>SimHostApp.OnUpdate()</c> pattern.
        /// </para>
        ///
        /// <para>
        /// When a spawn IS pending, sub-swaps A and B are required so that the event bus
        /// mediates the spawn pipeline synchronously within one tick.  During spawn ticks
        /// the sub-swaps consume the simulation write-buffer early; <see cref="MissionAdapterSystem"/>'s
        /// direct <c>BehaviorState</c> write ensures mission events are never silently
        /// dropped regardless of bus timing.
        /// </para>
        /// </summary>
        private void Tick(float dt)
        {
            // 🔴🔴🔴 ADVANCE THE WORLD'S VERSION CLOCK — the kernel does this on EVERY frame and this
            //   hand-rolled loop did not. ModuleHostKernel.cs:495 is `_liveWorld.Tick(); // Increment
            //   version`, called UNCONDITIONALLY (BehaviorFrame.cs:16 documents that word).
            //
            // ⛔ Without it GlobalVersion stays pinned at its initial 1 for the life of the harness
            //   (measured 2026-09-01: "version 1→1" on every tick while components were demonstrably
            //   being written). ⇒ EVERY CHANGE-DETECTION QUERY IN THE HARNESS IS PERMANENTLY EMPTY:
            //   QueryDelta(query, since) can never report a change when no version ever exceeds `since`.
            //
            // ⚠ That is not a niche facility. NavigationIntentBridgeSystem — the seam that turns a
            //   behaviour's NavigationIntent into the NavState physics reads — scans exactly this way
            //   (QueryDelta(query, _lastScanTick)). It was scheduled, ticked, and structurally blind:
            //   a MoveToLocation mission produced a perfectly good intent and NavState never left
            //   Mode=None, so the entity stood still and the rail reported "moved 0.0m".
            //   📄 WhyDoesTheMissionNotMoveProbe proves the mapping logic itself is correct: driving a
            //      fresh bridge by hand sets Mode=Direct, it survives the next scheduled tick, and the
            //      vehicle starts moving.
            _world.Tick();

            var view   = (ISimulationView)_world;
            var cmdBuf = (EntityCommandBuffer)view.GetCommandBuffer();

            // Update GlobalTime so ComponentSystem.DeltaTime is valid.
            _world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = dt, TimeScale = 1.0f });

            // â”€â”€ Phase 1: Simulation Logic â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Matches SimHostApp.OnUpdate() order: simulation runs first on the
            // previous tick's read buffer so BehaviorIngressSystem and other input
            // systems correctly see events published in the prior tick.
            foreach (var s in _inputSystems)   s.Execute(view, dt);
            foreach (var s in _simSystems)     s.Execute(view, dt);
            foreach (var s in _postSimSystems) s.Execute(view, dt);

            // â”€â”€ Phase 2: Geographic systems â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            _geoSystems.ExecuteAll(view, dt);
            cmdBuf.Playback(_world);

            // â”€â”€ Phase 3: Entity Spawn & Lifecycle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Only execute the spawn pipeline (and its internal sub-swaps) when a
            // create-entity request is actually pending.  Skipping sub-swaps in
            // the common steady-state case preserves the simulation write-buffer
            // so the final swap below makes sim events available next tick.
            if (RequestSource.HasPendingRequests)
            {
                // CreateEntityRequestSystem â†’ publishes SpawnEntityCommand + writes ACK.
                _requestSystem.Execute(view, dt);

                // Sub-swap A: SpawnEntityCommand moves to read buffer so
                // NetworkSpawningSystem.ReadManagedEvents can pick it up.
                _world.Bus.SwapBuffers();

                // NetworkSpawningSystem â†’ consumes SpawnEntityCommand, creates ECS entity,
                // calls elm.BeginConstruction (publishes ConstructionOrder via cmd buf).
                _spawnSystem.Execute(view, dt);
                cmdBuf.Playback(_world);

                // Sub-swap B: ConstructionOrder moves to read buffer so
                // BlueprintApplicationSystem can apply the TKB template.
                _world.Bus.SwapBuffers();

                // ELM systems: BlueprintApplicationSystem stamps TKB components;
                // LifecycleSystem processes any ConstructionAck (none expected with
                // zero participants in the test harness).
                _elmSystems.ExecuteAll(view, dt);
                cmdBuf.Playback(_world);
            }

            // Force-activate any entity still in Constructing state.
            // (With zero ELM participants the ACK protocol never completes, so we
            // short-circuit it here for the test harness.)
            ActivateConstructingEntities();

            // Phase-2 ACK dispatch: now that entities have been force-activated, the
            // finalization system can detect Active lifecycle and emit Success ACKs.
            _finalizationSystem.Execute(view, dt);

            // â”€â”€ Phase 4: Final swap â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // In non-spawn ticks: sim events written in Phase 1 move to the read
            // buffer and are visible to the next tick's input group.
            // In spawn ticks:  spawn sub-swaps have already cycled the write buffer;
            //                  this swap makes ELM lifecycle events available.
            _world.Bus.SwapBuffers();
        }

        /// <summary>
        /// Short-circuits the ELM construction ACK protocol: any entity still in
        /// <see cref="EntityLifecycle.Constructing"/> is immediately promoted to
        /// <see cref="EntityLifecycle.Active"/> so simulation systems can process it.
        /// </summary>
        private void ActivateConstructingEntities()
        {
            var view = (ISimulationView)_world;

            var query = view.Query()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.Constructing)
                .Build();

            foreach (var entity in query)
                _world.SetLifecycleState(entity, EntityLifecycle.Active);
        }

        // â”€â”€ World factory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static EntityRepository BuildWorld()
        {
            var world = new EntityRepository();

            // â”€â”€ IG metadata component â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            world.RegisterComponent<Fdp.Core.EntityInfo>();
            world.RegisterManagedComponent<ActiveMissionPlan>();

            // â”€â”€ Network components â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkOwnership>();
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterComponent<TkbIdentity>();
            world.RegisterComponent<GhostStateTracker>();
            world.RegisterComponent<PendingNetworkAck>();

            // â”€â”€ Geographic components â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();

            // â”€â”€ Behavior toolkit components â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            world.RegisterComponent<BehaviorState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<Hrot.CGF.Components.MissionAdapterState>();

            // HSM brain tiers (for APC-style HSM behaviors)
            world.RegisterComponent<BrainHsm64>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<PassengerBuffer>();
            world.RegisterComponent<IsEmbarkedTag>();

            // Perception
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();

            // Combat & Physics
            world.RegisterComponent<PhysicsCollider>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<BallisticProjectile>();

            // â”€â”€ CarKinem / Navigation components â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            world.RegisterComponent<CarKinem.Core.VehicleState>();
            world.RegisterComponent<CarKinem.Core.VehicleParams>();
            world.RegisterComponent<CarKinem.Core.NavState>();
            world.RegisterComponent<CarKinem.Formation.FormationController>();

            // â”€â”€ Mission components â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            world.RegisterComponent<MissionPlanQueue>();

            // â”€â”€ CQRS navigation contract (MOD1-P1T1 / CT-MOD1-C2) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // These components live in NedTkbBuilder.WithBehavior templates
            // and are written/read by MoveToExecutor and NavigationExecutionSystem.
            world.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();
            world.RegisterComponent<Fdp.Toolkit.Navigation.NavigationStatus>();
            world.RegisterComponent<CarKinem.Core.FrustrationTicks>();

            // â”€â”€ Lifecycle events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            world.RegisterEvent<ConstructionOrder>();
            world.RegisterEvent<ConstructionAck>();
            world.RegisterEvent<DestructionOrder>();
            world.RegisterEvent<DestructionAck>();

            return world;
        }

        // â”€â”€ TKB database factory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Builds a <see cref="TkbDatabase"/> with Tank_M1Abrams template.
        /// <summary>
        /// Builds a <see cref="TkbDatabase"/> with templates for all vehicle entity types.
        /// The template adds all simulation components required by CarKiem / Behavior,
        /// including the CQRS navigation contract components (CT-MOD1-C2).
        /// </summary>
        private static TkbDatabase BuildTkbDatabase()
        {
            var db = new TkbDatabase();

            // Register all entity types using the production builder.
            // Unmanaged components not registered in the world are silently skipped.
            // Managed components with factories (SimCombatDef, VisualData) are excluded
            // from the production catalog registration here by building without .WithCombat/.WithVisual.
            RegisterBehaviorTemplate(db, TkbEntityTypes.Tank_M1Abrams,     "M1 Abrams",    20.0f, VehicleClass.Tank);
            RegisterBehaviorTemplate(db, TkbEntityTypes.IFV_Bradley,       "M2 Bradley",   18.0f, VehicleClass.Tank);
            RegisterBehaviorTemplate(db, TkbEntityTypes.Truck_HMMWV,       "HMMWV",        25.0f, VehicleClass.Truck);
            RegisterBehaviorTemplate(db, TkbEntityTypes.Tank_T72,          "T-72",         17.0f, VehicleClass.Tank);
            RegisterBehaviorTemplate(db, TkbEntityTypes.Infantry_Rifleman, "Rifleman",      2.5f, VehicleClass.Pedestrian);

            return db;
        }

        /// <summary>
        /// Creates and registers a minimal simulation-ready vehicle template.
        /// Mirrors production <c>NedTkbBuilder.WithBehavior</c> without IG-only visual factories.
        /// </summary>
        private static void RegisterBehaviorTemplate(TkbDatabase db, long tkbType, string name,
            float maxSpeed, CarKinem.Core.VehicleClass vehicleClass)
        {
            var template = new TkbTemplate(name, tkbType);

            // Physics DTO -- consumed by VehicleKinematicsTkbTranslator and SpatialCoreTkbTranslator.
            var preset = CarKinem.Core.VehiclePresets.GetPreset(vehicleClass);
            template.AddDescriptor(new TkbMasterDto { CustomName = name });
            template.AddDescriptor(new VehicleParametersDto
            {
                Length      = preset.Length,
                Width       = preset.Width,
                MaxSpeedFwd = maxSpeed,
                MaxAccel    = preset.MaxAccel,
                // Carry the class itself, not just the dimensions it implied.  Flattening
                // the preset lost it, so the translator re-derived PersonalCar and every
                // template in this harness got a car's steering and accel gain regardless
                // of the vehicleClass the caller asked for.
                VehicleClass = vehicleClass,
            });

            // Behavior DTO -- consumed by BehaviorTkbTranslator.
            template.AddDescriptor(new BehaviorProfileDto
            {
                SimTier     = Fdp.Toolkit.Behavior.BehaviorConstants.SimTierTactical,
                BrainTier   = Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBTree,
                CanMove     = true,
                CanShoot    = true,
                CanInteract = true,
            });

            // Network replication gate: entity must have NetworkTransform before going Live.
            template.AddMandatoryComponent<NetworkTransform>(isHard: false, softTimeoutFrames: 10);

            // Managed components not covered by any translator.
            template.AddMandatoryComponent<ActiveMissionPlan>();

            db.Register(template);
        }

        // â”€â”€ Behavior registry factory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static BehaviorRegistry BuildBehaviorRegistry(
            Fdp.Modules.Geographic.IGeographicTransform wgs84,
            NetworkEntityMap entityMap)
        {
            var reg = new BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(reg);
            return reg;
        }
    }
}

