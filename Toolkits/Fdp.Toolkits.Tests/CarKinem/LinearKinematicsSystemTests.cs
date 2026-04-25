using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.CarKinem.Systems;
using Xunit;

namespace Fdp.Toolkit.CarKinem.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LinearKinematicsSystem"/> (CT-MOD1-F).
    /// Moved from FDP.Toolkit.Physics.Tests because the system now lives in
    /// FDP.Toolkit.CarKinem.Systems after the dependency-cycle resolution.
    ///
    /// Verifies position integration for entities with <see cref="SimTransform"/> +
    /// <see cref="SimVelocity"/> and correct exclusion of <see cref="VehicleState"/> entities.
    /// </summary>
    public class LinearKinematicsSystemTests : IDisposable
    {
        private readonly EntityRepository       _world;
        private readonly LinearKinematicsSystem _sys;

        public LinearKinematicsSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<VehicleState>();

            _sys = new LinearKinematicsSystem();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetDeltaTime(float dt)
            => _world.SetSingleton(new GlobalTime { DeltaTime = dt, TimeScale = 1f });

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Entity with <see cref="SimTransform"/> at origin and <see cref="SimVelocity"/>
        /// Linear=(10,0,0): after one tick at dt=1.0 the position must be (10,0,0).
        /// </summary>
        [Fact]
        public void LinearKinematics_AdvancesPosition_ByVelocityTimesDeltaTime()
        {
            SetDeltaTime(1.0f);

            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new SimVelocity  { Linear = new Vector3(10f, 0f, 0f) });

            _sys.Execute(_world, 1.0f);

            var tf = _world.GetComponent<SimTransform>(e);
            Assert.Equal(new Vector3(10f, 0f, 0f), tf.Position);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Entity that also has <see cref="VehicleState"/> must be excluded from the query
        /// (vehicles are handled by <c>CarKinematicsSystem</c>). Position must remain at origin.
        /// </summary>
        [Fact]
        public void LinearKinematics_DoesNotMove_EntityWithVehicleState()
        {
            SetDeltaTime(1.0f);

            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new SimVelocity  { Linear = new Vector3(10f, 0f, 0f) });
            _world.AddComponent(e, new VehicleState { Speed = 10f });   // excluded by .Without<VehicleState>()

            _sys.Execute(_world, 1.0f);

            var tf = _world.GetComponent<SimTransform>(e);
            Assert.Equal(Vector3.Zero, tf.Position);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Entity with only <see cref="SimTransform"/> (no <see cref="SimVelocity"/>)
        /// must not be touched by the system.
        /// </summary>
        [Fact]
        public void LinearKinematics_DoesNotMove_EntityWithoutSimVelocity()
        {
            SetDeltaTime(1.0f);

            var startPos = new Vector3(5f, 5f, 0f);
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = startPos, Rotation = Quaternion.Identity });
            // No SimVelocity — entity is static.

            _sys.Execute(_world, 1.0f);

            var tf = _world.GetComponent<SimTransform>(e);
            Assert.Equal(startPos, tf.Position);
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Two entities with different velocities must each advance independently.
        /// </summary>
        [Fact]
        public void LinearKinematics_MovesMultipleEntities_Independently()
        {
            SetDeltaTime(1.0f);

            var eA = _world.CreateEntity();
            _world.AddComponent(eA, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(eA, new SimVelocity  { Linear = new Vector3(1f, 0f, 0f) });

            var eB = _world.CreateEntity();
            _world.AddComponent(eB, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(eB, new SimVelocity  { Linear = new Vector3(0f, 2f, 0f) });

            _sys.Execute(_world, 1.0f);

            var tfA = _world.GetComponent<SimTransform>(eA);
            var tfB = _world.GetComponent<SimTransform>(eB);

            Assert.Equal(new Vector3(1f, 0f, 0f), tfA.Position);
            Assert.Equal(new Vector3(0f, 2f, 0f), tfB.Position);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Entity with zero velocity must not move regardless of delta time.
        /// </summary>
        [Fact]
        public void LinearKinematics_ZeroVelocity_PositionUnchanged()
        {
            SetDeltaTime(1.0f);

            var startPos = new Vector3(3f, 7f, 0f);
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = startPos, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new SimVelocity  { Linear = Vector3.Zero });

            _sys.Execute(_world, 1.0f);

            var tf = _world.GetComponent<SimTransform>(e);
            Assert.Equal(startPos, tf.Position);
        }
    }
}
