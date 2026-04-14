using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.SimHost.Configuration;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network.Interfaces;

namespace Hrot.SimHost.UI
{
    /// <summary>
    /// Provides GUI-driven entity spawning and scenario utilities for the SimHost 2-D window.
    ///
    /// <para>All spawners publish <see cref="SpawnEntityCommand"/> onto the event bus so that
    /// <c>NetworkSpawningSystem</c> creates entities with the full network component set and
    /// publishes them over DDS, making them visible on the IG map.</para>
    ///
    /// <para>Each spawned entity carries a <see cref="DoctrineState"/> and
    /// <see cref="BrainBlackboard"/> so the BTree cognitive tier drives its behaviour
    /// autonomously from the first frame.</para>
    /// </summary>
    public class SimHostScenarioManager
    {
        private readonly EntityRepository        _repo;
        private readonly RoadNetworkBlob          _road;
        private readonly TrajectoryPoolManager    _traj;
        private readonly FormationTemplateManager _formations;
        private readonly IEventBus               _spawnBus;
        private readonly INetworkIdAllocator?    _idAllocator;
        private readonly int                      _localNodeId;
        private readonly Random                   _rng = new();

        /// <param name="repo">Live entity repository.</param>
        /// <param name="road">Road network (may be empty).</param>
        /// <param name="traj">Trajectory pool manager.</param>
        /// <param name="formations">Formation template manager.</param>
        /// <param name="spawnBus">
        /// Event bus used to publish <see cref="SpawnEntityCommand"/> for network-visible
        /// entity creation.  Defaults to <paramref name="repo"/>.Bus when <c>null</c>.
        /// </param>
        /// <param name="idAllocator">
        /// Optional allocator for pre-allocating network IDs (required by
        /// <see cref="SpawnFormation"/> to wire followers to their leader at spawn time).
        /// When <c>null</c>, formation leaders use <c>NetworkId = 0</c> and followers
        /// cannot pre-resolve the leader ID.
        /// </param>
        public SimHostScenarioManager(
            EntityRepository        repo,
            RoadNetworkBlob          road,
            TrajectoryPoolManager    traj,
            FormationTemplateManager formations,
            IEventBus?               spawnBus    = null,
            INetworkIdAllocator?     idAllocator = null,
            int                      localNodeId = 0)
        {
            _repo        = repo;
            _road        = road;
            _traj        = traj;
            _formations  = formations;
            _spawnBus    = spawnBus ?? repo.Bus;
            _idAllocator = idAllocator;
            _localNodeId = localNodeId;
        }

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// No-op: autonomous wandering/routing is driven by each entity's BTree doctrine
        /// (e.g. <c>WanderMilitary_BT</c>); the UI layer no longer polls entity state.
        /// </summary>
        public void Update() { }

        // ── Spawn helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Publishes a <see cref="SpawnEntityCommand"/> so that
        /// <c>NetworkSpawningSystem</c> creates a fully-networked entity with
        /// <c>NetworkIdentity</c>, <c>NetworkOwnership</c>, and <c>NetworkSpawnRequest</c>.
        /// </summary>
        /// <param name="position">Initial world-space XY position in metres.</param>
        /// <param name="heading">Heading unit vector; yaw is derived from its angle from east.</param>
        /// <param name="vehicleClass">Vehicle archetype — determines TKB template type.</param>
        public void SpawnVehicle(Vector2 position, Vector2 heading,
            VehicleClass vehicleClass = VehicleClass.PersonalCar)
        {
            var tkbType = MapVehicleClassToTkbType(vehicleClass);
            var positionLabel = string.Concat(position.X, ",", position.Y);
            FdpLog<SimHostScenarioManager>.Debug(
                "[Node-{0}] SpawnVehicle: Requesting TkbType={1} at ({2})", _localNodeId, tkbType, positionLabel);

            float angle     = VectorMath.SignedAngle(Vector2.UnitX, heading);
            var   transform = new SimTransform
            {
                Position = new Vector3(position.X, position.Y, 0f),
                Rotation = SimMath.FromYaw(angle),
            };

            var entityInfo = new EntityInfo
            {
                Name        = vehicleClass.ToString(),
                ForceId     = ForceId.Unknown,
                CommanderId = 0,
            };

            var cmd = new SpawnEntityCommand
            {
                NetworkId         = 0, // 0 = auto-allocate by DdsIdAllocator
                TkbType           = tkbType,
                DisType           = 0,
                OwnerNodeId       = SimHostNetworkConstants.LocalNodeId,
                InitType          = ReliableInitType.AllPeers,
                InitialComponents = new List<object> { transform, entityInfo },
            };

            _spawnBus.PublishManaged(cmd);
        }

        /// <summary>Maps <see cref="VehicleClass"/> to the canonical TKB entity type constant.</summary>
        private static long MapVehicleClassToTkbType(VehicleClass vehicleClass) =>
            vehicleClass switch
            {
                VehicleClass.Tank       => TkbEntityTypes.Tank_M1Abrams,
                VehicleClass.Pedestrian => TkbEntityTypes.Infantry_Rifleman,
                _                       => TkbEntityTypes.Truck_HMMWV,
            };

        /// <summary>
        /// Spawns <paramref name="count"/> autonomous roaming vehicles via the network pipeline.
        /// Each entity wakes up executing <c>WanderMilitary_BT</c> and continuously picks
        /// random destinations without UI-layer polling.
        /// </summary>
        public void SpawnRoamers(int count, VehicleClass cls, TrajectoryInterpolation interp = TrajectoryInterpolation.CatmullRom)
        {
            long tkbType = MapVehicleClassToTkbType(cls);

            for (int i = 0; i < count; i++)
            {
                var pos     = RandomPos(500);
                var heading = RandomDir();
                float angle = VectorMath.SignedAngle(Vector2.UnitX, heading);

                var transform = new SimTransform
                {
                    Position = new Vector3(pos.X, pos.Y, 0f),
                    Rotation = SimMath.FromYaw(angle),
                };

                var entityInfo = new EntityInfo
                {
                    Name        = $"Roamer-{i + 1}",
                    ForceId     = ForceId.Unknown,
                    CommanderId = 0,
                };

                _spawnBus.PublishManaged(new SpawnEntityCommand
                {
                    NetworkId         = 0,
                    TkbType           = tkbType,
                    DisType           = 0,
                    OwnerNodeId       = SimHostNetworkConstants.LocalNodeId,
                    InitType          = ReliableInitType.AllPeers,
                    InitialTransform  = transform,
                    InitialComponents = new List<object> { entityInfo },
                });
            }
        }

        /// <summary>
        /// Spawns <paramref name="count"/> road-user vehicles. When a road network is available
        /// entities are placed at random road nodes; otherwise at random world positions.
        /// All use the <c>WanderMilitary_BT</c> doctrine for autonomous roaming and are
        /// visible on the IG map.
        /// </summary>
        public void SpawnRoadUsers(int count, VehicleClass cls)
        {
            long tkbType = MapVehicleClassToTkbType(cls);

            for (int i = 0; i < count; i++)
            {
                Vector2 pos;
                if (_road.Nodes.IsCreated && _road.Nodes.Length > 0)
                {
                    int nodeIdx = _rng.Next(_road.Nodes.Length);
                    pos = new Vector2(_road.Nodes[nodeIdx].Position.X, _road.Nodes[nodeIdx].Position.Y);
                }
                else
                {
                    pos = RandomPos(500);
                }

                var heading = RandomDir();
                float angle = VectorMath.SignedAngle(Vector2.UnitX, heading);

                var transform = new SimTransform
                {
                    Position = new Vector3(pos.X, pos.Y, 0f),
                    Rotation = SimMath.FromYaw(angle),
                };

                var entityInfo = new EntityInfo
                {
                    Name        = $"RoadUser-{i + 1}",
                    ForceId     = ForceId.Unknown,
                    CommanderId = 0,
                };

                _spawnBus.PublishManaged(new SpawnEntityCommand
                {
                    NetworkId         = 0,
                    TkbType           = tkbType,
                    DisType           = 0,
                    OwnerNodeId       = SimHostNetworkConstants.LocalNodeId,
                    InitType          = ReliableInitType.AllPeers,
                    InitialTransform  = transform,
                    InitialComponents = new List<object> { entityInfo },
                });
            }
        }

        /// <summary>
        /// Spawns a formation of <paramref name="count"/> vehicles: one leader and
        /// <c>count-1</c> followers. The CGF node assigns doctrines (WanderMilitary for
        /// leader, JoinFormation for followers) via MissionControlRequest after spawn.
        /// Requires an <see cref="INetworkIdAllocator"/> (supplied at construction time) to
        /// pre-allocate the leader ID so followers can reference it at spawn time.
        /// </summary>
        public void SpawnFormation(VehicleClass cls, FormationType formType, int count, TrajectoryInterpolation interp = TrajectoryInterpolation.CatmullRom)
        {
            long tkbType = MapVehicleClassToTkbType(cls);
            var  center  = RandomPos(300);

            // Pre-allocate the leader's network ID so followers can reference it at spawn time.
            // Without an allocator the leader uses 0 (deferred) and followers cannot pre-wire it.
            long leaderNetId = _idAllocator?.AllocateId() ?? 0L;

            // 1. Leader
            var leaderInfo = new EntityInfo
            {
                Name        = "Leader",
                ForceId     = ForceId.Friend,
                CommanderId = 0,
            };

            _spawnBus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId         = leaderNetId,
                TkbType           = tkbType,
                DisType           = 0,
                OwnerNodeId       = SimHostNetworkConstants.LocalNodeId,
                InitType          = ReliableInitType.AllPeers,
                InitialTransform  = new SimTransform { Position = new Vector3(center.X, center.Y, 0f), Rotation = SimMath.FromYaw(0f) },
                InitialComponents = new List<object> { leaderInfo },
            });

            // 2. Followers
            for (int i = 0; i < count - 1; i++)
            {
                var followerPos  = center + new Vector2(_rng.Next(-20, 20), _rng.Next(-20, 20));
                var followerInfo = new EntityInfo
                {
                    Name        = $"Follower-{i + 1}",
                    ForceId     = ForceId.Friend,
                    CommanderId = 0,
                };

                _spawnBus.PublishManaged(new SpawnEntityCommand
                {
                    NetworkId         = 0,
                    TkbType           = tkbType,
                    DisType           = 0,
                    OwnerNodeId       = SimHostNetworkConstants.LocalNodeId,
                    InitType          = ReliableInitType.AllPeers,
                    InitialTransform  = new SimTransform { Position = new Vector3(followerPos.X, followerPos.Y, 0f), Rotation = SimMath.FromYaw(0f) },
                    InitialComponents = new List<object> { followerInfo },
                });
            }
        }

        /// <summary>
        /// Spawns two opposing vehicles on a collision course using <c>FollowRoute_BT</c>.
        /// Both entities are network-visible and their trajectories are pre-registered.
        /// </summary>
        public void SpawnCollisionTest(VehicleClass cls)
        {
            long tkbType = MapVehicleClassToTkbType(cls);

            var pathA = new[] { new Vector2(100f, 100f), new Vector2(350f, 100f) };
            var pathB = new[] { new Vector2(300f, 100f), new Vector2(50f,  100f) };

            int trajIdA = _traj.RegisterTrajectory(pathA, interpolation: TrajectoryInterpolation.CatmullRom);
            int trajIdB = _traj.RegisterTrajectory(pathB, interpolation: TrajectoryInterpolation.CatmullRom);

            SpawnNetworkedTrajectoryVehicle(tkbType, new Vector2(100f, 100f),  Vector2.UnitX, trajIdA, "Test-A");
            SpawnNetworkedTrajectoryVehicle(tkbType, new Vector2(300f, 100f), -Vector2.UnitX, trajIdB, "Test-B");
        }

        private void SpawnNetworkedTrajectoryVehicle(
            long tkbType, Vector2 startPos, Vector2 heading, int trajId, string name)
        {
            float angle   = VectorMath.SignedAngle(Vector2.UnitX, heading);
            var transform = new SimTransform
            {
                Position = new Vector3(startPos.X, startPos.Y, 0f),
                Rotation = SimMath.FromYaw(angle),
            };

            // Use NavigationIntent directly (FollowRoute mode) instead of doctrine + blackboard.
            // This is the architecturally-correct Muscle-tier mechanism: receive navigation
            // commands as pure data, not as Brain-tier AI directives.
            var intent = new NavigationIntent
            {
                Mode         = NavigationMode.FollowRoute,
                TrajectoryId = trajId,
                TargetSpeed  = 15f,
                IntentId     = 1,
            };

            var entityInfo = new EntityInfo
            {
                Name        = name,
                ForceId     = ForceId.Unknown,
                CommanderId = 0,
            };

            _spawnBus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId         = 0,
                TkbType           = tkbType,
                DisType           = 0,
                OwnerNodeId       = SimHostNetworkConstants.LocalNodeId,
                InitType          = ReliableInitType.AllPeers,
                InitialTransform  = transform,
                InitialComponents = new List<object> { intent, entityInfo },
            });
        }

        /// <summary>
        /// Seeds a small initial scenario by spawning 5 vehicles via the network-aware
        /// <see cref="SpawnVehicle"/> path so they are published over DDS and visible on
        /// all connected IG / ExCon clients.
        ///
        /// <para>Previous implementation used <see cref="SpawnEntityLocal"/> which created
        /// ECS-only entities without <c>NetworkIdentity</c> / <c>NetworkAuthority</c> — those
        /// entities were never published and were therefore invisible on the IG map.</para>
        /// </summary>
        public void SpawnFastOne()
        {
            for (int i = 0; i < 5; i++)
                SpawnVehicle(RandomPos(500), RandomDir());
        }

        // ── Navigation helpers ────────────────────────────────────────────────

        public void SetDestination(Entity entity, Vector2 dest,
            TrajectoryInterpolation interp = TrajectoryInterpolation.CatmullRom)
        {
            if (!_repo.IsAlive(entity)) return;

            var pos2   = new Vector2(
                _repo.GetComponentRO<SimTransform>(entity).Position.X,
                _repo.GetComponentRO<SimTransform>(entity).Position.Y);

            var tId = _traj.RegisterTrajectory(new[] { pos2, dest }, interpolation: interp);
            NavigationIntent intent = _repo.HasComponent<NavigationIntent>(entity)
                ? _repo.GetComponent<NavigationIntent>(entity)
                : new NavigationIntent();
            intent.IntentId++;
            intent.Mode = NavigationMode.FollowRoute;
            intent.TrajectoryId = tId;
            if (_repo.HasComponent<NavigationIntent>(entity))
                _repo.SetComponent(entity, intent);
            else
                _repo.AddComponent(entity, intent);
        }

        public void ClearAll()
        {
            var q = _repo.Query().With<VehicleState>().Build();
            var lst = new List<Entity>();
            foreach (var e in q) lst.Add(e);
            foreach (var e in lst)
            {
                if (!_repo.IsAlive(e)) continue;

                if (_repo.HasComponent<NetworkIdentity>(e))
                {
                    // Network-replicated entity — route through NetworkSpawningSystem
                    // so the IG ghost is also removed via DDS EntityMaster DISPOSE.
                    ref readonly var netId = ref _repo.GetComponentRO<NetworkIdentity>(e);
                    _repo.Bus.PublishManaged(new DestroyEntityCommand
                    {
                        NetworkId = netId.Value,
                        Reason    = "clear-all",
                    });
                }
                else
                {
                    _repo.DestroyEntity(e);
                }
            }
            _traj.Clear();
        }

        // ── Private ───────────────────────────────────────────────────────────

        private Vector2 RandomPos(float range)
            => new(_rng.NextSingle() * range, _rng.NextSingle() * range);

        private Vector2 RandomDir()
        {
            float a = _rng.NextSingle() * MathF.PI * 2f;
            return new Vector2(MathF.Cos(a), MathF.Sin(a));
        }
    }
}
