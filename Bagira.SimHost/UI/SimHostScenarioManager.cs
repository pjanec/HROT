using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Kernel;

namespace Bagira.SimHost.UI
{
    /// <summary>
    /// Provides local (GUI-driven) entity spawning and scenario utilities for the
    /// SimHost 2-D window.  Entities are created directly in the ECS world — bypassing
    /// the DDS network round-trip that <c>CreateEntityRequestSystem</c> uses — so that
    /// the operator can spawn test traffic instantly without a connected IOS client.
    ///
    /// Mirrors <c>Fdp.Examples.CarKinem.Core.ScenarioManager</c> but adapted for the
    /// SimHost component set.
    /// </summary>
    public class SimHostScenarioManager
    {
        private readonly EntityRepository        _repo;
        private readonly RoadNetworkBlob          _road;
        private readonly TrajectoryPoolManager    _traj;
        private readonly FormationTemplateManager _formations;
        private readonly Random                   _rng = new();

        // Entities that wander to a random point when they arrive
        private readonly HashSet<Entity> _roamers = new();

        public SimHostScenarioManager(
            EntityRepository        repo,
            RoadNetworkBlob          road,
            TrajectoryPoolManager    traj,
            FormationTemplateManager formations)
        {
            _repo       = repo;
            _road       = road;
            _traj       = traj;
            _formations = formations;
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

        public Entity SpawnVehicle(Vector2 position, Vector2 heading, VehicleClass vehicleClass = VehicleClass.PersonalCar)
        {
            var e = _repo.CreateEntity();

            float angle = VectorMath.SignedAngle(Vector2.UnitX, heading);
            var rot     = System.Numerics.Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);

            _repo.AddComponent(e, new SimTransform { Position = new Vector3(position.X, position.Y, 0), Rotation = rot });
            _repo.AddComponent(e, new SimVelocity  { Linear = Vector3.Zero, Angular = Vector3.Zero });
            _repo.AddComponent(e, new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });

            var preset = VehiclePresets.GetPreset(vehicleClass);
            preset.Class = vehicleClass;
            _repo.AddComponent(e, preset);
            _repo.AddComponent(e, new NavState());

            return e;
        }

        public void SpawnRoamers(int count, VehicleClass cls, TrajectoryInterpolation interp = TrajectoryInterpolation.CatmullRom)
        {
            for (int i = 0; i < count; i++)
            {
                var e = SpawnVehicle(RandomPos(500), RandomDir());
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
                var e = SpawnVehicle(new Vector2(_road.Nodes[nodeIdx].Position.X, _road.Nodes[nodeIdx].Position.Y), RandomDir(), cls);

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
            var leader = SpawnVehicle(center, Vector2.UnitX, cls);
            _repo.AddComponent(leader, new FormationRoster { Type = formType });
            SetDestination(leader, RandomPos(500), interp);
            _roamers.Add(leader);

            // Spawn members
            for (int i = 0; i < count - 1; i++)
            {
                var member = SpawnVehicle(center + new Vector2(_rng.Next(-20, 20), _rng.Next(-20, 20)), Vector2.UnitX, cls);
                _repo.Bus.Publish(new CmdJoinFormation { Entity = member, LeaderEntity = leader });
            }
        }

        public void SpawnCollisionTest(VehicleClass cls)
        {
            var a = SpawnVehicle(new Vector2(100, 100), Vector2.UnitX, cls);
            var b = SpawnVehicle(new Vector2(300, 100), -Vector2.UnitX, cls);
            SetDestination(a, new Vector2(350, 100));
            SetDestination(b, new Vector2(50,  100));
        }

        public void SpawnFastOne()
            => SpawnRoamers(5, VehicleClass.PersonalCar);

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
            if (nav.Mode == NavigationMode.CustomTrajectory && _traj.TryGetTrajectory(nav.TrajectoryId, out var existing))
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
            foreach (var e in lst) _repo.DestroyEntity(e);
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
