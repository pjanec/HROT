using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IG.Components;
using Bagira.Map.Common;
using Bagira.Map.Definitions.Tkb;
using Bagira.SimHost;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Systems;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Trajectory;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Physics.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Bagira.SimHost.Integration.Tests.Infrastructure
{
    // ── Stubs (DDS-free test doubles) ────────────────────────────────────────────

    /// <summary>
    /// In-memory request source: push <see cref="CreateEntityRequest"/> messages
    /// synchronously for deterministic testing.
    /// </summary>
    public sealed class StubRequestSource : ICreateEntityRequestSource
    {
        private readonly List<CreateEntityRequest> _pending = new();

        public void Enqueue(CreateEntityRequest r) => _pending.Add(r);

        public void ProcessRequests(Action<CreateEntityRequest> processor)
        {
            foreach (var req in _pending)
                processor(req);
            _pending.Clear();
        }
    }

    /// <summary>
    /// In-memory ACK sink: records every <see cref="CreateEntityAck"/> written by
    /// <see cref="CreateEntityRequestSystem"/>.
    /// </summary>
    public sealed class StubAckSink : ICreateEntityAckSink
    {
        private readonly List<CreateEntityAck> _written = new();

        public IReadOnlyList<CreateEntityAck> WrittenAcks => _written;

        public void WriteAck(CreateEntityAck ack) => _written.Add(ack);

        public CreateEntityAck? TryGetAck(Guid requestId)
        {
            foreach (var a in _written)
                if (a.RequestId == requestId)
                    return a;
            return null;
        }
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

    // ── Simple ISystemRegistry adapter around SystemList ─────────────────────────

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

        public void ExecuteAll(ISimulationView view, float dt)
        {
            foreach (var s in _systems)
                s.Execute(view, dt);
        }
    }

    // ── Performance metrics ───────────────────────────────────────────────────────

    /// <summary>Captured frame-rate statistics from a performance run.</summary>
    public sealed class PerformanceMetrics
    {
        public float AverageFPS { get; init; }
        public float MinFPS     { get; init; }
        public float MaxFPS     { get; init; }
        public int   FrameCount { get; init; }
    }

    // ── SimHostInstance ───────────────────────────────────────────────────────────

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
        // ── ECS world ────────────────────────────────────────────────────────────
        private readonly EntityRepository _world;

        // ── Infrastructure ───────────────────────────────────────────────────────
        private readonly WGS84Transform   _wgs84;
        private readonly NetworkEntityMap _entityMap;
        private readonly TkbDatabase      _tkbDb;
        private readonly DoctrineRegistry _doctrineRegistry;
        private readonly EntityLifecycleModule _elm;

        // ── Public world accessors (accessible to MockIOSClient) ────────────────
        public EntityRepository World    => _world;
        public NetworkEntityMap EntityMap => _entityMap;

        // ── Public stubs (accessible to MockIOSClient) ───────────────────────────
        public readonly StubRequestSource RequestSource = new();
        public readonly StubAckSink       AckSink       = new();
        public readonly StubIdAllocator   IdAllocator   = new(startId: 1000);

        // ── Systems: IEcsModuleSystem-based (executed manually each tick) ────────────
        private readonly CreateEntityRequestSystem _requestSystem;
        private readonly NetworkSpawningSystem     _spawnSystem;
        private readonly SystemList                _elmSystems  = new();
        private readonly SystemList                _geoSystems  = new();

        // ── Systems: ComponentSystem-based (executed via SystemGroup) ─────────────
        private readonly SystemGroup _inputGroup;
        private readonly SystemGroup _simGroup;
        private readonly SystemGroup _postSimGroup;

        // ── Performance metrics ──────────────────────────────────────────────────
        private bool               _metricsEnabled;
        private readonly List<float> _frameTimes = new();

        // ── Disposal flag ────────────────────────────────────────────────────────
        private bool _disposed;

        // ── Constructor ──────────────────────────────────────────────────────────

        public SimHostInstance()
        {
            // 1. World ─────────────────────────────────────────────────────────────
            _world = BuildWorld();

            // 2. Infrastructure ────────────────────────────────────────────────────
            _wgs84 = new WGS84Transform();
            _wgs84.SetOrigin(32.0853, 34.7818, 10.0);   // Tel-Aviv origin (same as config.json)

            _entityMap        = new NetworkEntityMap();
            _tkbDb            = BuildTkbDatabase();
            _doctrineRegistry = BuildDoctrineRegistry(_wgs84);

            // 3. Entity lifecycle module (empty participant list → bypass ACK protocol) ─
            _elm = new EntityLifecycleModule(_tkbDb, new List<int>());
            _elm.RegisterSystems(_elmSystems);

            // 4. Request / spawn systems ────────────────────────────────────────────
            var jsonAttributeCompiler = AttributeCompilerFactory.Build(_wgs84);
            _requestSystem = new CreateEntityRequestSystem(
                RequestSource, AckSink, _tkbDb, IdAllocator, localNodeId: 1, _wgs84,
                jsonAttributeCompiler);

            _spawnSystem = new NetworkSpawningSystem(
                _tkbDb, _elm, _entityMap, IdAllocator, localNodeId: 1,
                onEntitySpawned: (world, entity, isLocalAuthority) =>
                {
                    // Mark locally-owned physics components as authoritative so
                    // CarKinematicsSystem (.WithOwned<SimTransform>()) processes this entity.
                    if (isLocalAuthority && world.HasComponent<SimTransform>(entity))
                        world.SetAuthority<SimTransform>(entity, true);
                });

            // 5. Geographic systems ─────────────────────────────────────────────────
            new GeographicModule(_wgs84).RegisterSystems(_geoSystems);

            // 6. Simulation-logic SystemGroup ──────────────────────────────────────
            var roadNetwork    = new RoadNetworkBuilder().Build(10f, 100, 100);
            var trajectoryPool = new TrajectoryPoolManager();

            _inputGroup = new SystemGroup();
            _inputGroup.Create(_world);
            _simGroup = new SystemGroup();
            _simGroup.Create(_world);
            _postSimGroup = new SystemGroup();
            _postSimGroup.Create(_world);

            var simLogicModule = new SimulationLogicModule(
                _doctrineRegistry,
                _entityMap,
                vehicleAPI:              null,
                roadNetwork:             roadNetwork,
                trajectoryPool:          trajectoryPool,
                formationTemplateManager: null);
            simLogicModule.RegisterSystems(_inputGroup, _simGroup, _postSimGroup);
            _simGroup.AddSystem(new Bagira.SimHost.Systems.MissionAdapterSystem(_doctrineRegistry, _entityMap));

            var physicsModule = new PhysicsToolkitModule();
            physicsModule.Initialize(_world);

            // 7. Seed GlobalTime ────────────────────────────────────────────────────
            const float dt = 1f / 60f;
            _world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = dt, TimeScale = 1.0f });
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a single entity synchronously.  Runs enough ticks to:
        /// (a) process the <see cref="CreateEntityRequest"/>,
        /// (b) spawn the ECS entity via <see cref="NetworkSpawningSystem"/>, and
        /// (c) confirm the entity is fully active in the world.
        /// </summary>
        /// <param name="tkbType">Entity template type (e.g. <see cref="TkbEntityTypes.Tank_M1Abrams"/>).</param>
        /// <param name="position">Cartesian spawn position (ENU metres from origin).</param>
        /// <returns>The <see cref="CreateEntityAck"/> produced by <see cref="CreateEntityRequestSystem"/>.</returns>
        public CreateEntityAck CreateEntity(long tkbType, Vector2 position = default)
        {
            var requestId = Guid.NewGuid();

            var request = new CreateEntityRequest
            {
                RequestId = requestId,
                Owner     = new DDS.DM.NodeId { AppDomainId = 1, AppInstanceId = 1 },
                Flags     = 0,
                InitialDescriptors = new List<EntityDescriptorUnion>
                {
                    new EntityDescriptorUnion
                    {
                        _d           = EDescriptorType.dtEntityMaster,
                        EntityMaster = new EntityMaster { TkbType = tkbType, DisType = default }
                    }
                }
            };

            RequestSource.Enqueue(request);

            // Tick 1: CreateEntityRequestSystem fires → SpawnEntityCommand published + ACK written
            Tick(1f / 60f);

            // Tick 2: NetworkSpawningSystem consumes SpawnEntityCommand → entity created
            Tick(1f / 60f);

            // Ticks 3-5: ELM lifecycle events processed; entity promoted to Active
            for (int i = 0; i < 3; i++) Tick(1f / 60f);

            var ack = AckSink.TryGetAck(requestId)
                ?? throw new InvalidOperationException($"No ACK received for request {requestId}");

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
            _world.SetManagedComponent(entity, new Bagira.SimHost.Components.EntityMissionHolder { Mission = mission });
        }

        private MissionPlanQueue BuildQueue(MissionPlan plan)
        {
            var queue = new MissionPlanQueue
            {
                CurrentPhase = 0,
                PhaseElapsedSeconds = 0f,
                PhaseCount = (byte)Math.Min(plan.Tasks?.Count ?? 0, MissionPlanQueue.MaxPhases)
            };

            var tasks = plan.Tasks ?? new List<MissionTask>();
            int count = Math.Min(tasks.Count, MissionPlanQueue.MaxPhases);

            for (int i = 0; i < count; i++)
            {
                var task = tasks[i];
                int doctrineId = ResolveDoctrineId(task.BehaviorId);
                var (trigger, param) = ResolveTrigger(task.Triggers);
                
                System.Console.WriteLine($"[SimHostInstance] task {i}: trigger={trigger}, param={param}");

                queue.Phases[i] = new MissionPhase
                {
                    DoctrineId   = doctrineId,
                    Trigger      = trigger,
                    TriggerParam = param
                };
            }

            queue.PhaseCount = (byte)count;
            return queue;
        }

        private int ResolveDoctrineId(string? behaviorId)
        {
            if (string.IsNullOrWhiteSpace(behaviorId))
                return 0;

            if (_doctrineRegistry.TryGetId(behaviorId, out int doctrineId))
                return doctrineId;

            return 0;
        }

        private static (FDP.Toolkit.Behavior.Components.MissionTrigger Trigger, float Param) ResolveTrigger(
            List<Bagira.BDC.SSTD.MissionTrigger>? triggers)
        {
            if (triggers == null || triggers.Count == 0)
                return (FDP.Toolkit.Behavior.Components.MissionTrigger.TimerElapsed, float.MaxValue);

            var trigger = triggers[0];
            var type = trigger.Type ?? string.Empty;

            return type switch
            {
                "TimerElapsed"       => (FDP.Toolkit.Behavior.Components.MissionTrigger.TimerElapsed, ParseTriggerParam(trigger.Params)),
                "ReachedDestination" => (FDP.Toolkit.Behavior.Components.MissionTrigger.ReachedDestination, 0f),
                "HealthCritical"     => (FDP.Toolkit.Behavior.Components.MissionTrigger.HealthCritical, ParseTriggerParam(trigger.Params)),
                _                    => (FDP.Toolkit.Behavior.Components.MissionTrigger.TimerElapsed, float.MaxValue)
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
        /// Reads the latest <see cref="GeoSpatial"/> state of the entity identified by
        /// <paramref name="networkId"/> by querying its <see cref="SimTransform"/> ECS
        /// component and converting to geodetic coordinates on-the-fly.
        /// Returns <c>null</c> when no such entity is found.
        /// </summary>
        public GeoSpatial? ReadGeoSpatial(int networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return null;

            if (!_world.HasComponent<SimTransform>(entity))
                return null;

            ref readonly var simTf = ref _world.GetComponentRO<SimTransform>(entity);
            var (lat, lon, alt) = _wgs84.ToGeodetic(simTf.Position);

            return new GeoSpatial
            {
                EntityId = networkId,
                Time     = DateTime.UtcNow,
                Pos      = new DDS.DM.GeoPosition
                {
                    Latitude  = lat,
                    Longitude = lon,
                    Altitude  = alt,
                }
            };
        }

        /// <summary>
        /// Converts a <see cref="DDS.DM.GeoPosition"/> back to a local Cartesian
        /// <see cref="Vector2"/> (X = East, Y = North) using the same WGS-84 origin as
        /// the simulation.
        /// </summary>
        public Vector2 GeoToCartesian(DDS.DM.GeoPosition geoPos)
        {
            var cart = _wgs84.ToCartesian(geoPos.Latitude, geoPos.Longitude, geoPos.Altitude);
            return new Vector2(cart.X, cart.Y);
        }

        public DDS.DM.GeoPosition CartesianToGeo(System.Numerics.Vector3 cartesian)
        {
            var geo = _wgs84.ToGeodetic(cartesian);
            return new DDS.DM.GeoPosition { Latitude = geo.lat, Longitude = geo.lon, Altitude = geo.alt };
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

        // ── IDisposable ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_world.HasSingleton<RaycastBatchData>())
            {
                ref var batch = ref _world.GetSingleton<RaycastBatchData>();
                if (batch.Requests.IsCreated) batch.Requests.Dispose();
                if (batch.Hits.IsCreated) batch.Hits.Dispose();
            }
            _postSimGroup.Dispose();
            _inputGroup.Dispose();
            _simGroup.Dispose();
            _world.Dispose();
        }

        // ── Internal tick loop ────────────────────────────────────────────────────

        /// <summary>
        /// Executes one simulation tick in the correct system-phase order.
        /// </summary>
        private void Tick(float dt)
        {
            var view   = (ISimulationView)_world;
            var cmdBuf = (EntityCommandBuffer)view.GetCommandBuffer();

            // Update GlobalTime so ComponentSystem.DeltaTime is valid.
            _world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = dt, TimeScale = 1.0f });

            // ── Phase 1: Input ─────────────────────────────────────────────────
            // CreateEntityRequestSystem → publishes SpawnEntityCommand + writes ACK
            _requestSystem.Execute(view, dt);

            // Swap so the SpawnEntityCommand written to the write-buffer above is now
            // visible on the read-buffer for NetworkSpawningSystem's ConsumeManagedEvents.
            _world.Bus.SwapBuffers();

            // ── Phase 2: BeforeSync ────────────────────────────────────────────
            // NetworkSpawningSystem → consumes SpawnEntityCommand, creates ECS entity,
            // calls elm.BeginConstruction (publishes ConstructionOrder to cmd buf).
            _spawnSystem.Execute(view, dt);

            // Flush cmd-buf so ConstructionOrder events become readable this tick.
            cmdBuf.Playback(_world);
            _world.Bus.SwapBuffers();

            // ELM systems: BlueprintApplicationSystem processes ConstructionOrder,
            // LifecycleSystem processes ConstructionAck (from previous frame).
            _elmSystems.ExecuteAll(view, dt);

            // Flush again so lifecycle state changes are visible.
            cmdBuf.Playback(_world);
            _world.Bus.SwapBuffers();

            // Force-activate any entity still in Constructing state.
            // (With zero participants the ELM never receives ConstructionAck, so we
            // short-circuit the ACK protocol for the test harness.)
            ActivateConstructingEntities();

            // ── Phase 3: Simulation logic (ComponentSystem-based, sorted by [UpdateInGroup]) ─
            _inputGroup.Run();
            _simGroup.Run();
            _postSimGroup.Run();

            // ── Phase 4: Geographic systems (smoothing, coordinate transforms) ──────
            _geoSystems.ExecuteAll(view, dt);

            // Final flush for any cmd-buf writes from geo systems.
            cmdBuf.Playback(_world);
            
            // Swap event buffers so any events published this tick (e.g. AssignDoctrineEvent)
            // become readable on the next tick.
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

        // ── World factory ─────────────────────────────────────────────────────────

        private static EntityRepository BuildWorld()
        {
            var world = new EntityRepository();

            // ── IG metadata component ─────────────────────────────────────────────
            world.RegisterComponent<IG.Components.EntityInfo>();
            world.RegisterManagedComponent<Bagira.SimHost.Components.EntityMissionHolder>();

            // ── Network components ────────────────────────────────────────────────
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkOwnership>();
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterComponent<TkbIdentity>();
            world.RegisterComponent<GhostStateTracker>();
            world.RegisterComponent<PendingNetworkAck>();

            // ── Geographic components ─────────────────────────────────────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();

            // ── Behavior toolkit components ────────────────────────────────────────
            world.RegisterComponent<DoctrineState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainBlackboard>();

            // HSM brain tiers (for APC-style HSM doctrines)
            world.RegisterComponent<BrainHsm64>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<PassengerBuffer>();
            world.RegisterComponent<IsEmbarkedTag>();

            // Perception
            world.RegisterComponent<Faction>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();

            // Combat & Physics
            world.RegisterComponent<PhysicsCollider>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<BallisticProjectile>();
            world.RegisterComponent<HealthData>();

            // ── CarKinem / Navigation components ──────────────────────────────────
            world.RegisterComponent<CarKinem.Core.VehicleState>();
            world.RegisterComponent<CarKinem.Core.VehicleParams>();
            world.RegisterComponent<CarKinem.Core.NavState>();
            world.RegisterComponent<CarKinem.Formation.FormationRoster>();

            // ── Mission components ────────────────────────────────────────────────
            world.RegisterComponent<MissionPlanQueue>();
            world.RegisterComponent<Bagira.SimHost.Components.MissionAdapterState>();
            world.RegisterComponent<Bagira.SimHost.Components.MissionAdapterState>();

            // ── CQRS navigation contract (MOD1-P1T1 / CT-MOD1-C2) ─────────────────
            // These components live in BdcTkbBuilder.WithBehavior templates
            // and are written/read by MoveToExecutor and NavigationExecutionSystem.
            world.RegisterComponent<FDP.Toolkit.Navigation.NavigationIntent>();
            world.RegisterComponent<FDP.Toolkit.Navigation.NavigationStatus>();
            world.RegisterComponent<CarKinem.Core.FrustrationTicks>();

            // ── Lifecycle events ───────────────────────────────────────────────────
            world.RegisterEvent<ConstructionOrder>();
            world.RegisterEvent<ConstructionAck>();
            world.RegisterEvent<DestructionOrder>();
            world.RegisterEvent<DestructionAck>();

            return world;
        }

        // ── TKB database factory ──────────────────────────────────────────────────

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
        /// Mirrors production <c>BdcTkbBuilder.WithBehavior</c> without IG-only visual factories.
        /// </summary>
        private static void RegisterBehaviorTemplate(TkbDatabase db, long tkbType, string name,
            float maxSpeed, CarKinem.Core.VehicleClass vehicleClass)
        {
            var template = new TkbTemplate(name, tkbType);

            // Physics
            var preset = CarKinem.Core.VehiclePresets.GetPreset(vehicleClass);
            preset.Class = vehicleClass;
            preset.MaxSpeedFwd = maxSpeed;
            template.AddComponent(preset);
            template.AddComponent(new CarKinem.Core.VehicleState());
            template.AddComponent(new CarKinem.Core.NavState());
            template.AddComponent(new CarKinem.Formation.FormationRoster());
            template.AddComponent(new NetworkTransform());

            // Simulation transform
            template.AddComponent(new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            template.AddComponent(new SimVelocity  { Linear  = Vector3.Zero, Angular  = Vector3.Zero });

            // Behavior
            template.AddComponent(new DoctrineState { BrainTier = FDP.Toolkit.Behavior.BehaviorConstants.BrainTierBTree });
            template.AddComponent(new BrainBlackboard());
            template.AddComponent(new BrainBTreeState());
            template.AddComponent(new LocomotionChannel());
            template.AddComponent(new WeaponChannel());
            template.AddComponent(new InteractionChannel());
            template.AddComponent(new ActorCapabilityState
            {
                Capabilities = FDP.Toolkit.Behavior.Components.ActorCapabilities.CanMove
                             | FDP.Toolkit.Behavior.Components.ActorCapabilities.CanShoot
            });
            template.AddComponent(new MissionPlanQueue());

            // CQRS navigation contract (CT-MOD1-C2 fix):
            // These must be present so MoveToExecutor.OnEnter does not throw
            // "Entity missing NavigationIntent".
            template.AddComponent(new FDP.Toolkit.Navigation.NavigationIntent());
            template.AddComponent(new FDP.Toolkit.Navigation.NavigationStatus());
            template.AddComponent(new CarKinem.Core.FrustrationTicks());

            // Managed components
            template.AddManagedComponent<Bagira.SimHost.Components.EntityMissionHolder>(() => new Bagira.SimHost.Components.EntityMissionHolder());

            db.Register(template);
        }

        // ── Doctrine registry factory ─────────────────────────────────────────────

        private static DoctrineRegistry BuildDoctrineRegistry(Fdp.Modules.Geographic.IGeographicTransform wgs84)
        {
            var reg = new DoctrineRegistry();

            unsafe
            {
                reg.Register(SimHostDoctrineIds.MoveTo_BT, "MoveToLocation",
                    new DoctrineDefinition
                    {
                        Name       = "MoveToLocation",
                        BrainTier  = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => Bagira.SimHost.Brains.SimHostNodes.ParseMoveToParams(json, ptr, wgs84),
                        BTreeInterpreter = Bagira.SimHost.Brains.SimHostNodes.BuildMoveToLocationInterpreter()
                    });
                reg.Register(SimHostDoctrineIds.FollowRoute_BT, "FollowRoute",
                    new DoctrineDefinition
                    {
                        Name       = "FollowRoute",
                        BrainTier  = BehaviorConstants.BrainTierBTree,
                        ParseParams = null,
                    });
                reg.Register(SimHostDoctrineIds.JoinFormation_BT, "JoinFormation",
                    new DoctrineDefinition
                    {
                        Name       = "JoinFormation",
                        BrainTier  = BehaviorConstants.BrainTierBTree,
                        ParseParams = null,
                    });
                reg.Register(SimHostDoctrineIds.Idle_HSM, "Idle",
                    new DoctrineDefinition
                    {
                        Name       = "Idle",
                        BrainTier  = BehaviorConstants.BrainTierHsm,
                        ParseParams = null,
                    });
            }

            return reg;
        }
    }
}
