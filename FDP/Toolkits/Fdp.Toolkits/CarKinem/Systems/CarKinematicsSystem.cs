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
        /// <summary>⭐ 5d-3 — below this speed (m/s) a held entity (<see cref="NavState.IsBlocked"/>) is standing.</summary>
        public const float HeldStopSpeed = 0.05f;

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
            
            // ⭐ W8 (docs/DESIGN_Terrain_World.md §7.1) — the terrain world the movement model stands on. Read
            //   once per tick; SurfaceZ is read-only, so the parallel update may share it. Null = flat world.
            _terrain = repo.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()
                ? repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()
                : null;

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
        /// <summary>The terrain world for this tick (W8), or null on a flat / terrain-less world.</summary>
        private Fdp.Toolkit.Terrain.TerrainWorld? _terrain;

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
            // The direction along which motion counts as PROGRESS (CE-2059): the path tangent on a trajectory, which since
            // CE-3115 is no longer the steering heading.
            Vector2? progressTangent = null;
            
            switch (nav.Mode)
            {
                case KinematicsMode.RoadGraph:
                    // This updates nav state internal phase/progress, so we pass by ref
                    (targetPos, targetHeading, targetSpeed) = RoadGraphNavigator.UpdateRoadGraphNavigation(
                        ref nav, pos2D, roadNetwork);
                    break;
                    
                case KinematicsMode.CustomTrajectory:
                    (targetPos, targetHeading, targetSpeed) = SampleCustomTrajectory(ref nav, in @params, pos2D);
                    progressTangent = targetHeading;
                    // ⭐ CE-3115 — PURE PURSUIT ON THE PATH (📄 FDP.Toolkit.CarKinem.md "Pure Pursuit — geometric path-following
                    //   using lookahead points"): steer at the path point a lookahead ahead of the progress, so a sideways error —
                    //   a cut corner, an avoidance swerve — closes. ⛔ It steered along the path's TANGENT there, so any drift
                    //   stayed for good: live on bt-doors a walker ran 1.4 m off its path, through House A's wall, and passed a
                    //   closed door out of reach. The same "parallel driving" the Formation branch below already fixes.
                    //   ⚠ Not on the end-of-path homing leg (already aimed at the end point) or when standing.
                    if (targetSpeed > 0f && nav.HasArrived == 0
                        && _trajectoryPool.TryGetTrajectory(nav.TrajectoryId, out var pursued)
                        && (pursued.IsLooped != 0 || nav.ProgressS < pursued.TotalLength - 0.1f))
                    {
                        float ld = PathLookahead(in @params, state.Speed);
                        var (ahead, _, _) = _trajectoryPool.SampleTrajectory(nav.TrajectoryId, nav.ProgressS + ld);
                        var toAhead = new Vector2(ahead.X, ahead.Y) - pos2D;
                        if (toAhead.LengthSquared() > 1e-4f)
                        {
                            targetHeading = Vector2.Normalize(toAhead);
                            targetPos = new Vector2(ahead.X, ahead.Y);
                        }
                    }
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
                    // If we have a destination and we are not in a specific mode, drive to point.
                    // Steering is 2D-projected (§0.2): project the 3D destination to XY.
                    Vector2 navDestXY = new Vector2(nav.FinalDestination.X, nav.FinalDestination.Y);
                    if (nav.HasArrived == 0 && nav.TargetSpeed > 0 && Vector2.DistanceSquared(pos2D, navDestXY) > nav.ArrivalRadius * nav.ArrivalRadius)
                    {
                         Vector2 toDest = navDestXY - pos2D;
                         targetHeading = Vector2.Normalize(toDest);
                         targetPos = pos2D + targetHeading; // Look ahead

                         // Approach braking. Without this, targetSpeed is a STEP function of
                         // distance — full cruise until inside ArrivalRadius, then 0 — so the
                         // vehicle commits to cruise speed with only ArrivalRadius left in which
                         // to shed it. At 15 m/s with MaxDecel 4 m/s² the stopping distance is
                         // v²/2a = 28 m against a 5 m radius, i.e. a ~23 m overshoot.
                         // Cap the target by the braking envelope that still stops on the point:
                         //     v_max(d) = sqrt(2 · MaxDecel · (d − ArrivalRadius))
                         // Design basis: FDP/Docs/projects/toolkits/FDP.Toolkit.CarKinem.md
                         // §"Speed Controller" (MaxBraking is a control input, not just a clamp)
                         // and .dev/_DONE/demos-1/FDP-demos-all.md:605/636, whose acceptance is
                         // "halts ... without overshooting the objective".
                         // MaxDecel <= 0 means the profile is unknown (a default-constructed
                         // VehicleParams — see CE-103); cap nothing rather than freeze the vehicle.
                         targetSpeed = nav.TargetSpeed;
                         if (@params.MaxDecel > 0f)
                         {
                             float brakingDistance = MathF.Max(0f, toDest.Length() - nav.ArrivalRadius);
                             float approachSpeed   = MathF.Sqrt(2f * @params.MaxDecel * brakingDistance);
                             targetSpeed = MathF.Min(targetSpeed, approachSpeed);
                         }
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
            
            // ⭐ Buildings 5d-3 — NavState.IsBlocked, "obstacle ahead" (📄 FDP.Toolkit.CarKinem.md, designed and until now never
            //   read): the entity brakes to a stop where it is and KEEPS its path and progress; it drives on when the flag clears.
            //   Set today only by the door passage system while a door ahead is being opened.
            if (nav.IsBlocked != 0) targetSpeed = 0f;

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

            // ⭐ CE-3145 (R-247) — a PERSON turns on the spot and walks where he faces (HumanGait); a vehicle steers.
            bool person = @params.Class == VehicleClass.Pedestrian;

            // Pure Pursuit steering
            float steerAngle = person ? 0f : PurePursuitController.CalculateSteering(
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
            if (person)
                finalTargetSpeed = HumanGait.SpeedTarget(fwd2D, avoidanceVelocity, targetSpeedAfterAvoidance);   // far off ⇒ turn first

            if (finalTargetSpeed < 0f)
                finalTargetSpeed = MathF.Max(finalTargetSpeed, -@params.MaxSpeedRev);
            else
                finalTargetSpeed = MathF.Min(finalTargetSpeed, @params.MaxSpeedFwd);
            
            float accel = SpeedController.CalculateAcceleration(
                state.Speed,
                finalTargetSpeed,
                @params.AccelGain,
                @params.MaxAccel,
                person ? MathF.Max(@params.MaxDecel, HumanGait.StopDecel) : @params.MaxDecel);
            
            // Integrate: a person's gait, else the bicycle model
            float yawRate;
            if (person)
            {
                yawRate = HumanGait.Integrate(ref pos2D, ref fwd2D, ref state, avoidanceVelocity, targetSpeedAfterAvoidance > 0.01f, accel, dt);
            }
            else
            {
                BicycleModel.Integrate(ref pos2D, ref fwd2D, ref state, steerAngle, accel, dt, @params.WheelBase);
                yawRate = (state.Speed / @params.WheelBase) * MathF.Tan(steerAngle);
            }

            if (nav.ReverseAllowed == 0 && state.Speed < 0f)
            {
                state.Speed = 0f;
            }

            // ⭐ 5d-3 — the speed controller is proportional, so a held entity's speed only DECAYS towards 0 and it creeps on;
            //   held means standing, so the last crawl is cut.
            if (nav.IsBlocked != 0 && MathF.Abs(state.Speed) < HeldStopSpeed)
            {
                state.Speed = 0f;
            }

            // Update progress (for trajectory/road modes)
            if (nav.Mode == KinematicsMode.CustomTrajectory)
            {
                // ⭐ CE-2059 — only the motion ALONG the path is progress (targetHeading is the path tangent here); a turn
                //   or a sideways drift no longer counts. ⚠ Not for the homing leg's direct heading, which is not a tangent —
                //   progress is already at the end there.
                nav.ProgressS += state.Speed * dt * MathF.Max(0f, Vector2.Dot(fwd2D, progressTangent ?? targetHeading));
            }
            else if (nav.Mode == KinematicsMode.RoadGraph)
            {
                nav.ProgressS += state.Speed * dt;
            }
            
            // Output conversion
            // ⭐⭐ W8 (R-182) — the movement model itself puts the vehicle on the surface under it: the
            //   ground, a roof, or the floor nearest its current Z (a garage deck). There is NO separate
            //   ground-clamp step. Without a terrain world the Z is kept, as before.
            float z = _terrain != null
                ? _terrain.SurfaceZ(pos2D.X, pos2D.Y, tf.Position.Z)
                : tf.Position.Z;
            float dz = z - tf.Position.Z;
            tf.Position = new Vector3(pos2D.X, pos2D.Y, z);
            float yaw = MathF.Atan2(fwd2D.Y, fwd2D.X);
            
            // X-forward, Y-left, Z-up convention.
            // Yaw is rotation around Z. 
            // We use CreateFromAxisAngle directly because CreateFromYawPitchRoll uses Y-axis for Yaw.
            tf.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);

            vel.Linear = new Vector3(fwd2D.X * state.Speed, fwd2D.Y * state.Speed, dt > 0f ? dz / dt : 0f);
            vel.Angular = new Vector3(0, 0, yawRate); // Yaw rate around Z

            // Write back state
            repo.SetComponent(entity, state);
            repo.SetComponent(entity, nav);
            repo.SetComponent(entity, tf);
            repo.SetComponent(entity, vel);
        }
        
        private (Vector2 pos, Vector2 heading, float speed) SampleCustomTrajectory(ref NavState nav, in VehicleParams @params, Vector2 pos2D)
        {
            // Check if we reached the end of the trajectory
            bool haveTraj = _trajectoryPool.TryGetTrajectory(nav.TrajectoryId, out var traj);
            if (haveTraj)
            {
                if (traj.IsLooped == 0 && nav.ProgressS >= traj.TotalLength - 0.1f && traj.Waypoints.Length > 0) // 10cm tolerance
                {
                    // ⭐ CE-2059 — arrival is confirmed by POSITION. Progress is dead-reckoned, so it can run ahead of the
                    //   vehicle (a turn-around at the start counted as progress: measured live, "arrived" 23 m short of home).
                    //   Outside the arrival radius the vehicle HOMES on the end point, braking by the real distance.
                    var endWp   = traj.Waypoints[traj.Waypoints.Length - 1];
                    var endXY   = new Vector2(endWp.Position.X, endWp.Position.Y);
                    float tol   = nav.ArrivalRadius > 0f ? nav.ArrivalRadius : DefaultTrajectoryArrivalRadius;
                    float toEnd = Vector2.Distance(pos2D, endXY);
                    if (toEnd > tol)
                    {
                        Vector2 dir   = (endXY - pos2D) / toEnd;
                        float homing  = nav.TargetSpeed > 0f ? nav.TargetSpeed : MathF.Max(endWp.DesiredSpeed, TrajectoryEndCrawlSpeed);
                        if (@params.MaxDecel > 0f)
                            homing = MathF.Min(homing, MathF.Max(MathF.Sqrt(2f * @params.MaxDecel * (toEnd - tol)), TrajectoryEndCrawlSpeed));
                        return (pos2D + dir, dir, homing);
                    }

                    nav.HasArrived = 1;
                    // Provide the last waypoint position/tangent but 0 speed
                    {
                         var last = traj.Waypoints[traj.Waypoints.Length - 1];
                         // Keep current heading (via last tangent) to avoid spinning
                         // Steering is 2D-projected (§0.2); the carried trajectory Z is not fed here.
                         Vector2 lastXY = new Vector2(last.Position.X, last.Position.Y);
                         return (lastXY, TrajectoryPoolManager.EndTangent(traj.Waypoints), 0f);
                    }
                }
            }

            // SampleTrajectory returns the 3D position; project to XY for steering. The trajectory
            // Z is carried for fidelity/replication but does NOT drive vehicle dynamics: the vehicle's Z is
            // set by THIS system from the terrain world's surface (W8, docs/DESIGN_Terrain_World.md).
            var (pos, tangent, speed) = _trajectoryPool.SampleTrajectory(nav.TrajectoryId, nav.ProgressS);

            // ⭐ CE-2060 — the move's requested speed CAPS the path's own speed (a solver path is registered at a flat
            //   10 m/s). NavState.TargetSpeed is the intent's, set by NavigationIntentBridgeSystem for PathToPoint and
            //   FollowRoute; 0 means "no cap — drive the trajectory's speeds". Navigation design §3.1 (CE-2059/2060).
            if (nav.TargetSpeed > 0f)
                speed = MathF.Min(speed, nav.TargetSpeed);

            // ⭐ CE-2059 — brake on approach to the END of a one-shot trajectory, with the same envelope Direct mode uses
            //   (v_max = sqrt(2·MaxDecel·d)); it stopped dead at the end and rolled 11 m past it. A small crawl floor keeps
            //   ProgressS reaching the end. MaxDecel <= 0 is an unknown profile (CE-103): cap nothing.
            if (haveTraj && @params.MaxDecel > 0f && traj.IsLooped == 0 && traj.Waypoints.Length > 0)
            {
                var endWp2      = traj.Waypoints[traj.Waypoints.Length - 1];
                float straight  = Vector2.Distance(pos2D, new Vector2(endWp2.Position.X, endWp2.Position.Y));
                // ⭐ the larger of the two: progress that ran ahead must not brake the vehicle early.
                float remaining = MathF.Max(MathF.Max(0f, traj.TotalLength - nav.ProgressS), straight);
                float approach  = MathF.Max(MathF.Sqrt(2f * @params.MaxDecel * remaining), TrajectoryEndCrawlSpeed);
                speed = MathF.Min(speed, approach);
            }
            return (new Vector2(pos.X, pos.Y), tangent, speed);
        }

        /// <summary>The nearest a path lookahead point may be (m) — a walker turns within it.</summary>
        public const float MinPathLookaheadMetres = 1.0f;

        /// <summary>
        /// ⭐ CE-3115 — how far ahead on its path a mover aims (m): at least <see cref="MinPathLookaheadMetres"/> and two wheelbases
        /// (a vehicle cannot close a nearer point), growing with speed by <see cref="VehicleParams.LookaheadTimeMin"/> seconds.
        /// A walker at 1.5 m/s aims 1 m ahead; a car at 15 m/s 7.5 m.
        /// </summary>
        public static float PathLookahead(in VehicleParams p, float speed)
            => MathF.Max(MathF.Max(MinPathLookaheadMetres, 2f * p.WheelBase), MathF.Abs(speed) * p.LookaheadTimeMin);

        /// <summary>⭐ CE-2059 — the slowest a vehicle approaches a trajectory's end, so the braking envelope (which tends to
        /// 0 at the end) still lets it get there.</summary>
        private const float TrajectoryEndCrawlSpeed = 0.5f;

        /// <summary>⭐ CE-2059 — the arrival tolerance at a trajectory's end when the move set no <c>ArrivalRadius</c>.</summary>
        private const float DefaultTrajectoryArrivalRadius = 2f;
        
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
