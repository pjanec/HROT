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
    [TickMode(TickPolicy.WhenTimeAdvances)]
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
            // ⭐ CE-3286 — a held hull's recovery keeps its state on the watchdog's component; a world without it does not recover
            _recovery = repo.IsComponentTypeRegistered<FrustrationTicks>();
            // ⭐ CE-3287 — a wreck (Health ≤ 0) is not driven; a world without Health has none
            _health = repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>();

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
        private bool _recovery;
        private bool _health;

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
            {
                float slowed = Hrot.MuscleCharacter.Animation.Components.StanceSpeed.Factor(
                    Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(repo, entity))
                    * Fdp.Toolkit.Behavior.CarryLoad.Factor(repo, entity)
                    // ⭐ CE-1082 (Injury slice I-b) — and a WOUNDED man limps along at his type's WoundedSpeedFactor (0.6)
                    * (repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() ? Fdp.Toolkit.Combat.Injury.SpeedFactor(repo, entity) : 1f);
                @params.WalkSpeed    = HumanGait.WalkOf(in @params) * slowed;   // ⭐ CE-1208 — his walk slows as his run does
                @params.MaxSpeedFwd *= slowed;
            }
            
            // Input conversion (bridge from SimTransform to 2D locals)
            Vector2 pos2D = new Vector2(tf.Position.X, tf.Position.Y);
            // X-forward convention (Model Space Front=X)
            Vector3 fwd3D = Vector3.Transform(Vector3.UnitX, tf.Rotation); 
            Vector2 fwd2D = new Vector2(fwd3D.X, fwd3D.Y);
            if (fwd2D.LengthSquared() < 0.0001f) fwd2D = Vector2.UnitX;
            else fwd2D = Vector2.Normalize(fwd2D);

            // ⭐ AQ93 R-4023 — SUMO drives this body: TrafficShadowFollowSystem writes its pose from the shadow car. Nothing to integrate.
            if (nav.Mode == KinematicsMode.Traffic) return;

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
                    // ⭐ CE-1208 — Gait 1 WALKS at his type's walk (HumanGait.WalkOf, scaled above), never above his run; Gait 0 RUNS at
                    //   the type's top (R-265). Was a hard-coded half of the top: a BDC rifleman ran 2.5 and walked 1.25.
                    float top = drive.Gait == 1 ? MathF.Min(@params.WalkSpeed, @params.MaxSpeedFwd) : @params.MaxSpeedFwd;
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
                FinishMove(repo, entity, ref state, ref nav, ref tf, ref vel, pos2D, fwd2D, planar, turnRate, walker, dt, hull: @params);
                return;
            }

            // ⭐⭐ CE-3286 (R-3025; Motion Domains §5f) — A HELD HULL BACKS OUT. The recovery's state is the watchdog's (FrustrationTicks);
            //   a hull with a size, on a move it is driving, over a world with walls. ⭐ The "smart" half: a STANDING START on a new move is
            //   checked first — a hull parked nose to a wall whose way on is not clear backs out before it touches it.
            // ⭐⭐ CE-3287 (R-3026; Motion Domains §5g) — a WRECK has no driver: no move, no recovery, no push (📐 one man 4 m off pushed a
            //   destroyed M1 ~10 m). Belt and braces with CE-3091, which ends the move on the brain at death.
            bool wreck = _health && @params.Class != VehicleClass.Pedestrian && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(repo, entity);
            // ⭐⭐ CE-2321 (R-3027; Motion Domains §5h) — a MAN DOWN (incapacitated or dead) lies where he fell: no move, no push, no turn.
            //   📌 Live: whoever came close pushed and turned a lying body. He moves only carried or dragged (embarked, so not driven here);
            //   others still walk round him.
            bool down = _health && @params.Class == VehicleClass.Pedestrian && !Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, entity);
            bool recoverable = !wreck && _recovery && _terrain != null && HullClearance.HasHull(in @params) && HullRecovery.Driving(in nav)
                               && repo.HasComponent<FrustrationTicks>(entity);
            FrustrationTicks ft = recoverable ? repo.GetComponent<FrustrationTicks>(entity) : default;
            if (recoverable)
            {
                if (ft.RecoverDir == 0 && MathF.Abs(state.Speed) < HullRecovery.StandingSpeed && ft.CheckedMoveKey != HullRecovery.MoveKey(in nav))
                {
                    ft.CheckedMoveKey = HullRecovery.MoveKey(in nav);
                    if (ft.Recoveries < HullRecovery.MaxRecoveries && !HullClearance.WayOnClear(_terrain!, _trajectoryPool, in nav, in @params, pos2D, fwd2D, tf.Position.Z))
                        HullRecovery.Begin(ref ft, -1, fwd2D, AimOf(in nav, in @params, pos2D, state.Speed) - pos2D);
                    repo.SetComponent(entity, ft);
                }
                if (ft.RecoverDir != 0)
                {
                    DriveRecovery(repo, entity, ref state, ref nav, ref tf, ref vel, in @params, ref ft, pos2D, fwd2D, dt);
                    return;
                }
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
                    (targetPos, targetHeading, targetSpeed) = SampleCustomTrajectory(ref nav, in @params, pos2D, fwd2D, state.Speed);
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
                         // ⭐ CE-2198 — the envelope less the controller's lag (StoppingSpeed)
                         targetSpeed = MathF.Min(targetSpeed, StoppingSpeed(in @params, toDest.Length() - nav.ArrivalRadius, state.Speed));
                         // ⭐ CE-2206 — a point inside the hull's turning circle: drive on straight until it is not, then turn to it
                         if (@params.Class != VehicleClass.Pedestrian && InsideTurningCircle(in @params, pos2D, fwd2D, navDestXY))
                         {
                             targetHeading = fwd2D;
                             targetPos = pos2D + fwd2D;
                             targetSpeed = MathF.Min(nav.TargetSpeed, TurnClearanceSpeed);
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
            if (wreck || down) targetSpeed = 0f;   // ⭐ CE-3287 / CE-2321 — it brakes and stays
            // ⭐ CE-3255 (BR-2; DESIGN_Building_Clearing CB-6) — a STUNNED person of any class stands where he is until the stun ends (his path
            //   and progress kept, as for a blocked one); before, only the civilian code read the stun.
            if (@params.Class == VehicleClass.Pedestrian && Fdp.Toolkit.Combat.Effects.IsStunned(repo, entity)) targetSpeed = 0f;

            // ⭐⭐ CE-3195 (R-3003, docs/DESIGN_Weapon_Layers.md W-14b–e) — an AIMED person (his weapon brain's replicated WeaponPosture)
            //   turns his body to the aim — snapping beyond his twist, smoothly inside it — and moves along his way independently of
            //   it (sidestep, back-pedal), instead of walking where he faces. 📌 He used to fire at a target behind his back.
            bool aimedPerson = false;
            Vector2 toAim = default;
            Vector2 movedBefore = new Vector2(vel.Linear.X, vel.Linear.Y);
            if (@params.Class == VehicleClass.Pedestrian && !down
                && Fdp.Toolkit.Combat.WeaponPostureOf.TryAimPoint(repo, entity, out var aimAt))
            {
                toAim = new Vector2(aimAt.X - pos2D.X, aimAt.Y - pos2D.Y);
                aimedPerson = toAim.LengthSquared() > 0.01f;
            }

            // ⭐ CE-3252 (LD-C, R-2049) — a STANDING person with a look and no aim turns his body to it (the W-14 lag — a lasting look
            //   ends with the body facing it; the head leads, LookFollowSystem). ⛔ Walking he keeps his way: the head only glances.
            //   The aim wins by construction — this is only reached when he does not aim.
            bool lookingPerson = false;
            if (!aimedPerson && !down && @params.Class == VehicleClass.Pedestrian && targetSpeed <= 0.01f
                && repo.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Look.LookState>()
                && repo.HasComponent<Fdp.Toolkit.Perception.Look.LookState>(entity))
            {
                ref readonly var look = ref repo.GetComponentRO<Fdp.Toolkit.Perception.Look.LookState>(entity);
                if (look.HasTarget != 0)
                {
                    toAim = new Vector2(look.Target.X - pos2D.X, look.Target.Y - pos2D.Y);
                    lookingPerson = toAim.LengthSquared() > 0.01f;
                }
            }

            // Calculate desired velocity
            Vector2 desiredVelocity = targetHeading * targetSpeed;
            
            // Apply collision avoidance
            // ⭐⭐ CE-3287 (R-3026) — avoidance bends a DRIVING vehicle's own motion round others; a vehicle with no wanted speed (parked,
            //   arrived, held, a wreck) has none, and is not pushed — people walk round it (a person's avoidance is untouched).
            //   ⭐ CE-2321 — nor is a man down: avoidance turned him to face away from whoever came close.
            Vector2 avoidanceVelocity = (@params.Class != VehicleClass.Pedestrian && targetSpeed <= 0f) || down
                ? desiredVelocity
                : ApplyCollisionAvoidance(desiredVelocity, pos2D, tf.Position.Z, aimedPerson ? movedBefore : fwd2D * state.Speed,
                                          spatialGrid, @params, repo, entity);
            
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
            Vector2 stepFrom = pos2D;
            float yawRate;
            Vector2 planarOut = default;
            if (person && (aimedPerson || lookingPerson))
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

            // ⭐ CE-2304 — an AI WALKER's step stays on the walkable mesh too (CE-3181's rule for an operator's, ManualMotion.KeepOnNavmesh):
            //   his path is on the mesh, but avoidance pushes him off it, and with nothing to stop it, through a wall. 📌 Live in
            //   sq-defend-house a rifleman ended 1 m outside the house he was posted in. Only a step avoidance bent is checked — the
            //   path-following step stays on the path (CE-3115, CE-3145), and that keeps the navmesh queries off the open-ground walkers.
            Vector2? keptPlanar = null;
            if (person && dt > 0f && avoidanceVelocity != desiredVelocity
                && repo.HasSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>()
                && ManualMotion.KeepOnNavmesh(repo.GetSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>()!,
                    (uint)Fdp.Toolkit.Navigation.NavLayerSelection.For(repo, entity, 0), stepFrom, ref pos2D, tf.Position.Z))
                keptPlanar = (pos2D - stepFrom) / dt;

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
            
            bool held = FinishMove(repo, entity, ref state, ref nav, ref tf, ref vel, pos2D, fwd2D,
                keptPlanar ?? (aimedPerson ? (state.Speed > 0f ? planarOut : Vector2.Zero) : fwd2D * state.Speed), yawRate, person, dt,
                bentWalker: person && avoidanceVelocity != desiredVelocity, hull: @params);

            // ⭐⭐ CE-3286 — the wall held this hull: at its move's end it has ARRIVED (the nose met the wall); else, held HoldSeconds, it backs out
            if (recoverable)
            {
                var at = new Vector2(tf.Position.X, tf.Position.Y);
                var facing = FacingOf(tf.Rotation);
                if (!held)
                {
                    if (ft.WallHeldSeconds != 0f) { ft.WallHeldSeconds = 0f; repo.SetComponent(entity, ft); }
                    return;
                }
                bool forward = finalTargetSpeed >= 0f;
                var toEnd = HullClearance.EndPoint(_trajectoryPool, in nav, nav.Mode == KinematicsMode.CustomTrajectory) - at;
                float tol = nav.ArrivalRadius > 0f ? nav.ArrivalRadius : DefaultTrajectoryArrivalRadius;
                if (forward && toEnd.Length() <= @params.Length * 0.5f + tol && Vector2.Dot(facing, toEnd) > 0f)
                {
                    nav.HasArrived = 1;   // a point beside a wall is reached when the nose meets the wall
                    repo.SetComponent(entity, nav);
                    ft.WallHeldSeconds = 0f;
                }
                else
                {
                    ft.WallHeldSeconds += dt;
                    if (ft.WallHeldSeconds >= HullRecovery.HoldSeconds && ft.Recoveries < HullRecovery.MaxRecoveries)
                        HullRecovery.Begin(ref ft, (sbyte)(forward ? -1 : 1), facing, AimOf(in nav, in @params, at, state.Speed) - at);
                }
                repo.SetComponent(entity, ft);
            }
        }

        /// <summary>⭐ CE-3286 — the point the follower steers at from <paramref name="at"/> (the path's lookahead, else the destination).</summary>
        private Vector2 AimOf(in NavState nav, in VehicleParams p, Vector2 at, float speed)
            => HullClearance.AimPoint(_trajectoryPool, in nav, in p,
                nav.Mode == KinematicsMode.CustomTrajectory && _trajectoryPool.TryGetTrajectory(nav.TrajectoryId, out _), nav.ProgressS,
                MathF.Max(MathF.Abs(speed), nav.TargetSpeed), at);

        /// <summary>
        /// ⭐⭐ <c>CE-3286</c> (R-3025; Motion Domains §5f) — one tick of a recovery try: drive against the held motion at
        /// <see cref="HullRecovery.SpeedMps"/> on full lock (the bicycle model, guarded like any step); every
        /// <see cref="HullRecovery.CheckEveryM"/> ask <see cref="HullClearance.WayOnClear"/> — clear ends the try and the path is driven on. Held
        /// (blocked behind) or <see cref="HullRecovery.MaxHullLengths"/> driven also ends it; the next try steers the other way.
        /// </summary>
        private void DriveRecovery(EntityRepository repo, Entity entity, ref VehicleState state, ref NavState nav, ref SimTransform tf,
            ref SimVelocity vel, in VehicleParams p, ref FrustrationTicks ft, Vector2 pos2D, Vector2 fwd2D, float dt)
        {
            float target = HullRecovery.Speed(in p, ft.RecoverDir);
            float accel = SpeedController.CalculateAcceleration(state.Speed, target, p.AccelGain, p.MaxAccel, p.MaxDecel);
            float steer = ft.RecoverSteer * p.MaxSteerAngle;
            var before = pos2D;
            BicycleModel.Integrate(ref pos2D, ref fwd2D, ref state, steer, accel, dt, p.WheelBase,
                ft.RecoverDir < 0 ? -target : 0f, p.MaxSpeedFwd > 0f ? p.MaxSpeedFwd : float.MaxValue);
            float yawRate = (state.Speed / p.WheelBase) * MathF.Tan(steer);
            bool held = FinishMove(repo, entity, ref state, ref nav, ref tf, ref vel, pos2D, fwd2D, fwd2D * state.Speed, yawRate, false, dt, hull: p);
            var at = new Vector2(tf.Position.X, tf.Position.Y);
            ft.RecoverMetres += Vector2.Distance(before, at);
            if (held || ft.RecoverMetres >= HullRecovery.MaxHullLengths * p.Length)
                HullRecovery.End(ref ft);
            else if (ft.RecoverMetres - ft.RecoverCheckedMetres >= HullRecovery.CheckEveryM)
            {
                ft.RecoverCheckedMetres = ft.RecoverMetres;
                if (HullClearance.WayOnClear(_terrain!, _trajectoryPool, in nav, in p, at, FacingOf(tf.Rotation), tf.Position.Z))
                    HullRecovery.End(ref ft);
            }
            repo.SetComponent(entity, ft);
        }

        /// <summary>
        /// The output of a step, shared by the autonomous and the ⭐ CE-3173 manual integrators: the surface under the body, the
        /// ledge rule, the pose and the velocity (<paramref name="planar"/> — a manual person's carries his strafe). Returns whether a WALL
        /// held the step (⭐ CE-3286 — the recovery's trigger); <paramref name="hull"/> is the vehicle's, for its corners.
        /// </summary>
        private bool FinishMove(EntityRepository repo, Entity entity, ref VehicleState state, ref NavState nav,
            ref SimTransform tf, ref SimVelocity vel, Vector2 pos2D, Vector2 fwd2D, Vector2 planar, float yawRate, bool person, float dt,
            bool bentWalker = false, in VehicleParams hull = default)
        {
            bool held = false;
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
            // ⭐ CE-2198 — A VEHICLE NEVER ENTERS A BUILDING: a step whose hull-height line crosses a CLOSED BARRIER (a building's wall, a
            //   closed door — R-252's meaning, never a geometry type), or that rises like a wall (steeper than VehicleMaxClimbSlope and
            //   taller than VehicleMaxStepM — inside a solid footprint the sampler offers only the roof), is not taken: it stops there.
            // ⭐ CE-2304 (N3) — and A PERSON IS NEVER PUSHED THROUGH ONE: a walker's step that avoidance bent is traced against closed
            //   barriers the same way (only the barrier test — a stair rises like a wall). The navmesh rule above it cannot hold him
            //   alone: the mesh stops a body radius short of every wall, and a man pushed into that margin is "off the mesh" and free.
            //   📌 Live in sq-defend-house a rifleman with no move at all was shoved 1.3 m out through the south wall at 3 m/s.
            // ⭐⭐ CE-3286 (R-3025; Motion Domains §5f) — and the VEHICLE is its HULL, not its centre: the step is traced for the four corners
            //   and the centre (HullClearance.StepBlocked; a corner already inside may move out), and a refused step keeps the HEADING too.
            //   📌 Live: the centre-only trace let the front half of a tank and of a technical stand inside a building, and the heading crept
            //   ~1°/s while held, sweeping corners in.
            if ((!person || bentWalker) && _terrain != null)
            {
                var here = new Vector2(tf.Position.X, tf.Position.Y);
                float rise = z - tf.Position.Z;
                bool wall = !person && rise > VehicleMaxStepM && rise > VehicleMaxClimbSlope * Vector2.Distance(here, pos2D);
                Vector2 hereFwd = fwd2D;
                if (!wall && person && Vector2.DistanceSquared(here, pos2D) > 1e-8f)
                {
                    var crossings = t_crossings ??= new System.Collections.Generic.List<Fdp.Toolkit.World.TraceCrossing>();
                    _terrain.Trace(new Vector3(here, tf.Position.Z + HullProbeM), new Vector3(pos2D, z + HullProbeM),
                                   Fdp.Toolkit.World.TracePurpose.Fire, crossings);
                    foreach (var c in crossings) if (c.ClosedBarrier) { wall = true; break; }
                }
                else if (!wall && !person)
                {
                    hereFwd = FacingOf(tf.Rotation);
                    bool sized = HullClearance.HasHull(in hull);
                    wall = HullClearance.StepBlocked(_terrain, here, hereFwd, tf.Position.Z, pos2D, fwd2D, z,
                                                     sized ? hull.Length : 0f, sized ? hull.Width : 0f);
                }
                if (wall)
                {
                    pos2D = here;
                    z = tf.Position.Z;
                    state.Speed = 0f;
                    planar = Vector2.Zero;
                    if (!person) { fwd2D = hereFwd; yawRate = 0f; }
                    held = true;
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
            return held;
        }

        /// <summary>The 2-D facing of a rotation (X-forward).</summary>
        private static Vector2 FacingOf(Quaternion rotation)
        {
            var f3 = Vector3.Transform(Vector3.UnitX, rotation);
            var f = new Vector2(f3.X, f3.Y);
            return f.LengthSquared() < 0.0001f ? Vector2.UnitX : Vector2.Normalize(f);
        }

        private (Vector2 pos, Vector2 heading, float speed) SampleCustomTrajectory(ref NavState nav, in VehicleParams @params, Vector2 pos2D,
                                                                               Vector2 fwd2D, float speedNow)
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
                    // ⭐⭐ CE-2198 — homing is for a vehicle that has NOT arrived yet (progress ran ahead). 🔴 Once arrived it rolled past the
                    //   radius on its lag, homing re-engaged on a point now BEHIND it, at a speed capped only by the distance — which it
                    //   could not turn to, so the distance grew and it drove off at full speed (measured: 37 m sent, 440 m driven).
                    if (toEnd > tol && nav.HasArrived == 0)
                    {
                        Vector2 dir   = (endXY - pos2D) / toEnd;
                        float homing  = nav.TargetSpeed > 0f ? nav.TargetSpeed : MathF.Max(endWp.DesiredSpeed, TrajectoryEndCrawlSpeed);
                        homing = MathF.Min(homing, MathF.Max(StoppingSpeed(in @params, toEnd - tol, speedNow), TrajectoryEndCrawlSpeed));
                        // ⭐ CE-2198 — the end BEHIND the hull: creep while it turns, never speed away from it
                        if (Vector2.Dot(dir, fwd2D) < 0f) homing = MathF.Min(homing, TrajectoryEndCrawlSpeed);
                        // ⭐ CE-2206 — the end inside the hull's turning circle: drive on straight until it is not, then turn to it
                        if (@params.Class != VehicleClass.Pedestrian && InsideTurningCircle(in @params, pos2D, fwd2D, endXY))
                            return (pos2D + fwd2D, fwd2D, MathF.Min(nav.TargetSpeed > 0f ? nav.TargetSpeed : TurnClearanceSpeed, TurnClearanceSpeed));
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
                float approach  = MathF.Max(StoppingSpeed(in @params, remaining, speedNow), TrajectoryEndCrawlSpeed);   // ⭐ CE-2198 — less the lag
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

        /// <summary>⭐ <c>CE-2206</c> — how fast a hull drives straight on to bring a point out of its turning circle (m/s).</summary>
        internal const float TurnClearanceSpeed = 2f;

        /// <summary>
        /// ⭐⭐ <c>CE-2206</c> (docs/designs/motion-models/DESIGN_Motion_Domains.md §5c) — is <paramref name="point"/> inside either of the hull's
        /// smallest turning circles (radius WheelBase / sin MaxSteerAngle, the bicycle model's)? No forward arc reaches such a point: pure
        /// pursuit at full lock circles it for ever (📐 live ua-javelin-lock: an M1 sent 2.3 m to a cover point orbited it 60 s). Driving on
        /// straight moves the circles off it; once it is outside both, one arc of radius ≥ the smallest reaches it.
        /// </summary>
        internal static bool InsideTurningCircle(in VehicleParams p, Vector2 pos, Vector2 fwd, Vector2 point)
        {
            if (p.WheelBase <= 0f || p.MaxSteerAngle <= 0f) return false;
            float r = p.WheelBase / MathF.Sin(MathF.Min(p.MaxSteerAngle, 1.5f));
            var left = new Vector2(-fwd.Y, fwd.X) * r;
            return Vector2.DistanceSquared(point, pos + left) < r * r || Vector2.DistanceSquared(point, pos - left) < r * r;
        }

        /// <summary>
        /// ⭐ <c>CE-2198</c> — the speed that still stops <paramref name="distance"/> ahead: the braking envelope sqrt(2·MaxDecel·d), with
        /// the speed controller's LAG taken off the distance first — a proportional controller (<c>AccelGain</c>) follows a braking ramp
        /// late, so the vehicle carries ~v / gain metres more than the envelope promises. 🔴 Without it a tank sent 37 m at 20 m/s met its
        /// end point at 5.4 m/s and rolled past the 2 m arrival radius. MaxDecel ≤ 0 (an unknown profile, CE-103) caps nothing.
        /// </summary>
        internal static float StoppingSpeed(in VehicleParams p, float distance, float currentSpeed)
        {
            if (p.MaxDecel <= 0f) return float.MaxValue;
            float lag = p.AccelGain > 0f ? MathF.Abs(currentSpeed) / p.AccelGain : 0f;
            return MathF.Sqrt(2f * p.MaxDecel * MathF.Max(0f, distance - lag));
        }

        /// <summary>⭐ <c>CE-2198</c> — a ground vehicle never climbs a WALL: a step up of more than this (m), or steeper than
        /// <see cref="VehicleMaxClimbSlope"/>, is not taken (it stops there). 🔴 Inside a building's footprint the surface sampler offers
        /// only the roof (the footprint is solid), and a tank that overshot into a building was lifted onto its 15 m roof.</summary>
        public const float VehicleMaxStepM = 0.5f, VehicleMaxClimbSlope = 1.0f;

        /// <summary>⭐ <c>CE-2198</c> — the height above its wheels a vehicle's step is traced at (m): below a window's sill, so a wall with a
        /// window is still a wall.</summary>
        public const float HullProbeM = 0.5f;

        /// <summary>⭐ CE-3286 — one list PER THREAD: the update runs ForEachParallel (the single shared list since CE-2198 was a latent race).</summary>
        [ThreadStatic] private static System.Collections.Generic.List<Fdp.Toolkit.World.TraceCrossing>? t_crossings;

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
        
        /// <summary>
        /// ⭐ <c>CE-2304</c> — a neighbour more than this far above or below is on another storey (or under a deck) and is not
        /// avoided: the spatial grid is 2-D, so before this a man upstairs was pushed by the men in the room below him. 📌 Live in
        /// <c>sq-defend-house</c> (seven men posted on two storeys of one house) four of seven never reached their posts.
        /// A stair step is well inside it, so two men on one stair still give way.
        /// </summary>
        public const float AvoidanceStoreyGapM = 1.5f;

        /// <summary>
        /// ⭐ AQ99 C (R-2065) — no way round a man down at <paramref name="down"/>: on NEITHER side of him, across the walker's way, does
        /// the walkable mesh hold the strip a walker needs to pass him (<paramref name="clear"/> — his own hard shell — and half of it). A
        /// host without a navmesh answers false (there is always room in the open), so R-3027's walk-round stands there.
        /// </summary>
        private static bool NoWayRound(EntityRepository repo, Entity self, Vector2 down, Vector2 selfPos, float z, Vector2 preferredVel, float clear)
        {
            if (!repo.HasSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>()) return false;
            var mesh = repo.GetSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>()!;
            uint layer = (uint)Fdp.Toolkit.Navigation.NavLayerSelection.For(repo, self, 0);
            Vector2 way = preferredVel.LengthSquared() > 1e-6f ? preferredVel : down - selfPos;
            if (way.LengthSquared() < 1e-6f) return false;
            way = Vector2.Normalize(way);
            var side = new Vector2(-way.Y, way.X);
            for (int s = -1; s <= 1; s += 2)
                if (ManualMotion.IsOnMesh(mesh, layer, down + side * (s * clear * 0.5f), z) && ManualMotion.IsOnMesh(mesh, layer, down + side * (s * clear), z))
                    return false;
            return true;
        }

        // THREAD-SAFE: Read-only access to neighbors, writes only to local stack vars
        private Vector2 ApplyCollisionAvoidance(Vector2 preferredVel, Vector2 selfPos, float selfZ,
            Vector2 selfVel, SpatialHashGrid spatialGrid, VehicleParams @params, EntityRepository repo, Entity self)
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
                // ⭐ CE-2304 — another storey is not a neighbour
                if (!neighborEntity.IsNull && repo.HasComponent<SimTransform>(neighborEntity)
                    && MathF.Abs(repo.GetComponentRO<SimTransform>(neighborEntity).Position.Z - selfZ) > AvoidanceStoreyGapM) continue;
                // ⭐⭐ AQ99 C (R-2065; Motion Domains §5h) — a MAN DOWN is walked round where there is room (R-3027) and STEPPED OVER
                //   where there is not: a walker skips him as a neighbour when the walkable mesh beside him holds no way past on either
                //   side (a doorway, a narrow corridor). 📌 bc-room-clear: one body 0.9 m inside room A's door walled it off for every man
                //   behind him. With no navmesh the walker always walks round, as before.
                if (@params.Class == VehicleClass.Pedestrian && _health && !neighborEntity.IsNull
                    && Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.IsPerson(repo, neighborEntity)
                    && !Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, neighborEntity)
                    && NoWayRound(repo, self, pos, selfPos, selfZ, preferredVel, @params.AvoidanceRadius * 2f)) continue;
                
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
