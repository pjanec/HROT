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
    /// <summary>
    /// PACK3-Z001 — Unit tests for <see cref="ZoneEnvironmentData"/> singleton integration
    /// with <see cref="CarKinematicsSystem"/>.
    /// </summary>
    public class ZoneEnvironmentDataTests
    {
        // ── Shared setup helpers ──────────────────────────────────────────────

        private static EntityRepository BuildMinimalRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SpatialGridData>();
            repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
            return repo;
        }

        private static Entity AddMovingVehicle(EntityRepository repo)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new VehicleState { Speed = 5f });
            repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = SimMath.FacingNorth,
            });
            repo.SetAuthority<SimTransform>(entity, true);
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(0f, 5f, 0f) });
            repo.AddComponent(entity, new VehicleParams
            {
                WheelBase        = 2.7f,
                MaxSpeedFwd      = 30f,
                MaxAccel         = 3f,
                MaxDecel         = 6f,
                MaxSteerAngle    = 0.6f,
                LookaheadTimeMin = 2f,
                LookaheadTimeMax = 10f,
                AccelGain        = 2f,
                AvoidanceRadius  = 2.5f,
            });
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None });
            return entity;
        }

        // ── Test 1: No ZoneEnvironmentData — vehicle physics still run ────────

        [Fact]
        public void CarKinematicsSystem_WithoutZoneSingleton_VehiclePhysicsStillRun()
        {
            var repo          = BuildMinimalRepo();
            var trajectoryPool = new TrajectoryPoolManager();
            var spatialSystem = new SpatialHashSystem();
            var kinematics    = new CarKinematicsSystem(trajectoryPool);

            spatialSystem.Create(repo);
            kinematics.Create(repo);

            var entity     = AddMovingVehicle(repo);
            var before     = repo.GetComponent<SimTransform>(entity).Position;

            // Ensure NO ZoneEnvironmentData singleton is present
            Assert.False(repo.HasSingleton<ZoneEnvironmentData>(),
                "Precondition: ZoneEnvironmentData singleton must be absent");

            var ex = Record.Exception(() =>
            {
                spatialSystem.Run();
                kinematics.Run();
            });

            Assert.Null(ex);

            var after = repo.GetComponent<SimTransform>(entity).Position;
            // Vehicle must have moved (non-road physics continue)
            Assert.True(Vector3.Distance(before, after) > 0.001f,
                $"Vehicle should move without ZoneEnvironmentData. before={before}, after={after}");

            // Cleanup
            spatialSystem.Dispose();
            kinematics.Dispose();
            repo.Dispose();
            trajectoryPool.Dispose();
        }

        // ── Test 2: ZoneEnvironmentData present — navigation tick succeeds ─────

        [Fact]
        public void CarKinematicsSystem_WithZoneSingleton_NavigationTickSucceeds()
        {
            var repo          = BuildMinimalRepo();
            var roadNetwork   = new RoadNetworkBuilder().Build(5f, 20, 20);
            var trajectoryPool = new TrajectoryPoolManager();
            var spatialSystem = new SpatialHashSystem();
            var kinematics    = new CarKinematicsSystem(trajectoryPool);

            spatialSystem.Create(repo);
            kinematics.Create(repo);

            // Inject ZoneEnvironmentData singleton (simulates what ZoneManagerService will do)
            repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = roadNetwork });
            Assert.True(repo.HasSingleton<ZoneEnvironmentData>(),
                "Precondition: ZoneEnvironmentData must be present");

            // Spawn a vehicle in RoadGraph mode to exercise road navigation code path
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new VehicleState { Speed = 5f });
            repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = SimMath.FacingNorth,
            });
            repo.SetAuthority<SimTransform>(entity, true);
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(0f, 5f, 0f) });
            repo.AddComponent(entity, new VehicleParams
            {
                WheelBase        = 2.7f,
                MaxSpeedFwd      = 30f,
                MaxAccel         = 3f,
                MaxDecel         = 6f,
                MaxSteerAngle    = 0.6f,
                LookaheadTimeMin = 2f,
                LookaheadTimeMax = 10f,
                AccelGain        = 2f,
                AvoidanceRadius  = 2.5f,
            });
            // RoadGraph mode: exercises the road-network code path
            repo.AddComponent(entity, new NavState
            {
                Mode        = KinematicsMode.RoadGraph,
                TargetSpeed = 5f,
                FinalDestination = new Vector2(10f, 10f),
            });

            var ex = Record.Exception(() =>
            {
                spatialSystem.Run();
                kinematics.Run();
            });

            Assert.Null(ex);

            // Cleanup
            spatialSystem.Dispose();
            kinematics.Dispose();
            roadNetwork.Dispose();
            repo.Dispose();
            trajectoryPool.Dispose();
        }
    }
}
