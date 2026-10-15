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
            _terrain = Fdp.Toolkit.World.WorldQuery.Of(repo);   // ⭐ CE-1035 Q2 — the movement model asks the world query
            // ⭐ CE-1034 H1 (R-249) — an entity's runtime ground-clamping flag decides whether the movement model holds it to the
            //   surface; no flag = clamped, as before. Read once per tick: the parallel update only reads it.
            _clampFlags = repo.IsComponentTypeRegistered<Fdp.Modules.Geographic.Components.GroundClampingConfig>();

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
                // ⭐ CE-3110 I-2c (R-271) — a passenger is CARRIED (EmbarkedStateSystem writes its transform from the vehicle's on the
                //   position owner); driving it here would fight the carry every frame and let a possessed passenger walk out of the hull.
                .Without<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()
                // ⭐ airborne insertion D-3 — a man coming down a rope or under a canopy is moved by his descent model (AirMotionSystem);
                //   driving and ground-clamping him here would pull him out of the sky
                .Without<Fdp.Toolkit.Motion.Descent>()
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
        private Fdp.Toolkit.World.IWorldQuery? _terrain;
        private bool _clampFlags;

        /// <summary>⭐ R-249 — whether the movement model puts <paramref name="entity"/> on the surface: its ground-clamping flag, else yes.</summary>
        private bool Clamped(EntityRepository repo, Entity entity)
            => !_clampFlags || !repo.HasComponent<Fdp.Modules.Geographic.Components.GroundClampingConfig>(entity)
               || repo.GetComponentRO<Fdp.Modules.Geographic.Components.GroundClampingConfig>(entity).IsClampingActive;

        private void UpdateVehicle(EntityRepository repo, Entity entity, float dt, SpatialHashGrid spatialGrid,
            RoadNetworkBlob roadNetwork)
        {
            var state = repo.GetComponent<VehicleState>(entity);
            var tf = repo.GetComponent<SimTransform>(entity);
            var vel = repo.GetComponent<SimVelocity>(entity);
            var @params = repo.GetComponent<VehicleParams>(entity);
            var nav = repo.GetComponent<NavState>(entity);
            // ⭐ AQ88 B (R-265) — a person's top speed depends on his LOGICAL stance (Nav §11 ratios): crouched runs at half, prone
            //   crawls at a fifth. A local copy — the component is not written back.
            //   ⭐ CE-2152 (R-2011 C8) — and a casualty on his shoulder or in tow slows him by the carry / drag factor (CarryLoad).
            if (@params.Class == VehicleClass.Pedestrian)
                @params.MaxSpeedFwd *= Hrot.MuscleCharacter.Animation.Components.StanceSpeed.Factor(
                    Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(repo, entity))
                    * Fdp.Toolkit.Behavior.CarryLoad.Factor(repo, entity)
                    // ⭐ CE-1082 (Injury slice I-b) — and a WOUNDED man limps along at his type's WoundedSpeedFactor (0.6)
                    * (repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() ? Fdp.Toolkit.Combat.Injury.SpeedFactor(repo, entity) : 1f);
            
            // Input conversion (bridge from SimTransform to 2D locals)
            Vector2 pos2D = new Vector2(tf.Position.X, tf.Position.Y);
            // X-forward convention (Model Space Front=X)
            Vector3 fwd3D = Vector3.Transform(Vector3.UnitX, tf.Rotation); 
            Vector2 fwd2D = new Vector2(fwd3D.X, fwd3D.Y);
            if (fwd2D.LengthSquared() < 0.0001f) fwd2D = Vector2.UnitX;
            else fwd2D = Vector2.Normalize(fwd2D);

            // ⭐⭐ CE-3173 (M-1..M-3, docs/DESIGN_Entity_Control.md §8b) — an OPERATOR drives this body: ManualDrive's axes, through
            //   their own integrators (ManualMotion) — the autonomous models below are untouched. No ManualDrive yet ⇒ it holds still.
            if (nav.Mode == KinematicsMode.Manual)
            {
                bool hasDrive = repo.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.ManualDrive>()
                                && repo.HasComponent<Fdp.Toolkit.Navigation.ManualDrive>(entity);
                var drive = hasDrive ? repo.GetComponentRO<Fdp.Toolkit.Navigation.ManualDrive>(entity) : default;
                bool walker = @params.Class == VehicleClass.Pedestrian;
                Vector2 planar;
                float turnRate;
                if (walker)
                {
                    planar = new Vector2(vel.Linear.X, vel.Linear.Y);
                    float yawTo = hasDrive ? drive.Yaw : MathF.Atan2(fwd2D.Y, fwd2D.X);
                    float top = @params.MaxSpeedFwd * (drive.Gait == 1 ? 0.5f : 1f);
                    var before = pos2D;
                    turnRate = ManualMotion.Person(ref pos2D, ref fwd2D, ref planar, ref state, drive.Forward, drive.Lateral, yawTo,
                        top, MathF.Max(@params.MaxAccel, HumanGait.StopDecel), dt);
                    // ⭐ CE-3181 (M-5) — never off the walkable mesh: slide along it, or stop. The navmesh is this host's world singleton
                    //   (EngineBackedNavigationModule); a host without one walks freely, as before.
                    if (repo.HasSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>()
                        && ManualMotion.KeepOnNavmesh(repo.GetSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>()!,
                            (uint)Fdp.Toolkit.Navigation.NavLayerSelection.For(repo, entity, 0), before, ref pos2D, tf.Position.Z))
                        planar = (pos2D - before) / dt;
                }
                else
                {
                    turnRate = ManualMotion.Vehicle(ref pos2D, ref fwd2D, ref state, in @params, drive.Forward, drive.Steer, dt);
                    planar = fwd2D * state.Speed;
                }
                FinishMove(repo, entity, ref state, ref nav, ref tf, ref vel, pos2D, fwd2D, planar, turnRate, walker, dt);
                return;
            }

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
                        // ⭐ CE-3145 (R-247) — a PERSON closes onto his path but never looks past the next corner (HumanGait.Aim)
                        if (@params.Class == VehicleClass.Pedestrian && HumanGait.Aim(in pursued, ref nav.ProgressS, pos2D, out var corner))
                        {
                            var toCorner = corner - pos2D;
                            if (toCorner.LengthSquared() > 1e-4f)
                            {
                                targetHeading = Vector2.Normalize(toCorner);
                                targetPos = corner;
                            }
                            break;
                        }
                        // ⭐ CE-3029 (§5.2b C7) — …but never across a curve: the lookahead shrinks until the path to it is nearly straight
                        float ld = CapLookaheadByChord(_trajectoryPool, nav.TrajectoryId, nav.ProgressS, PathLookahead(in @params, state.Speed),
                                                       MathF.Max(MinPathLookaheadMetres, @params.WheelBase * ChordCapFloorWheelBases));
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

            // ⭐⭐ CE-3195 (R-3003, docs/DESIGN_Weapon_Layers.md W-14b–e) — an AIMED person (his weapon brain's replicated WeaponPosture)
            //   turns his body to the aim — snapping beyond his twist, smoothly inside it — and moves along his way independently of
            //   it (sidestep, back-pedal), instead of walking where he faces. 📌 He used to fire at a target behind his back.
            bool aimedPerson = false;
            Vector2 toAim = default;
            Vector2 movedBefore = new Vector2(vel.Linear.X, vel.Linear.Y);
            if (@params.Class == VehicleClass.Pedestrian
                && Fdp.Toolkit.Combat.WeaponPostureOf.TryAimPoint(repo, entity, out var aimAt))
            {
                toAim = new Vector2(aimAt.X - pos2D.X, aimAt.Y - pos2D.Y);
                aimedPerson = toAim.LengthSquared() > 0.01f;
            }

            // Calculate desired velocity
            Vector2 desiredVelocity = targetHeading * targetSpeed;
            
            // Apply collision avoidance
            Vector2 avoidanceVelocity = ApplyCollisionAvoidance(
                desiredVelocity, pos2D, aimedPerson ? movedBefore : fwd2D * state.Speed,
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
                finalTargetSpeed = aimedPerson
                    ? targetSpeedAfterAvoidance * HumanGait.FacingSpeedFactor(fwd2D, avoidanceVelocity)   // ⭐ CE-3195 W-14d — no turn-first
                    : HumanGait.SpeedTarget(fwd2D, avoidanceVelocity, targetSpeedAfterAvoidance);   // far off ⇒ turn first

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
            Vector2 planarOut = default;
            if (person && aimedPerson)
            {
                // ⭐ CE-3195 W-14d/e — the way he goes (or, stopping, the way he was going), the body turned to the aim
                Vector2 travel = avoidanceVelocity.LengthSquared() > 1e-6f ? Vector2.Normalize(avoidanceVelocity)
                               : movedBefore.LengthSquared() > 1e-6f ? Vector2.Normalize(movedBefore) : fwd2D;
                yawRate = HumanGait.IntegrateFacing(ref pos2D, ref fwd2D, ref state, travel, Vector2.Normalize(toAim), accel, dt, out planarOut);
            }
            else if (person)
            {
                yawRate = HumanGait.Integrate(ref pos2D, ref fwd2D, ref state, avoidanceVelocity, targetSpeedAfterAvoidance > 0.01f, accel, dt);
            }
            else
            {
                BicycleModel.Integrate(ref pos2D, ref fwd2D, ref state, steerAngle, accel, dt, @params.WheelBase,
                    nav.ReverseAllowed == 1 ? @params.MaxSpeedRev : 0f,   // ⭐ CE-2167 (V5) — reverse when allowed
                    @params.MaxSpeedFwd > 0f ? @params.MaxSpeedFwd : float.MaxValue);   // 0 = an unknown profile (CE-103): no cap
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
                // ⭐ CE-3145 — a person's progress is his position projected on the segment he walks (HumanGait.Progress)
                if (person && _trajectoryPool.TryGetTrajectory(nav.TrajectoryId, out var walked) && walked.IsLooped == 0)
                    nav.ProgressS = HumanGait.Progress(in walked, nav.ProgressS, pos2D);
                else
                {
                    // ⭐ CE-2167 (V5) — the way it MOVES, not the way it faces: a vehicle backing along its path makes progress too
                    //   (it froze: the forward vector points against the tangent while reversing).
                    var travel = state.Speed < 0f ? -fwd2D : fwd2D;
                    nav.ProgressS += MathF.Abs(state.Speed) * dt * MathF.Max(0f, Vector2.Dot(travel, progressTangent ?? targetHeading));
                }
            }
            else if (nav.Mode == KinematicsMode.RoadGraph)
            {
                nav.ProgressS += state.Speed * dt;
            }
            
            FinishMove(repo, entity, ref state, ref nav, ref tf, ref vel, pos2D, fwd2D, aimedPerson ? (state.Speed > 0f ? planarOut : Vector2.Zero) : fwd2D * state.Speed, yawRate, person, dt);
        }

        /// <summary>
        /// The output of a step, shared by the autonomous and the ⭐ CE-3173 manual integrators: the surface under the body, the
        /// ledge rule, the pose and the velocity (<paramref name="planar"/> — a manual person's carries his strafe).
        /// </summary>
        private void FinishMove(EntityRepository repo, Entity entity, ref VehicleState state, ref NavState nav,
            ref SimTransform tf, ref SimVelocity vel, Vector2 pos2D, Vector2 fwd2D, Vector2 planar, float yawRate, bool person, float dt)
        {
            // Output conversion
            // ⭐⭐ W8 (R-182) — the movement model itself puts the vehicle on the surface under it: the
            //   ground, a roof, or the floor nearest its current Z (a garage deck). There is NO separate
            //   ground-clamp step. Without a terrain world the Z is kept, as before.
            float z = _terrain != null && Clamped(repo, entity)
                ? _terrain.SurfaceZ(pos2D.X, pos2D.Y, tf.Position.Z)
                : tf.Position.Z;
            // ⭐ CE-3145 (R-247) — A PERSON NEVER STEPS OFF A LEDGE: standing on a floor, a step whose floor is more than
            //   HumanGait.MaxStepDown lower is not taken (he stays, stopped). Stairs and ramps descend in small steps, so they pass.
            //   📐 bt-window-duel: the path skirts House A's stairwell by 5 cm and a cut corner dropped A 3 m to the ground floor.
            if (person && _terrain != null && z < tf.Position.Z - HumanGait.MaxStepDown)
            {
                var here = new Vector2(tf.Position.X, tf.Position.Y);
                if (MathF.Abs(_terrain.SurfaceZ(here.X, here.Y, tf.Position.Z) - tf.Position.Z) <= HumanGait.OnFloorTolerance)
                {
                    pos2D = here;
                    z = tf.Position.Z;
                    state.Speed = 0f;
                    planar = Vector2.Zero;
                }
            }
            float dz = z - tf.Position.Z;
            tf.Position = new Vector3(pos2D.X, pos2D.Y, z);
            float yaw = MathF.Atan2(fwd2D.Y, fwd2D.X);
            
            // X-forward, Y-left, Z-up convention.
            // Yaw is rotation around Z. 
            // We use CreateFromAxisAngle directly because CreateFromYawPitchRoll uses Y-axis for Yaw.
            tf.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);

            vel.Linear = new Vector3(planar.X, planar.Y, dt > 0f ? dz / dt : 0f);
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
        /// <summary>⭐ CE-3029 (Navigation v2 §5.2b C7) — the most the path between a vehicle and its lookahead point may bow away from
        /// the straight chord to it (m). Measured: a lookahead longer than the curve it looks across starts the turn early and drives
        /// inside it (a rounded R 3.8 m corner cut to 0.47 m of a 1.8 m clearance); capped at 0.1 m it holds 1.65–1.81 m.</summary>
        public const float PathChordTolerance = 0.1f;

        /// <summary>⭐ CE-3029 — the cap never shortens the lookahead below this many wheelbases (nor <see cref="MinPathLookaheadMetres"/>).</summary>
        public const float ChordCapFloorWheelBases = 0.5f;

        /// <summary>
        /// ⭐⭐ CE-3029 (§5.2b C7) — shrink a vehicle's lookahead <paramref name="ld"/> (by ¾ steps, never below <paramref name="floor"/>)
        /// until the path between progress <paramref name="s"/> and <paramref name="s"/> + ld stays within <paramref name="tolerance"/>
        /// of the straight chord between them (sampled at the quarter points). A straight path keeps the full lookahead (CE-3115's
        /// two wheelbases); a curve gets one short enough to follow it.
        /// </summary>
        public static float CapLookaheadByChord(TrajectoryPoolManager pool, int id, float s, float ld, float floor, float tolerance = PathChordTolerance)
        {
            if (ld <= floor) return ld;
            var (p0, _, _) = pool.SampleTrajectory(id, s);
            var a = new Vector2(p0.X, p0.Y);
            for (int k = 0; k < 8; k++)
            {
                var (pe, _, _) = pool.SampleTrajectory(id, s + ld);
                var b = new Vector2(pe.X, pe.Y);
                float dev = 0f;
                for (int q = 1; q <= 3; q++)
                {
                    var (pm, _, _) = pool.SampleTrajectory(id, s + ld * q * 0.25f);
                    dev = MathF.Max(dev, DistanceToSegment(new Vector2(pm.X, pm.Y), a, b));
                }
                if (dev <= tolerance) return ld;
                ld *= 0.75f;
                if (ld <= floor) return floor;
            }
            return ld;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.LengthSquared();
            if (l2 < 1e-8f) return Vector2.Distance(p, a);
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f);
            return Vector2.Distance(p, a + ab * t);
        }

        public static float PathLookahead(in VehicleParams p, float speed)
            => p.Class == VehicleClass.Pedestrian
                ? MathF.Max(HumanGait.MinPathLookahead, MathF.Abs(speed) * HumanGait.PathLookaheadSeconds)   // ⭐ CE-3145 — a person tracks his path
                : MathF.Max(MathF.Max(MinPathLookaheadMetres, 2f * p.WheelBase), MathF.Abs(speed) * p.LookaheadTimeMin);

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
            // ⭐ CE-3110 I-2c (R-271) — an EMBARKED neighbour rides inside some hull (an exposed passenger stays in the collider hash so it
            //   can be seen and hit): avoiding the hull covers it, and avoiding it too would make a vehicle swerve around its own riders.
            int k = 0;
            for (int i = 0; i < count; i++)
            {
                var (neighborEntity, pos) = neighbors[i];
                if (!neighborEntity.IsNull && repo.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(neighborEntity)) continue;
                
                // neighborEntity is a full Entity handle (Index + Generation) — no reconstruction needed.
                // Check if entity is valid and has SimVelocity (universal)
                if (!neighborEntity.IsNull && repo.HasComponent<SimVelocity>(neighborEntity))
                {
                    var neighborVel3D = repo.GetComponent<SimVelocity>(neighborEntity).Linear;
                    neighborData[k++] = (pos, new Vector2(neighborVel3D.X, neighborVel3D.Y));
                }
                else if (!neighborEntity.IsNull && repo.HasComponent<VehicleState>(neighborEntity))
                {
                    // Fallback for legacy (should not happen after migration) but keeping logic just in case
                    // But VehicleState no longer has Forward/Speed combined vector easily available?
                    // Ideally we rely on SimVelocity.
                    // If no SimVelocity, assume static.
                    neighborData[k++] = (pos, Vector2.Zero);
                }
                else
                {
                    // Fallback to stationary if entity is invalid
                    neighborData[k++] = (pos, Vector2.Zero);
                }
            }
            
            if (k == 0) return preferredVel;
            return RVOAvoidance.ApplyAvoidance(
                preferredVel, selfPos, selfVel, neighborData.Slice(0, k),
                @params.AvoidanceRadius, @params.MaxSpeedFwd);
        }
    }
}
