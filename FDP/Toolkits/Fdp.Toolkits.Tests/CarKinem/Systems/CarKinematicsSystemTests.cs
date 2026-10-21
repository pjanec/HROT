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
                WheelBase = 2.7f, MaxSpeedFwd = 30f, MaxAccel = 3f, MaxDecel = 6f, MaxSteerAngle = 0.6f,
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
            KinematicsMode mode = KinematicsMode.CustomTrajectory, float arrivalRadius = 2f, float startSpeed = 0f, bool recovery = true)
        {
            var (repo, e, pool) = TrajectoryWorld(targetSpeed: 5f, startSpeed: startSpeed, facing: facing, vehicle: VehiclePresets.GetPreset(VehicleClass.Tank));
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(BlockWorld));
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
            Assert.Equal(Vector3.Zero, repo.GetComponent<SimTransform>(e).Position);
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
            Assert.Equal(new Vector3(10, 2, 0), aboard.GetComponent<SimTransform>(a).Position);
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
            repo.AddComponent(e, new Fdp.Toolkit.Combat.EffectState { StunnedUntil = Fdp.Toolkit.Combat.HitModel.Now(repo) + 1000.0 });
            var spatial = new SpatialHashSystem();
            var kin = new CarKinematicsSystem(pool);
            void RunFor(float seconds) { for (int i = 0; i < (int)(seconds * 60); i++) { spatial.Execute(repo, 1f / 60f); kin.Execute(repo, 1f / 60f); } }

            var start = repo.GetComponent<SimTransform>(e).Position;
            RunFor(3f);
            Assert.True(Vector3.Distance(start, repo.GetComponent<SimTransform>(e).Position) < 0.05f, "stunned, he stood");

            repo.GetComponentRW<Fdp.Toolkit.Combat.EffectState>(e).StunnedUntil = 0.0;
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
            MaxDecel = 4f, MaxSteerAngle = 0.8f, MaxSteerRate = 0.34906584f, MaxLatAccel = 6f, AvoidanceRadius = 5f,
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
}
}
