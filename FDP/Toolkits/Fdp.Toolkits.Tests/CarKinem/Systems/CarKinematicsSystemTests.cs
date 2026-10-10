using System;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using Xunit;

namespace CarKinem.Tests.Systems
{
    public class CarKinematicsSystemTests
    {
        /// <summary>
        /// W8 (docs/DESIGN_Terrain_World.md §7.1, R-182) — the movement model sets the vehicle's Z from the
        /// terrain world as it computes the position: driving north up a ramp (z 0→3 over y 0→10) raises it,
        /// with no separate ground-clamp step.
        /// </summary>
        [Fact]
        public void Vehicle_OnARamp_TakesItsZFromTheTerrainWorld_W8()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
                {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"ramp"},
                 "geometry":{"type":"Polygon","coordinates":[[[0,0,0],[20,0,0],[20,10,3],[0,10,3],[0,0,0]]]}}]}
                """));

            var spatialSystem = new SpatialHashSystem();
            var kinematicsSystem = new CarKinematicsSystem(new TrajectoryPoolManager());

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new VehicleState { Speed = 10f });
            // ⭐ starts on the flat at the ramp FOOT and drives up it — each frame's rise is far inside the
            // 0.6 m step reach, so the surface tracks the ramp (a vehicle placed mid-ramp at Z=0 is below it).
            repo.AddComponent(entity, new SimTransform { Position = new Vector3(10, 0.5f, 0.15f), Rotation = SimMath.FacingNorth });
            repo.SetAuthority<SimTransform>(entity, true);
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(0, 10, 0) });
            repo.AddComponent(entity, new VehicleParams
            {
                WheelBase = 2.7f, MaxSpeedFwd = 30f, MaxAccel = 3f, MaxDecel = 6f, MaxSteerAngle = 0.6f,
                LookaheadTimeMin = 2f, LookaheadTimeMax = 10f, AccelGain = 2.0f, AvoidanceRadius = 2.5f,
            });
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None });

            for (int i = 0; i < 30; i++)
            {
                spatialSystem.Execute(repo, 0.016f);
                kinematicsSystem.Execute(repo, 0.016f);
            }

            var pos = repo.GetComponent<SimTransform>(entity).Position;
            Assert.True(pos.Y > 4f && pos.Y < 10f, $"vehicle should be mid-ramp, Y={pos.Y}");
            Assert.InRange(pos.Z, 3f * pos.Y / 10f - 0.05f, 3f * pos.Y / 10f + 0.05f);   // on the ramp surface
            Assert.True(repo.GetComponent<SimVelocity>(entity).Linear.Z > 0f, "climbing ⇒ positive vertical velocity");
        }

        [Fact]
        public void System_UpdatesVehiclePosition()
        {
            // Setup
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            // Also need SimTransform and SimVelocity for CarKinematics
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            
            // Register GlobalTime for DeltaTime
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            var roadNetwork = new RoadNetworkBuilder().Build(5f, 40, 40);
            var trajectoryPool = new TrajectoryPoolManager();
            
            var spatialSystem = new SpatialHashSystem();
            var kinematicsSystem = new CarKinematicsSystem(trajectoryPool);
            
            // Create vehicle
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new VehicleState
            {
                Speed = 10f
            });
            // SimTransform: Position (0,0), Forward East (1,0) -> Rotation -PI/2? No, let's use Rotation Identity (North/Y)
            // If VehicleState Forward was (1,0) (East), we should use SimTransform appropriately.
            // Let's test with Forward=North (0,1) -> Yaw=PI/2 -> rotation=PI/2.
            // Or simpler: Yaw=0 -> North (0,1).
            // Let's assume North for simplicity (0,1,0).
            // Yaw=0 -> Rotation Identity -> North? 
            // Wait, previous investigation suggested Yaw=0 -> North.
            repo.AddComponent(entity, new SimTransform { 
                Position = Vector3.Zero, 
                Rotation = SimMath.FacingNorth
            });
            repo.SetAuthority<SimTransform>(entity, true); // mark as locally-owned so WithOwned filter passes
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(0, 10, 0) }); // North at 10 m/s
            
            repo.AddComponent(entity, new VehicleParams
            {
                WheelBase = 2.7f,
                MaxSpeedFwd = 30f,
                MaxAccel = 3f,
                MaxDecel = 6f,
                MaxSteerAngle = 0.6f,
                LookaheadTimeMin = 2f,
                LookaheadTimeMax = 10f,
                AccelGain = 2.0f,
                AvoidanceRadius = 2.5f
            });
            
            repo.AddComponent(entity, new NavState
            {
                Mode = KinematicsMode.None
            });
            
            Vector3 initialPos = repo.GetComponent<SimTransform>(entity).Position;
            
            // Update systems
            spatialSystem.Execute(repo, 0.016f);
            kinematicsSystem.Execute(repo, 0.016f);
            
            // Verify singleton exists
            Assert.True(repo.HasSingleton<SpatialGridData>());
            
            Vector3 finalPos = repo.GetComponent<SimTransform>(entity).Position;
            
            // Vehicle should have moved (speed = 10 m/s, dt = 0.016 -> 0.16m move)
            // Moving North (Y+)
            Assert.NotEqual(initialPos, finalPos);
            Assert.True(finalPos.Y > initialPos.Y, "Should move North (Positive Y)");
            Assert.Equal(0.16f, finalPos.Y, precision: 2);
            
            // Cleanup
            roadNetwork.Dispose();
            trajectoryPool.Dispose();
            repo.Dispose();
        }

        [Fact]
        public void System_AvoidanceMovesVehicle()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            // Register Sim components
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();

            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.1f, TimeScale = 1.0f });

            var roadNetwork = new RoadNetworkBuilder().Build(5f, 40, 40);
            var trajectoryPool = new TrajectoryPoolManager();
            var spatialSystem = new SpatialHashSystem();
            var kinematicsSystem = new CarKinematicsSystem(trajectoryPool);

            // Create Entity A moving East at (0,0) with Speed 5.
            // East -> Yaw=-PI/2? Or X-Forward?
            // If SimTransform is Y-Forward, to face East (+X), we need -90 deg rotation.
            // Yaw = -PI/2.
            var entA = repo.CreateEntity();
            repo.AddComponent(entA, new VehicleState { Speed = 5f });
            repo.AddComponent(entA, new SimTransform { 
                Position = Vector3.Zero,
                Rotation = SimMath.FacingEast
            });
            repo.AddComponent(entA, new SimVelocity { Linear = new Vector3(5, 0, 0) });

            repo.AddComponent(entA, new NavState { Mode = KinematicsMode.None }); // Move straight
            repo.AddComponent(entA, new VehicleParams { 
                WheelBase = 2.0f, MaxSpeedFwd=10f, MaxAccel=10f, MaxDecel=10f, MaxSteerAngle=1f, 
                LookaheadTimeMin=1f, LookaheadTimeMax=2f, AccelGain=1f, AvoidanceRadius=2.0f
            });
            repo.SetAuthority<SimTransform>(entA, true); // mark as locally-owned so WithOwned filter passes

            // Create Entity B at (2, 0) stationary (Blocking path)
            var entB = repo.CreateEntity();
            repo.AddComponent(entB, new VehicleState { Speed = 0f });
            repo.AddComponent(entB, new SimTransform { 
                Position = new Vector3(2, 0, 0),
                Rotation = SimMath.FacingEast
            });
            repo.AddComponent(entB, new SimVelocity { Linear = Vector3.Zero });

            repo.AddComponent(entB, new NavState { Mode = KinematicsMode.None });
            repo.AddComponent(entB, new VehicleParams { AvoidanceRadius=2.0f });

            Vector3 before = repo.GetComponent<SimTransform>(entA).Position;

            // Run update
            spatialSystem.Execute(repo, 0.1f);
            kinematicsSystem.Execute(repo, 0.1f); // A should steer or decelerate/avoid

            Vector3 after = repo.GetComponent<SimTransform>(entA).Position;

            Assert.True(Vector3.Distance(before, after) > 0.01f,
                $"Vehicle did not move. before={before}, after={after}");

            roadNetwork.Dispose();
            trajectoryPool.Dispose();
            repo.Dispose();
        }

        [Fact]
        public void System_FollowsTrajectory()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            // Register Sim components
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();

            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.1f, TimeScale = 1.0f });

            var roadNetwork = new RoadNetworkBuilder().Build(5f, 40, 40);
            var trajectoryPool = new TrajectoryPoolManager();
            // Create a simple trajectory: (0,0) to (100,0) (East)
            int trajId = trajectoryPool.RegisterTrajectory(new[] { new Vector2(0,0), new Vector2(100,0) });

            var spatialSystem = new SpatialHashSystem();
            var kinematicsSystem = new CarKinematicsSystem(trajectoryPool);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new VehicleState { Speed = 10f });
            // Start at (0,0) facing East (-PI/2)
            repo.AddComponent(entity, new SimTransform { 
                Position = Vector3.Zero,
                Rotation = SimMath.FacingEast
            });
            repo.SetAuthority<SimTransform>(entity, true); // mark as locally-owned so WithOwned filter passes
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(10, 0, 0) });
            
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.CustomTrajectory, TrajectoryId = trajId, ProgressS = 0f });
            repo.AddComponent(entity, new VehicleParams { 
                WheelBase = 2.0f, MaxSpeedFwd=20f, MaxAccel=10f, MaxDecel=10f, MaxSteerAngle=1f, 
                LookaheadTimeMin=1f, LookaheadTimeMax=2f, AccelGain=1f, AvoidanceRadius=2.0f 
            });

            // Update
            spatialSystem.Execute(repo, 0.1f); // Build grid
            kinematicsSystem.Execute(repo, 0.1f);

            // Check ProgressS increased
            var nav = repo.GetComponent<NavState>(entity);
            // Expected progress: 10m/s * 0.1s = 1.0m (approx, assuming constant speed)
            Assert.True(nav.ProgressS > 0.5f, "Progress should advance");

            // Cleanup
            roadNetwork.Dispose();
            trajectoryPool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// CE-167 / the overshoot the user observed in the editor: a vehicle driving to a
        /// point must brake on the APPROACH, not only once it is already inside
        /// <c>ArrivalRadius</c>.
        ///
        /// <para>
        /// Before the approach-braking cap, <c>targetSpeed</c> was a step function of
        /// distance — full <c>NavState.TargetSpeed</c> right up to the radius, then 0 — so a
        /// tank at 15 m/s with <c>MaxDecel</c> 4 m/s² needed v²/2a = 28 m to stop with 5 m
        /// left, and coasted ~20 m past the destination. That is what made the cluster
        /// report <c>NavigationStatus.Arrived</c> at 20–21 m with an <c>ArrivalRadius</c>
        /// of 5: a REAL arrival followed by an overshoot, never a false one.
        /// </para>
        ///
        /// <para>
        /// Design basis: <c>.dev/_DONE/demos-1/FDP-demos-all.md:605/636</c> —
        /// "braking friction correctly halts the vehicle exactly at the destination
        /// coordinate without ... overshooting".
        /// </para>
        /// </summary>
        [Fact]
        public void VehicleBrakesOnApproachAndStopsNearTheDestination_WithoutOvershooting()
        {
            const float ArrivalRadius = 5f;
            const float MaxDecel      = 4f;
            const float CruiseSpeed   = 15f;
            const float Dt            = 1f / 60f;

            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = Dt, TimeScale = 1.0f });

            var roadNetwork     = new RoadNetworkBuilder().Build(5f, 40, 40);
            var trajectoryPool  = new TrajectoryPoolManager();
            var spatialSystem   = new SpatialHashSystem();
            var kinematicsSystem = new CarKinematicsSystem(trajectoryPool);

            // Tank at the origin already at cruise speed, facing the destination (North).
            var destination = new Vector3(0f, 200f, 0f);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new VehicleState { Speed = CruiseSpeed });
            repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = SimMath.FacingNorth
            });
            repo.SetAuthority<SimTransform>(entity, true);
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(0, CruiseSpeed, 0) });
            repo.AddComponent(entity, new VehicleParams
            {
                // The Tank preset's shape: enough decel to stop on a point if it is commanded early.
                WheelBase        = 4.758f,
                MaxSpeedFwd      = 20f,
                MaxAccel         = 2.5f,
                MaxDecel         = MaxDecel,
                MaxSteerAngle    = 0.8f,
                MaxLatAccel      = 6f,
                LookaheadTimeMin = 0.8f,
                LookaheadTimeMax = 2.5f,
                AccelGain        = 1.8f,
                AvoidanceRadius  = 2.5f
            });
            repo.AddComponent(entity, new NavState
            {
                Mode             = KinematicsMode.Direct,
                FinalDestination = destination,
                ArrivalRadius    = ArrivalRadius,
                TargetSpeed      = CruiseSpeed
            });

            // Drive for 40 simulated seconds — far more than the ~15 s the 200 m leg needs.
            float closestApproach = float.MaxValue;
            for (int i = 0; i < (int)(40f / Dt); i++)
            {
                spatialSystem.Execute(repo, Dt);
                kinematicsSystem.Execute(repo, Dt);

                var p = repo.GetComponent<SimTransform>(entity).Position;
                closestApproach = MathF.Min(closestApproach, Vector3.Distance(p, destination));
            }

            var finalPos   = repo.GetComponent<SimTransform>(entity).Position;
            var finalNav   = repo.GetComponent<NavState>(entity);
            var finalState = repo.GetComponent<VehicleState>(entity);
            float finalDistance = Vector3.Distance(finalPos, destination);

            // It must actually get there — a cap that simply freezes the vehicle also
            // "does not overshoot", so pin the arrival first.
            Assert.Equal(1, finalNav.HasArrived);
            Assert.True(closestApproach <= ArrivalRadius,
                $"Vehicle must reach the destination. Closest approach was {closestApproach:F1} m " +
                $"against an ArrivalRadius of {ArrivalRadius} m.");

            // And it must come to rest, not orbit.
            Assert.True(MathF.Abs(finalState.Speed) < 0.5f,
                $"Vehicle must be stopped after arriving; speed was {finalState.Speed:F2} m/s.");

            // The defect: it used to settle ~20 m out. Without approach braking a vehicle
            // that latches HasArrived at 5 m while doing 15 m/s coasts v²/2a = 28 m further.
            Assert.True(finalDistance <= ArrivalRadius,
                $"Vehicle must come to rest INSIDE the arrival radius, not coast past it. " +
                $"Final distance {finalDistance:F1} m against an ArrivalRadius of {ArrivalRadius} m " +
                $"(pos {finalPos}, destination {destination}). A distance near 20 m is the " +
                $"pre-fix step-function behaviour: full cruise speed until the radius, then brake.");

            roadNetwork.Dispose();
            trajectoryPool.Dispose();
            repo.Dispose();
        }
    
        // ── CE-2059 / CE-2060: a planned path is driven at the requested speed and stops at its end ──────────────

        private static (EntityRepository repo, Entity e, TrajectoryPoolManager pool) TrajectoryWorld(float targetSpeed, float startSpeed, Quaternion? facing = null,
            float startY = 0f, VehicleParams? vehicle = null)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 1f / 60f, TimeScale = 1.0f });

            var pool = new TrajectoryPoolManager();
            // ⭐ The solver's registration: a flat 10 m/s per waypoint (TrajectoryPoolManager.RegisterTrajectoryWithKey).
            pool.RegisterTrajectoryWithKey(new[] { new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f) }, 7);

            var e = repo.CreateEntity();
            repo.AddComponent(e, new VehicleState { Speed = startSpeed });
            repo.AddComponent(e, new SimTransform { Position = new Vector3(0f, startY, 0f), Rotation = facing ?? SimMath.FacingEast });
            repo.SetAuthority<SimTransform>(e, true);
            repo.AddComponent(e, new SimVelocity { Linear = new Vector3(startSpeed, 0, 0) });
            repo.AddComponent(e, vehicle ?? new VehicleParams
            {
                WheelBase = 4.758f, MaxSpeedFwd = 20f, MaxAccel = 2.5f, MaxDecel = 4f, MaxSteerAngle = 0.8f,
                MaxLatAccel = 6f, LookaheadTimeMin = 0.8f, LookaheadTimeMax = 2.5f, AccelGain = 1.8f, AvoidanceRadius = 2.5f,
            });
            repo.AddComponent(e, new NavState
            {
                Mode = KinematicsMode.CustomTrajectory, TrajectoryId = 7, ProgressS = 0f,
                TargetSpeed = targetSpeed, ArrivalRadius = 5f, FinalDestination = new Vector3(100f, 0f, 0f),
            });
            return (repo, e, pool);
        }

        /// <summary>
        /// ⭐ <c>CE-2060</c> — a path is driven at the move's requested speed (<c>NavState.TargetSpeed</c>), not the solver's flat
        /// 10 m/s; 0 means uncapped. 🔴 Red before: measured live 10.0 m/s for a requested 5.
        /// </summary>
        [Theory]
        [InlineData(5f, 5.2f)]
        [InlineData(0f, 10.2f)]
        public void CE2060_APath_IsDrivenAtTheRequestedSpeed(float targetSpeed, float maxAllowed)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed, startSpeed: 0f);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float top = 0f;
            for (int i = 0; i < 6 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                top = MathF.Max(top, repo.GetComponent<VehicleState>(e).Speed);
            }
            Assert.InRange(top, maxAllowed - 1.5f, maxAllowed);
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-2059</c> — a vehicle on a one-shot path brakes on the approach and comes to rest within the arrival radius of
        /// the path's end — the same standard as Direct mode's rail above (the speed controller's lag is the residue).
        /// 🔴 Red before: it commanded 0 only at the end and rolled on (measured live 11 m past the end node).
        /// </summary>
        [Fact]
        public void CE2059_APath_BrakesToItsEnd_WithoutOvershooting()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 10f, startSpeed: 10f);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float farthest = 0f;
            for (int i = 0; i < 40 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                farthest = MathF.Max(farthest, repo.GetComponent<SimTransform>(e).Position.X);
            }
            var nav = repo.GetComponent<NavState>(e);
            Assert.Equal(1, nav.HasArrived);
            Assert.True(farthest <= 105f, $"overshot the path's end at x=100 to x={farthest:F1} (arrival radius 5 m)");
            Assert.True(repo.GetComponent<VehicleState>(e).Speed < 0.5f);
            pool.Dispose();
            repo.Dispose();
        }
    
        /// <summary>
        /// ⭐ <c>CE-3128</c> — a path of two COINCIDENT points (a move to where the unit already stands) arrives, and the unit's
        /// position stays finite. 🔴 Measured in the ua-danger-crossing-bp twin: the end-of-path heading normalised the zero last
        /// segment and the rifleman's position became NaN.
        /// </summary>
        [Fact]
        public void CE3128_APathOfTwoCoincidentPoints_Arrives_AndThePositionStaysFinite()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f);
            pool.RegisterTrajectoryWithKey(new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f) }, 7);
            var nav0 = repo.GetComponent<NavState>(e);
            nav0.FinalDestination = Vector3.Zero;
            repo.SetComponent(e, nav0);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            for (int i = 0; i < 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var p = repo.GetComponent<SimTransform>(e).Position;
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"position became {p} at tick {i}");
            }
            Assert.Equal(1, repo.GetComponent<NavState>(e).HasArrived);
            var (pos, tangent, _) = pool.SampleTrajectory(7, 1f);
            Assert.True(float.IsFinite(tangent.X) && float.IsFinite(tangent.Y) && float.IsFinite(pos.X));
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-2059</c> — a vehicle that must TURN AROUND to start its path still ends at the path's end. 🔴 Measured live
        /// (`--mode all`, the Return leg): progress is integrated from speed, so the U-turn counted as progress along the path
        /// and the vehicle "arrived" 23 m short of home. Arrival is now confirmed by POSITION: a vehicle whose progress says
        /// "end" but which is outside the arrival radius homes on the end point.
        /// </summary>
        [Fact]
        public void CE2059_AVehicleThatMustTurnAround_StillEndsAtThePathsEnd()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 0f, facing: SimMath.FromYaw(0.75f * MathF.PI));   // facing north-west, away from the path (as the live Return leg)
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            for (int i = 0; i < 60 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
            var p = repo.GetComponent<SimTransform>(e).Position;
            Assert.Equal(1, repo.GetComponent<NavState>(e).HasArrived);
            float d = Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(100f, 0f));
            Assert.True(d <= 5f, $"came to rest {d:F1} m from the path's end (100, 0) at ({p.X:F1}, {p.Y:F1}); arrival radius 5 m");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3145</c> (R-247) — A PERSON TURNS ON THE SPOT, then walks: a walker facing WEST, his path going EAST, turns where
        /// he stands (never more than 5 cm from it) in about half a second, then walks the path. 📐 The car model it replaces walked a
        /// half-circle forward first — on <c>bt-window-duel</c> that carried A 0.5 m east into House A's open stairwell. 🔴 Red-proof:
        /// route the walker through <c>BicycleModel</c> again and he swings ≈ 0.3 m west before coming round.
        /// </summary>
        [Fact]
        public void CE3145_APerson_TurnsOnTheSpot_ThenWalks()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, facing: SimMath.FromYaw(MathF.PI),
                vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float wander = 0f, turnedAt = -1f;
            for (int i = 0; i < 20 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var tf = repo.GetComponent<SimTransform>(e);
                var fwd = Vector3.Transform(Vector3.UnitX, tf.Rotation);
                if (turnedAt < 0f)
                {
                    wander = MathF.Max(wander, new Vector2(tf.Position.X, tf.Position.Y).Length());
                    if (fwd.X > MathF.Cos(MathF.PI / 6f)) turnedAt = (i + 1) / 60f;
                }
            }
            Assert.True(turnedAt > 0f && turnedAt <= 0.7f, $"turned round in {turnedAt:F2} s");
            Assert.True(wander <= 0.05f, $"moved {wander:F2} m while turning — a person turns where he stands");
            Assert.True(repo.GetComponent<SimTransform>(e).Position.X > 15f, "then walked the path");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3145</c> — A PERSON ON THE REAL HOUSE: from House A's upstairs window (107.1, 100.9, 3) along the route the navmesh
        /// plans to the ground-floor east window (109.1, 103.6, 0) — east past the stairwell's edge (5 cm away), north, down the stairs —
        /// he arrives downstairs and never drops more than a step (`HumanGait.MaxStepDown`) in one frame; and sent STRAIGHT across the stairwell he never falls
        /// (the ledge rule). 📐 Measured in-process before: the car-sized 1 m lookahead cut the corner over the hole and A fell 3 m.
        /// 🔴 Red-proof: restore the vehicle lookahead and drop the ledge rule — the route run falls at the corner (≈ 108.1, 101.0).
        /// </summary>
        [Theory]
        [InlineData("route")]
        [InlineData("across")]
        public void CE3145_APerson_DownTheStairs_NeverFallsThroughTheStairwell(string run)
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
            var folder = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
            var world = Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Directory.GetFiles(folder, "*.world.geojson")[0]),
                "bt-range", Fdp.Toolkit.Terrain.TerrainAssets.ForFolder(folder));

            // the navmesh's own route (measured: RecastNavmeshFactory over bt-range, Infantry), or a straight line over the hole
            var route = run == "route"
                ? new[] { new Vector3(107.10f, 100.90f, 3.00f), new Vector3(107.40f, 100.80f, 3.20f), new Vector3(108.75f, 100.95f, 3.20f),
                          new Vector3(108.75f, 105.45f, 3.20f), new Vector3(108.15f, 105.45f, 2.80f), new Vector3(108.15f, 104.10f, 2.00f),
                          new Vector3(108.30f, 101.85f, 0.60f), new Vector3(108.90f, 101.85f, 0.20f), new Vector3(109.10f, 103.60f, 0.00f) }
                : new[] { new Vector3(107.10f, 100.90f, 3.00f), new Vector3(108.00f, 103.50f, 3.00f) };
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 2f, startSpeed: 0f, facing: SimMath.FacingEast,
                vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            pool.RegisterTrajectoryWithKey(route, 8);
            ref var tf0 = ref repo.GetComponentRW<SimTransform>(e);
            tf0.Position = route[0];
            ref var nav0 = ref repo.GetComponentRW<NavState>(e);
            nav0.TrajectoryId = 8;
            nav0.FinalDestination = route[^1];
            nav0.ArrivalRadius = 0.5f;
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(world);

            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float worstDrop = 0f, lowest = float.MaxValue, lastZ = route[0].Z;
            string trace = "";
            for (int i = 0; i < 30 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                float z = repo.GetComponent<SimTransform>(e).Position.Z;
                worstDrop = MathF.Max(worstDrop, lastZ - z);
                lowest = MathF.Min(lowest, z);
                lastZ = z;
                if (i % 60 == 0)
                {
                    var n = repo.GetComponent<NavState>(e);
                    var st = repo.GetComponent<VehicleState>(e);
                    trace += $" t{i / 60}:({repo.GetComponent<SimTransform>(e).Position.X:F2},{repo.GetComponent<SimTransform>(e).Position.Y:F2},{z:F2}) v{st.Speed:F2} s{n.ProgressS:F1} arr{n.HasArrived}";
                }
            }
            var end = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(worstDrop <= CarKinem.Controllers.HumanGait.MaxStepDown, $"{run}: dropped {worstDrop:F2} m in one frame (ended at {end})");   // a step off the stair's side (0.5 m) is fine; a storey is not
            if (run == "route")
                Assert.True(Vector2.Distance(new Vector2(end.X, end.Y), new Vector2(109.1f, 103.6f)) <= 0.8f && end.Z < 0.5f,
                    $"walked down the stairs to the east window; ended at {end};{trace}");
            else
                Assert.True(lowest > 2.5f, $"stayed on the upper floor at the stairwell's edge; lowest z {lowest:F2}, ended at {end}");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>⭐ <c>CE-3145</c> — a NEGATIVE or zero step (📐 the cluster hands one: the in-process duel crashed the mover with
        /// <c>Math.Clamp(min &gt; max)</c>) turns nothing and never throws.</summary>
        [Theory]
        [InlineData(-1f / 60f)]
        [InlineData(0f)]
        public void CE3145_HumanGait_ANonPositiveStep_TurnsNothing(float dt)
        {
            var pos = Vector2.Zero;
            var fwd = Vector2.UnitX;
            var state = new VehicleState();
            float yaw = CarKinem.Controllers.HumanGait.Integrate(ref pos, ref fwd, ref state, -Vector2.UnitX, moving: true, accel: 0f, dt);
            Assert.Equal(Vector2.UnitX, fwd);
            Assert.Equal(0f, yaw);
        }

        /// <summary>
        /// ⭐ Buildings 5d-3 — <see cref="NavState.IsBlocked"/> ("obstacle ahead", designed in FDP.Toolkit.CarKinem.md and never read before):
        /// the mover brakes to a stop where it is, KEEPS its path and progress, and drives on along the same path when it clears.
        /// </summary>
        [Fact]
        public void Stage5d_IsBlocked_StopsOnThePath_KeepsIt_AndDrivesOnWhenCleared()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 2f, startSpeed: 0f);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            void Run(float seconds) { for (int i = 0; i < (int)(seconds * 60); i++) { spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f); } }

            Run(3f);
            float before = repo.GetComponent<NavState>(e).ProgressS;
            Assert.True(before > 2f, $"should have started along the path ({before:F2} m)");

            var nav = repo.GetComponent<NavState>(e); nav.IsBlocked = 1; repo.SetComponent(e, nav);
            Run(2f);
            float braked = repo.GetComponent<NavState>(e).ProgressS;
            Run(3f);
            var held = repo.GetComponent<NavState>(e);
            Assert.True(repo.GetComponent<VehicleState>(e).Speed < 0.05f, "a held agent stands still");
            Assert.Equal(braked, held.ProgressS, 2);                                    // no progress while held
            Assert.Equal(7, held.TrajectoryId);                                          // the path is kept
            Assert.Equal(KinematicsMode.CustomTrajectory, held.Mode);

            held.IsBlocked = 0; repo.SetComponent(e, held);
            Run(3f);
            Assert.True(repo.GetComponent<NavState>(e).ProgressS > braked + 2f, "drives on along the same path");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3115</c> — a mover OFF its path closes onto it (pure pursuit on the PATH, 📄 FDP.Toolkit.CarKinem.md). 🔴 Red
        /// before: it steered along the path's tangent, so it drove the whole path parallel to it at the starting offset — live on
        /// bt-doors a walker ran 1.4 m off, through House A's wall, and passed a closed door out of reach.
        /// </summary>
        [Theory]
        [InlineData("car", 3.0f)]
        [InlineData("walker", 1.5f)]
        public void CE3115_AMoverOffItsPath_ClosesOntoIt(string who, float offset)
        {
            var walker = who == "walker";
            var p = walker ? VehiclePresets.GetPreset(VehicleClass.Pedestrian) : (VehicleParams?)null;
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: walker ? 1.5f : 5f, startSpeed: 0f, startY: offset, vehicle: p);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float worstLate = 0f;
            for (int i = 0; i < 40 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var pos = repo.GetComponent<SimTransform>(e).Position;
                if (pos.X > (walker ? 10f : 40f) && pos.X < 90f) worstLate = MathF.Max(worstLate, MathF.Abs(pos.Y));
            }
            Assert.True(repo.GetComponent<SimTransform>(e).Position.X > (walker ? 30f : 60f), "it travelled along the path");
            Assert.True(worstLate < 0.3f, $"{who} still {worstLate:F2} m off its path after closing (started {offset} m off)");
            pool.Dispose();
            repo.Dispose();
        }
    }
}
