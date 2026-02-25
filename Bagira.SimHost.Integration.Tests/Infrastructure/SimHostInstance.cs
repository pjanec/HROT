using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.Map.Common;
using Bagira.SimHost;
using Bagira.SimHost.Components;
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
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Transforms;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
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

        public List<CreateEntityRequest> TakeRequests()
        {
            var result = new List<CreateEntityRequest>(_pending);
            _pending.Clear();
            return result;
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
    /// Collects <see cref="IModuleSystem"/> instances registered via
    /// <see cref="IModule.RegisterSystems"/> for manual per-frame execution.
    /// </summary>
    internal sealed class SystemList : ISystemRegistry
    {
        private readonly List<IModuleSystem> _systems = new();
        public IReadOnlyList<IModuleSystem> Systems => _systems;

        public void RegisterSystem<T>(T system) where T : IModuleSystem
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

        // ── Systems: IModuleSystem-based (executed manually each tick) ────────────
        private readonly CreateEntityRequestSystem _requestSystem;
        private readonly NetworkSpawningSystem     _spawnSystem;
        private readonly SystemList                _elmSystems  = new();
        private readonly SystemList                _geoSystems  = new();

        // ── Systems: ComponentSystem-based (executed via SystemGroup) ─────────────
        private readonly SystemGroup _simGroup;

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
            _doctrineRegistry = BuildDoctrineRegistry();

            // 3. Entity lifecycle module (empty participant list → bypass ACK protocol) ─
            _elm = new EntityLifecycleModule(_tkbDb, new List<int>());
            _elm.RegisterSystems(_elmSystems);

            // 4. Request / spawn systems ────────────────────────────────────────────
            _requestSystem = new CreateEntityRequestSystem(
                RequestSource, AckSink, _tkbDb, IdAllocator, localNodeId: 1, _wgs84);

            _spawnSystem = new NetworkSpawningSystem(
                _tkbDb, _elm, _entityMap, IdAllocator, localNodeId: 1,
                disTypeExtractor: (object c, out ulong dis) =>
                {
                    if (c is EntityMaster m) { dis = m.DisType; return true; }
                    dis = 0; return false;
                });

            // 5. Geographic systems ─────────────────────────────────────────────────
            new GeographicModule(_wgs84).RegisterSystems(_geoSystems);

            // 6. Simulation-logic SystemGroup ──────────────────────────────────────
            var roadNetwork    = new RoadNetworkBuilder().Build(10f, 100, 100);
            var trajectoryPool = new TrajectoryPoolManager();

            _simGroup = new SystemGroup();
            _simGroup.Create(_world);

            var simLogicModule = new SimulationLogicModule(
                _doctrineRegistry,
                _entityMap,
                vehicleAPI:              null,
                roadNetwork:             roadNetwork,
                trajectoryPool:          trajectoryPool,
                formationTemplateManager: null);
            simLogicModule.RegisterSystems(_simGroup);

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
                        EntityMaster = new EntityMaster { TkbType = tkbType }
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
        /// Assigns an <see cref="EntityMission"/> to the specified entity by directly
        /// adding an <see cref="EntityMissionHolder"/> managed component (bypasses DDS
        /// ingress translator for deterministic testing).
        /// </summary>
        public void PublishEntityMission(EntityMission mission)
        {
            if (!_entityMap.TryGetEntity(mission.EntityId, out var entity))
                throw new InvalidOperationException(
                    $"Entity with network-id {mission.EntityId} not found in entity map.");

            _world.SetManagedComponent(entity, new EntityMissionHolder { Mission = mission });
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
        /// <paramref name="networkId"/> by querying its <see cref="GeoTransform"/> ECS
        /// component.  Returns <c>null</c> when no such entity is found.
        /// </summary>
        public GeoSpatial? ReadGeoSpatial(int networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return null;

            if (!_world.HasComponent<GeoTransform>(entity))
                return null;

            ref readonly var geo = ref _world.GetComponentRO<GeoTransform>(entity);

            return new GeoSpatial
            {
                EntityId = networkId,
                Time     = DateTime.UtcNow,
                Pos      = new DDS.DM.GeoPosition
                {
                    Latitude  = geo.Latitude,
                    Longitude = geo.Longitude,
                    Altitude  = geo.Altitude,
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
            _simGroup.Run();

            // ── Phase 4: Geographic egress ─────────────────────────────────────
            // SimTransformBridgeSystem → converts SimTransform → GeoTransform
            _geoSystems.ExecuteAll(view, dt);

            // Final flush for any cmd-buf writes from geo systems.
            cmdBuf.Playback(_world);
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

            // ── Network components ────────────────────────────────────────────────
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkOwnership>();
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterComponent<NetworkSpawnRequest>();
            world.RegisterComponent<PendingNetworkAck>();

            // ── DDS descriptor components (written by EntityComponentReflector) ─────
            world.RegisterComponent<EntityMaster>();

            // ── Geographic components ─────────────────────────────────────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<GeoTransform>();
            world.RegisterComponent<GeoVelocity>();

            // ── Behavior toolkit components ────────────────────────────────────────
            world.RegisterComponent<DoctrineState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainBlackboard>();

            // ── CarKinem / Navigation components ──────────────────────────────────
            world.RegisterComponent<CarKinem.Core.VehicleState>();
            world.RegisterComponent<CarKinem.Core.VehicleParams>();
            world.RegisterComponent<CarKinem.Core.NavState>();
            world.RegisterComponent<CarKinem.Formation.FormationRoster>();

            // ── Managed components ─────────────────────────────────────────────────
            world.RegisterManagedComponent<EntityMissionHolder>();

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
        /// The template adds all simulation components required by CarKiem / Behavior.
        /// </summary>
        private static TkbDatabase BuildTkbDatabase()
        {
            var db = new TkbDatabase();

            // ── Tank M1 Abrams ────────────────────────────────────────────────────
            var tankTemplate = new TkbTemplate("Tank_M1Abrams", TkbEntityTypes.Tank_M1Abrams);

            // Navigation / physics
            tankTemplate.AddComponent(new CarKinem.Core.VehicleParams
            {
                Class          = CarKinem.Core.VehicleClass.Tank,
                Length         = 7.93f,
                Width          = 3.66f,
                WheelBase      = 4.0f,
                MaxSpeedFwd    = 20.0f,   // m/s
                MaxAccel       = 2.5f,
                MaxDecel       = 4.0f,
                MaxSteerAngle  = 0.6f,
                MaxSteerRate   = 0.5f,
                MaxLatAccel    = 5.0f,
                AvoidanceRadius = 4.0f,
                LookaheadTimeMin = 1.0f,
                LookaheadTimeMax = 3.0f,
                AccelGain        = 1.5f,
            });
            tankTemplate.AddComponent(new CarKinem.Core.VehicleState());
            tankTemplate.AddComponent(new CarKinem.Core.NavState
            {
                Mode           = CarKinem.Core.NavigationMode.Direct,
                TargetSpeed    = 10.0f,
                ArrivalRadius  = 5.0f,
            });
            tankTemplate.AddComponent(new CarKinem.Formation.FormationRoster());

            // Simulation transform (spawned at origin by default)
            tankTemplate.AddComponent(new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            });
            tankTemplate.AddComponent(new SimVelocity
            {
                Linear  = Vector3.Zero,
                Angular = Vector3.Zero
            });

            // Geographic components (filled by SimTransformBridgeSystem each tick)
            tankTemplate.AddComponent(new GeoTransform());
            tankTemplate.AddComponent(new GeoVelocity());

            // Behavior toolkit
            tankTemplate.AddComponent(new DoctrineState());
            tankTemplate.AddComponent(new BrainBlackboard());
            tankTemplate.AddComponent(new LocomotionChannel());
            tankTemplate.AddComponent(new WeaponChannel());
            tankTemplate.AddComponent(new InteractionChannel());
            tankTemplate.AddComponent(new ActorCapabilityState());
            tankTemplate.AddComponent(new BrainBTreeState());

            db.Register(tankTemplate);
            return db;
        }

        // ── Doctrine registry factory ─────────────────────────────────────────────

        private static DoctrineRegistry BuildDoctrineRegistry()
        {
            var reg = new DoctrineRegistry();

            reg.Register(SimHostDoctrineIds.MoveTo_BT, "MoveToLocation",
                new DoctrineDefinition
                {
                    Name       = "MoveToLocation",
                    BrainTier  = BehaviorConstants.BrainTierBTree,
                    ParseParams = null,
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

            return reg;
        }
    }
}
