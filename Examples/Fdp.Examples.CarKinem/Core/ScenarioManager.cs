using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Examples.CarKinem.Components;
using Fdp.Core;

namespace Fdp.Examples.CarKinem.Core
{
    public class ScenarioManager
    {
        private readonly EntityRepository _repository;
        private readonly RoadNetworkBlob _roadNetwork;
        private readonly TrajectoryPoolManager _trajectoryPool;
        private readonly FormationTemplateManager _formationTemplates;
        private readonly Random _rng = new Random();

        // Roaming Logic
        private HashSet<Entity> _roamingEntities = new HashSet<Entity>();
        
        // Waypoint Logic
        private Dictionary<Entity, List<Vector2>> _waypointQueues = new Dictionary<Entity, List<Vector2>>();

        public ScenarioManager(
            EntityRepository repository, 
            RoadNetworkBlob roadNetwork, 
            TrajectoryPoolManager trajectoryPool,
            FormationTemplateManager formationTemplates)
        {
            _repository = repository;
            _roadNetwork = roadNetwork;
            _trajectoryPool = trajectoryPool;
            _formationTemplates = formationTemplates;
        }

        public void ClearAll()
        {
             // Query all vehicles
             var query = _repository.Query().With<VehicleState>().Build();
             var toDestroy = new System.Collections.Generic.List<Fdp.Core.Entity>();
             
             foreach(var e in query)
             {
                 toDestroy.Add(e);
             }
             
             foreach(var e in toDestroy)
             {
                 _repository.DestroyEntity(e);
             }
             
             // Clear local state
             _roamingEntities.Clear();
             _waypointQueues.Clear();
             
             // Clear Trajectories
             _trajectoryPool.Clear();
        }

        public void Update()
        {
            UpdateWaypointQueues();
            UpdateRoamers();
        }

        private void UpdateRoamers()
        {
            foreach (var entity in new List<Entity>(_roamingEntities))
            {
                if (!_repository.IsAlive(entity)) { _roamingEntities.Remove(entity); continue; }

                if (!_repository.HasComponent<NavState>(entity)) continue;

                var nav = _repository.GetComponentRO<NavState>(entity);
                if (nav.HasArrived == 1)
                {
                    // Pick new random destination
                    SetDestination(entity, new Vector2(_rng.Next(0, 500), _rng.Next(0, 500)));
                }
            }
        }

        private void UpdateWaypointQueues()
        {
            foreach (var entity in new List<Entity>(_waypointQueues.Keys))
            {
                var queue = _waypointQueues[entity];
                if (queue.Count == 0) continue;

                if (!_repository.IsAlive(entity))
                {
                    _waypointQueues.Remove(entity);
                    continue;
                }

                var tf = _repository.GetComponentRO<SimTransform>(entity);
                var pos2D = new Vector2(tf.Position.X, tf.Position.Y);

                // Check distance to next target
                if (Vector2.Distance(pos2D, queue[0]) < 8.0f)
                {
                    queue.RemoveAt(0);
                    // Trajectory continues to next point automatically if it was built with multiple points?
                    // Actually, AddWaypoint builds a single trajectory with ALL points.
                    // So we don't need to re-issue command.
                    // We just track progress here to remove from queue.
                    // Wait, if we just remove from queue, do we need to do anything?
                    // The vehicle follows the *generated* trajectory.
                    // This queue seems to be local tracking only.
                }
            }
        }

        public Entity SpawnVehicle(Vector2 position, Vector2 heading, VehicleClass vehicleClass = VehicleClass.PersonalCar)
        {
            var e = _repository.CreateEntity();
            
            // Calculate initial rotation
            // Use UnitX as reference (Model Front)
            float angle = VectorMath.SignedAngle(Vector2.UnitX, heading);
            var initialRot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);

            _repository.AddComponent(e, new SimTransform { 
                Position = new Vector3(position.X, position.Y, 0), 
                Rotation = initialRot 
            });

            _repository.AddComponent(e, new SimVelocity { 
                Linear = Vector3.Zero, 
                Angular = Vector3.Zero 
            });
            
            _repository.AddComponent(e, new VehicleState { 
                Speed = 0,
                SteerAngle = 0,
                Accel = 0
            });
            
            var preset = global::CarKinem.Core.VehiclePresets.GetPreset(vehicleClass);
            preset.Class = vehicleClass; // Ensure class is set
            _repository.AddComponent(e, preset);
            
            // Use component-based color defaults if needed, or rely on visualizer
            // DemoSimulation defaulted to GreenYellow via component
            _repository.AddComponent(e, VehicleColor.GreenYellow);
            
            _repository.AddComponent(e, new NavState {
                Mode = KinematicsMode.None
            });
            
            return e;
        }

        public void AddWaypoint(Entity entity, Vector2 destination, TrajectoryInterpolation interpolation = TrajectoryInterpolation.Linear)
        {
             // 1. Get/Create Queue
             if (!_waypointQueues.ContainsKey(entity))
             {
                 _waypointQueues[entity] = new List<Vector2>();
             }
             
             // 2. Add to Queue
             _waypointQueues[entity].Add(destination);
             
             // 3. Construct Trajectory from Current Position
             if (!_repository.IsAlive(entity)) return;

             var tf = _repository.GetComponentRO<SimTransform>(entity);
             var pos2D = new Vector2(tf.Position.X, tf.Position.Y);
             
             var path = new List<Vector2>();
             path.Add(pos2D);
             path.AddRange(_waypointQueues[entity]);
             
             // 4. Create Speeds (Cruise=15, Stop=0 at end)
             var speeds = new float[path.Count];
             for(int i=0; i<speeds.Length; i++) speeds[i] = 15.0f;
             speeds[speeds.Length - 1] = 0.0f; // Stop at end
             
             // 5. Register new Trajectory
             int trajId = _trajectoryPool.RegisterTrajectory(path.ToArray(), speeds, false, interpolation);
             
             // Cleanup old trajectory
             var oldNav = _repository.GetComponentRO<NavState>(entity);
             if (oldNav.Mode == KinematicsMode.CustomTrajectory && oldNav.TrajectoryId > 0)
             {
                 _trajectoryPool.RemoveTrajectory(oldNav.TrajectoryId);
             }
             
             // 6. Write NavState directly (legacy Cmd bus removed)
             var nav = _repository.GetComponent<NavState>(entity);
             nav.Mode         = KinematicsMode.CustomTrajectory;
             nav.TrajectoryId = trajId;
             nav.ProgressS    = 0f;
             nav.HasArrived   = 0;
             _repository.SetComponent(entity, nav);
        }
        
        public void SetDestination(Entity entity, Vector2 destination, TrajectoryInterpolation interpolation = TrajectoryInterpolation.Linear)
        {
             if (_waypointQueues.ContainsKey(entity))
             {
                 _waypointQueues[entity].Clear();
             }
             AddWaypoint(entity, destination, interpolation);
        }

        // --- Scenarios ---

        public void SpawnCollisionTest(VehicleClass vClass)
        {
            for(int i=0; i<5; i++)
            {
                 Vector2 center = new Vector2(250 + i * 20, 250 + i * 20); 
                 Vector2 offset = new Vector2(40, 0);
                 
                 var entityA = SpawnVehicle(center - offset, new Vector2(1, 0), vClass);
                 SetDestination(entityA, center + offset);
                 
                 var entityB = SpawnVehicle(center + offset, new Vector2(-1, 0), vClass);
                 SetDestination(entityB, center - offset);
            }
        }

        public void SpawnFastOne()
        {
            if (!_roadNetwork.Nodes.IsCreated || _roadNetwork.Nodes.Length < 2) return;
            
            // Pick two random nodes
            int startIdx = _rng.Next(0, _roadNetwork.Nodes.Length);
            int endIdx = _rng.Next(0, _roadNetwork.Nodes.Length);
            while (startIdx == endIdx) endIdx = _rng.Next(0, _roadNetwork.Nodes.Length);
            
            var startNode = _roadNetwork.Nodes[startIdx];
            var endNode = _roadNetwork.Nodes[endIdx];
            
            var entity = SpawnVehicle(startNode.Position, new Vector2(1,0), VehicleClass.PersonalCar);
            
            // Boost speed
            var vParams = _repository.GetComponentRO<VehicleParams>(entity); // Struct copy
            vParams.MaxSpeedFwd = 50.0f; 
            vParams.MaxAccel = 10.0f;     
            vParams.MaxLatAccel = 15.0f;  
            _repository.SetComponent(entity, vParams);
            
            // Write NavState directly (legacy Cmd bus removed)
            var nav = _repository.GetComponent<NavState>(entity);
            nav.Mode             = KinematicsMode.RoadGraph;
            nav.RoadPhase        = RoadGraphPhase.Approaching;
            nav.FinalDestination = endNode.Position;
            nav.ArrivalRadius    = 5.0f;
            nav.CurrentSegmentId = -1;
            nav.ProgressS        = 0f;
            nav.HasArrived       = 0;
            _repository.SetComponent(entity, nav);
        }

        public void SpawnRoadUsers(int count, VehicleClass vClass)
        {
            if (!_roadNetwork.Nodes.IsCreated || _roadNetwork.Nodes.Length < 2) return;
            
            for(int i=0; i<count; i++)
            {
                int startNodeIdx = _rng.Next(0, _roadNetwork.Nodes.Length);
                var startNode = _roadNetwork.Nodes[startNodeIdx];
                int endNodeIdx = _rng.Next(0, _roadNetwork.Nodes.Length);
                var endNode = _roadNetwork.Nodes[endNodeIdx];
                
                var entity = SpawnVehicle(startNode.Position, new Vector2(1,0), vClass);
                _repository.SetComponent(entity, VehicleColor.Blue);
                
                // Write NavState directly (legacy Cmd bus removed)
                var navRoad = _repository.GetComponent<NavState>(entity);
                navRoad.Mode             = KinematicsMode.RoadGraph;
                navRoad.RoadPhase        = RoadGraphPhase.Approaching;
                navRoad.FinalDestination = endNode.Position;
                navRoad.ArrivalRadius    = 5.0f;
                navRoad.CurrentSegmentId = -1;
                navRoad.ProgressS        = 0f;
                navRoad.HasArrived       = 0;
                _repository.SetComponent(entity, navRoad);
            }
        }

        public void SpawnRoamers(int count, VehicleClass vClass, TrajectoryInterpolation interpolation = TrajectoryInterpolation.Linear)
        {
            for(int i=0; i<count; i++)
            {
                 Vector2 pos = new Vector2(_rng.Next(0,500), _rng.Next(0,500));
                 Vector2 heading = new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f);
                 if (heading == Vector2.Zero) heading = new Vector2(1, 0);
                 else heading = Vector2.Normalize(heading);

                 var entity = SpawnVehicle(pos, heading, vClass);
                 _repository.SetComponent(entity, VehicleColor.Orange);
                 
                 _roamingEntities.Add(entity);
                 SetDestination(entity, new Vector2(_rng.Next(0,500), _rng.Next(0,500)), interpolation);
            }
        }

        public void SpawnFormation(VehicleClass vClass, FormationType type, int count, TrajectoryInterpolation interpolation)
        {
             Vector2 startPos = new Vector2(_rng.Next(100, 400), _rng.Next(100, 400));
             Vector2 heading = new Vector2(1, 0); 
             
             var leaderEntity = SpawnVehicle(startPos, heading, vClass);
             int leaderId = leaderEntity.Index;
             _repository.SetComponent(leaderEntity, VehicleColor.Magenta);
             
             _repository.Bus.Publish(new CmdCreateFormation
             {
                 LeaderEntity = leaderEntity,
                 Type = type,
                 Params = new FormationParams 
                 {
                     Spacing = 12.0f,
                     WedgeAngleRad = 0.5f,
                     MaxCatchUpFactor = 1.25f,
                     BreakDistance = 50.0f,
                     ArrivalThreshold = 2.0f,
                     SpeedFilterTau = 1.0f
                 }
             });
             
             var template = _formationTemplates.GetTemplate(type);
             
             for (int i = 0; i < count - 1; i++) 
             {
                 Vector2 followerPos = template.GetSlotPosition(i, startPos, heading);
                 var followerEntity = SpawnVehicle(followerPos, heading, vClass);
                 _repository.SetComponent(followerEntity, VehicleColor.Cyan);
                 
                 _repository.Bus.Publish(new CmdJoinFormation
                 {
                     Entity = followerEntity,
                     LeaderEntity = leaderEntity,
                     SlotIndex = i
                 });
             }
             
             Vector2 dest = startPos + new Vector2(200, 0);
             SetDestination(leaderEntity, dest, interpolation);
         }
    }
}
