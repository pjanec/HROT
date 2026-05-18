using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Core
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="BallisticsSystem"/> (BCS-P5-T4, second half).
    /// Tests isolate the system by seeding <see cref="BallisticProjectile"/> components
    /// directly, running the system, and asserting post-run state.
    /// BallisticsSystem now publishes <see cref="RaycastRequestEvent"/> via the cmd buffer
    /// instead of writing to <see cref="RaycastBatchData.Requests"/> directly.
    /// </summary>
    public class BallisticsSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly BallisticsSystem _sys;

        public BallisticsSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<BallisticProjectile>();
            _world.RegisterComponent<PhysicsCollider>();
            _world.RegisterEvent<HitEvent>();
            _world.RegisterEvent<RaycastRequestEvent>();

            // Initialise GlobalTime singleton so CurrentTick reads are valid.
            _world.SetSingleton(new GlobalTime { FrameNumber = 0, TimeScale = 1f });

            _sys = new BallisticsSystem();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Sets the world's simulated tick via the GlobalTime singleton.</summary>
        private void SetCurrentTick(uint tick)
        {
            _world.SetSingleton(new GlobalTime { FrameNumber = tick, TimeScale = 1f });
        }

        /// <summary>
        /// Spawns a bullet entity at the given position with the specified spawn tick and shooter.
        /// </summary>
        private Entity SpawnBullet(Vector3 position, uint spawnTick, Entity shooter = default,
                                   Vector3 previousPosition = default)
        {
            var bullet = _world.CreateEntity();
            _world.AddComponent(bullet, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            _world.AddComponent(bullet, new SimVelocity  { Linear = new Vector3(100f, 0f, 0f), Angular = Vector3.Zero });
            _world.AddComponent(bullet, new BallisticProjectile
            {
                Shooter          = shooter,
                PreviousPosition = previousPosition,
                Damage           = CombatConstants.DefaultBulletDamage,
                SpawnTick        = spawnTick,
            });
            return bullet;
        }

        /// <summary>
        /// Runs the system, flushes the cmd buffer, and swaps buffers so that the
        /// published <see cref="RaycastRequestEvent"/>s become readable.
        /// Returns a snapshot array of the published events.
        /// </summary>
        private RaycastRequestEvent[] RunAndReadEvents()
        {
            ISimulationView view = _world;
            _sys.Execute(view, 0.016f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
            var span = _world.Bus.Read<RaycastRequestEvent>();
            var arr  = new RaycastRequestEvent[span.Length];
            span.CopyTo(arr);
            return arr;
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Two live bullet entities must each publish one <see cref="RaycastRequestEvent"/>.
        /// </summary>
        [Fact]
        public void Ballistics_SubmitsRaycastRequest_ForEachLiveBullet()
        {
            SetCurrentTick(0);
            SpawnBullet(new Vector3(5f,  0f, 0f), spawnTick: 0);
            SpawnBullet(new Vector3(10f, 0f, 0f), spawnTick: 0);

            var events = RunAndReadEvents();
            Assert.Equal(2, events.Length);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After the system runs, <see cref="BallisticProjectile.PreviousPosition"/> must
        /// equal the bullet's <see cref="SimTransform.Position"/> at the time of the run.
        /// </summary>
        [Fact]
        public void Ballistics_UpdatesPreviousPosition_AfterRequest()
        {
            SetCurrentTick(0);
            var currentPos = new Vector3(5f, 0f, 0f);
            var bullet = SpawnBullet(currentPos, spawnTick: 0, previousPosition: Vector3.Zero);

            _sys.Execute(_world, 0.016f);

            var proj = _world.GetComponent<BallisticProjectile>(bullet);
            Assert.Equal(currentPos, proj.PreviousPosition);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// A bullet whose <c>CurrentTick - SpawnTick >= BulletLifetimeTicks</c> must be
        /// destroyed (<see cref="EntityRepository.IsAlive"/> returns false).
        /// </summary>
        [Fact]
        public void Ballistics_DestroysEntity_WhenLifetimeExpired()
        {
            // SpawnTick=0, CurrentTick=121 -> age = 121 >= BulletLifetimeTicks(120) -> destroy.
            SetCurrentTick(121);
            var bullet = SpawnBullet(new Vector3(1f, 0f, 0f), spawnTick: 0);

            _sys.Execute(_world, 0.016f);

            Assert.False(_world.IsAlive(bullet), "Bullet entity should have been destroyed after lifetime expired.");
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// An expired bullet must NOT publish a <see cref="RaycastRequestEvent"/> — it is
        /// destroyed before the event is published.
        /// </summary>
        [Fact]
        public void Ballistics_DoesNotSubmitRaycast_WhenLifetimeExpired()
        {
            SetCurrentTick(121);
            SpawnBullet(new Vector3(1f, 0f, 0f), spawnTick: 0);

            var events = RunAndReadEvents();
            Assert.Equal(0, events.Length);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// The <see cref="RaycastRequestEvent.IgnoreEntity"/> field must be set to
        /// <see cref="BallisticProjectile.Shooter"/> to prevent self-hits.
        /// </summary>
        [Fact]
        public void Ballistics_IgnoresShooter_InRaycastRequest()
        {
            SetCurrentTick(0);

            var shooter = _world.CreateEntity();
            _world.AddComponent(shooter, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            SpawnBullet(new Vector3(5f, 0f, 0f), spawnTick: 0, shooter: shooter);

            var events = RunAndReadEvents();
            Assert.Equal(1, events.Length);
            Assert.Equal(shooter, events[0].IgnoreEntity);
        }
    }
}
