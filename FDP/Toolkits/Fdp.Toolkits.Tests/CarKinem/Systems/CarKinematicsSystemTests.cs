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

        private static (EntityRepository repo, Entity e, TrajectoryPoolManager pool) TrajectoryWorld(float targetSpeed, float startSpeed, Quaternion? facing = null)
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
            repo.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = facing ?? SimMath.FacingEast });
            repo.SetAuthority<SimTransform>(e, true);
            repo.AddComponent(e, new SimVelocity { Linear = new Vector3(startSpeed, 0, 0) });
            repo.AddComponent(e, new VehicleParams
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
    }
}
