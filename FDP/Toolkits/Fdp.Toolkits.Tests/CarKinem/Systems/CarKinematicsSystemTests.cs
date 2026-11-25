using System;
using System.Collections.Generic;
using System.Linq;
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
                WheelBase = 2.7f, MaxSpeedFwd = 30f, MaxAccel = 3f, MaxDecel = 6f, MaxSteerAngle = (Fdp.Core.Angle)(0.6f),
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

        // ── ⭐ CE-1034 H1 — the ground has height (R-248); the clamping flag decides who is held to it (R-249) ──

        private static (EntityRepository Repo, Entity E) OnTheSlope(Vector3 start, Vector3 velocity, bool registerClamp = false,
            Fdp.Toolkit.Terrain.TerrainWorld? world = null, Quaternion? facing = null)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            if (registerClamp) repo.RegisterComponent<Fdp.Modules.Geographic.Components.GroundClampingConfig>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(world ?? Fdp.Toolkit.Terrain.Tests.SlopeFixture.World());
            var e = repo.CreateEntity();
            repo.AddComponent(e, new VehicleState { Speed = velocity.Length() });
            repo.AddComponent(e, new SimTransform { Position = start, Rotation = facing ?? SimMath.FacingNorth });
            repo.SetAuthority<SimTransform>(e, true);
            repo.AddComponent(e, new SimVelocity { Linear = velocity });
            repo.AddComponent(e, new VehicleParams
            {
                WheelBase = 2.7f, MaxSpeedFwd = 30f, MaxAccel = 3f, MaxDecel = 6f, MaxSteerAngle = (Fdp.Core.Angle)(0.6f),
                LookaheadTimeMin = 2f, LookaheadTimeMax = 10f, AccelGain = 2.0f, AvoidanceRadius = 2.5f,
            });
            repo.AddComponent(e, new NavState { Mode = KinematicsMode.None });
            return (repo, e);
        }

        private static void Drive(EntityRepository repo, int frames)
        {
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            for (int i = 0; i < frames; i++) { spatial.Execute(repo, 0.016f); kin.Execute(repo, 0.016f); }
        }

        [Fact]
        public void H1_AVehicleDrivingUpAHill_TakesItsZFromTheHeightGrid()
        {
            var (repo, e) = OnTheSlope(new Vector3(100, 20, Fdp.Toolkit.Terrain.Tests.SlopeFixture.Rise * 20), new Vector3(0, 10, 0));
            Drive(repo, 60);
            var pos = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(pos.Y > 24f, $"the vehicle must have moved uphill, Y={pos.Y}");
            Assert.Equal(Fdp.Toolkit.Terrain.Tests.SlopeFixture.Rise * pos.Y, pos.Z, 2);   // on the hillside, not on a flat bed
            repo.Dispose();
        }

        // ── ⭐ CE-1034 H5d — on highway-rest: a car on the local road drives up the ramp and over the deck; one on the highway passes
        //   under it — both by the movement model's own SurfaceZ, no navmesh (TH-J) ──

        [Fact]
        public void H5d_ACarOnTheLocalRoad_ClimbsTheRampAndCrossesTheDeck_OverTheHighway()
        {
            var w = Fdp.Toolkit.Terrain.Tests.TerrainHeightTests.HighwayRest();
            var (repo, e) = OnTheSlope(new Vector3(200, 70, w.GroundHeightAt(200, 70)), new Vector3(0, 10, 0), world: w);
            repo.SetComponent(e, new NavState { Mode = KinematicsMode.Direct, FinalDestination = new Vector3(200, 230, 0), TargetSpeed = 10f, ArrivalRadius = 3f });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            float? zOverHighway = null;
            for (int i = 0; i < 1200 && repo.GetComponent<SimTransform>(e).Position.Y < 190f; i++)
            {
                spatial.Execute(repo, 0.016f); kin.Execute(repo, 0.016f);
                var p = repo.GetComponent<SimTransform>(e).Position;
                if (zOverHighway == null && p.Y >= 150f) zOverHighway = p.Z;
            }
            Assert.NotNull(zOverHighway);
            Assert.InRange(zOverHighway!.Value, w.GroundHeightAt(200, 150) + 5.6f, w.GroundHeightAt(200, 150) + 6.4f);   // on the deck
            repo.Dispose();
        }

        [Fact]
        public void H5d_ACarOnTheHighway_PassesUnderTheDeck_OnTheGround()
        {
            var w = Fdp.Toolkit.Terrain.Tests.TerrainHeightTests.HighwayRest();
            var east = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f);
            var (repo, e) = OnTheSlope(new Vector3(140, 150, w.GroundHeightAt(140, 150)), new Vector3(10, 0, 0), world: w, facing: east);
            repo.SetComponent(e, new NavState { Mode = KinematicsMode.Direct, FinalDestination = new Vector3(300, 150, 0), TargetSpeed = 10f, ArrivalRadius = 3f });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            float? zUnderDeck = null;
            for (int i = 0; i < 1200 && repo.GetComponent<SimTransform>(e).Position.X < 260f; i++)
            {
                spatial.Execute(repo, 0.016f); kin.Execute(repo, 0.016f);
                var p = repo.GetComponent<SimTransform>(e).Position;
                if (zUnderDeck == null && p.X >= 200f) zUnderDeck = p.Z;
            }
            Assert.NotNull(zUnderDeck);
            Assert.Equal(w.GroundHeightAt(200, 150), zUnderDeck!.Value, 1);   // under the bridge, on the highway
            repo.Dispose();
        }

        [Fact]
        public void H1_ClampingOff_KeepsTheAltitude_ClampingOnOrAbsent_Grounds_R249()
        {
            var (repo, e) = OnTheSlope(new Vector3(100, 50, 80f), new Vector3(0, 10, 0), registerClamp: true);
            repo.AddComponent(e, new Fdp.Modules.Geographic.Components.GroundClampingConfig { Mode = Fdp.Modules.Geographic.EClampingMode.ForceOff });
            Drive(repo, 30);
            Assert.Equal(80f, repo.GetComponent<SimTransform>(e).Position.Z, 3);   // an aircraft keeps its altitude
            repo.Dispose();

            var (repo2, e2) = OnTheSlope(new Vector3(100, 50, Fdp.Toolkit.Terrain.Tests.SlopeFixture.Rise * 50 + 0.1f), new Vector3(0, 10, 0), registerClamp: true);
            repo2.AddComponent(e2, new Fdp.Modules.Geographic.Components.GroundClampingConfig { Mode = Fdp.Modules.Geographic.EClampingMode.ForceOn });
            Drive(repo2, 30);
            var p2 = repo2.GetComponent<SimTransform>(e2).Position;
            Assert.Equal(Fdp.Toolkit.Terrain.Tests.SlopeFixture.Rise * p2.Y, p2.Z, 2);
            repo2.Dispose();
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
                MaxSteerAngle = (Fdp.Core.Angle)(0.6f),
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
                WheelBase = 2.0f, MaxSpeedFwd=10f, MaxAccel=10f, MaxDecel=10f, MaxSteerAngle=(Fdp.Core.Angle)(1f), 
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
                WheelBase = 2.0f, MaxSpeedFwd=20f, MaxAccel=10f, MaxDecel=10f, MaxSteerAngle=(Fdp.Core.Angle)(1f), 
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
                MaxSteerAngle    = (Fdp.Core.Angle)(0.8f),
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
                WheelBase = 4.758f, MaxSpeedFwd = 20f, MaxAccel = 2.5f, MaxDecel = 4f, MaxSteerAngle = (Fdp.Core.Angle)(0.8f),
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
        /// ⭐ <c>CE-4369</c> (L-7, R-4190 Q-T2) — a TRACKED hull in a wood crawls at ≈ 10 km/h; outside the wood it drives the path's
        /// 10 m/s, and a wheeled one is not capped (it never plans into a wood — the navmesh refuses it).
        /// </summary>
        [Theory]
        [InlineData(VehicleClass.Tank, true, 2.0f, 2.9f)]
        [InlineData(VehicleClass.Tank, false, 8.5f, 10.2f)]
        [InlineData(VehicleClass.Truck, true, 8.5f, 10.2f)]
        public void CE4369_ATrackedHull_CrawlsThroughAWood(VehicleClass cls, bool wood, float min, float max)
        {
            var (repo, e, pool) = TrajectoryWorld(0f, startSpeed: 0f, vehicle: new VehicleParams
            {
                Class = cls, WheelBase = 4.758f, MaxSpeedFwd = 20f, MaxAccel = 2.5f, MaxDecel = 4f, MaxSteerAngle = (Fdp.Core.Angle)(0.8f),
                MaxLatAccel = 6f, LookaheadTimeMin = 0.8f, LookaheadTimeMax = 2.5f, AccelGain = 1.8f, AvoidanceRadius = 2.5f,
            });
            string forest = wood ? """,{"type":"Feature","properties":{"kind":"surface","surface":"forest"},"geometry":{"type":"Polygon","coordinates":[[[-50,-50],[200,-50],[200,50],[-50,50],[-50,-50]]]}}""" : "";
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[-100,-100,300,100],"groundZ":0},"features":[{"type":"Feature","properties":{"kind":"surface","surface":"water"},"geometry":{"type":"Polygon","coordinates":[[[280,80],[290,80],[290,90],[280,90],[280,80]]]}}FOREST]}"""
                .Replace("FOREST", forest), "w"));
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float top = 0f;
            for (int i = 0; i < 6 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                top = MathF.Max(top, repo.GetComponent<VehicleState>(e).Speed);
            }
            Assert.InRange(top, min, max);
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4621</c> MC-2 (DESIGN_Motorcycle §2, M-1 acceptance) — a TYPE with authored off-road factors (the motorcycle: open 0.6 ·
        /// scrub 0.45 · forest 0.3) drives each ground class at that factor of its top speed (10 m/s here, the path's speed): 6 on open
        /// ground, 4.5 in scrub, 3 in a wood; the same type with NO factors (every type before CE-4621) is unchanged at 10.
        /// 🔴 Mutation: the factor block in <c>UpdateVehicle</c> removed ⇒ every factored row drives 10 ⇒ red.
        /// </summary>
        [Theory]
        [InlineData("open",   true,  5.6f, 6.1f)]
        [InlineData("scrub",  true,  4.1f, 4.6f)]
        [InlineData("forest", true,  2.7f, 3.1f)]
        [InlineData("open",   false, 9.4f, 10.2f)]
        [InlineData("forest", false, 9.4f, 10.2f)]
        public void CE4621_AFactoredType_DrivesEachGroundClassAtItsFactor_AnUnfactoredOneIsUnchanged(string ground, bool factored, float min, float max)
        {
            var (repo, e, pool) = TrajectoryWorld(0f, startSpeed: 0f, vehicle: new VehicleParams
            {
                Class = VehicleClass.PersonalCar, WheelBase = 1.4f, MaxSpeedFwd = 10f, MaxAccel = 4.5f, MaxDecel = 6f, MaxSteerAngle = (Fdp.Core.Angle)(0.6f),
                MaxLatAccel = 6f, LookaheadTimeMin = 0.8f, LookaheadTimeMax = 2.5f, AccelGain = 1.8f, AvoidanceRadius = 1f,
                OffRoadOpen = factored ? 0.6f : 0f, OffRoadScrub = factored ? 0.45f : 0f, OffRoadForest = factored ? 0.3f : 0f,
            });
            string surface = ground == "open" ? "water" : ground;   // open ground: no surface under the path (a far water pond makes the grid)
            string coords = ground == "open" ? "[[280,80],[290,80],[290,90],[280,90],[280,80]]" : "[[-50,-50],[200,-50],[200,50],[-50,50],[-50,-50]]";
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[-100,-100,300,100],"groundZ":0},"features":[{"type":"Feature","properties":{"kind":"surface","surface":"SURF"},"geometry":{"type":"Polygon","coordinates":[COORDS]}},{"type":"Feature","properties":{"kind":"surface","surface":"scrub"},"geometry":{"type":"Polygon","coordinates":[[[260,60],[270,60],[270,70],[260,70],[260,60]]]}}]}"""
                .Replace("SURF", surface).Replace("COORDS", coords), "w"));
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float top = 0f;
            for (int i = 0; i < 6 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                top = MathF.Max(top, repo.GetComponent<VehicleState>(e).Speed);
            }
            Assert.InRange(top, min, max);
            Assert.Equal(factored, kin.SurfaceGrid != null);   // the grid is built only when a factored type moves
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4623</c> AB-2 (Animals AR-1) — a COW sent along a path at a right angle to it turns WHILE it moves (it steers round, the legged
        /// preset), never the person's turn-on-the-spot: by the time it faces its path it has walked several metres. 🔴 Red-proof: treat the
        /// Quadruped as a person in the steering (<c>person</c>) ⇒ it pivots in place ⇒ red.
        /// </summary>
        [Fact]
        public void CE4623_ACow_TurnsWhileItMoves_NeverOnTheSpot()
        {
            var north = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);   // its path runs east: a quarter turn to make
            var (repo, e, pool) = TrajectoryWorld(6f, startSpeed: 0f, facing: north, vehicle: VehiclePresets.GetPreset(VehicleClass.Quadruped));
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            var start = repo.GetComponent<SimTransform>(e).Position;
            float movedWhenFacing = -1f;
            for (int i = 0; i < 20 * 60 && movedWhenFacing < 0f; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var tf = repo.GetComponent<SimTransform>(e);
                var fwd = Vector3.Transform(Vector3.UnitX, tf.Rotation);
                if (fwd.X > MathF.Cos(30f * MathF.PI / 180f)) movedWhenFacing = Vector3.Distance(start, tf.Position);
            }
            Assert.True(movedWhenFacing > 0.5f, $"a cow turns while it walks (moved {movedWhenFacing:F1} m by the time it faced its path)");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-4621</c> MC-2 (R-2084) — reading the surface grid costs nothing per frame: a factored body's steady move allocates 0 B.
        /// </summary>
        [Fact]
        public void CE4621_TheOffRoadFactor_AllocatesNothingPerSteadyFrame()
        {
            var (repo, e, pool) = TrajectoryWorld(0f, startSpeed: 0f, vehicle: new VehicleParams
            {
                Class = VehicleClass.PersonalCar, WheelBase = 1.4f, MaxSpeedFwd = 10f, MaxAccel = 4.5f, MaxDecel = 6f, MaxSteerAngle = (Fdp.Core.Angle)(0.6f),
                MaxLatAccel = 6f, LookaheadTimeMin = 0.8f, LookaheadTimeMax = 2.5f, AccelGain = 1.8f, AvoidanceRadius = 1f,
                OffRoadOpen = 0.6f, OffRoadScrub = 0.45f, OffRoadForest = 0.3f,
            });
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[-100,-100,300,100],"groundZ":0},"features":[{"type":"Feature","properties":{"kind":"surface","surface":"forest"},"geometry":{"type":"Polygon","coordinates":[[[-50,-50],[200,-50],[200,50],[-50,50],[-50,-50]]]}}]}""", "w"));
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool) { ForceSerial = true };
            for (int i = 0; i < 120; i++) { spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 60; i++) kin.Execute(repo, 1f / 60f);
            long perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 60;
            Assert.NotNull(kin.SurfaceGrid);
            // the integrator's own baseline (the query build) is measured by its existing rails; the factor adds nothing on top of it
            var plain = TrajectoryWorld(0f, startSpeed: 0f, vehicle: new VehicleParams
            {
                Class = VehicleClass.PersonalCar, WheelBase = 1.4f, MaxSpeedFwd = 10f, MaxAccel = 4.5f, MaxDecel = 6f, MaxSteerAngle = (Fdp.Core.Angle)(0.6f),
                MaxLatAccel = 6f, LookaheadTimeMin = 0.8f, LookaheadTimeMax = 2.5f, AccelGain = 1.8f, AvoidanceRadius = 1f,
            });
            var kin2 = new CarKinematicsSystem(plain.Item3) { ForceSerial = true };
            for (int i = 0; i < 120; i++) { spatial.Execute(plain.Item1, 1f / 60f); kin2.Execute(plain.Item1, 1f / 60f); }
            long b2 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 60; i++) kin2.Execute(plain.Item1, 1f / 60f);
            long plainPerFrame = (GC.GetAllocatedBytesForCurrentThread() - b2) / 60;
            Assert.True(perFrame <= plainPerFrame, $"the off-road factor adds allocation per frame: {perFrame} B vs {plainPerFrame} B without it");
            plain.Item3.Dispose(); plain.Item1.Dispose();
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>AQ88</c> A/B (R-265) — a PERSON can run, and his stance caps how fast: asked 5 m/s on a path, the pedestrian preset
        /// (a 6 m/s sprint) runs ≈ 5 upright, 3 crouched (× 0.5) and 1.2 prone (× 0.2) — Nav §11's ratios on the LOGICAL stance.
        /// 🔴 Red before: the preset capped every person at 2.0 m/s, and the stance changed nothing.
        /// </summary>
        [Theory]
        [InlineData(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, 5.0f)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, 3.0f)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.StanceId.Prone,    1.2f)]
        public void AQ88_APerson_Runs_AndHisStanceCapsHowFast(Fdp.Toolkit.Tkb.Domain.StanceId stance, float expected)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            repo.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            repo.AddComponent(e, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = stance });
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float top = 0f;
            for (int i = 0; i < 8 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                top = MathF.Max(top, repo.GetComponent<VehicleState>(e).Speed);
            }
            Assert.InRange(top, expected - 0.25f, expected + 0.05f);
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-1082</c> (Injury slice I-b; docs/DESIGN_Map_3D_Mode.md §6m C-2) — a WOUNDED person moves at his type's
        /// <c>WoundedSpeedFactor</c> beside the stance: it scales the 6 m/s sprint (the cap), so asked 5 m/s he makes 6 × 0.6 = 3.6; healthy ⇒ 5
        /// (the asked speed); an unset factor (0) ⇒ 5.
        /// 🔒 User, 2026-10-14: "when soldier is wounded his motion pattern could be degraded" — the 0.6 approved.
        /// </summary>
        [Theory]
        [InlineData(100f, 0.6f, 5.0f)]
        [InlineData(50f,  0.6f, 3.6f)]
        [InlineData(50f,  0f,   5.0f)]
        public void CE1082_AWoundedPerson_MovesAtHisTypesWoundedSpeed(float health, float factor, float expected)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.InjuryThresholds>();
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f });
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f, WoundedSpeedFactor = factor });
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float top = 0f;
            for (int i = 0; i < 8 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                top = MathF.Max(top, repo.GetComponent<VehicleState>(e).Speed);
            }
            Assert.InRange(top, expected - 0.25f, expected + 0.05f);
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-2152</c> C-a (R-2011 C8) — a person carrying a casualty is slowed by the carry factor, dragging by the drag factor, beside
        /// the stance (the same 6 m/s sprint: × 0.5 carried ⇒ 3, × 0.3 dragged ⇒ 1.8; carrying nobody ⇒ 5, the path's speed).
        /// ✅ Red-proof: CarKinematicsSystem without the CarryLoad factor ⇒ he drags at full speed ⇒ red.
        /// </summary>
        [Theory]
        [InlineData(Fdp.Toolkit.Behavior.Components.CarryMode.Seated,  false, 5.0f)]
        [InlineData(Fdp.Toolkit.Behavior.Components.CarryMode.Carried, true,  3.0f)]
        [InlineData(Fdp.Toolkit.Behavior.Components.CarryMode.Dragged, true,  1.8f)]
        public void CE2152_APersonCarrier_IsSlowedByWhatHeCarries(Fdp.Toolkit.Behavior.Components.CarryMode mode, bool loaded, float expected)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
            repo.AddComponent(e, new Fdp.Toolkit.Behavior.Components.PersonCarrier
                { CarrySpeedFactor = 0.5f, DragSpeedFactor = 0.3f, DragBehindMetres = 1f });
            var buffer = new Fdp.Toolkit.Behavior.Components.PassengerBuffer();
            if (loaded)
            {
                buffer.Passengers[0] = repo.CreateEntity();
                buffer.SetMode(0, mode);
                buffer.Count = 1;
            }
            repo.AddComponent(e, buffer);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float top = 0f;
            for (int i = 0; i < 8 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                top = MathF.Max(top, repo.GetComponent<VehicleState>(e).Speed);
            }
            Assert.InRange(top, expected - 0.25f, expected + 0.05f);
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
        /// ⭐ <c>CE-2198</c> (behaviors handoff ①, live <c>ua-tank-corner</c>) — a TANK sent 37 m at its top speed (20 m/s, braking 4 m/s²) with
        /// a 2 m arrival radius stops within it, on a path (the move a <c>LocomotionMoveTo</c> issues) and driven direct — never its ~26 m
        /// braking distance past the point (measured live: 9 m, then 26 m past, inside a building).
        /// </summary>
        [Theory]
        [InlineData(KinematicsMode.CustomTrajectory)]
        [InlineData(KinematicsMode.Direct)]
        public void CE2198_ATankSentAShortWayAtItsTopSpeed_StopsWithinItsArrivalRadius(KinematicsMode mode)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 20f, startSpeed: 0f);
            pool.RegisterTrajectoryWithKey(new[] { new Vector3(0f, 0f, 0f), new Vector3(37f, 0f, 0f) }, 8);
            var nav0 = repo.GetComponent<NavState>(e);
            nav0.Mode = mode;
            nav0.TrajectoryId = 8;
            nav0.ArrivalRadius = 2f;
            nav0.FinalDestination = new Vector3(37f, 0f, 0f);
            repo.SetComponent(e, nav0);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float farthest = 0f, top = 0f;
            for (int i = 0; i < 30 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                farthest = MathF.Max(farthest, repo.GetComponent<SimTransform>(e).Position.X);
                top = MathF.Max(top, repo.GetComponent<VehicleState>(e).Speed);
            }
            Assert.Equal(1, repo.GetComponent<NavState>(e).HasArrived);
            Assert.True(farthest <= 37f + 2f, $"{mode}: overshot x=37 to x={farthest:F1} (top {top:F1} m/s; arrival radius 2 m)");
            var end = repo.GetComponent<SimTransform>(e).Position.X;
            Assert.True(MathF.Abs(end - 37f) <= 2f, $"{mode}: came to rest at x={end:F1}");   // 🔴 measured: arrived, then drove off to x=440
            Assert.True(repo.GetComponent<VehicleState>(e).Speed < 0.5f);
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2206</c> (behaviors handoff, live <c>ua-javelin-lock</c>) — an M1 sent 2.3 m to its LEFT (inside its ~6.6 m turning circle)
        /// arrives within its 2 m radius, on a path and driven direct: it drives on, turns back round and comes to the point, in a bounded
        /// excursion — never orbiting it (measured live: 60 s round a ~5 m circle, the move never ended).
        /// </summary>
        [Theory]
        [InlineData(KinematicsMode.CustomTrajectory)]
        [InlineData(KinematicsMode.Direct)]
        public void CE2206_AHullSentToAPointInsideItsTurningCircle_Arrives(KinematicsMode mode)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 0f);
            var point = new Vector3(0.5f, 2.3f, 0f);
            Assert.True(CarKinematicsSystem.InsideTurningCircle(repo.GetComponent<VehicleParams>(e), Vector2.Zero, Vector2.UnitX, new Vector2(point.X, point.Y)));
            pool.RegisterTrajectoryWithKey(new[] { Vector3.Zero, point }, 9);
            var nav0 = repo.GetComponent<NavState>(e);
            nav0.Mode = mode;
            nav0.TrajectoryId = 9;
            nav0.ArrivalRadius = 2f;
            nav0.FinalDestination = point;
            repo.SetComponent(e, nav0);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float farthest = 0f;
            int arrivedAt = -1;
            for (int i = 0; i < 60 * 60 && arrivedAt < 0; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var p = repo.GetComponent<SimTransform>(e).Position;
                farthest = MathF.Max(farthest, Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(point.X, point.Y)));
                if (repo.GetComponent<NavState>(e).HasArrived == 1) arrivedAt = i;
            }
            var end = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(arrivedAt >= 0, $"{mode}: never arrived in 60 s — at ({end.X:F1}, {end.Y:F1}), farthest {farthest:F1} m");
            Assert.True(Vector2.Distance(new Vector2(end.X, end.Y), new Vector2(point.X, point.Y)) <= 2.5f, $"{mode}: arrived at ({end.X:F1}, {end.Y:F1})");
            Assert.True(farthest < 30f, $"{mode}: an excursion of {farthest:F1} m");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-2198</c> (behaviors handoff ②, live <c>ua-tank-corner</c>) — a ground vehicle driven at a building's wall STOPS at it: inside
        /// the solid footprint the surface sampler offers only the roof, and the hull was lifted onto the 15 m roof. ✅ Red-proof: the mover
        /// without the wall rule ⇒ z = 12 ⇒ red.
        /// </summary>
        [Fact]
        public void CE2198_AVehicleDrivenAtABuilding_StopsAtItsWall_NeverOnItsRoof()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 10f, startSpeed: 0f);
            const string wall = "\"walls\":[{\"from\":[0,0],\"to\":[20,0]},{\"from\":[20,0],\"to\":[20,20]},{\"from\":[20,20],\"to\":[0,20]},{\"from\":[0,20],\"to\":[0,0]}]";
            string storey = "{\"height\":3," + wall + "}";
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"R\",\"building\":{\"footprint\":[[0,0],[20,0],[20,20],[0,20]]," +
                "\"storeys\":[" + storey + "," + storey + "," + storey + "," + storey + "]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[30,-10]}}]}"));
            var nav0 = repo.GetComponent<NavState>(e);
            nav0.Mode = KinematicsMode.Direct;
            nav0.ArrivalRadius = 2f;
            nav0.FinalDestination = new Vector3(45f, 0f, 0f);   // inside the building
            repo.SetComponent(e, nav0);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float topZ = 0f, farthest = 0f;
            for (int i = 0; i < 20 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var p = repo.GetComponent<SimTransform>(e).Position;
                topZ = MathF.Max(topZ, p.Z);
                farthest = MathF.Max(farthest, p.X);
            }
            Assert.True(topZ < 0.5f, $"lifted to z={topZ:F1}");
            Assert.True(farthest <= 30.5f, $"inside the building at x={farthest:F1}");
            pool.Dispose();
            repo.Dispose();
        }

        // ── CE-3286 (R-3025; docs/designs/motion-models/DESIGN_Motion_Domains.md §5f) — a hull never enters a building; a held one backs out ──

        /// <summary>A 60 × 30 m solid block (test-town's form: a building polygon with a height) whose south wall runs along y = 10.</summary>
        private const string BlockWorld =
            "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
            "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"height\":15,\"label\":\"Block\"}," +
            "\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[0,10],[60,10],[60,40],[0,40],[0,10]]]}}]}";

        /// <summary>
        /// The tank preset (7.0 × 3.5 m, turn radius ~6.3 m) at <paramref name="start"/> facing <paramref name="facing"/>, on a path through
        /// <paramref name="path"/> (or driven direct to its last point), beside <see cref="BlockWorld"/>; with the watchdog's
        /// <see cref="FrustrationTicks"/> (the recovery's state) unless <paramref name="recovery"/> is off.
        /// </summary>
        private static (EntityRepository Repo, Entity E, TrajectoryPoolManager Pool) HullWorld(Vector2 start, Quaternion facing, Vector3[] path,
            KinematicsMode mode = KinematicsMode.CustomTrajectory, float arrivalRadius = 2f, float startSpeed = 0f, bool recovery = true,
            Fdp.Toolkit.Terrain.TerrainWorld? world = null)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: startSpeed, facing: facing, vehicle: VehiclePresets.GetPreset(VehicleClass.Tank));
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(world ?? Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(BlockWorld));
            if (recovery)
            {
                repo.RegisterComponent<FrustrationTicks>();
                repo.AddComponent(e, new FrustrationTicks());
            }
            var tf = repo.GetComponent<SimTransform>(e);
            tf.Position = new Vector3(start.X, start.Y, 0f);
            repo.SetComponent(e, tf);
            repo.SetComponent(e, new SimVelocity { Linear = new Vector3(Vector2.Transform(Vector2.UnitX, facing) * startSpeed, 0f) });
            pool.RegisterTrajectoryWithKey(path, 11);
            var nav = repo.GetComponent<NavState>(e);
            nav.Mode = mode;
            nav.TrajectoryId = 11;
            nav.ArrivalRadius = arrivalRadius;
            nav.FinalDestination = path[^1];
            repo.SetComponent(e, nav);
            return (repo, e, pool);
        }

        /// <summary>How deep (m) the hull's deepest corner stands inside the block (0 = none inside).</summary>
        private static float CornerDepth(EntityRepository repo, Entity e)
        {
            var p = repo.GetComponent<VehicleParams>(e);
            var tf = repo.GetComponent<SimTransform>(e);
            var f3 = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            var f = Vector2.Normalize(new Vector2(f3.X, f3.Y));
            var c = new Vector2(tf.Position.X, tf.Position.Y);
            float deepest = 0f;
            foreach (var (a, b) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
            {
                var k = CarKinem.Controllers.HullClearance.Corner(c, f, a * p.Length * 0.5f, b * p.Width * 0.5f);
                if (k.X > 0f && k.X < 60f && k.Y > 10f && k.Y < 40f) deepest = MathF.Max(deepest, MathF.Min(k.Y - 10f, MathF.Min(k.X, 60f - k.X)));
            }
            return deepest;
        }

        private static readonly Quaternion FacingNorth = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

        /// <summary>
        /// ⭐⭐ <c>CE-3286</c> A — a tank driven NOSE-FIRST at a wall stops with its nose AT the wall: no corner ever inside. 📌 Live
        /// <c>ua-tank-corner</c>: the centre-only guard (CE-2198) let the front half stand in the block. No recovery here — the guard alone.
        /// 🔴 Red-proof: trace the centre only ⇒ the nose 3.5 m deep.
        /// </summary>
        [Fact]
        public void CE3286_AHullDrivenNoseFirstAtAWall_StopsWithNoCornerInside()
        {
            var (repo, e, pool) = HullWorld(new Vector2(30f, -15f), FacingNorth, new[] { new Vector3(30f, -15f, 0f), new Vector3(30f, 25f, 0f) },
                                            mode: KinematicsMode.Direct, recovery: false);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float deepest = 0f, northmost = float.MinValue;
            for (int i = 0; i < 15 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                deepest = MathF.Max(deepest, CornerDepth(repo, e));
                northmost = MathF.Max(northmost, repo.GetComponent<SimTransform>(e).Position.Y);
            }
            Assert.True(deepest <= 0.05f, $"a corner stood {deepest:F2} m inside the block");
            Assert.True(northmost >= 10f - 3.5f - 0.6f, $"stopped short of the wall: the centre reached y={northmost:F2}");   // it drove up to it
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3286</c> B, the "smart" half (R-3025) — a tank STANDING nose to the wall (0.3 m off it) on a new path west along the wall:
        /// its way on is not clear (a left turn at full lock carries the nose into the block), so it BACKS OUT before it touches the wall, then
        /// drives its path and arrives — never touching the wall, never a corner inside. 📌 Live <c>ua-technical-scoot</c>: held 207 s.
        /// 🔴 Red-proofs: no recovery ⇒ never arrives (held); a fixed 1 m back-up instead of the rollout ⇒ it touches the wall again.
        /// </summary>
        [Fact]
        public void CE3286_AHullStandingNoseToAWall_BacksOutFirst_ThenDrivesItsPathAlongTheWall()
        {
            var start = new Vector2(30f, 10f - 3.5f - 0.3f);
            var (repo, e, pool) = HullWorld(start, FacingNorth,
                new[] { new Vector3(start.X, start.Y, 0f), new Vector3(20f, 3f, 0f), new Vector3(-30f, 3f, 0f) });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float deepest = 0f, slowest = 0f;
            bool touched = false;
            int arrivedAt = -1;
            for (int i = 0; i < 60 * 60 && arrivedAt < 0; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                deepest = MathF.Max(deepest, CornerDepth(repo, e));
                slowest = MathF.Min(slowest, repo.GetComponent<VehicleState>(e).Speed);
                touched |= repo.GetComponent<FrustrationTicks>(e).WallHeldSeconds > 0f;
                if (repo.GetComponent<NavState>(e).HasArrived == 1) arrivedAt = i;
            }
            var end = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(arrivedAt >= 0, $"never arrived in 60 s — at ({end.X:F1}, {end.Y:F1}), recoveries {repo.GetComponent<FrustrationTicks>(e).Recoveries}");
            Assert.True(slowest < -0.5f, $"it never backed (slowest {slowest:F2} m/s)");
            Assert.False(touched, "it touched the wall — the recovery ended before its way on was clear");
            Assert.True(deepest <= 0.05f, $"a corner stood {deepest:F2} m inside the block");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3286</c> B, the reactive half — a tank already ROLLING north at 2 m/s with its nose 0.3 m off the wall, on a path that
        /// turns west along it, is HELD at the wall (its left turn carries the nose in), backs out and drives on to the path's end, held ONCE —
        /// never back in the same situation. 📌 Live <c>ua-tank-corner</c>: four more moves were issued and each was held again.
        /// 🔴 Red-proof: no recovery ⇒ held for good.
        /// </summary>
        [Fact]
        public void CE3286_AHullHeldAtAWall_BacksOut_AndDrivesOn_HeldOnlyOnce()
        {
            var start = new Vector2(30f, 10f - 3.5f - 0.3f);
            var (repo, e, pool) = HullWorld(start, FacingNorth,
                new[] { new Vector3(start.X, start.Y, 0f), new Vector3(30f, 7f, 0f), new Vector3(-30f, 4f, 0f) }, startSpeed: 2f);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float deepest = 0f;
            int holds = 0, arrivedAt = -1;
            bool wasHeld = false;
            for (int i = 0; i < 60 * 60 && arrivedAt < 0; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                deepest = MathF.Max(deepest, CornerDepth(repo, e));
                bool isHeld = repo.GetComponent<FrustrationTicks>(e).WallHeldSeconds > 0f;
                if (isHeld && !wasHeld) holds++;
                wasHeld = isHeld;
                if (repo.GetComponent<NavState>(e).HasArrived == 1) arrivedAt = i;
            }
            var end = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(arrivedAt >= 0, $"never arrived in 60 s — at ({end.X:F1}, {end.Y:F1}), recoveries {repo.GetComponent<FrustrationTicks>(e).Recoveries}");
            Assert.True(holds <= 1, $"held at the wall {holds} times — it drove back into the same situation");
            Assert.True(deepest <= 0.05f, $"a corner stood {deepest:F2} m inside the block");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-5052</c> — a hull TURNING beside a building's convex corner never swings its long side onto it: 📐 live (Stechovice) a recovery reversing
        /// while turning put a house's corner ~1 m inside an M1's left-rear quarter, between the traced corners, and held it there both ways for good.
        /// A hull whose side already crosses the wall may still turn back out.
        /// </summary>
        [Fact]
        public void CE5052_APivotBesideACorner_IsRefused_ATurnBackOut_IsNot()
        {
            string world = "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"height\":8,\"label\":\"Diamond\"}," +
                "\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[-4.24,4.24],[-8.49,0],[-4.24,-4.24],[0,0]]]}}]}";
            var q = new Fdp.Toolkit.Terrain.TerrainWorldQuery(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(world));
            const float L = 7.93f, W = 3.66f;
            var at = new Vector2(W * 0.5f + 0.1f, 2.7f);   // the left side 0.1 m east of the house's east corner, which lies 2.7 m behind the centre
            static Vector2 Turned(float deg) { float r = -deg * MathF.PI / 180f; return new Vector2(-MathF.Sin(r), MathF.Cos(r)); }   // clockwise from north: the tail swings west
            Assert.False(CarKinem.Controllers.HullClearance.StepBlocked(q, at, Vector2.UnitY, 0f, at + new Vector2(0f, 0.5f), Vector2.UnitY, 0f, L, W));   // straight on: free
            foreach (float deg in new[] { 6f, 10f })
                Assert.True(CarKinem.Controllers.HullClearance.StepBlocked(q, at, Vector2.UnitY, 0f, at, Turned(deg), 0f, L, W), $"a {deg}° pivot swung the side onto the corner");
            Assert.False(CarKinem.Controllers.HullClearance.StepBlocked(q, at, Turned(10f), 0f, at, Turned(6f), 0f, L, W));   // already across: turning back out is free

            // ⭐ the live house's case: WALL PANELS (here a garden wall's east end at x 80, y 10) — the side 0.2 m east of its end
            var g = new Fdp.Toolkit.Terrain.TerrainWorldQuery(GardenWall(2.0f));
            var by = new Vector2(80f + 0.2f + W * 0.5f, 10f + 2.7f);
            Assert.False(CarKinem.Controllers.HullClearance.StepBlocked(g, by, Vector2.UnitY, 0f, by + new Vector2(0f, 0.5f), Vector2.UnitY, 0f, L, W));
            foreach (float deg in new[] { 10f, 15f })
                Assert.True(CarKinem.Controllers.HullClearance.StepBlocked(g, by, Vector2.UnitY, 0f, by, Turned(deg), 0f, L, W), $"a {deg}° pivot swung the side onto the wall's end");
        }

        /// <summary>
        /// ⭐ <c>CE-3286</c> — a point beside the wall that the hull's centre cannot reach (the live hide 2.25 m off the L-Block, a 7 m hull
        /// arriving nose-first, here with a 1 m radius) is REACHED when the nose meets the wall — not a hold to back out of. 🔴 Red-proof:
        /// drop the rule ⇒ never arrives.
        /// </summary>
        [Fact]
        public void CE3286_APointBesideTheWall_IsReached_WhenTheNoseMeetsTheWall()
        {
            var (repo, e, pool) = HullWorld(new Vector2(30f, -15f), FacingNorth,
                new[] { new Vector3(30f, -15f, 0f), new Vector3(30f, 7.75f, 0f) }, arrivalRadius: 1f);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            int arrivedAt = -1;
            float deepest = 0f;
            for (int i = 0; i < 30 * 60 && arrivedAt < 0; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                deepest = MathF.Max(deepest, CornerDepth(repo, e));
                if (repo.GetComponent<NavState>(e).HasArrived == 1) arrivedAt = i;
            }
            var end = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(arrivedAt >= 0, $"never arrived — at ({end.X:F1}, {end.Y:F1})");
            Assert.InRange(end.Y, 10f - 3.5f - 0.6f, 10f - 3.5f + 0.05f);   // the nose at the wall
            Assert.True(deepest <= 0.05f, $"a corner stood {deepest:F2} m inside the block");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>A height-grid hillside (the ground model of basic-desert's ridge): flat to y = 0, then rising 0.3 m per metre (the
        /// hill attack's ridge is 0.1–0.31).</summary>
        private static Fdp.Toolkit.Terrain.TerrainWorld Hillside()
        {
            var sb = new System.Text.StringBuilder("ncols 21\nnrows 21\nxllcenter -100\nyllcenter -100\ncellsize 10\nNODATA_value -9999\n");
            for (int row = 0; row < 21; row++)
            {
                float y = -100f + (20 - row) * 10f;
                string z = (MathF.Max(0f, y) * 0.3f).ToString(System.Globalization.CultureInfo.InvariantCulture);
                for (int c = 0; c < 21; c++) sb.Append(z).Append(c < 20 ? " " : "\n");
            }
            string grid = sb.ToString();
            const string geojson = "{ \"type\": \"FeatureCollection\", \"hrot\": { \"schemaVersion\": 1, \"bounds\": [-100, -100, 100, 100], "
                + "\"groundZ\": 0, \"heightGrid\": \"hill.height.asc\" }, \"features\": [] }";
            return Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(geojson, "hill", new Fdp.Toolkit.Terrain.TerrainAssets { ReadFile = n => n == "hill.height.asc" ? grid : null });
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4041</c> — a tank drives UP A HILLSIDE it can climb (0.3 m/m on a height grid, far under <see cref="CarKinematicsSystem.VehicleMaxClimbSlope"/>):
        /// it is never held. 📌 ua-hill-attack: each corner's rise was read against the hull CENTRE's height, so at the ramp's foot a corner
        /// half a hull ahead (L/2 × slope above the centre) "rose like a wall" past 0.5 m — every tank was held there and backed out again.
        /// 🔴 Red-proof: the rise against the centre's height ⇒ held at the foot.
        /// </summary>
        [Fact]
        public void CE4041_AHullDrivesUpARampItCanClimb_NeverHeld()
        {
            var (repo, e, pool) = HullWorld(new Vector2(30f, -15f), FacingNorth, new[] { new Vector3(30f, -15f, 0f), new Vector3(30f, 25f, 7.5f) },
                                            mode: KinematicsMode.Direct, recovery: false, world: Hillside());
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            for (int i = 0; i < 20 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
            var end = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(end.Y > 20f, $"held on the ramp at y={end.Y:F1} (z {end.Z:F2})");
            Assert.InRange(end.Z, 0.3f * end.Y - 0.1f, 0.3f * end.Y + 0.1f);   // on the ramp surface
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3286</c> — a hull LEFT half inside a block (spawned there, or by the old centre-only guard) is not walled in: its nose 1.5 m
        /// in, sent south, it drives out. 🔴 Red-proof: block a corner whose surface already rose ⇒ it never moves.
        /// </summary>
        [Fact]
        public void CE3286_AHullLeftHalfInsideABlock_CanDriveOut()
        {
            var start = new Vector2(30f, 10f - 3.5f + 1.5f);
            var (repo, e, pool) = HullWorld(start, FacingNorth,
                new[] { new Vector3(start.X, start.Y, 0f), new Vector3(30f, -30f, 0f) }, recovery: false);
            var nav = repo.GetComponent<NavState>(e);
            nav.ReverseAllowed = 1;   // it backs straight out (a forward turn would swing the nose further in)
            repo.SetComponent(e, nav);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            for (int i = 0; i < 20 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
            var end = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(end.Y < start.Y - 10f, $"walled in: still at y={end.Y:F1} (started {start.Y:F1})");
            Assert.True(CornerDepth(repo, e) <= 0.05f, "still inside");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3286</c> — a hull HELD at the wall while it steers for a point off to its side keeps its HEADING: the refused step is
        /// refused whole, so no corner rotates in. 📌 Live: held hulls' headings crept ~1°/s (the one tick of speed the controller gives
        /// before the hold zeroes it), sweeping a corner into the block. No recovery — the guard alone. 🔴 Red-proof: keep the position, let
        /// the heading turn ⇒ it creeps while held.
        /// </summary>
        [Fact]
        public void CE3286_AHeldHull_KeepsItsHeading()
        {
            // 6 m beside its path (x = 30, north into the block) and 9 m from the wall: it is still turning onto the path when the nose meets
            // the wall, so the follower keeps steering while held
            var (repo, e, pool) = HullWorld(new Vector2(24f, 1f), FacingNorth, new[] { new Vector3(30f, -20f, 0f), new Vector3(30f, 30f, 0f) },
                                            recovery: false);
            var nav0 = repo.GetComponent<NavState>(e);
            nav0.ProgressS = 21f;
            repo.SetComponent(e, nav0);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float? heldYaw = null;
            float creep = 0f, deepest = 0f;
            for (int i = 0; i < 20 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                deepest = MathF.Max(deepest, CornerDepth(repo, e));
                float yaw = YawOf(repo, e);
                bool standing = MathF.Abs(repo.GetComponent<VehicleState>(e).Speed) < 1e-4f && i > 60;
                if (standing && heldYaw is null) heldYaw = yaw;
                if (standing && heldYaw is float h) creep = MathF.Max(creep, MathF.Abs(MathF.IEEERemainder(yaw - h, 2f * MathF.PI)));
            }
            Assert.NotNull(heldYaw);   // it reached the wall and was held
            Assert.True(creep < 0.01f, $"the heading crept {creep * 180f / MathF.PI:F1}° while held");
            Assert.True(deepest <= 0.05f, $"a corner stood {deepest:F2} m inside the block");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3287</c> (R-3026; Motion Domains §5g; user: <i>"I have seen destroyed vehicle still moving, maybe being pushed by people
        /// coming close"</i> · <i>"parked vehicles should not be pushed either"</i>) — a WRECK, and a PARKED live tank, with no move, beside
        /// which a man stands 4 m off, are not moved: avoidance's separation push (inside twice the avoidance radius, 10 m for the tank)
        /// drives only a vehicle that is driving. 📐 Measured red before: the wreck was pushed ~10 m. 🔴 Red-proof: push a vehicle with no
        /// wanted speed ⇒ both move.
        /// </summary>
        [Theory]
        [InlineData(0f)]
        [InlineData(3000f)]
        public void CE3287_AWreck_OrAParkedVehicle_IsNotPushedByAManBesideIt(float health)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 0f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Tank));
            var nav = repo.GetComponent<NavState>(e);
            nav.Mode = KinematicsMode.None;
            nav.TargetSpeed = 0f;
            repo.SetComponent(e, nav);
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 3000f });
            Stander(repo, new Vector3(0f, 4f, 0f));
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
            var p = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(new Vector2(p.X, p.Y).Length() < 0.05f, $"the wreck was pushed to ({p.X:F2}, {p.Y:F2})");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3287</c> — a WRECK whose move the brain has not ended yet (CE-3091 runs on the brain, a frame or more later) does not drive
        /// it: on a path at 5 m/s with Health 0 it stays put. 🔴 Red-proof: drop the wreck test ⇒ it drives the path.
        /// </summary>
        [Fact]
        public void CE3287_AWreck_DoesNotDriveTheMoveItStillHas()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Tank));
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 3000f });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            for (int i = 0; i < 5 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
            var p = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(new Vector2(p.X, p.Y).Length() < 0.05f, $"the wreck drove to ({p.X:F2}, {p.Y:F2})");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>A man whose Health is <paramref name="health"/> of 100, incapacitated below 30 %, wounded below 60 %.</summary>
        private static void Hurt(EntityRepository repo, Entity e, float health)
        {
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>())
                repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.InjuryThresholds>())
                repo.RegisterComponent<Fdp.Toolkit.Combat.Components.InjuryThresholds>();
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f });
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2321</c> (R-3027) — a MAN DOWN (dead, or incapacitated) is neither pushed nor turned by a man standing 0.6 m north of
        /// him; the same man fit (the control) is pushed off and turned away — which proves the encounter is close enough to matter.
        /// 📌 Live: a lying body was shoved and turned to face away from whoever came close. 🔴 Red-proof: drop the down test ⇒ he moves.
        /// </summary>
        [Theory]
        [InlineData(0f, false)]    // dead
        [InlineData(20f, false)]   // incapacitated
        [InlineData(100f, true)]   // fit — the control
        public void CE2321_AManDown_IsNotPushedNorTurned_ByAManBesideHim(float health, bool moves)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 0f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            var nav = repo.GetComponent<NavState>(e);
            nav.Mode = KinematicsMode.None;
            nav.TargetSpeed = 0f;
            repo.SetComponent(e, nav);
            Hurt(repo, e, health);
            Stander(repo, new Vector3(0f, 0.6f, 0f));
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            for (int i = 0; i < 5 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
            var p = repo.GetComponent<SimTransform>(e).Position;
            float moved = new Vector2(p.X, p.Y).Length(), turned = MathF.Abs(YawOf(repo, e));
            if (moves)
                Assert.True(moved > 0.1f && turned > 0.2f, $"the fit man was not pushed ({moved:F2} m, {turned:F2} rad) — this proves nothing");
            else
                Assert.True(moved < 0.01f && turned < 0.01f, $"the man down was pushed {moved:F2} m and turned {turned:F2} rad");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-2321</c> — an incapacitated man whose move the brain has not ended yet does not walk it. 🔴 Red-proof: drop the down
        /// test from the target speed ⇒ he walks the path.
        /// </summary>
        [Fact]
        public void CE2321_AManDown_DoesNotWalkTheMoveHeStillHas()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            Hurt(repo, e, 20f);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            for (int i = 0; i < 3 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
            var p = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(new Vector2(p.X, p.Y).Length() < 0.01f, $"the man down walked to ({p.X:F2}, {p.Y:F2})");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-2321</c> — and a WALKER still steps round a man lying on his path (the man down is an obstacle, not a ghost).
        /// 🔴 Red-proof: no avoidance at all ⇒ the walker walks over him.
        /// </summary>
        [Fact]
        public void CE2321_AWalker_StillStepsRoundAManDown()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            var lying = Stander(repo, new Vector3(5f, 0.1f, 0f));
            Hurt(repo, lying, 0f);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float closest = float.MaxValue;
            for (int i = 0; i < 8 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var p = repo.GetComponent<SimTransform>(e).Position;
                var q = repo.GetComponent<SimTransform>(lying).Position;
                closest = MathF.Min(closest, Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(q.X, q.Y)));
            }
            var at = repo.GetComponent<SimTransform>(lying).Position;
            Assert.True(closest > 0.3f, $"the walker came within {closest:F2} m of the man down — over him");
            Assert.True(Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(5f, 0.1f)) < 0.01f, "the man down was pushed");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>A corridor along x: the walkable mesh is |y| ≤ <c>half</c>; off it projects onto its edge, as Recast's nearest-poly does.</summary>
        private sealed class Corridor : Fdp.Toolkit.Navigation.INavmeshProvider
        {
            private readonly float _half;
            public Corridor(float half) => _half = half;
            public bool IsWalkable(Vector3 p, uint layerMask = 0xFFFFFFFF) => MathF.Abs(p.Y) <= _half;
            public bool ProjectToNavmesh(Vector3 p, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
            {
                snapped = new Vector3(p.X, Math.Clamp(p.Y, -_half, _half), p.Z);
                return true;
            }
            public int SampleNavmeshPoints(Vector3 c, float r, Span<Vector3> results, uint layerMask = 0xFFFFFFFF) => 0;
            public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => false;
            public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => float.PositiveInfinity;
            public uint QueryVersion() => 0;
            public int PlanPath(Vector3 from, Vector3 to, Span<Fdp.Toolkit.Navigation.NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF) => 0;
        }

        /// <summary>
        /// ⭐ AQ99 C (R-2065) — a man down in a corridor too narrow to pass him (a doorway's mesh) is STEPPED OVER: a soldier (close order,
        /// 0.3 m) walking the corridor gets past him; in a corridor wide enough he still walks ROUND him (R-3027 — the control: he never
        /// comes over him). 🔴 Red-proof: drop the step-over ⇒ the narrow walker never passes the body.
        /// </summary>
        [Theory]
        [InlineData(0.3f, true)]    // the walkable strip is 0.6 m: no way round
        [InlineData(2.0f, false)]   // a 4 m corridor: room to walk round
        public void AQ99C_AManDown_IsSteppedOver_OnlyWhereThereIsNoWayRound(float half, bool over)
        {
            var preset = VehiclePresets.GetPreset(VehicleClass.Pedestrian);
            preset.AvoidanceRadius = 0.3f;
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: preset);
            repo.SetSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>(new Corridor(half));
            var lying = Stander(repo, new Vector3(5f, 0f, 0f));
            Hurt(repo, lying, 0f);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float closest = float.MaxValue, x = 0f;
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var p = repo.GetComponent<SimTransform>(e).Position;
                x = p.X;
                closest = MathF.Min(closest, Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(5f, 0f)));
            }
            Assert.True(x > 7f, $"the walker never got past the man down (x {x:F2}, corridor ±{half})");
            if (over) Assert.True(closest < 0.3f, $"he passed {closest:F2} m from him — round, not over: the narrow case proves nothing");
            else Assert.True(closest > 0.3f, $"he came within {closest:F2} m — over him, where there was room to walk round");
            var at = repo.GetComponent<SimTransform>(lying).Position;
            Assert.True(Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(5f, 0f)) < 0.01f, "the man down was pushed");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3287</c> — and a DRIVING vehicle still steers round a man in its way: a tank on its path past a man 1 m off the line bends
        /// away from him (avoidance kept where it belongs — on a vehicle that drives). 🔴 Red-proof: no avoidance at all ⇒ it runs straight.
        /// </summary>
        [Fact]
        public void CE3287_ADrivingVehicle_StillSteersRoundAMan()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 5f, vehicle: VehiclePresets.GetPreset(VehicleClass.Tank));
            Stander(repo, new Vector3(25f, 1f, 0f));
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float swerve = 0f;
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                swerve = MathF.Min(swerve, repo.GetComponent<SimTransform>(e).Position.Y);
            }
            Assert.True(swerve < -0.3f, $"it ran straight past the man (least y {swerve:F2})");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4622</c> K-2 (Checkpoint RM-2, KV-6) — a hull on a RAM order does not give way to its TARGET, nor to anyone when the target is a CROWD: the tank of
        /// CE-3287 passes a man 1 m off its line — it bends away with no order, and with the order switched off; it runs straight at him when he is its target, or when
        /// it rams a crowd. 🔴 Red-proof: drop the target skip in the avoidance ⇒ the "target" row swerves.
        /// </summary>
        [Theory]
        [InlineData(0, true)]    // no order
        [InlineData(1, false)]   // he is the target
        [InlineData(2, false)]   // a crowd
        [InlineData(3, true)]    // the order switched off
        public void CE4622_K2_ARammingHull_DoesNotSteerRoundItsTarget_NorRoundACrowd(int order, bool swerves)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 5f, vehicle: VehiclePresets.GetPreset(VehicleClass.Tank));
            var man = Stander(repo, new Vector3(25f, 1f, 0f));
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            repo.AddComponent(man, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 4622 });
            repo.RegisterComponent<RamOrder>();
            if (order != 0)
                repo.AddComponent(e, new RamOrder
                {
                    Active = order == 3 ? (byte)0 : (byte)1, Crowd = order == 2 ? (byte)1 : (byte)0,
                    Target = order == 1 ? new Fdp.Toolkit.Replication.EntityRef(4622) : default,
                });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float swerve = 0f;
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                swerve = MathF.Min(swerve, repo.GetComponent<SimTransform>(e).Position.Y);
            }
            Assert.Equal(swerves, swerve < -0.3f);
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4622</c> K-3 (Checkpoint CP-3, KV-2) — a disabled RAMMING car COASTS: a destroyed hull at 10 m/s with an active ram order loses only the rolling
        /// resistance (0.7 m/s² — about 9.3 m/s after 1 s), so a bomber still covers ground; the same hull with no order (any stopped vehicle) brakes. 🔴 Red-proof:
        /// the coast dropped ⇒ the ramming hull brakes like the other ⇒ red.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CE4622_K3_ADisabledRammingCar_Coasts_AnyOtherBrakes(bool ramming)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 10f, startSpeed: 10f, vehicle: VehiclePresets.GetPreset(VehicleClass.PersonalCar));
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>()) repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 300f });   // destroyed
            repo.RegisterComponent<RamOrder>();
            if (ramming) repo.AddComponent(e, new RamOrder { Active = 1, Crowd = 1 });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            for (int i = 0; i < 60; i++) { spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f); }
            float v = repo.GetComponent<VehicleState>(e).Speed;
            if (ramming) Assert.InRange(v, 10f - CarKinematicsSystem.CoastDecel - 0.15f, 10f - CarKinematicsSystem.CoastDecel + 0.15f);
            else Assert.True(v < 10f - 2f, $"a stopped vehicle brakes: {v:F2} m/s after 1 s");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3286</c> — a box with no way out is BOUNDED: a tank sent INTO the block (its destination inside it) is held, backs out
        /// <see cref="CarKinem.Controllers.HullRecovery.MaxRecoveries"/> times and then stays the watchdog's (it never enters, never loops).
        /// </summary>
        [Fact]
        public void CE3286_AHullWithNoWayOn_TriesABoundedNumberOfTimes()
        {
            var (repo, e, pool) = HullWorld(new Vector2(30f, -10f), FacingNorth,
                new[] { new Vector3(30f, -10f, 0f), new Vector3(30f, 25f, 0f) }, mode: KinematicsMode.Direct, startSpeed: 3f);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float deepest = 0f;
            for (int i = 0; i < 90 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                deepest = MathF.Max(deepest, CornerDepth(repo, e));
            }
            var ft = repo.GetComponent<FrustrationTicks>(e);
            Assert.Equal(CarKinem.Controllers.HullRecovery.MaxRecoveries, ft.Recoveries);
            Assert.Equal(0, ft.RecoverDir);
            Assert.True(deepest <= 0.05f, $"a corner stood {deepest:F2} m inside the block");
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
        /// ⭐⭐ <c>CE-4695</c> (backend, cross-lane — user "fix yourself") — NOTHING ON THE GROUND WADES INTO A RIVER FROM DRY GROUND (a man, and a tank — 📌 bl-floating-bridge): sent straight across a river (water y 10–20)
        /// he stops at the bank and never stands in it; on dry ground the same route is walked to its end. 📌 bl-assault-crossing (measured live): crowded off a laid
        /// bridge's ramp, a rifleman slid along the deck's side over the river bed. 🔴 Red-proof: drop the rule in <c>FinishMove</c> ⇒ he walks the river bed to y 30.
        /// </summary>
        [Theory]
        [InlineData(true, VehicleClass.Pedestrian)]
        [InlineData(false, VehicleClass.Pedestrian)]
        [InlineData(true, VehicleClass.Tank)]
        [InlineData(false, VehicleClass.Tank)]
        public void CE4695_APerson_NeverWadesIntoARiver_FromDryGround(bool river, VehicleClass cls)
        {
            string water = river
                ? """,{"type":"Feature","properties":{"kind":"surface","surface":"water"},"geometry":{"type":"Polygon","coordinates":[[[-20,10],[20,10],[20,20],[-20,20],[-20,10]]]}}"""
                : "";
            var world = Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[-50,-50,50,50],"groundZ":0},"features":[""" +
                """{"type":"Feature","properties":{"kind":"surface","surface":"forest"},"geometry":{"type":"Polygon","coordinates":[[[40,40],[45,40],[45,45],[40,45],[40,40]]]}}""" + water + "]}", "river");
            var route = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 30f, 0f) };
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 2f, startSpeed: 0f, facing: SimMath.FromYaw(MathF.PI / 2f),
                vehicle: VehiclePresets.GetPreset(cls));
            pool.RegisterTrajectoryWithKey(route, 9);
            ref var tf0 = ref repo.GetComponentRW<SimTransform>(e);
            tf0.Position = route[0];
            ref var nav0 = ref repo.GetComponentRW<NavState>(e);
            nav0.TrajectoryId = 9;
            nav0.FinalDestination = route[^1];
            nav0.ArrivalRadius = 0.5f;
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(world);
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float furthest = 0f;
            for (int i = 0; i < 25 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                furthest = MathF.Max(furthest, repo.GetComponent<SimTransform>(e).Position.Y);
            }
            if (river) Assert.True(furthest < 10f, $"{cls} waded into the river (y {furthest:F2})");
            else Assert.True(furthest > 29f, $"on dry ground the route is walked (y {furthest:F2})");
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
            var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "bt-range");
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
    
        // ── ⭐ CE-3173 (M-1..M-3, docs/DESIGN_Entity_Control.md §6, §8b) — KinematicsMode.Manual: the operator drives the body ──

        private static (EntityRepository Repo, Entity E) ManualWorld(VehicleClass cls, Fdp.Toolkit.Navigation.ManualDrive? drive)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            repo.RegisterComponent<Fdp.Toolkit.Navigation.ManualDrive>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 1f / 60f, TimeScale = 1.0f });
            var e = repo.CreateEntity();
            repo.AddComponent(e, new VehicleState());
            repo.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = SimMath.FromYaw(0f) });   // facing +X
            repo.SetAuthority<SimTransform>(e, true);
            repo.AddComponent(e, new SimVelocity());
            var p = VehiclePresets.GetPreset(cls);
            if (cls != VehicleClass.Pedestrian) { p.MaxSpeedRev = 5f; p.MaxSteerRate = 1f; }
            repo.AddComponent(e, p);
            repo.AddComponent(e, new NavState { Mode = KinematicsMode.Manual });
            if (drive is { } d) repo.AddComponent(e, d);
            return (repo, e);
        }

        /// <summary>The smallest turning radius <paramref name="p"/> may drive at <paramref name="speed"/>: full lock (L / tan δ) or v² / a, whichever is wider.</summary>
        private static float MinRadius(in VehicleParams p, float speed)
            => MathF.Max(p.WheelBase / MathF.Tan(p.MaxSteerAngle), speed * speed / p.MaxLatAccel);

        /// <summary>Steps the kinematics frame by frame and returns the tightest radius / allowed-radius ratio seen above 3 m/s, and the top speed.</summary>
        private static (float WorstRatio, float TopSpeed) TurnAgainstTheLimit(EntityRepository repo, Entity e, float seconds, TrajectoryPoolManager? pool = null)
        {
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool ?? new TrajectoryPoolManager());
            var p = repo.GetComponent<VehicleParams>(e);
            float worst = float.MaxValue, top = 0f;
            for (int i = 0; i < (int)(seconds * 60); i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                float v = MathF.Abs(repo.GetComponent<VehicleState>(e).Speed);
                float w = MathF.Abs(repo.GetComponent<SimVelocity>(e).Angular.Z);
                top = MathF.Max(top, v);
                if (v < 3f || w < 1e-4f) continue;
                worst = MathF.Min(worst, v / w / MinRadius(in p, v));
            }
            return (worst, top);
        }

        /// <summary>
        /// ⭐ CE-4345 — a POSSESSED vehicle on full lock and full throttle: at every speed its turning radius is at least the type's minimum for that
        /// speed — full lock's L / tan δ when slow, v² / a_lat when fast (it turns wide at speed, as tightly as ever at a crawl). 🔴 Mutation: no
        /// steer limit in the integrator ⇒ full lock at 20+ m/s ⇒ a radius far under v² / a ⇒ red.
        /// </summary>
        [Fact]
        public void CE4345_APossessedVehicle_TurnsWiderAtSpeed_NeverUnderTheTypesMinimumRadius()
        {
            var (repo, e) = ManualWorld(VehicleClass.PersonalCar, new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f, Steer = 1f });
            var (worst, top) = TurnAgainstTheLimit(repo, e, 12f);
            Assert.True(top > 12f, $"anti-vacuity: the car must reach speed (top {top:0.0} m/s)");
            Assert.True(worst >= 0.97f, $"the radius fell to {worst:0.00}× the type's minimum for its speed");
        }

        /// <summary>
        /// ⭐ CE-4345 — the AI drives through the SAME integrator: a car at 18 m/s told to follow a path at right angles to its heading turns no
        /// tighter than v² / a_lat (it used to slew to full lock at once, the cornering-speed cap only braking it afterwards). 🔴 Same mutation ⇒ red.
        /// </summary>
        [Fact]
        public void CE4345_AnAiVehicle_TurnsWiderAtSpeed_NeverUnderTheTypesMinimumRadius()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 18f, startSpeed: 18f, facing: FacingNorth, startY: -40f);
            var (worst, top) = TurnAgainstTheLimit(repo, e, 4f, pool);
            Assert.True(top > 15f, $"anti-vacuity: it turns at speed (top {top:0.0} m/s)");
            Assert.True(worst >= 0.97f, $"the radius fell to {worst:0.00}× the type's minimum for its speed");
        }

        private static void Run(EntityRepository repo, float seconds)
        {
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            for (int i = 0; i < (int)(seconds * 60); i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
        }

        private static float YawOf(EntityRepository repo, Entity e)
        {
            var f = Vector3.Transform(Vector3.UnitX, repo.GetComponent<SimTransform>(e).Rotation);
            return MathF.Atan2(f.Y, f.X);
        }

        [Fact]
        public void CE3173_APerson_Strafes_SidewaysAtSixtyPercent_WithoutTurning()
        {
            var (repo, e) = ManualWorld(VehicleClass.Pedestrian, new Fdp.Toolkit.Navigation.ManualDrive { Lateral = 1f, Yaw = 0f });
            float top = repo.GetComponent<VehicleParams>(e).MaxSpeedFwd;
            Run(repo, 2f);
            var pos = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(pos.Y > 0.5f, $"moved left (+Y): {pos}");
            Assert.InRange(MathF.Abs(pos.X), 0f, 0.05f);                                  // no forward drift
            Assert.InRange(YawOf(repo, e), -0.01f, 0.01f);                               // still facing +X
            Assert.InRange(repo.GetComponent<SimVelocity>(e).Linear.Y, 0.6f * top - 0.05f, 0.6f * top + 0.05f);
            repo.Dispose();
        }

        [Fact]
        public void CE3173_APerson_TurnsOnTheSpot_ToTheAskedYaw_AndWalksBackSlower()
        {
            var (repo, e) = ManualWorld(VehicleClass.Pedestrian, new Fdp.Toolkit.Navigation.ManualDrive { Yaw = MathF.PI / 2f });
            Run(repo, 0.5f);
            Assert.InRange(YawOf(repo, e), MathF.PI / 2f - 0.01f, MathF.PI / 2f + 0.01f);
            Assert.InRange(new Vector2(repo.GetComponent<SimTransform>(e).Position.X, repo.GetComponent<SimTransform>(e).Position.Y).Length(), 0f, 0.001f);

            float top = repo.GetComponent<VehicleParams>(e).MaxSpeedFwd;
            repo.SetComponent(e, new Fdp.Toolkit.Navigation.ManualDrive { Forward = -1f, Yaw = MathF.PI / 2f });
            Run(repo, 2f);
            Assert.True(repo.GetComponent<SimTransform>(e).Position.Y < -0.5f, "backwards, away from where he faces");
            Assert.InRange(repo.GetComponent<VehicleState>(e).Speed, -0.6f * top - 0.05f, -0.6f * top + 0.05f);   // signed speed
            repo.Dispose();
        }

        [Fact]
        public void CE3173_AVehicle_Steers_ThenBrakesAndReverses()
        {
            var (repo, e) = ManualWorld(VehicleClass.PersonalCar, new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f, Steer = 0.5f });
            Run(repo, 3f);
            Assert.True(repo.GetComponent<VehicleState>(e).Speed > 1f, "throttle moves it");
            Assert.True(YawOf(repo, e) > 0.1f, "steering left turns it left");

            repo.SetComponent(e, new Fdp.Toolkit.Navigation.ManualDrive { Forward = -1f });
            Run(repo, 8f);
            var state = repo.GetComponent<VehicleState>(e);
            Assert.True(state.Speed < -0.5f, $"held back it brakes, then reverses: speed {state.Speed}");
            var v = repo.GetComponent<SimVelocity>(e).Linear;
            var fwd = Vector3.Transform(Vector3.UnitX, repo.GetComponent<SimTransform>(e).Rotation);
            Assert.True(Vector3.Dot(v, fwd) < 0f, "moving backwards");
            repo.Dispose();
        }

        [Fact]
        public void CE3173_ManualWithNoDriveYet_HoldsStill()
        {
            var (repo, e) = ManualWorld(VehicleClass.Pedestrian, drive: null);
            Run(repo, 1f);
            Assert.Equal<System.Numerics.Vector3>(Vector3.Zero, repo.GetComponent<SimTransform>(e).Position);
            repo.Dispose();
        }

        // ── ⭐ CE-3181 (M-5, docs/DESIGN_Entity_Control.md §6) — a manual person never leaves the walkable navmesh ──

        /// <summary>The walkable mesh is x ≤ 5. <c>clamp</c>: off it projects to the nearest mesh point, as Recast's nearest-poly
        /// does (the wall case); otherwise off it has no projection at all, as a blocked polygon in the fake navmesh.</summary>
        private sealed class WallAtFive : Fdp.Toolkit.Navigation.INavmeshProvider
        {
            private readonly bool _clamp;
            public WallAtFive(bool clamp) => _clamp = clamp;
            public bool IsWalkable(Vector3 p, uint layerMask = 0xFFFFFFFF) => p.X <= 5f;
            public bool ProjectToNavmesh(Vector3 p, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
            {
                if (p.X <= 5f) { snapped = p; return true; }
                snapped = _clamp ? new Vector3(5f, p.Y, p.Z) : p;
                return _clamp;
            }
            public int SampleNavmeshPoints(Vector3 c, float r, Span<Vector3> results, uint layerMask = 0xFFFFFFFF) => 0;
            public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => false;
            public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => float.PositiveInfinity;
            public uint QueryVersion() => 0;
            public int PlanPath(Vector3 from, Vector3 to, Span<Fdp.Toolkit.Navigation.NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF) => 0;
        }

        [Fact]
        public void CE3181_APersonWalkingIntoAWall_SlidesAlongIt_NeverThrough()
        {
            var (repo, e) = ManualWorld(VehicleClass.Pedestrian,
                new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f, Yaw = MathF.PI / 4f });   // north-east, into the wall
            repo.SetSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>(new WallAtFive(clamp: true));
            Run(repo, 7f);                                                     // ~1 m/s at 45°: the wall is reached, then followed
            var atWall = repo.GetComponent<SimTransform>(e).Position;
            Run(repo, 4f);
            var pos = repo.GetComponent<SimTransform>(e).Position;
            Assert.InRange(pos.X, 4.9f, 5f + CarKinem.Controllers.ManualMotion.OnMeshTolerance);   // at the wall, not through it
            Assert.True(pos.Y > atWall.Y + 1.5f, $"slid along the wall (+Y): {atWall} -> {pos}");  // kept moving where the mesh allows
            repo.Dispose();
        }

        [Fact]
        public void CE3181_AStepWithNoMeshUnderIt_IsRefused_AndABodyAlreadyOffTheMesh_MovesFreely()
        {
            var (repo, e) = ManualWorld(VehicleClass.Pedestrian, new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f, Yaw = 0f });
            repo.SetSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>(new WallAtFive(clamp: false));
            Run(repo, 10f);
            Assert.InRange(repo.GetComponent<SimTransform>(e).Position.X, 4.8f, 5f);   // stopped at the edge
            repo.Dispose();

            (repo, e) = ManualWorld(VehicleClass.Pedestrian, new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f, Yaw = 0f });
            repo.SetSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>(new WallAtFive(clamp: false));
            var tf = repo.GetComponent<SimTransform>(e);
            tf.Position = new Vector3(10f, 0f, 0f);                                     // spawned off the mesh
            repo.SetComponent(e, tf);
            Run(repo, 1f);
            Assert.True(repo.GetComponent<SimTransform>(e).Position.X > 10.5f, "not trapped where it already stood");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-1208</c> (Entity Control §8b; R-265) — the operator's WALK is his type's walk, not half his run: Gait 0 runs at the top
        /// (the pedestrian preset's 6 m/s sprint), Gait 1 walks at <c>HumanGait.WalkSpeed</c> (1.5) when the type states none, at the
        /// type's own <c>WalkSpeed</c> when it does, and never above the run. 🔴 Red before: Gait 1 = top × 0.5 (3 m/s here).
        /// </summary>
        [Theory]
        [InlineData(0, 0f, 6.0f)]     // run: the top
        [InlineData(1, 0f, 1.5f)]     // walk: the engine's default
        [InlineData(1, 1.2f, 1.2f)]   // walk: the type's own
        [InlineData(1, 9f, 6.0f)]     // a walk stated above the run is held to the run
        public void CE1208_TheOperatorsWalk_IsHisTypesWalk_AndHisRunIsHisTop(byte gait, float typeWalk, float expected)
        {
            var (repo, e) = ManualWorld(VehicleClass.Pedestrian, new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f, Yaw = 0f, Gait = gait });
            var p = repo.GetComponent<VehicleParams>(e);
            p.WalkSpeed = typeWalk;
            repo.SetComponent(e, p);
            Run(repo, 5f);
            var v = repo.GetComponent<SimVelocity>(e).Linear;
            Assert.Equal(expected, new Vector2(v.X, v.Y).Length(), 1);
            repo.Dispose();
        }

        // ── ⭐ CE-2304 (Navigation v2 §3.4a) — seven men inside one house: avoidance across storeys, and through walls ──

        /// <summary>A man standing still at <paramref name="at"/>, in the collider hash (as every person is).</summary>
        private static Entity Stander(EntityRepository repo, Vector3 at)
        {
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>())
                repo.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            var n = repo.CreateEntity();
            repo.AddComponent(n, new VehicleState());
            repo.AddComponent(n, new SimTransform { Position = at, Rotation = SimMath.FacingEast });
            repo.AddComponent(n, new SimVelocity());
            repo.AddComponent(n, new NavState { Mode = KinematicsMode.None });
            repo.AddComponent(n, VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            repo.AddComponent(n, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 0.4f, CollisionLayer = 1 });
            return n;
        }

        /// <summary>
        /// ⭐ <c>CE-2304</c> — a man on ANOTHER STOREY is not avoided: the walker passes over the man standing in the room below without
        /// a swerve; the same man on his own floor makes him give way (which proves the encounter is close enough to matter).
        /// ✅ Red-proof: drop the storey test in <c>ApplyCollisionAvoidance</c> ⇒ the walker swerves for the man below.
        /// </summary>
        [Theory]
        [InlineData(-3f, false)]
        [InlineData(0f, true)]
        public void CE2304_AWalker_GivesWayToAManOnHisFloor_NeverToOneOnTheFloorBelow(float dz, bool givesWay)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            Stander(repo, new Vector3(4f, 0.3f, dz));
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float worst = 0f;
            for (int i = 0; i < 8 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                worst = MathF.Max(worst, MathF.Abs(repo.GetComponent<SimTransform>(e).Position.Y));
            }
            float x = repo.GetComponent<SimTransform>(e).Position.X;
            if (givesWay) Assert.True(worst > 0.2f || x < 6f, $"a man on his own floor in his way changed nothing: swerve {worst:F2} m, at x {x:F2}");
            else Assert.True(x > 6f && worst < 0.05f, $"the man on the floor below held him up: swerve {worst:F2} m, at x {x:F2}");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>The walkable mesh is y ≤ 0.5 (a wall along the path's north side); off it projects onto the wall, as Recast's nearest-poly does.</summary>
        private sealed class WallNorthOfThePath : Fdp.Toolkit.Navigation.INavmeshProvider
        {
            public bool IsWalkable(Vector3 p, uint layerMask = 0xFFFFFFFF) => p.Y <= 0.5f;
            public bool ProjectToNavmesh(Vector3 p, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
            {
                snapped = p.Y <= 0.5f ? p : new Vector3(p.X, 0.5f, p.Z);
                return true;
            }
            public int SampleNavmeshPoints(Vector3 c, float r, Span<Vector3> results, uint layerMask = 0xFFFFFFFF) => 0;
            public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => false;
            public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => float.PositiveInfinity;
            public uint QueryVersion() => 0;
            public int PlanPath(Vector3 from, Vector3 to, Span<Fdp.Toolkit.Navigation.NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF) => 0;
        }

        /// <summary>
        /// ⭐ <c>CE-2304</c> — a walker the men beside him push off his path is never pushed THROUGH A WALL: his step stays on the walkable
        /// mesh (CE-3181's rule, now for an AI walker too). 📌 Live: a rifleman ended 1 m outside the house he was posted in.
        /// ✅ Red-proof: drop the <c>KeepOnNavmesh</c> call on the autonomous step ⇒ he ends north of the wall.
        /// </summary>
        [Fact]
        public void CE2304_AWalkerPushedByTheMenBesideHim_StaysInsideTheWall()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            repo.SetSingletonManaged<Fdp.Toolkit.Navigation.INavmeshProvider>(new WallNorthOfThePath());
            for (float x = 2f; x <= 9f; x += 0.7f) Stander(repo, new Vector3(x, -0.6f, 0f));   // a file of men along his south side
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float north = float.MinValue;
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                north = MathF.Max(north, repo.GetComponent<SimTransform>(e).Position.Y);
            }
            Assert.True(north > 0.3f, $"the men beside him pushed him north ({north:F2} m) — else this proves nothing");
            Assert.True(north <= 0.5f + CarKinem.Controllers.ManualMotion.OnMeshTolerance, $"pushed {north:F2} m north, through the wall at 0.5 m");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-2304</c> (N3) — a walker shoved by the men beside him is never pushed THROUGH A BUILDING'S WALL, even with no navmesh to
        /// hold him: his bent step is traced against closed barriers like a vehicle's (CE-2198). 📌 Live a rifleman with no move at all
        /// was shoved 1.3 m out through a house's wall — the navmesh stops a body radius short of the wall, so in that margin he was free.
        /// ✅ Red-proof: drop the <c>bentWalker</c> trace ⇒ he ends inside the wall line.
        /// </summary>
        [Fact]
        public void CE2304_AWalkerShovedAtABuildingsWall_NeverGoesThroughIt()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            const string walls = "\"walls\":[{\"from\":[0,0],\"to\":[30,0]},{\"from\":[30,0],\"to\":[30,10]},{\"from\":[30,10],\"to\":[0,10]},{\"from\":[0,10],\"to\":[0,0]}]";
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(   // a house whose south wall runs 0.5 m north of his path
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"H\",\"building\":{\"footprint\":[[0,0],[30,0],[30,10],[0,10]]," +
                "\"storeys\":[{\"height\":3," + walls + "}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[-5,0.5]}}]}"));
            for (float x = 2f; x <= 9f; x += 0.6f) Stander(repo, new Vector3(x, -0.45f, 0f));   // a file of men pressing him north
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float north = float.MinValue;
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                north = MathF.Max(north, repo.GetComponent<SimTransform>(e).Position.Y);
            }
            Assert.True(north > 0.2f, $"the men beside him pushed him north ({north:F2} m) — else this proves nothing");
            Assert.True(north < 0.5f, $"pushed {north:F2} m north, through the wall at 0.5 m");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-2450</c> (behaviors' note: a man inside a house held still on a short planned path) — a walker whose step avoidance bends INTO a
        /// building's wall takes his PATH's step instead (the CE-6014 rule for a hull, now for a person): he walks on along his path, never through the
        /// wall. 📌 Live (<c>pc-defend-house</c>): a rifleman 0.38 m from his path, beside a wall, was refused every step for minutes — speed 0 with
        /// acceleration asked, turning on the spot — while his neighbour walked 14 m. 🔴 Red-proof: without the path-step retry he stands at the wall.
        /// </summary>
        [Fact]
        public void CE2450_AWalkerAvoidanceBendsIntoAWall_TakesHisPathsStep_AndWalksOn()
        {
            var (openEast, _) = WalkPastAFileOfMen(house: false);
            var (houseEast, north) = WalkPastAFileOfMen(house: true);
            Assert.True(openEast > 1.5f, $"without the house the men only slow him ({openEast:F2} m in 10 s) — else this proves nothing");
            Assert.True(north < 0.5f, $"pushed {north:F2} m north, through the wall at 0.5 m");
            Assert.True(houseEast > 0.6f * openEast, $"he stood at the wall: {houseEast:F2} m along his path in 10 s (in the open: {openEast:F2} m)");
        }

        /// <summary>CE-2450's walk: 10 s on his path east along a file of men 0.45 m to his south; the house's south wall (when present) 0.5 m to his north.</summary>
        private (float East, float North) WalkPastAFileOfMen(bool house)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            if (house)
            {
                const string walls = "\"walls\":[{\"from\":[0,0],\"to\":[30,0]},{\"from\":[30,0],\"to\":[30,10]},{\"from\":[30,10],\"to\":[0,10]},{\"from\":[0,10],\"to\":[0,0]}]";
                repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
                repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                    "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
                    "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"H\",\"building\":{\"footprint\":[[0,0],[30,0],[30,10],[0,10]]," +
                    "\"storeys\":[{\"height\":3," + walls + "}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[-5,0.5]}}]}"));
            }
            for (float x = 1f; x <= 25f; x += 0.6f) Stander(repo, new Vector3(x, -0.45f, 0f));
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            float north = float.MinValue;
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                north = MathF.Max(north, repo.GetComponent<SimTransform>(e).Position.Y);
            }
            float east = repo.GetComponent<SimTransform>(e).Position.X;
            pool.Dispose();
            repo.Dispose();
            return (east, north);
        }

        /// <summary>⭐ AQ104 I (R-2079; behaviors' handoff item 1) — a person ARRIVES only on the destination's level. 📐 Peek and Fire
        /// §12.7: the attacker stood on the ground floor under the window man (z 3) and was "arrived" for 160 s — arrival was 2-D. ⭐ The
        /// same point on his own floor still arrives (the control); on a path and driven direct.</summary>
        [Theory]
        [InlineData(KinematicsMode.CustomTrajectory, 3f, false)]
        [InlineData(KinematicsMode.CustomTrajectory, 0f, true)]
        [InlineData(KinematicsMode.Direct, 3f, false)]
        [InlineData(KinematicsMode.Direct, 0f, true)]
        public void AQ104I_APersonUnderTheirDestination_OnAnotherFloor_HasNotArrived(KinematicsMode mode, float destZ, bool arrives)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            const string walls = "\"walls\":[{\"from\":[0,0],\"to\":[10,0]},{\"from\":[10,0],\"to\":[10,10]},{\"from\":[10,10],\"to\":[0,10]},{\"from\":[0,10],\"to\":[0,0]}]";
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(   // a two-storey house, a slab at z 3
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"H\",\"building\":{\"footprint\":[[0,0],[10,0],[10,10],[0,10]]," +
                "\"storeys\":[{\"height\":3," + walls + "},{\"height\":3," + walls + "}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[-5,0.5]}}]}"));
            var at = new Vector3(5f, 5f, 0f);
            var dest = new Vector3(5f, 5.2f, destZ);
            pool.RegisterTrajectoryWithKey(new[] { at, dest }, 8);   // the planner's short move: the two points as asked
            ref var tf0 = ref repo.GetComponentRW<SimTransform>(e);
            tf0.Position = at;
            ref var nav0 = ref repo.GetComponentRW<NavState>(e);
            nav0.Mode = mode;
            nav0.TrajectoryId = 8;
            nav0.FinalDestination = dest;
            nav0.ArrivalRadius = 0.5f;
            var spatial = new SpatialHashSystem();
            var kin     = new CarKinematicsSystem(pool);
            for (int i = 0; i < 3 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
            var end = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(end.Z < 0.5f, $"{mode}: still on the ground floor (z {end.Z:F2}) — else this proves nothing");
            Assert.Equal(arrives ? 1 : 0, repo.GetComponent<NavState>(e).HasArrived);
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>⭐ <c>CE-3110</c> I-2c (R-271) — kinematics does not drive an EMBARKED entity (the carry owns its transform): the same
        /// moving body that advances when free stays exactly where the carry left it once it carries <c>IsEmbarkedTag</c>.</summary>
        [Fact]
        public void CE3110_I2c_AnEmbarkedBody_IsNotDrivenByKinematics()
        {
            var (free, f) = OnTheSlope(new Vector3(10, 2, 0), new Vector3(0, 10, 0));
            Drive(free, 10);
            Assert.True(free.GetComponent<SimTransform>(f).Position.Y > 2.5f, "control: a free body moves");

            var (aboard, a) = OnTheSlope(new Vector3(10, 2, 0), new Vector3(0, 10, 0));
            aboard.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            aboard.AddComponent(a, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag());
            Drive(aboard, 10);
            Assert.Equal<System.Numerics.Vector3>(new Vector3(10, 2, 0), aboard.GetComponent<SimTransform>(a).Position);
        }

        // ── ⭐ CE-2167 (V5, docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §5) — an AI vehicle can REVERSE ────────────

        /// <summary>
        /// ⭐⭐ <c>CE-2167</c> (V5) — an AI tank given a point straight BEHIND it, reverse allowed, backs to it and keeps facing the way it
        /// faced (out of a firing position into the hide). 🔴 Red-proof: <c>BicycleModel</c> clamped speed at 0 — the kinematics asked
        /// for reverse and the tank stood still.
        /// </summary>
        [Fact]
        public void V5_AnAiTank_ReversesToAPointBehindIt_FacingUnchanged()
        {
            var (repo, e) = ManualWorld(VehicleClass.Tank, null);
            repo.SetComponent(e, new NavState
            {
                Mode = KinematicsMode.Direct, FinalDestination = new Vector3(-20f, 0f, 0f), TargetSpeed = 6f, ArrivalRadius = 1.5f,
                ReverseAllowed = 1,
            });
            Run(repo, 15f);
            var at = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(at.X < -17f, $"it backed to the point: x {at.X:F1}");
            Assert.InRange(MathF.Abs(at.Y), 0f, 0.5f);
            Assert.InRange(MathF.Abs(YawOf(repo, e)), 0f, 2f * MathF.PI / 180f);   // still facing east
            Assert.Equal(1, repo.GetComponent<NavState>(e).HasArrived);
            repo.Dispose();
        }

        /// <summary>⭐ <c>CE-2167</c> (V5) — backing ALONG a path counts as progress (it froze: the forward vector points against the
        /// tangent while reversing), so a reversing vehicle reaches the end of its path.</summary>
        [Fact]
        public void V5_ReversingAlongAPath_MakesProgress()
        {
            var tank = VehiclePresets.GetPreset(VehicleClass.Tank);
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 6f, startSpeed: 0f, facing: SimMath.FromYaw(MathF.PI), vehicle: tank);
            var nav = repo.GetComponent<NavState>(e);
            nav.ReverseAllowed = 1;
            repo.SetComponent(e, nav);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            for (int i = 0; i < 10 * 60; i++) { spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f); }
            var at = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(at.X > 30f, $"it backed east along the path: x {at.X:F1}");
            Assert.True(repo.GetComponent<NavState>(e).ProgressS > 25f, $"progress {repo.GetComponent<NavState>(e).ProgressS:F1}");
            Assert.InRange(MathF.Abs(MathF.Abs(YawOf(repo, e)) - MathF.PI), 0f, 5f * MathF.PI / 180f);   // still facing west
            pool.Dispose();
            repo.Dispose();
        }

        // ── ⭐ CE-3195 (R-3003, docs/DESIGN_Weapon_Layers.md W-14b–e) — an AIMED person turns his body to the aim ─────────────

        /// <summary>A standing person facing EAST, aimed (his weapon brain's posture) at <paramref name="aimAt"/>.</summary>
        private static (EntityRepository Repo, Entity E) AimedStander(Vector3 aimAt)
        {
            var (repo, e) = ManualWorld(VehicleClass.Pedestrian, null);
            repo.SetComponent(e, new NavState { Mode = KinematicsMode.None });
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture
                { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Point = aimAt });
            return (repo, e);
        }

        private static float Off(EntityRepository repo, Entity e, float bearing)
            => MathF.Abs(CarKinem.Controllers.HumanGait.SignedAngle(
                new Vector2(MathF.Cos(YawOf(repo, e)), MathF.Sin(YawOf(repo, e))), new Vector2(MathF.Cos(bearing), MathF.Sin(bearing))));

        /// <summary>
        /// ⭐⭐ <c>CE-3195</c> (W-14c/e) — a STANDING soldier aimed at a point BEHIND him snaps round at the full turn rate until it is
        /// inside his twist (≈ 0.3 s for 105°), then the body follows smoothly: still well off after a further 0.3 s, within 10° after
        /// 3 s. He does not move. 🔴 Red-proof: before CE-3195 <c>HumanGait</c> turned a person only while he walked — he never turned.
        /// </summary>
        [Fact]
        public void CE3195_AStandingAimedPerson_SnapsInsideHisTwist_ThenAlignsSmoothly()
        {
            var (repo, e) = AimedStander(new Vector3(-20f, 0f, 1f));
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            float inside = -1f;
            for (int i = 0; i < 60 && inside < 0f; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                if (Off(repo, e, MathF.PI) <= CarKinem.Controllers.HumanGait.MaxAimTwist + 0.01f) inside = (i + 1) / 60f;
            }
            Assert.InRange(inside, 0.2f, 0.4f);
            Run(repo, 0.3f);
            Assert.True(Off(repo, e, MathF.PI) > 0.5f, $"a smooth follow, not a snap: {Off(repo, e, MathF.PI) * 180f / MathF.PI:F0}° off after 0.3 s more");
            Run(repo, 3f);
            Assert.True(Off(repo, e, MathF.PI) < 10f * MathF.PI / 180f, $"aligned: {Off(repo, e, MathF.PI) * 180f / MathF.PI:F1}° off");
            Assert.True(new Vector2(repo.GetComponent<SimTransform>(e).Position.X, repo.GetComponent<SimTransform>(e).Position.Y).Length() < 0.01f);
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2163</c> — a soldier who stands and FIRES does not walk backwards. Every frame a fresh round leaves his muzzle 1 m in
        /// front of him at 800 m/s (as <c>FireProcessingSystem</c> spawns it: a collider, so the ballistics can carry it). After 3 s of
        /// fire he is where he stood. 🔴 Red-proof: with rounds in the collider hash (before CE-2163) the avoidance shell shoved him
        /// back on every shot — live, a man who only suppressed crept ~5 m a minute away from the enemy.
        /// </summary>
        [Fact]
        public void CE2163_AStandingSoldier_WhoFires_IsNotPushedBackByHisOwnRounds()
        {
            var (repo, e) = AimedStander(new Vector3(20f, 0f, 1f));   // aimed ahead (east), the way he faces
            repo.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
            repo.AddComponent(e, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 0.4f, CollisionLayer = 1 });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            Entity round = Entity.Null;
            for (int i = 0; i < 180; i++)
            {
                if (!round.IsNull) repo.DestroyEntity(round);
                round = repo.CreateEntity();
                repo.AddComponent(round, new SimTransform { Position = new Vector3(1f, 0f, 1.4f), Rotation = Quaternion.Identity });
                repo.AddComponent(round, new SimVelocity { Linear = new Vector3(800f, 0f, 0f) });
                repo.AddComponent(round, new Fdp.Toolkit.Combat.Components.BallisticProjectile { Shooter = e });
                repo.AddComponent(round, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 0.05f, CollisionLayer = Fdp.Toolkit.Combat.CombatConstants.BulletCollisionLayer });
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
            }
            var at = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(new Vector2(at.X, at.Y).Length() < 0.01f, $"he moved {at.X:F2} m along his facing while firing");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3252</c> (LD-C, R-2049) — a STANDING man told to look 90° to his left turns his BODY to it with the aim's lag (W-14):
        /// a lasting look ends with him facing it. While he AIMS the other way, the aim wins and the look turns nothing.
        /// ✅ Red-proof: drop the <c>lookingPerson</c> branch ⇒ he never turns.
        /// </summary>
        [Fact]
        public void CE3252_AStandingPersonWithALook_TurnsHisBodyToIt_ButTheAimWins()
        {
            var (repo, e) = ManualWorld(VehicleClass.Pedestrian, null);
            repo.SetComponent(e, new NavState { Mode = KinematicsMode.None });
            repo.RegisterComponent<Fdp.Toolkit.Perception.Look.LookState>();
            repo.AddComponent(e, new Fdp.Toolkit.Perception.Look.LookState { Target = new Vector3(0f, 20f, 0f), HasTarget = 1 });
            Run(repo, 4f);
            Assert.True(Off(repo, e, MathF.PI / 2f) < 10f * MathF.PI / 180f, $"faces the look: {Off(repo, e, MathF.PI / 2f) * 180f / MathF.PI:F1}° off");
            Assert.True(new Vector2(repo.GetComponent<SimTransform>(e).Position.X, repo.GetComponent<SimTransform>(e).Position.Y).Length() < 0.01f);

            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture
                { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Point = new Vector3(20f, 0f, 1f) });   // east
            Run(repo, 4f);
            Assert.True(Off(repo, e, 0f) < 10f * MathF.PI / 180f, $"the aim wins: {Off(repo, e, 0f) * 180f / MathF.PI:F1}° off east");
            repo.Dispose();
        }

        /// <summary>⭐ <c>CE-3255</c> (BR-2; DESIGN_Building_Clearing CB-6) — a STUNNED soldier on his path stands still until the stun ends,
        /// then walks on (path and progress kept). ✅ Red-proof: drop the stun line in <c>CarKinematicsSystem</c> ⇒ he walks while stunned.</summary>
        [Fact]
        public void CE3255_AStunnedSoldier_StandsStill_UntilTheStunEnds()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            repo.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            repo.AddComponent(e, new Fdp.Toolkit.Combat.EffectState { StunnedUntil = (SimInstant)(Fdp.Toolkit.Combat.HitModel.Now(repo) + 1000.0) });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            void RunFor(float seconds) { for (int i = 0; i < (int)(seconds * 60); i++) { spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f); } }

            var start = repo.GetComponent<SimTransform>(e).Position;
            RunFor(3f);
            Assert.True(Vector3.Distance(start, repo.GetComponent<SimTransform>(e).Position) < 0.05f, "stunned, he stood");

            repo.GetComponentRW<Fdp.Toolkit.Combat.EffectState>(e).StunnedUntil = (SimInstant)0.0;
            RunFor(3f);
            Assert.True(Vector3.Distance(start, repo.GetComponent<SimTransform>(e).Position) > 1f, "the stun over, he walks on");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>⭐ <c>CE-3195</c> (W-14c) — a BRIEF aim inside the twist barely turns him (a snap shot), and when the posture is no
        /// longer Aimed he stops turning.</summary>
        [Fact]
        public void CE3195_ABriefAimInsideTheTwist_BarelyTurnsHim()
        {
            float bearing = 60f * MathF.PI / 180f;
            var (repo, e) = AimedStander(new Vector3(20f * MathF.Cos(bearing), 20f * MathF.Sin(bearing), 1f));
            Run(repo, 0.3f);
            float turned = YawOf(repo, e);
            Assert.InRange(turned, 0.05f, 0.35f);   // ≈ 26 % of 60°
            repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponPosture>(e).Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready;
            Run(repo, 1f);
            Assert.InRange(YawOf(repo, e), turned - 0.001f, turned + 0.001f);
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3195</c> (W-14d) — a soldier WALKING his path EAST while aimed at a target WEST of him keeps his path, turns to face
        /// the target and BACK-PEDALS at <c>ManualMotion.BackAndStrafeFactor</c> of his speed; the aim lifted, he turns on the spot and
        /// walks on facing his way (R-247). 🔴 Red-proof: before CE-3195 he walked on facing east — firing behind his back.
        /// </summary>
        [Fact]
        public void CE3195_AnAimedPersonOnAPath_KeepsHisWay_FacesTheTarget_AndBackPedals()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 1.5f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
            repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture
                { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Point = new Vector3(-50f, 0f, 1f) });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            void RunFor(float seconds) { for (int i = 0; i < (int)(seconds * 60); i++) { spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f); } }

            RunFor(4f);
            var tf = repo.GetComponent<SimTransform>(e);
            Assert.True(tf.Position.X > 2f, $"kept his way east: {tf.Position}");
            Assert.True(Off(repo, e, MathF.PI) < 15f * MathF.PI / 180f, $"faces the target: {Off(repo, e, MathF.PI) * 180f / MathF.PI:F0}° off");
            float vx = repo.GetComponent<SimVelocity>(e).Linear.X;
            Assert.InRange(vx, 0.6f * 1.5f - 0.1f, 0.6f * 1.5f + 0.1f);   // back-pedalling at 60 %

            repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponPosture>(e).Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready;
            RunFor(2f);
            Assert.True(Off(repo, e, 0f) < 15f * MathF.PI / 180f, "the aim lifted, he faces his way again");
            Assert.InRange(repo.GetComponent<SimVelocity>(e).Linear.X, 1.4f, 1.6f);
            pool.Dispose();
            repo.Dispose();
        }
    
        // ── ⭐ CE-3029 (Navigation v2 §5.2b, R-3006) — a vehicle's corners are rounded for its turn radius ──

        /// <summary>The tt-nav-los Bradley (scenario VehicleParams): WheelBase 3.93, MaxSteer 0.8 (R = 3.8 m), a slow steer rate.</summary>
        private static VehicleParams Bradley() => new()
        {
            Class = VehicleClass.Tank, Length = 6.55f, Width = 3.6f, WheelBase = 3.93f, MaxSpeedFwd = 18f, MaxSpeedRev = 8f, MaxAccel = 3f,
            MaxDecel = 4f, MaxSteerAngle = (Fdp.Core.Angle)(0.8f), MaxSteerRate = 0.34906584f, MaxLatAccel = 6f, AvoidanceRadius = 5f,
            LookaheadTimeMin = 0.8f, LookaheadTimeMax = 2.5f, AccelGain = 1.8f,
        };

        /// <summary>
        /// Drives <paramref name="vehicle"/> along a 90° left corner planned the way Detour plans it — the vertex V = (60, 0) ON the eroded
        /// mesh corner, the building's corner <c>clearance</c> inside it along the bisector — and returns the closest the vehicle's centre
        /// came to the building corner. <paramref name="round"/> rounds the path with <see cref="Fdp.Toolkit.Navigation.CornerRounding"/>.
        /// </summary>
        private static (float Closest, Vector3 End) DriveCorner(VehicleParams vehicle, bool round, float speed, float clearance = 1.8f, float radius = 0f)
        {
            var planned = new[] { new Vector3(0f, 0f, 0f), new Vector3(60f, 0f, 0f), new Vector3(60f, 60f, 0f) };
            var points = new System.Collections.Generic.List<Vector3>(planned);
            if (round)
            {
                var outPts = new System.Collections.Generic.List<Vector3>();
                var outTrav = new System.Collections.Generic.List<byte>();
                Fdp.Toolkit.Navigation.CornerRounding.Round(planned, null,
                    radius > 0f ? radius : Fdp.Toolkit.Navigation.CornerRounding.TurnRadius(vehicle.WheelBase, vehicle.MaxSteerAngle), null, 0xFFFFFFFFu, outPts, outTrav);
                points = outPts;
            }
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: speed, startSpeed: 0f, vehicle: vehicle);
            pool.RegisterTrajectoryWithKey(points.ToArray(), 8);
            ref var nav = ref repo.GetComponentRW<NavState>(e);
            nav.TrajectoryId = 8; nav.FinalDestination = planned[^1];
            var corner = new Vector2(60f, 0f) + Vector2.Normalize(new Vector2(-1f, 1f)) * clearance;
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float closest = float.MaxValue;
            for (int i = 0; i < 60 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var p = repo.GetComponent<SimTransform>(e).Position;
                closest = MathF.Min(closest, Vector2.Distance(new Vector2(p.X, p.Y), corner));
            }
            var end = repo.GetComponent<SimTransform>(e).Position;
            pool.Dispose();
            repo.Dispose();
            return (closest, end);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3029</c> (§5.2b C3 + C7) — the Bradley drives a 90° corner whose building sits 1.8 m inside the planned vertex.
        /// Rounded for its turn radius and followed with the chord-capped lookahead, it keeps (nearly) the clearance the plan had.
        /// 📐 Measured: today's sharp corner 1.22–1.44 m; rounded with the uncapped lookahead 0.47–0.68 m (WORSE — the lookahead
        /// starts the turn early and drives inside any arc); rounded + capped 1.65–1.81 m.
        /// </summary>
        [Theory]
        [InlineData(3f, 1.6f)]
        [InlineData(5f, 1.75f)]
        [InlineData(8f, 1.75f)]
        public void CE3029_ARoundedCorner_IsDrivenWithItsClearance(float speed, float atLeast)
        {
            var (closest, end) = DriveCorner(Bradley(), round: true, speed: speed);
            Assert.True(closest >= atLeast, $"at {speed} m/s the Bradley came {closest:F2} m from the building corner (planned 1.8)");
            Assert.True(end.Y > 55f && MathF.Abs(end.X - 60f) < 3f, $"it reached the end of the path ({end})");
        }

        /// <summary>⭐ C7 — the cap shortens the lookahead across a curve, never along a straight (CE-3115's two wheelbases stand).</summary>
        [Fact]
        public void CE3029_TheLookahead_ShrinksAcrossACurve_NotAlongAStraight()
        {
            using var pool = new TrajectoryPoolManager();
            pool.RegisterTrajectoryWithKey(new[] { new Vector3(0, 0, 0), new Vector3(100, 0, 0) }, 1);
            var outPts = new System.Collections.Generic.List<Vector3>(); var outTrav = new System.Collections.Generic.List<byte>();
            Fdp.Toolkit.Navigation.CornerRounding.Round(new[] { new Vector3(0, 0, 0), new Vector3(60, 0, 0), new Vector3(60, 60, 0) },
                null, 3.8f, null, 0xFFFFFFFFu, outPts, outTrav);
            pool.RegisterTrajectoryWithKey(outPts.ToArray(), 2);
            Assert.Equal(7.86f, CarKinematicsSystem.CapLookaheadByChord(pool, 1, 10f, 7.86f, 1.965f), 3);
            float onCurve = CarKinematicsSystem.CapLookaheadByChord(pool, 2, 56f, 7.86f, 1.965f);
            Assert.True(onCurve < 4f, $"across the corner the lookahead stays {onCurve:F2} m");
        }

        // ── CE-4598 (B-1; docs/DESIGN_Vehicle_Breaching.md §7 V-1, V-4, V-5, V-14, D-2, D-4) — the hull sees every wall ──

        /// <summary>A free wall (a garden wall, a fence) along y = 10 from x = −20 to 80: <paramref name="kind"/> "wall" or "fence".</summary>
        private static Fdp.Toolkit.Terrain.TerrainWorld GardenWall(float height, string kind = "wall", string material = "brick", float thickness = 0.3f)
            => Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"" + kind + "\",\"label\":\"garden\",\"height\":" +
                height.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"thickness\":" +
                thickness.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"material\":\"" + material + "\"}," +
                "\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[-20,10],[80,10]]}}]}");

        /// <summary>The northmost corner (y) and the highest Z an AI tank driven north at the free wall reaches in 15 s.</summary>
        private static (float NorthCorner, float TopZ) DriveAtTheWall(Fdp.Toolkit.Terrain.TerrainWorld world)
        {
            var (repo, e, pool) = HullWorld(new Vector2(30f, -15f), FacingNorth, new[] { new Vector3(30f, -15f, 0f), new Vector3(30f, 25f, 0f) },
                                            mode: KinematicsMode.Direct, recovery: false, world: world);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float north = float.MinValue, topZ = 0f;
            for (int i = 0; i < 15 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                north = MathF.Max(north, NorthCorner(repo, e));
                topZ = MathF.Max(topZ, repo.GetComponent<SimTransform>(e).Position.Z);
            }
            pool.Dispose();
            repo.Dispose();
            return (north, topZ);
        }

        /// <summary>The y of the hull's northmost corner.</summary>
        private static float NorthCorner(EntityRepository repo, Entity e)
        {
            var p = repo.GetComponent<VehicleParams>(e);
            var tf = repo.GetComponent<SimTransform>(e);
            var f = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            var fwd = Vector2.Normalize(new Vector2(f.X, f.Y));
            var c = new Vector2(tf.Position.X, tf.Position.Y);
            float north = float.MinValue;
            foreach (var (a, b) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                north = MathF.Max(north, CarKinem.Controllers.HullClearance.Corner(c, fwd, a * p.Length * 0.5f, b * p.Width * 0.5f).Y);
            return north;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4598</c> (B-1, V-1, D-2) — an AI tank driven at a garden wall, a fence and a hedge-high wall is HELD at it: no corner ever
        /// past the wall. 📌 The user's observation: every hull drove through every garden wall — the guard's barrier was the blast meaning
        /// (a panel WITH a building), blind to free walls. 🔴 Red-proof: trace the guard with <c>TracePurpose.Fire</c> again ⇒ the tank drives
        /// through all three (the corner reaches y ≈ 28).
        /// </summary>
        [Theory]
        [InlineData(1.0f, "wall", "brick", 0.3f)]     // a brick garden wall
        [InlineData(1.6f, "fence", "fence-wood", 0.05f)]   // a fence
        [InlineData(1.5f, "wall", "brick", 0.6f)]     // hedge-high
        public void CE4598_AnAiHull_IsHeldAtAFreeWall_NeverThroughIt(float height, string kind, string material, float thickness)
        {
            var (north, _) = DriveAtTheWall(GardenWall(height, kind, material, thickness));
            Assert.True(north <= 10f - thickness / 2f + 0.05f, $"a corner reached y={north:F2} — through the {kind} at y=10");
            Assert.True(north >= 10f - thickness / 2f - 0.8f, $"stopped short: the nose reached only y={north:F2}");   // it drove up to it
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4598</c> (B-1, D-4) — a LOW garden wall (0.4 m, under the 0.5 m step a hull climbs) is MET, never climbed: the hull stays on
        /// the ground and never crosses it. 🔴 Red-proof: let <c>HullSurfaceZ</c> consider a panel's top ⇒ the tank stands on the wall
        /// (z 0.4) and drives on over it; drop the hull band (<c>QueryHull</c>'s top + band) ⇒ the 0.5 m probe line runs over the wall.
        /// </summary>
        [Fact]
        public void CE4598_ALowGardenWall_IsMet_NeverClimbed()
        {
            var (north, topZ) = DriveAtTheWall(GardenWall(0.4f));
            Assert.True(topZ < 0.05f, $"the hull climbed onto the wall: z={topZ:F2}");
            Assert.True(north <= 10f - 0.15f + 0.05f, $"a corner reached y={north:F2} — over the 0.4 m wall");
        }

        /// <summary>
        /// ⭐⭐ <c>R-4280</c> (R-5000 changed — <i>"vehicles should be able to cross the 10cm curb"</i>) — a tank drives over a 10 cm edging (a wall
        /// panel lower than <c>HullTrace.MinPanelHeightM</c>) and stays on the ground. 🔴 Red-proof: drop the kerb skip in <c>QueryFireCore</c> ⇒ held at y ≈ 10.
        /// </summary>
        [Fact]
        public void R4280_ATank_DrivesOverATenCentimetreKerb()
        {
            var (north, topZ) = DriveAtTheWall(GardenWall(0.1f, thickness: 0.15f));
            Assert.True(north > 14f, $"held at the kerb: the nose reached only y={north:F2}");
            Assert.True(topZ < 0.05f, $"the hull stood on the kerb: z={topZ:F2}");
        }

        /// <summary>
        /// ⭐⭐ <c>R-4280</c> — a possessed CAR drives over a 10 cm kerb; the same car is still held by a 1 m garden wall (the possessed-car rail below).
        /// 🔴 Red-proof: as above.
        /// </summary>
        [Theory]
        [InlineData(0.1f, true)]
        [InlineData(1.0f, false)]
        public void R4280_APossessedCar_CrossesAKerb_ButNotAGardenWall(float height, bool crosses)
        {
            var (repo, e) = ManualWorld(VehicleClass.PersonalCar, new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f });
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"wall\",\"label\":\"edge\",\"height\":" + height.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"thickness\":0.15,\"material\":\"concrete\"},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[15,-20],[15,20]]}}]}"));
            float east = float.MinValue;
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            var p = repo.GetComponent<VehicleParams>(e);
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                east = MathF.Max(east, repo.GetComponent<SimTransform>(e).Position.X + p.Length * 0.5f);
            }
            repo.Dispose();
            if (crosses) Assert.True(east > 20f, $"held at the {height} m kerb: nose x={east:F2}");
            else Assert.True(east <= 15f - 0.075f + 0.05f, $"through the {height} m wall: nose x={east:F2}");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4598</c> (B-1, V-1, acceptance §7.5 item 11) — a POSSESSED wheeled vehicle at full throttle into a garden wall is held at it
        /// too (the operator's drive goes through the same guard). 📌 The user saw exactly this one drive through. 🔴 Red-proof: as above.
        /// </summary>
        [Fact]
        public void CE4598_APossessedCar_AtFullThrottle_IsHeldAtAGardenWall()
        {
            var (repo, e) = ManualWorld(VehicleClass.PersonalCar, new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f });
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"wall\",\"label\":\"garden\",\"height\":1.0,\"thickness\":0.3,\"material\":\"brick\"}," +
                "\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[15,-20],[15,20]]}}]}"));
            float east = float.MinValue;
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            var p = repo.GetComponent<VehicleParams>(e);
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                east = MathF.Max(east, repo.GetComponent<SimTransform>(e).Position.X + p.Length * 0.5f);
            }
            Assert.True(east <= 15f - 0.15f + 0.05f, $"the nose reached x={east:F2} — through the wall at x=15");
            Assert.True(east >= 15f - 0.15f - 1.5f, $"never reached the wall: nose x={east:F2}");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4598</c> (B-1, V-5, acceptance §7.5 item 13) — a tank driven at a BUILDING wall straight through a Passage breach (its own
        /// bump, B-D4) is still held outside: a hull never enters a building. 🔴 Red-proof: trace the guard with <c>TracePurpose.Fire</c> ⇒ it
        /// drives in through the breach (the fire purpose skips breached crossings).
        /// </summary>
        [Fact]
        public void CE4598_ATankAtABuildingBreach_StaysOut()
        {
            var w = Fdp.Toolkit.Terrain.Tests.BreachFixtures.House();   // the brick house: south wall x 20..30 at y = 20
            int south = Fdp.Toolkit.Terrain.Tests.BreachFixtures.SouthWall(w);
            var (repo, e, pool) = HullWorld(new Vector2(25f, 5f), FacingNorth, new[] { new Vector3(25f, 5f, 0f), new Vector3(25f, 26f, 0f) },
                                            mode: KinematicsMode.Direct, recovery: false, world: w);
            repo.RegisterComponent<Fdp.Toolkit.Terrain.BuildingDamage>();
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainObjectKey>();
            Fdp.Toolkit.Terrain.Tests.BreachFixtures.Damage(repo, w, 0,
                new Fdp.Toolkit.Terrain.WallBreach(south, 0.5f, 9.5f, 0f, 3f, Fdp.Toolkit.Terrain.BreachKind.Passage));   // the whole wall open
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float north = float.MinValue;
            for (int i = 0; i < 15 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                north = MathF.Max(north, NorthCorner(repo, e));
            }
            Assert.True(north <= 20f - 0.15f + 0.05f, $"a corner reached y={north:F2} — inside the house through its breach");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4598</c> (B-1, V-4, V-14; acceptance §7.5 item 15) — the break gate: only a hull on the Manual branch, held by a LIVE Fps
        /// possession, outside a replay. A RELEASED hull keeps its <c>ManualDrive</c> (<c>Possession.LetGoOfManual</c>) and may not break; a
        /// rider holds his own body, not the hull; a replay re-decides nothing. 🔴 Red-proof: gate on <c>HasComponent&lt;ManualDrive&gt;</c>
        /// ⇒ the released row passes; drop the replay test ⇒ the replay row passes.
        /// </summary>
        [Fact]
        public void CE4598_TheBreakGate_IsALivePlayerDriving_OnTheManualBranch_NeverInAReplay()
        {
            var (repo, e) = ManualWorld(VehicleClass.Tank, new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f });
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>();
            bool May() => CarKem_MayBreak(repo, e);

            Assert.False(May());                                                              // Manual, ManualDrive kept, nobody holds it (released)
            repo.AddComponent(e, new Fdp.Toolkit.Behavior.Possession.PossessedBy { StationNodeId = (Fdp.Core.StationId)3, Mode = Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps, Epoch = 1 });
            Assert.True(May());                                                               // a player drives it
            repo.SetComponent(e, new Fdp.Toolkit.Behavior.Possession.PossessedBy { StationNodeId = (Fdp.Core.StationId)3, Mode = Fdp.Toolkit.Behavior.Possession.PossessionMode.Riding, Epoch = 2 });
            Assert.False(May());                                                              // a rider: not the hull's controls
            repo.SetComponent(e, new Fdp.Toolkit.Behavior.Possession.PossessedBy { StationNodeId = (Fdp.Core.StationId)3, Mode = Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps, Epoch = 3 });
            var nav = repo.GetComponent<NavState>(e); nav.Mode = KinematicsMode.Direct; repo.SetComponent(e, nav);
            Assert.False(May());                                                              // an autonomous move (the AI corner)
            nav.Mode = KinematicsMode.Manual; repo.SetComponent(e, nav);
            Fdp.Toolkit.Replay.ReplayState.Enter(repo);
            Assert.False(May());                                                              // a replay
            Fdp.Toolkit.Replay.ReplayState.Leave(repo);
            Assert.True(May());
            repo.RemoveComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(e);
            Assert.False(May());                                                              // released again
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4622</c> K-2 (Checkpoint RM-5, amends Breaching B-D1) — an AI hull (an autonomous move, nobody holds it) may break a barrier ONLY while it carries an ACTIVE
        /// <c>RamOrder</c>; switched off it may not; never in a replay. 🔴 Red-proof: drop the RamOrder test from <c>HullBreakGate.MayBreak</c> ⇒ the ramming row is false.
        /// </summary>
        [Fact]
        public void CE4622_K2_ARamOrder_OpensTheBreakGate_ToAnAiHull_NeverInAReplay()
        {
            var (repo, e) = ManualWorld(VehicleClass.Tank, null);
            var nav = repo.GetComponent<NavState>(e); nav.Mode = KinematicsMode.Direct; repo.SetComponent(e, nav);   // an autonomous move
            repo.RegisterComponent<RamOrder>();
            bool May() => CarKem_MayBreak(repo, e);
            Assert.False(May());                                                              // no order
            repo.AddComponent(e, new RamOrder { Active = 1, Crowd = 1 });
            Assert.True(May());                                                               // ramming
            Fdp.Toolkit.Replay.ReplayState.Enter(repo);
            Assert.False(May());                                                              // a replay
            Fdp.Toolkit.Replay.ReplayState.Leave(repo);
            repo.SetComponent(e, new RamOrder { Active = 0, Crowd = 1 });
            Assert.False(May());                                                              // the order switched off
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-6014</c> (found by CE-4598 B-1) — avoidance never steers a hull INTO a wall: a tank driving its path east past a man 1 m off its
        /// line, a garden wall 0.2 m beyond its other side, keeps its path (the swerve that would hit the wall is dropped) and drives on — never held,
        /// no corner past the wall. 📌 RoadblockBlastScenarioTests' IFV swerved round a man down and through a wall; with B-1 it was held there for
        /// good. 🔴 Red-proof: drop the fallback to the path step ⇒ the tank stops at the wall (it never passes x = 60).
        /// </summary>
        [Fact]
        public void CE6014_AvoidanceNeverSteersAHullIntoAWall_TheTankKeepsItsPath()
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 5f, vehicle: VehiclePresets.GetPreset(VehicleClass.Tank));
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"wall\",\"label\":\"garden\",\"height\":1.0,\"thickness\":0.3,\"material\":\"brick\"}," +
                "\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[-10,-2.1],[110,-2.1]]}}]}"));
            Stander(repo, new Vector3(25f, 1f, 0f));   // the man the tank swerves south for (CE3287_ADrivingVehicle_StillSteersRoundAMan)
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            var p = repo.GetComponent<VehicleParams>(e);
            float east = float.MinValue, south = float.MaxValue;
            for (int i = 0; i < 25 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                var tf = repo.GetComponent<SimTransform>(e);
                east = MathF.Max(east, tf.Position.X);
                var f3 = Vector3.Transform(Vector3.UnitX, tf.Rotation);
                var fwd = Vector2.Normalize(new Vector2(f3.X, f3.Y));
                foreach (var (a, b) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                    south = MathF.Min(south, CarKinem.Controllers.HullClearance.Corner(new Vector2(tf.Position.X, tf.Position.Y), fwd, a * p.Length * 0.5f, b * p.Width * 0.5f).Y);
            }
            Assert.True(east > 60f, $"held at the wall: it reached only x={east:F1}");
            Assert.True(south >= -1.95f - 0.05f, $"a corner reached y={south:F2} — through the wall's face at -1.95");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-6014</c> — a vehicle does not swerve for a man DOWN who lies clear of its hull's way (2.5 m off its line, the tank 3.5 m wide):
        /// it drives straight past; the same man STANDING there is still given way to, and a man down IN its way still makes it swerve.
        /// 📌 RoadblockBlastScenarioTests: the IFV swerved 7 m round a body at the gate's jamb into the wall. 🔴 Red-proof: drop the
        /// <c>InHullsWay</c> skip ⇒ the down row swerves like the standing one.
        /// </summary>
        [Theory]
        [InlineData(-2.6f, 0f, false)]    // down, clear of the hull: no swerve
        [InlineData(-2.6f, 100f, true)]   // the same man standing: give way
        [InlineData(0.5f, 0f, true)]      // down in its way: swerve round him
        public void CE6014_AVehicle_SwervesForAManDown_OnlyWhenHeLiesInItsWay(float manY, float health, bool swerves)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: 5f, vehicle: VehiclePresets.GetPreset(VehicleClass.Tank));
            var man = Stander(repo, new Vector3(25f, manY, 0f));
            Hurt(repo, man, health);
            Hurt(repo, e, 100f);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float most = 0f;
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                most = MathF.Max(most, MathF.Abs(repo.GetComponent<SimTransform>(e).Position.Y));
            }
            if (swerves) Assert.True(most > 0.3f, $"it ran straight past the man (most {most:F2} m off the line)");
            else Assert.True(most < 0.1f, $"it swerved {most:F2} m for a man down clear of its way");
            pool.Dispose();
            repo.Dispose();
        }

        // ── CE-4600 (B-3; docs/DESIGN_Vehicle_Breaching.md §3.2, §6, §7.5, D-3, V-3, V-9) — a player's vehicle breaks weak barriers ──

        /// <summary>
        /// A possessed vehicle (Manual, held by a live Fps station) of <paramref name="cls"/> and <paramref name="massKg"/> at (0, 0) facing +X, a world with
        /// <paramref name="geojsonFeatures"/>, the breach and cue events registered. The TKB template carries the mass.
        /// </summary>
        private static (EntityRepository Repo, Entity E) Possessed(VehicleClass cls, float massKg, float throttle, string geojsonFeatures)
        {
            CarKinem.Controllers.ContactBreach.Reset();
            var (repo, e) = ManualWorld(cls, new Fdp.Toolkit.Navigation.ManualDrive { Forward = throttle });
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>();
            repo.AddComponent(e, new Fdp.Toolkit.Behavior.Possession.PossessedBy { StationNodeId = (Fdp.Core.StationId)3, Mode = Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps, Epoch = 1 });
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (TkbTypeId)9001 });
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("breaker", 9001);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.VehicleParametersDto { Mass = massKg });
            db.Register(t);
            repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            repo.RegisterEvent<Fdp.Toolkit.Terrain.BuildingDamageEvent>();
            repo.RegisterEvent<Fdp.Toolkit.Terrain.ContactBreachEvent>();
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" + geojsonFeatures + "]}"));
            return (repo, e);
        }

        private const string BrickGardenWallAtX15 = "{\"type\":\"Feature\",\"properties\":{\"kind\":\"wall\",\"label\":\"garden\",\"height\":1.0,\"thickness\":0.3,\"material\":\"brick\"},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[15,-20],[15,20]]}}";

        /// <summary>Runs <paramref name="seconds"/>, collecting every breach and cue event; returns the farthest the nose reached (x) and the lowest speed after it first moved.</summary>
        private static (float Nose, List<Fdp.Toolkit.Terrain.BuildingDamageEvent> Breaches, List<Fdp.Toolkit.Terrain.ContactBreachEvent> Cues) DriveInto(EntityRepository repo, Entity e, float seconds)
        {
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            var p = repo.GetComponent<VehicleParams>(e);
            float nose = float.MinValue;
            var breaches = new List<Fdp.Toolkit.Terrain.BuildingDamageEvent>(); var cues = new List<Fdp.Toolkit.Terrain.ContactBreachEvent>();
            for (int i = 0; i < (int)(seconds * 60); i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                repo.Bus.SwapBuffers();
                breaches.AddRange(repo.Bus.Read<Fdp.Toolkit.Terrain.BuildingDamageEvent>().ToArray());
                cues.AddRange(repo.Bus.Read<Fdp.Toolkit.Terrain.ContactBreachEvent>().ToArray());
                nose = MathF.Max(nose, repo.GetComponent<SimTransform>(e).Position.X + p.Length * 0.5f);
            }
            return (nose, breaches, cues);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4600</c> (acceptance §6 item 2, §7.5 item 12) — a possessed 60 t TANK at full throttle into a 1 m brick garden wall BREAKS it and drives on THROUGH:
        /// one breach on the wall, a Passage from its base to its top, at least the drive-through width (V-9), and the nose well past the wall; the break costs speed
        /// but never zeroes it. 🔴 Red-proof: return false from <c>ContactBreach.OnContact</c> ⇒ held at the wall, no breach.
        /// </summary>
        [Fact]
        public void CE4600_APossessedTank_BreaksAGardenWall_AndDrivesThrough()
        {
            var (repo, e) = Possessed(VehicleClass.Tank, 60000f, 1f, BrickGardenWallAtX15);
            var (nose, breaches, cues) = DriveInto(repo, e, 8f);
            var b = Assert.Single(breaches);
            Assert.Equal((byte)Fdp.Toolkit.Terrain.BreachKind.Passage, b.Kind);
            Assert.True(b.To - b.From >= 4.2f - 1e-3f, $"the cut is {b.To - b.From:F2} m — narrower than a vehicle drives through");
            Assert.Equal(0f, b.Bottom, 3); Assert.Equal(1f, b.Top, 3);
            Assert.True(nose > 15f + 3f, $"the tank stopped at the wall: nose x={nose:F2}");
            Assert.Contains(cues, c => c.Kind == (byte)Fdp.Toolkit.Terrain.ContactBreachKind.Broke);
            Assert.Equal(e, b.Shooter);
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4600</c> (acceptance §6 item 2's slow half, item 3) — the same tank CREEPING (0.9 m/s, a light throttle — no ram, no push) stops at the wall and breaks
        /// nothing; a possessed CAR at full throttle stops too and is told the wall is TOO STRONG. 🔴 Red-proof: drop the minimum ram speed ⇒ the creeping tank breaks it.
        /// </summary>
        [Fact]
        public void CE4600_ACreepingTank_AndAPossessedCar_StopAtTheWall()
        {
            var (repo, e) = Possessed(VehicleClass.Tank, 60000f, 0.06f, BrickGardenWallAtX15);
            var (nose, breaches, _) = DriveInto(repo, e, 12f);
            Assert.Empty(breaches);
            Assert.True(nose <= 15f - 0.15f + 0.05f, $"the creeping tank went through: nose x={nose:F2}");
            repo.Dispose();

            var (car, c) = Possessed(VehicleClass.PersonalCar, 1500f, 1f, BrickGardenWallAtX15);
            var (carNose, carBreaches, cues) = DriveInto(car, c, 8f);
            Assert.Empty(carBreaches);
            Assert.True(carNose <= 15f - 0.15f + 0.05f, $"the car went through: nose x={carNose:F2}");
            Assert.Single(cues, q => q.Kind == (byte)Fdp.Toolkit.Terrain.ContactBreachKind.TooStrong);   // once, not every tick of contact
            car.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4600</c> (D-3, V-8; acceptance §7.5 item 12) — a tank RESTING against the wall (0.1 m off its face) at full throttle never reaches a ram speed, but
        /// the PUSH breaks it: the push cue's bar first, then the break after the row's push seconds, then it drives through. 🔴 Red-proof: drop the push branch ⇒ held for good.
        /// </summary>
        [Fact]
        public void CE4600_ATankRestingAgainstAWall_BreaksItByPush()
        {
            var (repo, e) = Possessed(VehicleClass.Tank, 60000f, 1f, BrickGardenWallAtX15);
            var tf = repo.GetComponent<SimTransform>(e);
            float half = repo.GetComponent<VehicleParams>(e).Length * 0.5f;
            tf.Position = new Vector3(15f - 0.15f - 0.1f - half, 0f, 0f);
            repo.SetComponent(e, tf);
            var (nose, breaches, cues) = DriveInto(repo, e, 8f);
            Assert.Contains(cues, c => c.Kind == (byte)Fdp.Toolkit.Terrain.ContactBreachKind.Pushing);
            Assert.Single(breaches);
            Assert.True(nose > 15f + 2f, $"pushed but not through: nose x={nose:F2}");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4600</c> (B-D4, V-5; acceptance §6 item 4) — a possessed tank at speed into a BUILDING wall opens a door-sized Passage (1.2 m wide, 2.2 m high, from the
        /// floor) and is held OUTSIDE — it never enters; an IFV (25 t) is told the building is too strong. 🔴 Red-proof: treat a building panel like a small barrier
        /// ⇒ the tank drives into the house.
        /// </summary>
        [Fact]
        public void CE4600_ATankBumpsABuildingWallOpen_ButNeverEnters()
        {
            const string House = "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"H\",\"building\":{\"footprint\":[[0,0],[10,0],[10,10],[0,10]]," +
                "\"storeys\":[{\"height\":3,\"walls\":[{\"from\":[0,0],\"to\":[10,0],\"thickness\":0.3,\"material\":\"brick\"},{\"from\":[10,0],\"to\":[10,10],\"thickness\":0.3,\"material\":\"brick\"}," +
                "{\"from\":[10,10],\"to\":[0,10],\"thickness\":0.3,\"material\":\"brick\"},{\"from\":[0,10],\"to\":[0,0],\"thickness\":0.3,\"material\":\"brick\"}]}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[15,-5]}}";
            var (repo, e) = Possessed(VehicleClass.Tank, 60000f, 1f, House);
            var (nose, breaches, cues) = DriveInto(repo, e, 8f);
            var b = Assert.Single(breaches);
            Assert.Equal((byte)Fdp.Toolkit.Terrain.BreachKind.Passage, b.Kind);
            Assert.Equal(1.2f, b.To - b.From, 2);
            Assert.Equal(2.2f, b.Top - b.Bottom, 2);
            Assert.True(nose <= 15f - 0.15f + 0.05f, $"the tank entered the house: nose x={nose:F2}");
            Assert.Contains(cues, c => c.Kind == (byte)Fdp.Toolkit.Terrain.ContactBreachKind.Bumped);
            repo.Dispose();

            var (ifv, i) = Possessed(VehicleClass.Tank, 25000f, 1f, House);
            var (_, ifvBreaches, ifvCues) = DriveInto(ifv, i, 6f);
            Assert.Empty(ifvBreaches);
            Assert.Contains(ifvCues, c => c.Kind == (byte)Fdp.Toolkit.Terrain.ContactBreachKind.TooStrong);
            ifv.Dispose();
        }

        private const string HouseAtX15 = "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"H\",\"building\":{\"footprint\":[[0,0],[10,0],[10,10],[0,10]]," +
            "\"storeys\":[{\"height\":3,\"walls\":[{\"from\":[0,0],\"to\":[10,0],\"thickness\":0.3,\"material\":\"brick\"},{\"from\":[10,0],\"to\":[10,10],\"thickness\":0.3,\"material\":\"brick\"}," +
            "{\"from\":[10,10],\"to\":[0,10],\"thickness\":0.3,\"material\":\"brick\"},{\"from\":[0,10],\"to\":[0,0],\"thickness\":0.3,\"material\":\"brick\"}]}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[15,-5]}}";

        /// <summary>The world y where a breach's middle lies on its panel.</summary>
        private static float BreachMiddleY(EntityRepository repo, in Fdp.Toolkit.Terrain.BuildingDamageEvent b)
        {
            var p = repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()!.Panels[b.Panel];
            var dir = Vector2.Normalize(p.B - p.A);
            return (p.A + dir * ((b.From + b.To) * 0.5f)).Y;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4629</c> ① — a possessed BULLDOZER (its own row, DIS 1.1.*.9.1, a blade) at speed into a BUILDING wall opens a BLADE-WIDE passage (its 4.3 m blade +
        /// the cut margin each side) of the wall's FULL height (3 m), centred on the hull — and is still held outside (B-D4, R-3025). A TANK's bump stays door-sized
        /// (1.2 × 2.2 m) but is centred on the hull too (it used to sit on the contact corner). 🔴 Red-proof: drop the <c>blade</c> branch in <c>ContactBreach.OnContact</c>
        /// ⇒ the dozer opens 1.2 × 2.2 m; centre on <c>hit.At</c> again ⇒ the tank's hole sits ~1.6 m off the hull's centreline.
        /// </summary>
        [Fact]
        public void CE4629_ABulldozerBumpsABuildingWall_BladeWideFullHeightCentred_AndATanksBumpIsCentredToo()
        {
            var (repo, e) = Possessed(VehicleClass.Tank, 49000f, 1f, HouseAtX15);
            ref var vp = ref repo.GetComponentRW<VehicleParams>(e); vp.Width = 4.3f; vp.Length = 8f;
            repo.SetDisType(e, new Fdp.Core.DISEntityType { Kind = 1, Domain = 1, Country = 225, Category = 9, Subcategory = 1 });
            var (nose, breaches, _) = DriveInto(repo, e, 8f);
            Assert.NotEmpty(breaches);
            float wide = 0f; foreach (var b in breaches) { wide += b.To - b.From; Assert.Equal(3f, b.Top - b.Bottom, 2); }
            Assert.True(wide >= 4.3f + 1f - 0.01f, $"the dozer's hole is {wide:F2} m wide — not the blade's");
            Assert.Equal(0f, BreachMiddleY(repo, breaches[0]), 1);
            Assert.True(nose <= 15f - 0.15f + 0.05f, $"the dozer entered the house: nose x={nose:F2}");
            repo.Dispose();

            var (tank, t) = Possessed(VehicleClass.Tank, 60000f, 1f, HouseAtX15);
            ref var tp = ref tank.GetComponentRW<VehicleParams>(t); tp.Width = 3.6f;
            var (_, tb, _) = DriveInto(tank, t, 8f);
            var one = Assert.Single(tb);
            Assert.Equal(1.2f, one.To - one.From, 2);
            Assert.Equal(0f, BreachMiddleY(tank, one), 1);
            tank.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-11054</c> — every contact breach carries its CAUSE, measured through the real contact rule: a tank at speed into a free garden wall is a RAM; the same tank RESTING against it and broken by the
        /// held full-throttle push is a PUSH; a tank's bump on a BUILDING wall is a RAM (a building is only ever rammed or bladed, D-3); a bulldozer's blade on a building is a BLADE. Each event names the hull
        /// as its shooter (the record turns it into the network id).
        /// 🔴 Red-proof: drop the <c>cause</c> argument of <c>ContactBreach.Event</c> ⇒ every row Unknown; classify the held push by <c>breaks</c> after the push branch ⇒ the push row says Ram; pass Ram to the
        /// blade cut ⇒ the dozer row says Ram.
        /// </summary>
        [Fact]
        public void CE11054_EveryContactBreach_CarriesItsCause_RamPushAndBlade()
        {
            static void All(List<Fdp.Toolkit.Terrain.BuildingDamageEvent> breaches, Entity e, Fdp.Toolkit.Terrain.BreachCause cause)
            {
                Assert.NotEmpty(breaches);
                Assert.All(breaches, b => { Assert.Equal((byte)cause, b.Cause); Assert.Equal(e, b.Shooter); Assert.Equal(0f, b.ExplosiveKg); });
            }

            var (rammer, r) = Possessed(VehicleClass.Tank, 60000f, 1f, BrickGardenWallAtX15);   // at speed into a free wall
            All(DriveInto(rammer, r, 8f).Breaches, r, Fdp.Toolkit.Terrain.BreachCause.Ram);
            rammer.Dispose();

            var (pusher, p) = Possessed(VehicleClass.Tank, 60000f, 1f, BrickGardenWallAtX15);   // resting against it: only the held push breaks it
            var tf = pusher.GetComponent<SimTransform>(p);
            tf.Position = new Vector3(15f - 0.15f - 0.1f - pusher.GetComponent<VehicleParams>(p).Length * 0.5f, 0f, 0f);
            pusher.SetComponent(p, tf);
            All(DriveInto(pusher, p, 8f).Breaches, p, Fdp.Toolkit.Terrain.BreachCause.Push);
            pusher.Dispose();

            var (tank, t) = Possessed(VehicleClass.Tank, 60000f, 1f, HouseAtX15);   // a building's bump
            All(DriveInto(tank, t, 8f).Breaches, t, Fdp.Toolkit.Terrain.BreachCause.Ram);
            tank.Dispose();

            var (dozer, d) = Possessed(VehicleClass.Tank, 49000f, 1f, HouseAtX15);   // a bulldozer's blade on a building
            ref var vp = ref dozer.GetComponentRW<VehicleParams>(d); vp.Width = 4.3f; vp.Length = 8f;
            dozer.SetDisType(d, new Fdp.Core.DISEntityType { Kind = 1, Domain = 1, Country = 225, Category = 9, Subcategory = 1 });
            All(DriveInto(dozer, d, 8f).Breaches, d, Fdp.Toolkit.Terrain.BreachCause.Blade);
            dozer.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-4629</c> (R-2084) — a bulldozer HELD at a building wall after its bump runs the contact rule every frame (its row matched by DIS type): steady frames
        /// allocate 0 B — possessed or not, dozer row or tank row. 📐 It caught a pre-existing 40 B a frame: <c>ContactBreachTable.For</c>'s foreach over the <c>IReadOnlyList</c> interface (CE-4600). 🔴 Red-proof: that foreach again ⇒ 2400 B over 60 frames.
        /// </summary>
        [Theory]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void CE4629_ABulldozerHeldAtAWall_AllocatesNothing_PerSteadyFrame(bool dozer, bool possessed)
        {
            var (repo, e) = Possessed(VehicleClass.Tank, 49000f, 1f, HouseAtX15);
            ref var vp = ref repo.GetComponentRW<VehicleParams>(e); vp.Width = 4.3f; vp.Length = 8f;
            if (dozer) repo.SetDisType(e, new Fdp.Core.DISEntityType { Kind = 1, Domain = 1, Country = 225, Category = 9, Subcategory = 1 });
            if (!possessed) repo.RemoveComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(e);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            for (int i = 0; i < 8 * 60; i++) { spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f); repo.Bus.SwapBuffers(); }   // bumped, held, the grace run out
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 60; i++) kin.Execute(repo, 1f / 60f);
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(repo.GetComponent<SimTransform>(e).Position.X + 4f <= 15f - 0.15f + 0.05f, "held at the wall");
            Assert.Equal(0L, bytes);
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3452</c> (P1, the bug queue; 📌 live <c>ua-moto-parked-bomb</c>) — a vehicle whose path STARTS with a short join segment pointing away from
        /// its heading (here 2 m back and south onto the lane, then 80 m east along it) drives on along the lane and its progress follows it: it never turns
        /// back to the path's start, and the no-progress watchdog has nothing to see. 🔴 Red-proof: drop the <c>PathProgress.OnPathAhead</c> line ⇒ progress
        /// stays ~0 while it drives east, and it loops back west.
        /// </summary>
        [Theory]
        [InlineData(VehicleClass.Truck)]
        [InlineData(VehicleClass.PersonalCar)]
        public void CE3452_APathStartingAtAnAngle_IsFollowedAlongTheLane_NeverBackToItsStart(VehicleClass cls)
        {
            var (repo, e, pool) = TrajectoryWorld(10f, startSpeed: 0f, vehicle: VehiclePresets.GetPreset(cls));
            pool.RegisterTrajectoryWithKey(new[] { new Vector3(0f, 0f, 0f), new Vector3(-1.5f, -1f, 0f), new Vector3(0f, -1.5f, 0f), new Vector3(80f, -1.5f, 0f) }, 13);
            var nav = repo.GetComponent<NavState>(e);
            nav.TrajectoryId = 13; nav.FinalDestination = new Vector3(80f, -1.5f, 0f); nav.ArrivalRadius = 3f;
            repo.SetComponent(e, nav);
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            float farthest = 0f, turnedBack = 0f;
            for (int i = 0; i < 12 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                float x = repo.GetComponent<SimTransform>(e).Position.X;
                farthest = MathF.Max(farthest, x);
                turnedBack = MathF.Max(turnedBack, farthest - x);
            }
            var end = repo.GetComponent<NavState>(e);
            Assert.True(turnedBack < 3f, $"{cls}: it turned back {turnedBack:F1} m toward the path's start");
            Assert.True(end.ProgressS > 50f && farthest > 50f, $"{cls}: progress {end.ProgressS:F1} at x {farthest:F1}");
            pool.Dispose();
            repo.Dispose();
        }

        /// <summary>A cow (Quadruped) at (0, 0) facing +X, driven forward (a running animal), its <see cref="Fdp.Toolkit.Animals.AnimalState"/> in
        /// <paramref name="mode"/> at <paramref name="band"/>, its species breaking fences when <paramref name="breaks"/>.</summary>
        private static (EntityRepository Repo, Entity E) RunningAnimal(bool breaks, Fdp.Toolkit.Animals.AnimalMode mode, byte band, string geojsonFeatures, bool jumps = false)
        {
            CarKinem.Controllers.ContactBreach.Reset();
            var (repo, e) = ManualWorld(VehicleClass.Quadruped, new Fdp.Toolkit.Navigation.ManualDrive { Forward = 1f });
            repo.RegisterComponent<Fdp.Toolkit.Animals.AnimalState>();
            repo.AddComponent(e, new Fdp.Toolkit.Animals.AnimalState { Mode = mode, FearBand = band });   // ⭐ CE-4628 — the species is the TKB type's; the band stays (§10b)
            repo.RegisterComponent<Fdp.Toolkit.Creatures.Fear>();
            repo.AddComponent(e, new Fdp.Toolkit.Creatures.Fear { Value = band >= 3 ? 0.9f : 0f });
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (TkbTypeId)700 });
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("Cattle", 700);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.AnimalTemperamentDto { Species = 0, Breaks = breaks, Jumps = jumps, FleeSpeed = 9f });
            db.Register(t);
            repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            repo.RegisterEvent<Fdp.Toolkit.Terrain.BuildingDamageEvent>();
            repo.RegisterEvent<Fdp.Toolkit.Terrain.ContactBreachEvent>();
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" + geojsonFeatures + "]}"));
            return (repo, e);
        }

        private static string FenceAtX15(string material, float thickness, float height)
            => "{\"type\":\"Feature\",\"properties\":{\"kind\":\"wall\",\"label\":\"f\",\"height\":" + height.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               ",\"thickness\":" + thickness.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"material\":\"" + material + "\"},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[15,-20],[15,20]]}}";

        /// <summary>
        /// ⭐⭐ <c>CE-4623</c> AB-7 (Animals AR-5) — the PANIC BREACH: a panicked cow (a species that breaks fences, fleeing in panic) running into a wire
        /// fence or a wooden one BREAKS it through the ONE contact rule (its "panicked-animal" row) and runs on through — a breach and a Broke cue (the break
        /// sound); a brick garden wall or a hedge holds it; a CALM cow, or a panicked SHEEP (it does not break fences), is held at the wire.
        /// 🔴 Red-proof: the animal arm dropped from the guard's gate ⇒ the panicked cow is held at the wire.
        /// </summary>
        [Theory]
        [InlineData("fence-chainlink", 0.01f, 1.5f, true, Fdp.Toolkit.Animals.AnimalMode.Flee, (byte)3, true)]
        [InlineData("fence-wood", 0.05f, 1.2f, true, Fdp.Toolkit.Animals.AnimalMode.Flee, (byte)3, true)]
        [InlineData("brick", 0.3f, 1.0f, true, Fdp.Toolkit.Animals.AnimalMode.Flee, (byte)3, false)]
        [InlineData("hedge", 0.6f, 1.5f, true, Fdp.Toolkit.Animals.AnimalMode.Flee, (byte)3, false)]
        [InlineData("fence-chainlink", 0.01f, 1.5f, true, Fdp.Toolkit.Animals.AnimalMode.Calm, (byte)0, false)]
        [InlineData("fence-chainlink", 0.01f, 1.5f, false, Fdp.Toolkit.Animals.AnimalMode.Flee, (byte)3, false)]
        public void CE4623_APanickedHerd_BreaksASmallFence_NeverAWall(string material, float thickness, float height, bool species, Fdp.Toolkit.Animals.AnimalMode mode, byte band, bool breaks)
        {
            var (repo, e) = RunningAnimal(species, mode, band, FenceAtX15(material, thickness, height));
            var (nose, breaches, cues) = DriveInto(repo, e, 8f);
            Assert.Equal(breaks, breaches.Count > 0);
            if (breaks)
            {
                Assert.True(nose > 15f + 3f, $"it ran on through: nose x={nose:F2}");
                Assert.Contains(cues, c => c.Kind == (byte)Fdp.Toolkit.Terrain.ContactBreachKind.Broke);
            }
            else Assert.True(nose <= 15f - thickness * 0.5f + 0.05f, $"held with its whole body short of the barrier (CE-1958): nose x={nose:F2}");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4623</c> AB-6 — a JUMPING animal (a horse: its temperament Jumps) walking into a FREE fence up to <c>JumpLinks.MaxHeightM</c> (1.2 m) CLEARS it — no hold,
        /// no breach; a cow (Jumps false) is held at the same fence; a horse is held by a wall above 1.2 m and by a building's wall.
        /// 🔴 Red-proof: drop the AB-6 jump arm from the barrier gate (CarKinematicsSystem) ⇒ the horse is held at the 1 m fence.
        /// </summary>
        [Theory]
        [InlineData("fence-wood", 0.05f, 1.0f, true, true)]
        [InlineData("fence-wood", 0.05f, 1.2f, true, true)]
        [InlineData("fence-wood", 0.05f, 1.0f, false, false)]
        [InlineData("brick", 0.3f, 2.0f, true, false)]
        public void CE4623_AJumpingHorse_ClearsALowFence_ACowIsHeld_AHighWallHoldsBoth(string material, float thickness, float height, bool jumps, bool clears)
        {
            var (repo, e) = RunningAnimal(false, Fdp.Toolkit.Animals.AnimalMode.Calm, 0, FenceAtX15(material, thickness, height), jumps);
            var (nose, breaches, _) = DriveInto(repo, e, 8f);
            Assert.Empty(breaches);   // it cleared the fence, it did not break it
            if (clears) Assert.True(nose > 15f + 3f, $"it cleared the fence: nose x={nose:F2}");
            else Assert.True(nose <= 15f - thickness * 0.5f + 0.05f, $"held with its whole body short of the barrier (CE-1958): nose x={nose:F2}");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4623</c> AB-10 (AR-5) — the STAMPEDE: an AI-driven cow whose planned flight goes NORTH (round the fence, as the navmesh plans it),
        /// frightened from the WEST, in panic, runs STRAIGHT east away from the fright — into the wooden fence at x = 15, breaking it and running on;
        /// the same cow calm follows its path north and never comes near the fence. 🔴 Red-proof: no stampede arm in the autonomous drive ⇒ the
        /// panicked cow follows its path north, no breach.
        /// </summary>
        [Theory]
        [InlineData(Fdp.Toolkit.Animals.AnimalMode.Flee, (byte)3, true)]
        [InlineData(Fdp.Toolkit.Animals.AnimalMode.Calm, (byte)0, false)]
        public void CE4623_APanickedAiCow_StampedesStraightAway_ThroughTheFence_ACalmOneFollowsItsPath(Fdp.Toolkit.Animals.AnimalMode mode, byte band, bool stampedes)
        {
            var (repo, e) = RunningAnimal(true, mode, band, FenceAtX15("fence-wood", 0.05f, 1.2f));
            repo.SetComponent(e, new NavState { Mode = KinematicsMode.Direct, FinalDestination = new Vector3(0f, 40f, 0f), TargetSpeed = 3f, ArrivalRadius = 3f });
            repo.GetComponentRW<Fdp.Toolkit.Creatures.Fear>(e) = new Fdp.Toolkit.Creatures.Fear { Value = band >= 3 ? 0.9f : 0f, FrightFrom = new Vector3(-20f, 0f, 0f), HasFrightFrom = 1 };
            var (nose, breaches, _) = DriveInto(repo, e, 8f);
            var at = repo.GetComponent<SimTransform>(e).Position;
            if (stampedes)
            {
                Assert.NotEmpty(breaches);
                Assert.True(nose > 15f + 3f, $"it ran east through the fence: nose x={nose:F2}");
            }
            else
            {
                Assert.Empty(breaches);
                Assert.True(nose < 5f && at.Y > 20f, $"it followed its path north: at ({at.X:F1}, {at.Y:F1})");
            }
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-1958</c> (the user, from the editor: "pigs stuck in the fence — half the body each side, never gets out") — an AI animal standing nose to a
        /// brick pen wall with its way on BEHIND it turns on the spot and walks there, and at no frame does any corner of its body cross the wall's face.
        /// 🔴 Red-proofs: no turn on the spot (CarKinematicsSystem's held branch) ⇒ held facing the wall for good, never arrives; the centre-only trace
        /// (HasHull for the corners) ⇒ walked at the wall it stands 1.1 m through.
        /// </summary>
        [Fact]
        public void CE1958_AnAnimalNoseToAWall_TurnsOnTheSpot_AndWalksAway_NeverPartlyThroughIt()
        {
            var (repo, e) = RunningAnimal(false, Fdp.Toolkit.Animals.AnimalMode.Calm, 0, FenceAtX15("brick", 0.25f, 1.0f));
            repo.RemoveComponent<Fdp.Toolkit.Navigation.ManualDrive>(e);
            var p = repo.GetComponent<VehicleParams>(e);
            float face = 15f - 0.125f;
            var tf = repo.GetComponent<SimTransform>(e);
            tf.Position = new Vector3(face - p.Length * 0.5f - 0.05f, 0f, 0f);   // its nose 5 cm off the wall, facing it
            repo.SetComponent(e, tf);
            // first it is sent THROUGH the wall: it walks up to it and stops with its whole body short of the face
            repo.SetComponent(e, new NavState { Mode = KinematicsMode.Direct, FinalDestination = new Vector3(30f, 0f, 0f), TargetSpeed = 1.5f, ArrivalRadius = 1f });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            float deepest = float.MinValue;
            void Frame()
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                repo.Bus.SwapBuffers();
                var t = repo.GetComponent<SimTransform>(e);
                var f3 = Vector3.Transform(Vector3.UnitX, t.Rotation);
                var f = Vector2.Normalize(new Vector2(f3.X, f3.Y));
                foreach (var (a, b) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                    deepest = MathF.Max(deepest, CarKinem.Controllers.HullClearance.Corner(new Vector2(t.Position.X, t.Position.Y), f, a * p.Length * 0.5f, b * p.Width * 0.5f).X);
            }
            for (int i = 0; i < 3 * 60; i++) Frame();
            Assert.True(deepest <= face + 0.02f, $"a corner reached x={deepest:F2} (the wall's face at {face:F3})");
            // then its way on is BEHIND it: it turns on the spot and walks there
            repo.SetComponent(e, new NavState { Mode = KinematicsMode.Direct, FinalDestination = new Vector3(0f, 0f, 0f), TargetSpeed = 1.5f, ArrivalRadius = 1f });
            for (int i = 0; i < 20 * 60; i++) Frame();
            var at = repo.GetComponent<SimTransform>(e).Position;
            Assert.True(Vector2.Distance(new Vector2(at.X, at.Y), Vector2.Zero) < 3f, $"it turned and walked away: at ({at.X:F1}, {at.Y:F1})");
            Assert.True(deepest <= face + 0.02f, $"a corner reached x={deepest:F2} while it turned (the wall's face at {face:F3})");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3471</c> (a pig walked through its pen's brick wall, farm-under-fire) — a point that starts EXACTLY ON a wall's face, or inside it, may move
        /// out but never deeper: on farm-under-fire's 0.25 m brick wall (y = 10 here, its face 9.875), a step north from the face is held, a step from just inside
        /// back south is free, a step along the face is free. 📐 The cause: a mover held at a wall restarts from rest, its steps shrink, and a float32 coordinate
        /// lands EXACTLY on the face; the guard then read the T = 0 crossing as "already inside" and let it through. 🔴 Red-proof: <c>Deepens</c> returning false ⇒
        /// the step north from the face passes ⇒ red.
        /// </summary>
        [Fact]
        public void CE3471_APointOnAWallsFace_MayLeaveIt_NeverGoDeeper()
        {
            var q = new Fdp.Toolkit.Terrain.TerrainWorldQuery(GardenWall(1.0f, thickness: 0.25f));
            const float face = 9.875f;
            bool Blocked(Vector2 from, Vector2 to) => CarKinem.Controllers.HullClearance.StepBarrier(q, from, Vector2.UnitY, 0f, to, Vector2.UnitY, 0f, 0f, 0f, out _);
            Assert.True(Blocked(new Vector2(30f, face), new Vector2(30f, face + 0.0035f)), "a step north from ON the face went into the wall");
            Assert.True(Blocked(new Vector2(30f, face), new Vector2(30.003f, face + 0.0018f)), "an oblique step from ON the face went into the wall");
            Assert.True(Blocked(new Vector2(30f, 9.95f), new Vector2(30f, 10.05f)), "a point inside walked on toward the far side");
            Assert.False(Blocked(new Vector2(30f, 9.9f), new Vector2(30f, 9.8f)), "a point just inside could not step back out");
            Assert.False(Blocked(new Vector2(30f, face - 0.001f), new Vector2(31f, face - 0.001f)), "a step along the face was held");
            Assert.True(Blocked(new Vector2(30f, face - 0.005f), new Vector2(30f, face + 0.0035f)), "a step from 5 mm short into the wall passed");   // the old rule still holds
        }

        /// <summary>
        /// ⭐ <c>CE-3471</c> — the same in the kinematics, the long way: an AI pig sent THROUGH a 0.25 m brick wall pushes at it for 60 s (held, restarting from rest
        /// every frame, as farm-under-fire's pig did) and at no frame does its centre or any corner of its body pass the wall's face. ⚠ A GUARD, not a
        /// reproduction: measured green with the CE-3471 fix off too (this approach never lands a point exactly on the face) — the face rail above is the red-proof.
        /// </summary>
        [Fact]
        public void CE3471_APigPushingAtABrickWallForAMinute_NeverPassesItsFace()
        {
            var (repo, e) = RunningAnimal(false, Fdp.Toolkit.Animals.AnimalMode.Calm, 0, FenceAtX15("brick", 0.25f, 1.0f));
            repo.RemoveComponent<Fdp.Toolkit.Navigation.ManualDrive>(e);
            var p = repo.GetComponent<VehicleParams>(e);
            float face = 15f - 0.125f;
            var tf = repo.GetComponent<SimTransform>(e);
            tf.Position = new Vector3(face - p.Length * 0.5f - 0.3f, 3f, 0f);
            tf.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f);   // oblique, as the pig's path met its wall
            repo.SetComponent(e, tf);
            repo.SetComponent(e, new NavState { Mode = KinematicsMode.Direct, FinalDestination = new Vector3(40f, 20f, 0f), TargetSpeed = 0.6f, ArrivalRadius = 1f });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            float deepest = float.MinValue;
            for (int i = 0; i < 60 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                repo.Bus.SwapBuffers();
                var t = repo.GetComponent<SimTransform>(e);
                var f3 = Vector3.Transform(Vector3.UnitX, t.Rotation);
                var f = Vector2.Normalize(new Vector2(f3.X, f3.Y));
                deepest = MathF.Max(deepest, t.Position.X);
                foreach (var (a, b) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                    deepest = MathF.Max(deepest, CarKinem.Controllers.HullClearance.Corner(new Vector2(t.Position.X, t.Position.Y), f, a * p.Length * 0.5f, b * p.Width * 0.5f).X);
            }
            Assert.True(deepest <= face + 0.02f, $"a point of the pig reached x={deepest:F3} (the wall's face at {face:F3})");
            repo.Dispose();
        }

        /// <summary>⭐ <c>CE-4623</c> AB-7 — the animal row is for a panicked animal ONLY: a possessed CAR at full throttle into the same wire fence a
        /// panicked cow breaks still breaks nothing (wheeled vehicles break nothing, B-3). 🔴 Red-proof: let any vehicle take an animal row ⇒ red.</summary>
        [Fact]
        public void CE4623_APossessedCar_StillBreaksNoWireFence()
        {
            var (repo, e) = Possessed(VehicleClass.PersonalCar, 1500f, 1f, FenceAtX15("fence-chainlink", 0.01f, 1.5f));
            var (_, breaches, _) = DriveInto(repo, e, 8f);
            Assert.Empty(breaches);
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-4600</c> (B-D3) — an IFV (25 t) breaks a hedge and a wooden fence but a brick garden wall is too strong for it (strength = resistance × thickness × height).
        /// </summary>
        [Theory]
        [InlineData("hedge", 0.6f, 1.5f, true)]
        [InlineData("fence-wood", 0.05f, 1.6f, true)]
        [InlineData("brick", 0.3f, 1.0f, false)]
        public void CE4600_AnIfv_BreaksHedgesAndFences_NotABrickWall(string material, float thickness, float height, bool breaks)
        {
            string wall = "{\"type\":\"Feature\",\"properties\":{\"kind\":\"wall\",\"label\":\"w\",\"height\":" + height.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"thickness\":" + thickness.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"material\":\"" + material + "\"},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[15,-20],[15,20]]}}";
            var (repo, e) = Possessed(VehicleClass.Tank, 25000f, 1f, wall);
            var (_, breaches, _) = DriveInto(repo, e, 8f);
            Assert.Equal(breaks, breaches.Count > 0);
            repo.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4600</c> (B-D3; acceptance §6 item 3) — a possessed IFV (25 t) into a SANDBAG wall (a static obstacle, baked into the terrain as its box) crushes it:
        /// ONE damage of the obstacle's whole Health through the ONE obstacle-damage path (its owner removes it), and the IFV drives on over the ground — not onto the
        /// bags' top. 🔴 Red-proof: name no obstacle in <c>ContactBreach.Identify</c> ⇒ held, no damage.
        /// </summary>
        [Fact]
        public void CE4600_AnIfv_CrushesASandbagWall_ThroughTheObstacleDamagePath()
        {
            var (repo, e) = Possessed(VehicleClass.Tank, 25000f, 1f, "");
            repo.RegisterComponent<Fdp.Toolkit.Terrain.StaticObstacle>();
            repo.RegisterComponent<Fdp.Toolkit.Terrain.ObstacleShape>();
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            repo.RegisterEvent<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>();
            var bags = repo.CreateEntity();
            var shape = new Fdp.Toolkit.Terrain.ObstacleShape { Length = 8f, Width = 1f, Height = 1f };
            repo.AddComponent(bags, new SimTransform { Position = new Vector3(15f, 0f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });
            repo.AddComponent(bags, new Fdp.Toolkit.Terrain.StaticObstacle { Material = new FixedString32("sandbags") });
            repo.AddComponent(bags, shape);
            repo.AddComponent(bags, new Fdp.Toolkit.Combat.Components.Health { Current = 300f, Max = 300f });
            var world = repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()!;
            var prism = Fdp.Toolkit.Terrain.TerrainObstacles.PrismOf(new Vector3(15f, 0f, 0f), MathF.PI / 2f, shape,
                Fdp.Toolkit.Terrain.TerrainMaterialLibrary.Shared.Get("sandbags", "t"), Fdp.Toolkit.Terrain.TerrainObstacles.LabelPrefix + bags.Index);
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainObstacles.With(world, new[] { prism }));

            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            var damage = new List<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>();
            float nose = float.MinValue, topZ = 0f;
            var p = repo.GetComponent<VehicleParams>(e);
            for (int i = 0; i < 8 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f);
                kin.Execute(repo, 1f / 60f);
                repo.Bus.SwapBuffers();
                damage.AddRange(repo.Bus.Read<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>().ToArray());
                var at = repo.GetComponent<SimTransform>(e).Position;
                nose = MathF.Max(nose, at.X + p.Length * 0.5f); topZ = MathF.Max(topZ, at.Z);
            }
            var d = Assert.Single(damage);
            Assert.Equal(bags, d.HitEntity);
            Assert.True(d.TotalDamage >= 300f);
            Assert.True(nose > 15f + 2f, $"held at the bags: nose x={nose:F2}");
            Assert.True(topZ < 0.05f, $"the IFV climbed onto the bags: z={topZ:F2}");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ CE-4594 item 3 — the members of an obstacle ROW as the spawn gives them: each of obstacle type <paramref name="box"/> (the ONE <c>ClutterTypes</c> table the TKB obstacle types are built from) gets the components the TKB translator injects
        /// (<c>ObstacleTkbTranslator.Inject</c> over a <c>StaticObstacleDto</c> of that box with <paramref name="maxHealth"/> — the catalog's value; ⚠ this project is FDP-level and cannot reference the Hrot catalog, so the dto is built here from the same entries), a
        /// <c>PlacementGroup</c>, and its baked prism. The row is laid ACROSS the line x = <paramref name="x"/>, tight (end to end), from y = <paramref name="y0"/>, <paramref name="count"/> members facing along the line.
        /// </summary>
        private static List<Entity> ObstacleRow(EntityRepository repo, Fdp.Toolkit.Terrain.ClutterTypes.Type box, float maxHealth, float x, float y0, int count, uint group)
        {
            repo.RegisterComponent<Fdp.Toolkit.Terrain.StaticObstacle>();
            repo.RegisterComponent<Fdp.Toolkit.Terrain.ObstacleShape>();
            repo.RegisterComponent<Fdp.Toolkit.Terrain.PlacementGroup>();
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            repo.RegisterEvent<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>();
            var template = new Fdp.Interfaces.TkbTemplate(box.Name, 9100);
            template.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.StaticObstacleDto { Length = box.Length, Width = box.Width, Height = box.Height, Material = box.Material, MaxHealth = maxHealth });
            var members = new List<Entity>(); var prisms = new List<Fdp.Toolkit.Terrain.TerrainPrism>();
            for (int i = 0; i < count; i++)
            {
                var at = new Vector3(x, y0 + box.Length * (i + 0.5f), 0f);
                var e = repo.CreateEntity();
                repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });
                repo.AddComponent(e, new Fdp.Toolkit.Terrain.PlacementGroup { Id = group });
                new Fdp.Toolkit.Terrain.ObstacleTkbTranslator().Inject(repo, e, template);
                members.Add(e);
                prisms.Add(Fdp.Toolkit.Terrain.TerrainObstacles.PrismOf(at, MathF.PI / 2f, repo.GetComponent<Fdp.Toolkit.Terrain.ObstacleShape>(e),
                    Fdp.Toolkit.Terrain.TerrainMaterialLibrary.Shared.Get(box.Material, "t"), Fdp.Toolkit.Terrain.TerrainObstacles.LabelPrefix + e.Index));
            }
            var world = repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()!;
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainObstacles.With(world, prisms));
            return members;
        }

        /// <summary>Drives the possessed hull for <paramref name="seconds"/> and returns the obstacle damage events and the hull's farthest nose x.</summary>
        private static (List<Fdp.Toolkit.Combat.Events.DamageAssessedEvent> Damage, float Nose) DriveCollectingDamage(EntityRepository repo, Entity e, float seconds)
        {
            var spatial = new SpatialHashSystem(); var kin = new CarKinematicsSystem(new TrajectoryPoolManager());
            var damage = new List<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>(); var p = repo.GetComponent<VehicleParams>(e); float nose = float.MinValue;
            for (int i = 0; i < (int)(seconds * 60); i++)
            {
                spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f);
                repo.Bus.SwapBuffers();
                damage.AddRange(repo.Bus.Read<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>().ToArray());
                nose = MathF.Max(nose, repo.GetComponent<SimTransform>(e).Position.X + p.Length * 0.5f);
            }
            return (damage, nose);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4594</c> item 3 (B-3) — a TANK (60 t) driving into the END member of a row of four concrete blocks (8 m of 1 m-thick blocks across its way, one <c>PlacementGroup</c>, the components the TKB translator gives the type) crushes THAT member through the same
        /// obstacle-damage path as the sandbag wall — ONE damage of its whole Health (600) naming that member, its owner removes it — and the other three stand with full Health. Concrete is strength 1500 (1500 mm/m × 1 m × 1 m) against the tank row's 2000.
        /// The hull is offset so only the row's end member is in its path (its half-width 1.8 m). 🔴 Red-proof: name no obstacle in <c>ContactBreach.Identify</c> ⇒ held, no damage ⇒ red.
        /// </summary>
        [Fact]
        public void CE4594_ATank_CrushesOneMemberOfAConcreteRow_ThroughTheObstacleDamagePath_TheOthersStand()
        {
            var (repo, e) = Possessed(VehicleClass.Tank, 60000f, 1f, "");
            float hw = repo.GetComponent<VehicleParams>(e).Width * 0.5f;
            var row = ObstacleRow(repo, Fdp.Toolkit.Terrain.ClutterTypes.ConcreteBlock, 600f, 15f, 2f, 4, 91u);   // members span y 2…10; the hull is offset below it
            // the hull's upper edge at y = 2.5: it clips ONLY the first member (y 2…4) by half a metre
            var at = repo.GetComponent<SimTransform>(e); at.Position = new Vector3(0f, 2.5f - hw, 0f); repo.SetComponent(e, at);
            var (damage, nose) = DriveCollectingDamage(repo, e, 8f);
            var d = Assert.Single(damage);
            Assert.Equal(row[0], d.HitEntity);
            Assert.True(d.TotalDamage >= 600f, $"the member's whole Health: {d.TotalDamage}");
            for (int i = 1; i < 4; i++) Assert.Equal(600f, repo.GetComponent<Fdp.Toolkit.Combat.Components.Health>(row[i]).Current);
            Assert.True(nose > 15f + 1f, $"the hull went through where the member stood: nose x={nose:F2}");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-4594</c> item 3 (B-3, MEASURED) — which obstacle materials a hull breaks, for each of the obstacle types (strength = the material's resistance × the box's short side × its height, against the hull row's limit): a TANK (60 t, limit 2000) breaks ALL
        /// (concrete block 1500, jersey barrier 720, guard booth 374, car 324, crate 60, sandbag wall 60, the barrels); an IFV (25 t, limit 150) breaks the sandbag wall, the crate and EVERY BARREL — a concrete block, a jersey barrier, a booth and a car are TooStrong for it.
        /// ⭐ CE-3473 (user 2026-11-18) — a barrel's FILL: the empty drum 2.7 (was a solid steel box, 540), water-filled 65, sand-filled 86.
        /// 🔴 Red-proof: the tank row's limit lowered to 1000 ⇒ the concrete block holds ⇒ red.
        /// </summary>
        [Theory]
        [InlineData("Car", 60000f, true)] [InlineData("Sandbag wall", 60000f, true)] [InlineData("Concrete block", 60000f, true)] [InlineData("Crate", 60000f, true)]
        [InlineData("Barrel", 60000f, true)] [InlineData("Jersey barrier", 60000f, true)] [InlineData("Guard booth", 60000f, true)]
        [InlineData("Car", 25000f, false)] [InlineData("Sandbag wall", 25000f, true)] [InlineData("Concrete block", 25000f, false)] [InlineData("Crate", 25000f, true)]
        [InlineData("Barrel", 25000f, true)] [InlineData("Jersey barrier", 25000f, false)] [InlineData("Guard booth", 25000f, false)]
        [InlineData("Barrel, water-filled", 60000f, true)] [InlineData("Barrel, sand-filled", 60000f, true)] [InlineData("Barrel, water-filled", 25000f, true)] [InlineData("Barrel, sand-filled", 25000f, true)]
        public void CE4594_WhatAHullBreaks_ByTheMaterialTable_ForEachObstacleType(string type, float massKg, bool breaks)
        {
            var box = ObstacleBox(type);
            var (repo, e) = Possessed(VehicleClass.Tank, massKg, 1f, "");
            var row = ObstacleRow(repo, box, 100f, 15f, -box.Length * 0.5f, 1, 92u);   // one member across the hull's centre line
            var (damage, _) = DriveCollectingDamage(repo, e, 8f);
            Assert.Equal(breaks, damage.Count > 0);
            if (breaks) Assert.Equal(row[0], damage[0].HitEntity);
            repo.Dispose();
        }

        /// <summary>The obstacle box of <paramref name="name"/>: the generated clutter's, or a filled barrel (an obstacle type only — CE-3473).</summary>
        private static Fdp.Toolkit.Terrain.ClutterTypes.Type ObstacleBox(string name)
            => name == Fdp.Toolkit.Terrain.ClutterTypes.BarrelWater.Name ? Fdp.Toolkit.Terrain.ClutterTypes.BarrelWater
             : name == Fdp.Toolkit.Terrain.ClutterTypes.BarrelSand.Name ? Fdp.Toolkit.Terrain.ClutterTypes.BarrelSand
             : Fdp.Toolkit.Terrain.ClutterTypes.All.Single(t => t.Name == name);

        /// <summary>
        /// ⭐⭐ <c>CE-3473</c> (Checkpoint RM-5; user 2026-11-18: <i>"follow your lean; also realistic bullet stopping power (if barrel is full of liquid, it might be a serious/resistive
        /// obstacle for both the ramming car as well as for bullets"</i>) — a WHEELED CAR (1.5 t) on a RAM ORDER breaks an EMPTY drum by driving into it, and is HELD by a water- or
        /// sand-filled one; the same car WITHOUT the order breaks nothing (a car takes no breach row — Breaching B-D1). 📌 Found by <c>cp-ram-barrier</c>: no row took a wheeled hull, and
        /// the drum was a solid steel box (540) — its old pass was the car going round the row. 🔴 Red-proof: drop the <c>ramming-car</c> row ⇒ the empty drum holds the ramming car.
        /// </summary>
        [Theory]
        [InlineData("Barrel", true, true)]
        [InlineData("Barrel, water-filled", true, false)]
        [InlineData("Barrel, sand-filled", true, false)]
        [InlineData("Barrel", false, false)]
        public void CE3473_ARammingCar_BreaksAnEmptyDrum_IsHeldByAFilledOne(string type, bool ramming, bool breaks)
        {
            var (repo, e) = Possessed(VehicleClass.PersonalCar, 1500f, 1f, "");
            repo.RegisterComponent<CarKinem.Core.RamOrder>();
            if (ramming) repo.AddComponent(e, new CarKinem.Core.RamOrder { Active = 1 });
            var box = ObstacleBox(type);
            var row = ObstacleRow(repo, box, 100f, 15f, -box.Length * 0.5f, 1, 93u);   // one drum across the car's centre line
            var (damage, nose) = DriveCollectingDamage(repo, e, 8f);
            Assert.Equal(breaks, damage.Count > 0);
            if (breaks) { Assert.Equal(row[0], damage[0].HitEntity); Assert.True(nose > 16f, $"through the drum: nose x={nose:F2}"); }
            else Assert.True(nose < 15f, $"held at the drum: nose x={nose:F2}");
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3452</c> — the progress resync takes a point ahead only on a segment the vehicle TRAVELS along: on a U-turn path (east 6 m, back west 3 m south)
        /// a vehicle near the return leg but heading NORTH (across it, turning) keeps its progress; heading WEST (along the return leg) it takes the point there.
        /// 📌 Found by <c>ua-technical-scoot</c>: the jump skipped the technical's turn at its scoot point. 🔴 Red-proof: drop the travel test ⇒ the north case jumps.
        /// </summary>
        [Fact]
        public void CE3452_TheProgressResync_NeverJumpsOntoALegItCrosses()
        {
            var wps = new[] { new Vector3(0f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(6f, -3f, 0f), new Vector3(0f, -3f, 0f) };
            var pool = new TrajectoryPoolManager();
            int id = pool.RegisterTrajectory(wps, new[] { 5f, 5f, 5f, 5f });
            Assert.True(pool.TryGetTrajectory(id, out var traj));
            var near = new Vector2(3f, -2.5f);   // beside the return leg (y = -3), 6 m + 3 m + 3 m = s 12 along it
            Assert.Equal(1f, CarKinem.Controllers.PathProgress.OnPathAhead(in traj, 1f, near, 15f, new Vector2(0f, 1f)));              // crossing it
            Assert.Equal(12f, CarKinem.Controllers.PathProgress.OnPathAhead(in traj, 1f, near, 15f, new Vector2(-1f, 0f)), 1);         // along it
        }

        /// <summary>A calm animal body standing at <paramref name="at"/> (a cow: a Quadruped, in the collider hash, carrying <c>AnimalState</c>).</summary>
        private static Entity Cow(EntityRepository repo, Vector3 at)
        {
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>()) repo.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Animals.AnimalState>()) repo.RegisterComponent<Fdp.Toolkit.Animals.AnimalState>();
            var n = repo.CreateEntity();
            repo.AddComponent(n, new VehicleState());
            repo.AddComponent(n, new SimTransform { Position = at, Rotation = SimMath.FacingEast });
            repo.AddComponent(n, new SimVelocity());
            repo.AddComponent(n, new NavState { Mode = KinematicsMode.None });
            repo.AddComponent(n, VehiclePresets.GetPreset(VehicleClass.Quadruped));
            repo.AddComponent(n, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 0.6f, CollisionLayer = 1 });
            repo.AddComponent(n, new Fdp.Toolkit.Animals.AnimalState());
            return n;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3457</c> (P5, the bug queue; Animals AR-17) — a car driving its path at 8 m/s YIELDS to a cow standing in its lane: it slows and stands
        /// short of it (never touches it), and drives on once the cow has moved off; a cow BESIDE the lane does not slow it. 📌 Before, a vehicle drove at
        /// full speed into a herd (ua-animal-herd-road: only the herd's own fear cleared the lane). 🔴 Red-proof: drop the yield cap ⇒ the car runs into the cow.
        /// </summary>
        [Fact]
        public void CE3457_ACar_YieldsToACowInItsLane_DrivesOnWhenItHasGone_NeverSlowsForOneBeside()
        {
            var car = VehiclePresets.GetPreset(VehicleClass.PersonalCar);
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 8f, startSpeed: 8f, vehicle: car);
            var cow = Cow(repo, new Vector3(40f, 0f, 0f));
            var beside = Cow(repo, new Vector3(20f, car.Width * 0.5f + 3f, 0f));   // off the lane: passed at speed
            var spatial = new SpatialHashSystem(); var kin = new CarKinematicsSystem(pool);
            float closest = float.MaxValue, speedPastBeside = 0f; string closestInfo = "";
            for (int i = 0; i < 10 * 60; i++)
            {
                spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f);
                var at = repo.GetComponent<SimTransform>(e).Position;
                float dNow = Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(40f, 0f));
                if (dNow < closest) { closest = dNow; closestInfo = $"at ({at.X:F2},{at.Y:F2}) speed {repo.GetComponent<VehicleState>(e).Speed:F2} t {i / 60f:F2}"; }
                if (MathF.Abs(at.X - 20f) < 0.5f) speedPastBeside = repo.GetComponent<VehicleState>(e).Speed;
            }
            Assert.True(speedPastBeside > 6f, $"the cow beside the lane slowed it: {speedPastBeside:F2} m/s");
            Assert.True(closest > car.Length * 0.5f + 1.5f, $"it came {closest:F2} m from the cow (its half-length {car.Length * 0.5f:F2}) {closestInfo}");
            Assert.True(MathF.Abs(repo.GetComponent<VehicleState>(e).Speed) < 0.3f, "it stands for the cow");
            var moved = repo.GetComponent<SimTransform>(cow); moved.Position = new Vector3(40f, 15f, 0f); repo.SetComponent(cow, moved);   // the cow wanders off
            for (int i = 0; i < 8 * 60; i++) { spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f); }
            Assert.True(repo.GetComponent<SimTransform>(e).Position.X > 55f, $"it drove on: x={repo.GetComponent<SimTransform>(e).Position.X:F1}");
            repo.Dispose();
        }

        private static bool CarKem_MayBreak(EntityRepository repo, Entity e)
            => CarKinem.Controllers.HullBreakGate.MayBreak(repo, e, repo.GetComponent<NavState>(e));
    }
}
