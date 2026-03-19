using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.IG.Components;
using Bagira.Map.Common;
using Bagira.SimHost.Brains;
using Bagira.SimHost.Configuration;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.SimHost.UI
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
            INetworkIdAllocator?     idAllocator = null)
        {
            _repo        = repo;
            _road        = road;
            _traj        = traj;
            _formations  = formations;
            _spawnBus    = spawnBus ?? repo.Bus;
            _idAllocator = idAllocator;
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
                "[TRACE-SH] SpawnVehicle: Requesting TkbType={0} at ({1})", tkbType, positionLabel);

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

                var doctrine = new DoctrineState
                {
                    ActiveDoctrineHash = SimHostDoctrineIds.WanderMilitary_BT,
                    BrainTier          = BehaviorConstants.BrainTierBTree,
                    InstanceId         = 1,
                };

                // BrainBlackboard: WanderMilitary requires no parameters — a blank blackboard
                // is correct.  To switch to a parameterised doctrine (e.g. MoveTo_BT), replace
                // the doctrine hash above and optionally write params into the blackboard:
                //
                //   unsafe {
                //       fixed (byte* ptr = blackboard.Memory) {
                //           var p = (SimHostNodes.MoveToLocationParams*)ptr;
                //           p->X = targetX; p->Y = targetY; p->Speed = 15f;
                //       }
                //   }
                var blackboard = new BrainBlackboard();

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
                    InitialComponents = new List<object> { doctrine, blackboard, entityInfo },
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

                var doctrine = new DoctrineState
                {
                    ActiveDoctrineHash = SimHostDoctrineIds.WanderMilitary_BT,
                    BrainTier          = BehaviorConstants.BrainTierBTree,
                    InstanceId         = 1,
                };

                var blackboard = new BrainBlackboard();

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
                    InitialComponents = new List<object> { doctrine, blackboard, entityInfo },
                });
            }
        }

        /// <summary>
        /// Spawns a formation of <paramref name="count"/> vehicles: one leader executing
        /// <c>WanderMilitary_BT</c> and <c>count-1</c> followers executing
        /// <c>JoinFormation_BT</c> with the leader's network ID pre-wired in their blackboard.
        /// Requires an <see cref="INetworkIdAllocator"/> (supplied at construction time) to
        /// pre-allocate the leader ID so followers can wire it at spawn time.
        /// </summary>
        public unsafe void SpawnFormation(VehicleClass cls, FormationType formType, int count, TrajectoryInterpolation interp = TrajectoryInterpolation.CatmullRom)
        {
            long tkbType = MapVehicleClassToTkbType(cls);
            var  center  = RandomPos(300);

            // Pre-allocate the leader's network ID so followers can reference it at spawn time.
            // Without an allocator the leader uses 0 (deferred) and followers cannot pre-wire it.
            long leaderNetId = _idAllocator?.AllocateId() ?? 0L;

            // 1. Leader — uses WanderMilitary so it roams autonomously while followers trail it.
            var leaderDoctrine = new DoctrineState
            {
                ActiveDoctrineHash = SimHostDoctrineIds.WanderMilitary_BT,
                BrainTier          = BehaviorConstants.BrainTierBTree,
                InstanceId         = 1,
            };

            var leaderBlackboard = new BrainBlackboard();

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
                InitialComponents = new List<object> { leaderDoctrine, leaderBlackboard, leaderInfo },
            });

            // 2. Followers
            for (int i = 0; i < count - 1; i++)
            {
                var followerDoctrine = new DoctrineState
                {
                    ActiveDoctrineHash = SimHostDoctrineIds.JoinFormation_BT,
                    BrainTier          = BehaviorConstants.BrainTierBTree,
                    InstanceId         = 1,
                };

                var followerBlackboard = new BrainBlackboard();
                var jp = (JoinFormationParams*)(&followerBlackboard);
                jp->LeaderNetworkId = (int)leaderNetId;
                jp->FormationTypeId = (byte)formType;

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
                    InitialComponents = new List<object> { followerDoctrine, followerBlackboard, followerInfo },
                });
            }
        }

        /// <summary>
        /// Spawns two opposing vehicles on a collision course using <c>FollowRoute_BT</c>.
        /// Both entities are network-visible and their trajectories are pre-registered.
        /// </summary>
        public unsafe void SpawnCollisionTest(VehicleClass cls)
        {
            long tkbType = MapVehicleClassToTkbType(cls);

            var pathA = new[] { new Vector2(100f, 100f), new Vector2(350f, 100f) };
            var pathB = new[] { new Vector2(300f, 100f), new Vector2(50f,  100f) };

            int trajIdA = _traj.RegisterTrajectory(pathA, interpolation: TrajectoryInterpolation.CatmullRom);
            int trajIdB = _traj.RegisterTrajectory(pathB, interpolation: TrajectoryInterpolation.CatmullRom);

            SpawnNetworkedTrajectoryVehicle(tkbType, new Vector2(100f, 100f),  Vector2.UnitX, trajIdA, "Test-A");
            SpawnNetworkedTrajectoryVehicle(tkbType, new Vector2(300f, 100f), -Vector2.UnitX, trajIdB, "Test-B");
        }

        private unsafe void SpawnNetworkedTrajectoryVehicle(
            long tkbType, Vector2 startPos, Vector2 heading, int trajId, string name)
        {
            float angle   = VectorMath.SignedAngle(Vector2.UnitX, heading);
            var transform = new SimTransform
            {
                Position = new Vector3(startPos.X, startPos.Y, 0f),
                Rotation = SimMath.FromYaw(angle),
            };

            var doctrine = new DoctrineState
            {
                ActiveDoctrineHash = SimHostDoctrineIds.FollowRoute_BT,
                BrainTier          = BehaviorConstants.BrainTierBTree,
                InstanceId         = 1,
            };

            var blackboard = new BrainBlackboard();
            var rp = (SimHostNodes.FollowRouteParams*)(&blackboard);
            rp->TrajectoryId = trajId;
            rp->Speed        = 15f;
            rp->Loop         = false;

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
                InitialComponents = new List<object> { doctrine, blackboard, entityInfo },
            });
        }

        /// <summary>
        /// Seeds a small initial scenario by spawning 5 vehicles via the network-aware
        /// <see cref="SpawnVehicle"/> path so they are published over DDS and visible on
        /// all connected IG / IOS clients.
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
            _repo.Bus.Publish(new CmdFollowTrajectory
            {
                Entity       = entity,
                TrajectoryId = tId,
            });
        }

        public void AddWaypoint(Entity entity, Vector2 point,
            TrajectoryInterpolation interp = TrajectoryInterpolation.CatmullRom)
        {
            if (!_repo.IsAlive(entity)) return;
            if (!_repo.HasComponent<NavState>(entity)) { SetDestination(entity, point, interp); return; }

            var nav = _repo.GetComponentRO<NavState>(entity);
            if (nav.Mode == KinematicsMode.CustomTrajectory && _traj.TryGetTrajectory(nav.TrajectoryId, out var existing))
            {
                // Extend the existing trajectory by one point
                var pts = new System.Collections.Generic.List<Vector2>();
                for (int i = 0; i < existing.Waypoints.Length; i++)
                    pts.Add(existing.Waypoints[i].Position);
                pts.Add(point);

                _traj.RemoveTrajectory(nav.TrajectoryId);
                var tId = _traj.RegisterTrajectory(pts.ToArray(), interpolation: interp);
                _repo.Bus.Publish(new CmdFollowTrajectory
                {
                    Entity       = entity,
                    TrajectoryId = tId,
                });
            }
            else
            {
                SetDestination(entity, point, interp);
            }
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
