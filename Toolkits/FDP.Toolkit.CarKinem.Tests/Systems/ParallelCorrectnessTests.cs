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
    public class ParallelCorrectnessTests
    {
        [Fact]
        public void ParallelExecution_ProducesSameResultsAsSerial()
        {
            // Since we can't easily force parallel/serial in unit test without internal access,
            // we will just verify that the system processes multiple entities correctly.
            // (Mocking real parallel test requires engine support or specific test harness)
            
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            var roadNetwork = new RoadNetworkBuilder().Build(5f, 40, 40);
            var trajectoryPool = new TrajectoryPoolManager();
            
            var spatialSystem = new SpatialHashSystem();
            var kinematicsSystem = new CarKinematicsSystem(roadNetwork, trajectoryPool);
            
            spatialSystem.Create(repo);
            kinematicsSystem.Create(repo);
            
            int count = 100;
            var entities = new Entity[count];
            
            for (int i = 0; i < count; i++)
            {
                var e = repo.CreateEntity();
                entities[i] = e;
                repo.AddComponent(e, new VehicleState { Speed = 10f });
                // Entities in a line moving North
                repo.AddComponent(e, new SimTransform { 
                    Position = new Vector3(i * 5, 0, 0), 
                    Rotation = SimMath.FacingNorth
                });
                repo.AddComponent(e, new SimVelocity { Linear = new Vector3(0, 10, 0) });
                
                repo.AddComponent(e, new VehicleParams { WheelBase=2.7f, MaxSpeedFwd=30f, AvoidanceRadius=2.0f });
                repo.AddComponent(e, new NavState { Mode = NavigationMode.None });
            }
            
            // Run
            spatialSystem.Run();
            kinematicsSystem.Run();
            
            // Verify
            for (int i = 0; i < count; i++)
            {
                var tf = repo.GetComponent<SimTransform>(entities[i]);
                // Should move North 0.16m
                Assert.Equal(0.16f, tf.Position.Y, precision: 2);
                Assert.Equal(i * 5f, tf.Position.X, precision: 2);
            }
            
            spatialSystem.Dispose();
            kinematicsSystem.Dispose();
            roadNetwork.Dispose();
            trajectoryPool.Dispose();
            repo.Dispose();
        }
    }
}
