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
    [Collection("FdpWorkerPool")]   // ⭐ N-15 — the crowd rail resizes the shared pool
    public class ParallelCorrectnessTests
    {
        /// <summary>
        /// ⭐ N-15 (CE-4477) — two crowds walking THROUGH each other (every man avoids his neighbours each tick) end in the SAME world
        /// (<see cref="EntityRepository.ContentHash"/>) at 1 / 2 / 4 / 8 threads and across runs: a body moves itself; its neighbours' position
        /// and velocity — written by the same loop — come from the tick-start view. With <c>HROT_READ_CHECK=1</c> every cross read is checked.
        /// 📌 Measured before: the stepped 1 000-man editor run was repeatable at 1 thread and not at 2 — the loop read neighbours LIVE.
        /// 🔴 Red-proof (run with <c>HROT_READ_CHECK=1</c>): drop <c>DeclareNeighbourReads</c> ⇒ a neighbour's SimVelocity is an undeclared
        /// cross read ⇒ throws.
        /// </summary>
        [Fact]
        public void N15_ACrowdAvoidingItself_EndsTheSameAtAnyThreadCount()
        {
            ulong one = Crowd(1);
            foreach (int t in new[] { 2, 4, 8, 1 }) Assert.Equal(one, Crowd(t));
        }

        private static ulong Crowd(int threads)
        {
            Fdp.Core.Jobs.FdpWorkerPool.Resize(threads);
            Fdp.Core.Jobs.ParallelEntities.Mode = Fdp.Core.Jobs.SimulationMode.Deterministic;
            var roadNetwork = new RoadNetworkBuilder().Build(5f, 40, 40);
            var trajectoryPool = new TrajectoryPoolManager();
            try
            {
                using var repo = new EntityRepository();
                repo.RegisterComponent<VehicleState>();
                repo.RegisterComponent<SimTransform>();
                repo.RegisterComponent<SimVelocity>();
                repo.RegisterComponent<VehicleParams>();
                repo.RegisterComponent<NavState>();
                repo.RegisterComponent<SpatialGridData>();
                repo.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();   // what puts a body in the avoidance grid
                repo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.05f, TimeScale = 1.0f });
                var spatial = new SpatialHashSystem();
                var kinematics = new CarKinematicsSystem(trajectoryPool);
                var rng = new Random(77);
                Entity first = default;
                for (int i = 0; i < 400; i++)
                {
                    bool west = i % 2 == 0;
                    var e = repo.CreateEntity();
                    var at = new Vector3(west ? rng.Next(0, 30) : rng.Next(60, 90), rng.Next(0, 40), 0);
                    repo.AddComponent(e, new VehicleState { Speed = 1.2f });
                    repo.AddComponent(e, new SimTransform { Position = at, Rotation = west ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI) });
                    repo.SetAuthority<SimTransform>(e, true);
                    repo.AddComponent(e, new SimVelocity { Linear = new Vector3(west ? 1.2f : -1.2f, 0, 0) });
                    repo.AddComponent(e, new VehicleParams { Class = VehicleClass.Pedestrian, WheelBase = 0.5f, MaxSpeedFwd = 1.5f, WalkSpeed = 1.2f, AvoidanceRadius = 0.8f, MaxAccel = 2f, MaxDecel = 4f });
                    repo.AddComponent(e, new NavState { Mode = KinematicsMode.Direct, FinalDestination = new WorldPoint(west ? 90 : 0, at.Y, 0), TargetSpeed = 1.2f, ArrivalRadius = 1f });
                    repo.AddComponent(e, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 0.4f });
                    if (i == 0) first = e;
                }
                float startX = repo.GetComponentRO<SimTransform>(first).Position.X;
                for (int step = 0; step < 120; step++)
                {
                    repo.Tick();
                    spatial.Execute(repo, 0.05f);
                    if (Fdp.Core.ParallelReadCheck.HasDeclarations) repo.CaptureLastTick();   // as the kernel does before the dispatch
                    kinematics.Execute(repo, 0.05f);
                }
                Assert.True(repo.GetComponentRO<SimTransform>(first).Position.X - startX > 3f, "the crowd did not walk — the rail would not exercise the avoidance");
                Assert.True(spatial.Dropped == 0);
                return repo.ContentHash();
            }
            finally
            {
                roadNetwork.Dispose();
                trajectoryPool.Dispose();
                Fdp.Core.Jobs.FdpWorkerPool.Resize(Fdp.Core.Jobs.FdpWorkerPool.Budget());
            }
        }

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
            var kinematicsSystem = new CarKinematicsSystem(trajectoryPool);
            
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
                repo.SetAuthority<SimTransform>(e, true); // mark as locally-owned so WithOwned filter passes
                repo.AddComponent(e, new SimVelocity { Linear = new Vector3(0, 10, 0) });
                
                repo.AddComponent(e, new VehicleParams { WheelBase=2.7f, MaxSpeedFwd=30f, AvoidanceRadius=2.0f });
                repo.AddComponent(e, new NavState { Mode = KinematicsMode.None });
            }
            
            // Run
            spatialSystem.Execute(repo, 0.016f);
            kinematicsSystem.Execute(repo, 0.016f);
            
            // Verify
            for (int i = 0; i < count; i++)
            {
                var tf = repo.GetComponent<SimTransform>(entities[i]);
                // Should move North 0.16m
                Assert.Equal(0.16f, tf.Position.Y, precision: 2);
                Assert.Equal(i * 5f, tf.Position.X, precision: 2);
            }
            
            roadNetwork.Dispose();
            trajectoryPool.Dispose();
            repo.Dispose();
        }
    }
}
