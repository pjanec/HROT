using System;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Kernel;
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
            var kinematicsSystem = new CarKinematicsSystem(roadNetwork, trajectoryPool);
            
            spatialSystem.Create(repo);
            kinematicsSystem.Create(repo);
            
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
                Rotation = Quaternion.CreateFromYawPitchRoll(0, 0, 0) // Yaw=0 -> North (0,1,0)
            });
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
                Mode = NavigationMode.None
            });
            
            Vector3 initialPos = repo.GetComponent<SimTransform>(entity).Position;
            
            // Update systems
            spatialSystem.Run();
            kinematicsSystem.Run();
            
            // Verify singleton exists
            Assert.True(repo.HasSingleton<SpatialGridData>());
            
            Vector3 finalPos = repo.GetComponent<SimTransform>(entity).Position;
            
            // Vehicle should have moved (speed = 10 m/s, dt = 0.016 -> 0.16m move)
            // Moving North (Y+)
            Assert.NotEqual(initialPos, finalPos);
            Assert.True(finalPos.Y > initialPos.Y, "Should move North (Positive Y)");
            Assert.Equal(0.16f, finalPos.Y, precision: 2);
            
            // Cleanup
            spatialSystem.Dispose();
            kinematicsSystem.Dispose();
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
            var kinematicsSystem = new CarKinematicsSystem(roadNetwork, trajectoryPool);
            
            spatialSystem.Create(repo);
            kinematicsSystem.Create(repo);

            // Create Entity A moving East at (0,0) with Speed 5.
            // East -> Yaw=-PI/2? Or X-Forward?
            // If SimTransform is Y-Forward, to face East (+X), we need -90 deg rotation.
            // Yaw = -PI/2.
            var entA = repo.CreateEntity();
            repo.AddComponent(entA, new VehicleState { Speed = 5f });
            repo.AddComponent(entA, new SimTransform { 
                Position = Vector3.Zero,
                Rotation = Quaternion.CreateFromYawPitchRoll(0, 0, -MathF.PI/2) // East
            });
            repo.AddComponent(entA, new SimVelocity { Linear = new Vector3(5, 0, 0) });

            repo.AddComponent(entA, new NavState { Mode = NavigationMode.None }); // Move straight
            repo.AddComponent(entA, new VehicleParams { 
                WheelBase = 2.0f, MaxSpeedFwd=10f, MaxAccel=10f, MaxDecel=10f, MaxSteerAngle=1f, 
                LookaheadTimeMin=1f, LookaheadTimeMax=2f, AccelGain=1f, AvoidanceRadius=2.0f
            });

            // Create Entity B at (2, 0) stationary (Blocking path)
            var entB = repo.CreateEntity();
            repo.AddComponent(entB, new VehicleState { Speed = 0f });
            repo.AddComponent(entB, new SimTransform { 
                Position = new Vector3(2, 0, 0),
                Rotation = Quaternion.CreateFromYawPitchRoll(0, 0, -MathF.PI/2) // East
            });
            repo.AddComponent(entB, new SimVelocity { Linear = Vector3.Zero });

            repo.AddComponent(entB, new NavState { Mode = NavigationMode.None });
            repo.AddComponent(entB, new VehicleParams { AvoidanceRadius=2.0f });

            // Run update
            spatialSystem.Run();
            kinematicsSystem.Run(); // A should steer or decelerate/avoid

            var tfA = repo.GetComponent<SimTransform>(entA);
            Vector3 posA = tfA.Position;
            // Removed checking fwdA directly, checking if position changed due to avoidance

            // Expected position if no avoidance: (0 + 5*0.1, 0) = (0.5, 0)
            Vector3 expectedNoAvoidance = new Vector3(0.5f, 0f, 0f);
            
            // Check if deviation occurred (Steering or Speed reduction)
            bool deviated = Vector3.Distance(posA, expectedNoAvoidance) > 0.001f;
            Assert.True(deviated, $"Vehicle did not react to obstacle. Pos: {posA}, Expected: {expectedNoAvoidance}");

            spatialSystem.Dispose();
            kinematicsSystem.Dispose();
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
            var kinematicsSystem = new CarKinematicsSystem(roadNetwork, trajectoryPool);
            
            spatialSystem.Create(repo);
            kinematicsSystem.Create(repo);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new VehicleState { Speed = 10f });
            // Start at (0,0) facing East (-PI/2)
            repo.AddComponent(entity, new SimTransform { 
                Position = Vector3.Zero,
                Rotation = Quaternion.CreateFromYawPitchRoll(0, 0, -MathF.PI/2) 
            });
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(10, 0, 0) });
            
            repo.AddComponent(entity, new NavState { Mode = NavigationMode.CustomTrajectory, TrajectoryId = trajId, ProgressS = 0f });
            repo.AddComponent(entity, new VehicleParams { 
                WheelBase = 2.0f, MaxSpeedFwd=20f, MaxAccel=10f, MaxDecel=10f, MaxSteerAngle=1f, 
                LookaheadTimeMin=1f, LookaheadTimeMax=2f, AccelGain=1f, AvoidanceRadius=2.0f 
            });

            // Update
            spatialSystem.Run(); // Build grid
            kinematicsSystem.Run();

            // Check ProgressS increased
            var nav = repo.GetComponent<NavState>(entity);
            // Expected progress: 10m/s * 0.1s = 1.0m (approx, assuming constant speed)
            Assert.True(nav.ProgressS > 0.5f, "Progress should advance");

            // Cleanup
            spatialSystem.Dispose();
            kinematicsSystem.Dispose();
            roadNetwork.Dispose();
            trajectoryPool.Dispose();
            repo.Dispose();
        }
    }
}
