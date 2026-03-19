using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.Map.Common;
using Bagira.SimHost.Configuration;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.SimHost.UI
{
    /// <summary>
    /// Provides local (GUI-driven) entity spawning and scenario utilities for the
    /// SimHost 2-D window.
    ///
    /// <para>The public <see cref="SpawnVehicle"/> method publishes a
    /// <see cref="SpawnEntityCommand"/> onto the event bus so that
    /// <c>NetworkSpawningSystem</c> constructs the entity with the full network
    /// component set (<c>NetworkIdentity</c>, <c>NetworkOwnership</c>,
    /// <c>NetworkSpawnRequest</c>) and publishes it over DDS.</para>
    ///
    /// <para>Internal demo helpers (<see cref="SpawnRoamers"/>,
    /// <see cref="SpawnFormation"/>, etc.) use <see cref="SpawnEntityLocal"/>
    /// for immediate entity access required by navigation setup.</para>
    /// </summary>
    public class SimHostScenarioManager
    {
        private readonly EntityRepository        _repo;
        private readonly RoadNetworkBlob          _road;
        private readonly TrajectoryPoolManager    _traj;
        private readonly FormationTemplateManager _formations;
        private readonly IEventBus               _spawnBus;
        private readonly Random                   _rng = new();

        // Entities that wander to a random point when they arrive
        private readonly HashSet<Entity> _roamers = new();

        /// <param name="repo">Live entity repository.</param>
        /// <param name="road">Road network (may be empty).</param>
        /// <param name="traj">Trajectory pool manager.</param>
        /// <param name="formations">Formation template manager.</param>
        /// <param name="spawnBus">
        /// Event bus used to publish <see cref="SpawnEntityCommand"/> for network-visible
        /// entity creation.  Defaults to <paramref name="repo"/>.Bus when <c>null</c>.
        /// </param>
        public SimHostScenarioManager(
            EntityRepository        repo,
            RoadNetworkBlob          road,
            TrajectoryPoolManager    traj,
            FormationTemplateManager formations,
            IEventBus?               spawnBus = null)
        {
            _repo       = repo;
            _road       = road;
            _traj       = traj;
            _formations = formations;
            _spawnBus   = spawnBus ?? repo.Bus;
        }

        // ── Tick ─────────────────────────────────────────────────────────────

        public void Update()
        {
            foreach (var entity in new List<Entity>(_roamers))
            {
                if (!_repo.IsAlive(entity)) { _roamers.Remove(entity); continue; }
                if (!_repo.HasComponent<NavState>(entity)) continue;

                var nav = _repo.GetComponentRO<NavState>(entity);
                if (nav.HasArrived == 1)
                    SetDestination(entity, RandomPos(500));
            }
        }

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

            var cmd = new SpawnEntityCommand
            {
                NetworkId         = 0, // 0 = auto-allocate by DdsIdAllocator
                TkbType           = tkbType,
                DisType           = 0,
                OwnerNodeId       = SimHostNetworkConstants.LocalNodeId,
                InitType          = ReliableInitType.AllPeers,
                InitialComponents = new List<object> { transform },
            };

            _spawnBus.PublishManaged(cmd);
        }

        /// <summary>
        /// Creates an entity directly in the ECS world for immediate use by
        /// demo/scenario helpers that require an entity reference (roaming,
        /// formations, collision tests).  Does NOT publish a DDS network event.
        /// </summary>
        private Entity SpawnEntityLocal(Vector2 position, Vector2 heading,
            VehicleClass vehicleClass = VehicleClass.PersonalCar)
        {
            var e = _repo.CreateEntity();

            float angle = VectorMath.SignedAngle(Vector2.UnitX, heading);
            var rot     = SimMath.FromYaw(angle);

            _repo.AddComponent(e, new SimTransform { Position = new Vector3(position.X, position.Y, 0), Rotation = rot });
            // Mark SimTransform as locally authoritative so that CarKinematicsSystem
            // (.WithOwned<SimTransform>()) includes this entity in its update query.
            // Without this flag, kinematics are silently skipped and the entity never moves.
            _repo.SetAuthority<SimTransform>(e, true);
            _repo.AddComponent(e, new SimVelocity  { Linear = Vector3.Zero, Angular = Vector3.Zero });
            _repo.AddComponent(e, new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });

            var preset = VehiclePresets.GetPreset(vehicleClass);
            preset.Class = vehicleClass;
            _repo.AddComponent(e, preset);
            _repo.AddComponent(e, new NavState());
            _repo.AddComponent(e, new PhysicsCollider
            {
                Radius         = Math.Max(preset.Length, preset.Width) / 2f,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer
            });

            // CQRS navigation contract components required by NavigationExecutionSystem
            // and NavigationIntentBridgeSystem.  Mode defaults to None so the bridge
            // leaves NavState untouched until a brain (or direct CmdFollowTrajectory) acts.
            _repo.AddComponent(e, new NavigationIntent());
            _repo.AddComponent(e, new NavigationStatus());
            _repo.AddComponent(e, new FrustrationTicks());

            return e;
        }

        /// <summary>Maps <see cref="VehicleClass"/> to the canonical TKB entity type constant.</summary>
        private static long MapVehicleClassToTkbType(VehicleClass vehicleClass) =>
            vehicleClass switch
            {
                VehicleClass.Tank       => TkbEntityTypes.Tank_M1Abrams,
                VehicleClass.Pedestrian => TkbEntityTypes.Infantry_Rifleman,
                _                       => TkbEntityTypes.Truck_HMMWV,
            };

        public void SpawnRoamers(int count, VehicleClass cls, TrajectoryInterpolation interp = TrajectoryInterpolation.CatmullRom)
        {
            for (int i = 0; i < count; i++)
            {
                var e = SpawnEntityLocal(RandomPos(500), RandomDir());
                SetDestination(e, RandomPos(500), interp);
                _roamers.Add(e);
            }
        }

        public void SpawnRoadUsers(int count, VehicleClass cls)
        {
            if (!_road.Nodes.IsCreated || _road.Nodes.Length == 0)
            {
                SpawnRoamers(count, cls);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int nodeIdx = _rng.Next(_road.Nodes.Length);
                var e = SpawnEntityLocal(new Vector2(_road.Nodes[nodeIdx].Position.X, _road.Nodes[nodeIdx].Position.Y), RandomDir(), cls);

                // Pick a random destination node
                int destNodeIdx = _rng.Next(_road.Nodes.Length);
                _repo.Bus.Publish(new CmdNavigateViaRoad
                {
                    Entity        = e,
                    Destination   = _road.Nodes[destNodeIdx].Position,
                    ArrivalRadius = 5f,
                });
            }
        }

        public void SpawnFormation(VehicleClass cls, FormationType formType, int count, TrajectoryInterpolation interp = TrajectoryInterpolation.CatmullRom)
        {
            var center = RandomPos(300);

            // Spawn leader
            var leader = SpawnEntityLocal(center, Vector2.UnitX, cls);
            _repo.AddComponent(leader, new FormationRoster { Type = formType });
            SetDestination(leader, RandomPos(500), interp);
            _roamers.Add(leader);

            // Spawn members
            for (int i = 0; i < count - 1; i++)
            {
                var member = SpawnEntityLocal(center + new Vector2(_rng.Next(-20, 20), _rng.Next(-20, 20)), Vector2.UnitX, cls);
                _repo.Bus.Publish(new CmdJoinFormation { Entity = member, LeaderEntity = leader });
            }
        }

        public void SpawnCollisionTest(VehicleClass cls)
        {
            var a = SpawnEntityLocal(new Vector2(100, 100), Vector2.UnitX, cls);
            var b = SpawnEntityLocal(new Vector2(300, 100), -Vector2.UnitX, cls);
            SetDestination(a, new Vector2(350, 100));
            SetDestination(b, new Vector2(50,  100));
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
            _roamers.Clear();
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
