using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CarKinem.Avoidance;
using CarKinem.Controllers;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace CarKinem.Systems
{
    /// <summary>
    /// Main vehicle physics system.
    /// Runs in parallel for all vehicles.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    // [UpdateAfter(typeof(SpatialHashSystem))] -- ordering maintained by array position in GroundKinematicsModule.
    // [UpdateAfter(typeof(FormationTargetSystem))] -- ordering maintained by array position in GroundKinematicsModule.
    public class CarKinematicsSystem : IEcsModuleSystem
    {
        private readonly TrajectoryPoolManager _trajectoryPool;
        
        public CarKinematicsSystem(TrajectoryPoolManager trajectoryPool)
        {
            _trajectoryPool = trajectoryPool;
            if (EnablePerformanceLogging)
                _perfStopwatch = new System.Diagnostics.Stopwatch();
        }

        private System.Diagnostics.Stopwatch? _perfStopwatch;
        private double _totalUpdateTime = 0;
        private int _updateCount = 0;
        
        public bool EnablePerformanceLogging { get; set; } = false;
        
        /// <summary>
        /// For testing/debugging purposes.
        /// </summary>
        public bool ForceSerial { get; set; } = false;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(CarKinematicsSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            _perfStopwatch?.Restart();

            float dt = deltaTime;

            // Read road network from ZoneEnvironmentData singleton (empty blob when no zone loaded).
            // Never return early on absence -- non-road vehicle physics must always run.
            var roadNetwork = repo.HasSingleton<ZoneEnvironmentData>()
                ? repo.GetSingleton<ZoneEnvironmentData>().RoadNetwork
                : default; // empty blob -- safe for non-road scenarios
            
            // Read spatial grid from singleton (Data-Oriented dependency)
            if (!repo.HasSingleton<SpatialGridData>()) return;
            
            var gridData = repo.GetSingleton<SpatialGridData>();
            var spatialGrid = gridData.Grid;
            
            // Get all vehicles (owned only -- skip ghost entities to enforce split-authority)
            var query = repo.Query()
                .With<VehicleState>()
                .With<SimTransform>()
                .WithOwned<SimTransform>()
                .With<SimVelocity>()
                .With<VehicleParams>()
                .With<NavState>()
                .Build();
            
            // We use FDP Kernel's optimized ForEachParallel which handles load balancing
            // and avoids allocations.
            
            if (ForceSerial)
            {
                // Zero-allocation standard iteration
                foreach (var entity in query)
                {
                    UpdateVehicle(repo, entity, dt, spatialGrid, roadNetwork);
                }
            }
            else
            {
                // Kernel optimized parallel execution
                query.ForEachParallel(entity =>
                {
                    UpdateVehicle(repo, entity, dt, spatialGrid, roadNetwork);
                });
            }

            if (EnablePerformanceLogging && _perfStopwatch != null)
            {
                _perfStopwatch.Stop();
                _totalUpdateTime += _perfStopwatch.Elapsed.TotalMilliseconds;
                _updateCount++;
                
                if (_updateCount % 60 == 0)  // Log every 60 frames
                {
                    double avgMs = _totalUpdateTime / _updateCount;
                    int vehicleCount = query.Count();
                    double usPerVehicle = vehicleCount > 0 ? (avgMs * 1000 / vehicleCount) : 0;
                    
                    Console.WriteLine($"[CarKinematics] Avg: {avgMs:F2} ms, Vehicles: {vehicleCount}, μs/vehicle: {usPerVehicle:F2}");
                }
            }
        }
        
        // THREAD-SAFE: Method operates on unique entity and uses read-only shared data
        private void UpdateVehicle(EntityRepository repo, Entity entity, float dt, SpatialHashGrid spatialGrid,
            RoadNetworkBlob roadNetwork)
        {
            var state = repo.GetComponent<VehicleState>(entity);
            var tf = repo.GetComponent<SimTransform>(entity);
            var vel = repo.GetComponent<SimVelocity>(entity);
            var @params = repo.GetComponent<VehicleParams>(entity);
            var nav = repo.GetComponent<NavState>(entity);
            
            // Input conversion (bridge from SimTransform to 2D locals)
            Vector2 pos2D = new Vector2(tf.Position.X, tf.Position.Y);
            // X-forward convention (Model Space Front=X)
            Vector3 fwd3D = Vector3.Transform(Vector3.UnitX, tf.Rotation); 
            Vector2 fwd2D = new Vector2(fwd3D.X, fwd3D.Y);
            if (fwd2D.LengthSquared() < 0.0001f) fwd2D = Vector2.UnitX;
            else fwd2D = Vector2.Normalize(fwd2D);

            // Determine target (position, heading, speed) based on navigation mode
            Vector2 targetPos;
            Vector2 targetHeading;
            float targetSpeed;
            
            switch (nav.Mode)
            {
                case KinematicsMode.RoadGraph:
                    // This updates nav state internal phase/progress, so we pass by ref
                    (targetPos, targetHeading, targetSpeed) = RoadGraphNavigator.UpdateRoadGraphNavigation(
                        ref nav, pos2D, roadNetwork);
                    break;
                    
                case KinematicsMode.CustomTrajectory:
                    (targetPos, targetHeading, targetSpeed) = SampleCustomTrajectory(ref nav);
                    break;
                    
                case KinematicsMode.Formation:
                    (targetPos, targetHeading, targetSpeed) = GetFormationTarget(repo, entity);

                    // Drive towards the slot if not reached
                    // This prevents "parallel driving" where vehicle maintains offset but never closes the gap
                    float distToSlot = Vector2.Distance(pos2D, targetPos);
                    if (distToSlot > 2.0f)
                    {
                        // Steer towards slot
                        targetHeading = Vector2.Normalize(targetPos - pos2D);
                        
                        // Catch up speed (P-controller)
                        targetSpeed += distToSlot * 0.5f; // Reduced gain to avoid overshooting
                        targetSpeed = MathF.Min(targetSpeed, @params.MaxSpeedFwd);
                    }
                    break;
                    
                case KinematicsMode.Direct:
                case KinematicsMode.None:
                default:
                    // If we have a destination and we are not in a specific mode, drive to point
                    // Simple "Drive to point" logic
                    if (nav.HasArrived == 0 && nav.TargetSpeed > 0 && Vector2.DistanceSquared(pos2D, nav.FinalDestination) > nav.ArrivalRadius * nav.ArrivalRadius)
                    {
                         Vector2 toDest = nav.FinalDestination - pos2D;
                         targetHeading = Vector2.Normalize(toDest);
                         targetPos = pos2D + targetHeading; // Look ahead
                         targetSpeed = nav.TargetSpeed;
                    }
                    else
                    {
                        // Idle / Arrived
                        targetPos = pos2D;
                        targetHeading = fwd2D;
                        targetSpeed = 0f;
                        // Only mark as arrived when the entity was actively navigating (TargetSpeed > 0).
                        // Static entities (TargetSpeed == 0) must not receive HasArrived=1 on spawn —
                        // that would falsely trigger arrival signals for entities with zero velocity.
                        if (nav.TargetSpeed > 0)
                            nav.HasArrived = 1;
                    }
                    break;
            }
            
            // Calculate desired velocity
            Vector2 desiredVelocity = targetHeading * targetSpeed;
            
            // Apply collision avoidance
            Vector2 avoidanceVelocity = ApplyCollisionAvoidance(
                desiredVelocity, pos2D, fwd2D * state.Speed, 
                spatialGrid, @params, repo);
            
            // Speed control
            float targetSpeedAfterAvoidance = avoidanceVelocity.Length();
            float speedSign = 1f;

            if (nav.ReverseAllowed == 1 && targetSpeedAfterAvoidance > 0.01f)
            {
                if (Vector2.Dot(fwd2D, targetHeading) < 0f)
                {
                    speedSign = -1f;
                }
            }

            // Pure Pursuit steering
            float steerAngle = PurePursuitController.CalculateSteering(
                pos2D,
                fwd2D,
                avoidanceVelocity,
                state.Speed,
                @params.WheelBase,
                @params.LookaheadTimeMin,
                @params.LookaheadTimeMax,
                @params.MaxSteerAngle,
                speedSign < 0f);

            // Cornering speed limit
            float maxCorneringSpeed = float.MaxValue;
            if (MathF.Abs(steerAngle) > 0.01f)
            {
                // Radius = L / sin(delta)
                float turnRadius = @params.WheelBase / MathF.Abs(MathF.Sin(steerAngle));
                // V_max = sqrt(a_lat_max * R)
                maxCorneringSpeed = MathF.Sqrt(@params.MaxLatAccel * turnRadius);
            }

            float finalTargetSpeed = MathF.Min(targetSpeedAfterAvoidance, maxCorneringSpeed) * speedSign;

            if (finalTargetSpeed < 0f)
                finalTargetSpeed = MathF.Max(finalTargetSpeed, -@params.MaxSpeedRev);
            else
                finalTargetSpeed = MathF.Min(finalTargetSpeed, @params.MaxSpeedFwd);
            
            float accel = SpeedController.CalculateAcceleration(
                state.Speed,
                finalTargetSpeed,
                @params.AccelGain,
                @params.MaxAccel,
                @params.MaxDecel);
            
            // Integrate bicycle model
            BicycleModel.Integrate(ref pos2D, ref fwd2D, ref state, steerAngle, accel, dt, @params.WheelBase);

            if (nav.ReverseAllowed == 0 && state.Speed < 0f)
            {
                state.Speed = 0f;
            }
            
            // Update progress (for trajectory/road modes)
            if (nav.Mode == KinematicsMode.CustomTrajectory || nav.Mode == KinematicsMode.RoadGraph)
            {
                nav.ProgressS += state.Speed * dt;
            }
            
            // Output conversion
            tf.Position = new Vector3(pos2D.X, pos2D.Y, tf.Position.Z);
            float yaw = MathF.Atan2(fwd2D.Y, fwd2D.X);
            
            // X-forward, Y-left, Z-up convention.
            // Yaw is rotation around Z. 
            // We use CreateFromAxisAngle directly because CreateFromYawPitchRoll uses Y-axis for Yaw.
            tf.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);

            vel.Linear = new Vector3(fwd2D.X * state.Speed, fwd2D.Y * state.Speed, 0);
            vel.Angular = new Vector3(0, 0, (state.Speed / @params.WheelBase) * MathF.Tan(steerAngle)); // Yaw rate around Z

            // Write back state
            repo.SetComponent(entity, state);
            repo.SetComponent(entity, nav);
            repo.SetComponent(entity, tf);
            repo.SetComponent(entity, vel);
        }
        
        private (Vector2 pos, Vector2 heading, float speed) SampleCustomTrajectory(ref NavState nav)
        {
            // Check if we reached the end of the trajectory
            if (_trajectoryPool.TryGetTrajectory(nav.TrajectoryId, out var traj))
            {
                if (traj.IsLooped == 0 && nav.ProgressS >= traj.TotalLength - 0.1f) // 10cm tolerance
                {
                    nav.HasArrived = 1;
                    // Provide the last waypoint position/tangent but 0 speed
                    if (traj.Waypoints.Length > 0)
                    {
                         var last = traj.Waypoints[traj.Waypoints.Length - 1];
                         // Keep current heading (via last tangent) to avoid spinning
                         Vector2 t = traj.Waypoints.Length > 1 ? Vector2.Normalize(last.Position - traj.Waypoints[traj.Waypoints.Length-2].Position) : new Vector2(1,0);
                         return (last.Position, t, 0f);
                    }
                }
            }

            var (pos, tangent, speed) = _trajectoryPool.SampleTrajectory(nav.TrajectoryId, nav.ProgressS);
            return (pos, tangent, speed);
        }
        
        private (Vector2 pos, Vector2 heading, float speed) GetFormationTarget(EntityRepository repo, Entity entity)
        {
            if (!repo.HasComponent<FormationTarget>(entity))
            {
                var tf = repo.GetComponent<SimTransform>(entity);
                var pos2D = new Vector2(tf.Position.X, tf.Position.Y);
                var fwd3D = Vector3.Transform(Vector3.UnitX, tf.Rotation);
                return (pos2D, new Vector2(fwd3D.X, fwd3D.Y), 0f);
            }
            
            var target = repo.GetComponent<FormationTarget>(entity);
            return (target.TargetPosition, target.TargetHeading, target.TargetSpeed);
        }
        
        // THREAD-SAFE: Read-only access to neighbors, writes only to local stack vars
        private Vector2 ApplyCollisionAvoidance(Vector2 preferredVel, Vector2 selfPos, 
            Vector2 selfVel, SpatialHashGrid spatialGrid, VehicleParams @params, EntityRepository repo)
        {
            // Query neighbors within avoidance radius
            Span<(Entity, Vector2)> neighbors = stackalloc (Entity, Vector2)[32];
            int count = spatialGrid.QueryNeighbors(selfPos, @params.AvoidanceRadius * 2.5f, neighbors);
            
            if (count == 0)
                return preferredVel;
            
            // Convert to (pos, vel) format for RVO
            Span<(Vector2 pos, Vector2 vel)> neighborData = stackalloc (Vector2, Vector2)[count];
            for (int i = 0; i < count; i++)
            {
                var (neighborEntity, pos) = neighbors[i];
                
                // neighborEntity is a full Entity handle (Index + Generation) — no reconstruction needed.
                // Check if entity is valid and has SimVelocity (universal)
                if (!neighborEntity.IsNull && repo.HasComponent<SimVelocity>(neighborEntity))
                {
                    var neighborVel3D = repo.GetComponent<SimVelocity>(neighborEntity).Linear;
                    neighborData[i] = (pos, new Vector2(neighborVel3D.X, neighborVel3D.Y));
                }
                else if (!neighborEntity.IsNull && repo.HasComponent<VehicleState>(neighborEntity))
                {
                    // Fallback for legacy (should not happen after migration) but keeping logic just in case
                    // But VehicleState no longer has Forward/Speed combined vector easily available?
                    // Ideally we rely on SimVelocity.
                    // If no SimVelocity, assume static.
                    neighborData[i] = (pos, Vector2.Zero);
                }
                else
                {
                    // Fallback to stationary if entity is invalid
                    neighborData[i] = (pos, Vector2.Zero);
                }
            }
            
            return RVOAvoidance.ApplyAvoidance(
                preferredVel, selfPos, selfVel, neighborData,
                @params.AvoidanceRadius, @params.MaxSpeedFwd);
        }
    }
}
