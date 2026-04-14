using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Kernel
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using Xunit;

namespace FDP.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="BallisticsSystem"/> (BCS-P5-T4, second half).
    /// Tests isolate the system by seeding <see cref="RaycastBatchData"/> and
    /// <see cref="BallisticProjectile"/> components directly, then running the system and
    /// asserting post-run state.
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

            // Initialise the RaycastBatchData singleton with persistent native arrays.
            var batch = new RaycastBatchData
            {
                Requests = new NativeArray<RaycastRequest>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
                Hits     = new NativeArray<RaycastHit>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
                Count    = 0,
            };
            _world.SetSingleton(batch);

            // Initialise GlobalTime singleton so CurrentTick reads are valid.
            _world.SetSingleton(new GlobalTime { FrameNumber = 0, TimeScale = 1f });

            _sys = new BallisticsSystem();
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();

            // Free the persistent native arrays allocated in the constructor.
            if (_world.HasSingleton<RaycastBatchData>())
            {
                ref var b = ref _world.GetSingleton<RaycastBatchData>();
                if (b.Requests.IsCreated) b.Requests.Dispose();
                if (b.Hits.IsCreated)     b.Hits.Dispose();
            }

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

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Two live bullet entities must each contribute one entry to
        /// <see cref="RaycastBatchData.Count"/>.
        /// </summary>
        [Fact]
        public void Ballistics_SubmitsRaycastRequest_ForEachLiveBullet()
        {
            SetCurrentTick(0);
            SpawnBullet(new Vector3(5f, 0f, 0f),  spawnTick: 0);
            SpawnBullet(new Vector3(10f, 0f, 0f), spawnTick: 0);

            _sys.Run();

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            Assert.Equal(2, batch.Count);
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

            _sys.Run();

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
            // SpawnTick=0, CurrentTick=121 → age = 121 >= BulletLifetimeTicks(120) → destroy.
            SetCurrentTick(121);
            var bullet = SpawnBullet(new Vector3(1f, 0f, 0f), spawnTick: 0);

            _sys.Run();

            Assert.False(_world.IsAlive(bullet), "Bullet entity should have been destroyed after lifetime expired.");
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// An expired bullet must NOT contribute a raycast request — it is destroyed before
        /// the batch is written, so <see cref="RaycastBatchData.Count"/> remains 0.
        /// </summary>
        [Fact]
        public void Ballistics_DoesNotSubmitRaycast_WhenLifetimeExpired()
        {
            SetCurrentTick(121);
            SpawnBullet(new Vector3(1f, 0f, 0f), spawnTick: 0);

            _sys.Run();

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            Assert.Equal(0, batch.Count);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// The <see cref="RaycastRequest.IgnoreEntity"/> field must be set to
        /// <see cref="BallisticProjectile.Shooter"/> to prevent self-hits.
        /// </summary>
        [Fact]
        public void Ballistics_IgnoresShooter_InRaycastRequest()
        {
            SetCurrentTick(0);

            var shooter = _world.CreateEntity();
            _world.AddComponent(shooter, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            var bullet = SpawnBullet(new Vector3(5f, 0f, 0f), spawnTick: 0, shooter: shooter);

            _sys.Run();

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            Assert.Equal(1, batch.Count);
            Assert.Equal(shooter, batch.Requests[0].IgnoreEntity);
        }

        // ── Test 6 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When the batch is already full (<c>Count == RaycastBatchCapacity</c>), the system
        /// must not increment <c>Count</c> further and must not throw.
        /// Confirms the DEBT-021 capacity guard at the fill site.
        /// </summary>
        [Fact]
        public void Ballistics_RespectsCapacity_WhenBatchFull()
        {
            SetCurrentTick(0);

            // Fill the batch to capacity.
            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Count = PhysicsConstants.RaycastBatchCapacity;

            // Spawn one bullet — system should detect batch is full and skip writing.
            SpawnBullet(new Vector3(5f, 0f, 0f), spawnTick: 0);

            _sys.Run();

            ref var batchAfter = ref _world.GetSingleton<RaycastBatchData>();
            Assert.Equal(PhysicsConstants.RaycastBatchCapacity, batchAfter.Count);
        }
    }
}
