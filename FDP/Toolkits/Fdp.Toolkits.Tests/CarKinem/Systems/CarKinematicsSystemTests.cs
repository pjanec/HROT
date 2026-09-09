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
    }
}
