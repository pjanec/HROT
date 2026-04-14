using Xunit;
using CarKinem.Core;
using Fdp.Kernel;
using System.Numerics;
using CarKinem.Systems;
using CarKinem.Road;
using CarKinem.Trajectory;
using System;
using CarKinem.Spatial;

namespace Fdp.Toolkit.CarKinem.Tests
{
    public class VehicleStateRefactorTests
    {
        [Fact] 
        public void VehicleState_DoesNotContain_PositionField() =>
            Assert.Null(typeof(VehicleState).GetField("Position"));

        [Fact] 
        public void VehicleState_DoesNotContain_ForwardField() =>
            Assert.Null(typeof(VehicleState).GetField("Forward"));

        [Fact] 
        public void CarKinematicsSystem_WritesSimTransform_AfterUpdate() 
        {
            // Setup Repo
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            // Dependencies
            var roadNetwork = new RoadNetworkBuilder().Build(10f, 10, 10);
            var trajectoryPool = new TrajectoryPoolManager();
            var sys = new CarKinematicsSystem(trajectoryPool);
            
            var spatialSys = new SpatialHashSystem();
            spatialSys.Create(repo);
            sys.Create(repo);

            var e = repo.CreateEntity();
            
            // Setup components - SimTransform in North (Yaw=PI/2)
            repo.AddComponent(e, new SimTransform { Position = new Vector3(0, 0, 0), Rotation = SimMath.FacingNorth });
            repo.SetAuthority<SimTransform>(e, true); // mark as locally-owned so WithOwned filter passes
            repo.AddComponent(e, new SimVelocity  { Linear = Vector3.Zero });
            repo.AddComponent(e, new VehicleState { Speed = 10f, SteerAngle = 0f, Accel = 0f, CurrentLaneIndex = 0 });
            repo.AddComponent(e, new NavState     { TargetSpeed = 10f, Mode = KinematicsMode.None }); 
            
            repo.AddComponent(e, new VehicleParams {
                WheelBase = 2.7f, MaxSpeedFwd=30f, MaxAccel=3f, MaxDecel=6f, MaxSteerAngle=0.6f, 
                LookaheadTimeMin=2f, LookaheadTimeMax=10f, AccelGain=2.0f, AvoidanceRadius=2.5f,
                Length = 4f, Width = 2f, MaxLatAccel = 10f, MaxSteerRate = 1f
            });
            
            // Run
            spatialSys.Run();
            sys.Run();
            
            var tf = repo.GetComponent<SimTransform>(e);
            
            // Should move North (Y+)
            // 10 m/s * 0.016 = 0.16m
            Assert.NotEqual(Vector3.Zero, tf.Position); 
            Assert.True(tf.Position.Y > 0);
            
            spatialSys.Dispose();
            sys.Dispose();
            roadNetwork.Dispose();
            trajectoryPool.Dispose();
            repo.Dispose();
        }
    }
}
