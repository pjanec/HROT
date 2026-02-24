using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Kernel
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Combat.Executors;
using Xunit;

namespace FDP.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AimAndFireExecutor"/> (BCS-P5-T2).
    /// Each test drives the executor directly without a dispatcher system, using a real
    /// <see cref="EntityRepository"/> for component access and event bus assertions.
    /// </summary>
    public class AimAndFireExecutorTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly AimAndFireExecutor _executor;

        public AimAndFireExecutorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<WeaponChannel>();
            _world.RegisterEvent<FireRequestEvent>();
            _world.RegisterEvent<HitEvent>();

            _executor = new AimAndFireExecutor();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Spawns a shooter entity at <paramref name="shooterPos"/> with a
        /// <see cref="WeaponState"/> and a <see cref="WeaponChannel"/> whose
        /// Params are pre-populated with the given <paramref name="target"/> and
        /// <paramref name="cooldownTicks"/>.
        /// </summary>
        private unsafe (Entity shooter, WeaponChannel channel)
            SpawnShooter(Vector3 shooterPos, int ammo, int cooldownRemaining,
                         Entity target, int cooldownTicks)
        {
            var shooter = _world.CreateEntity();
            _world.AddComponent(shooter, new SimTransform { Position = shooterPos, Rotation = Quaternion.Identity });
            _world.AddComponent(shooter, new WeaponState
            {
                Ammo                    = ammo,
                CooldownTicksRemaining  = cooldownRemaining,
                MuzzleVelocity          = 800f,
            });
            _world.AddComponent(shooter, new WeaponChannel());

            var channel = _world.GetComponent<WeaponChannel>(shooter);
            channel.Status = NodeStatus.Running;

            var p = new AimAndFireParams { Target = target, CooldownTicks = cooldownTicks };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            _world.SetComponent(shooter, channel);
            channel = _world.GetComponent<WeaponChannel>(shooter);

            return (shooter, channel);
        }

        /// <summary>Spawns a target entity at <paramref name="pos"/>.</summary>
        private Entity SpawnTarget(Vector3 pos)
        {
            var target = _world.CreateEntity();
            _world.AddComponent(target, new SimTransform { Position = pos, Rotation = Quaternion.Identity });
            return target;
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When ammo &gt; 0 and cooldown == 0 and target is alive, Execute must:
        ///   • Publish exactly one <see cref="FireRequestEvent"/>.
        ///   • Direction is a normalised unit vector pointing from shooter to target.
        ///   • WeaponState.Ammo is decremented by 1.
        /// </summary>
        [Fact]
        public void AimAndFire_EmitsFireRequestEvent_WhenConditionsAreMet()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(
                shooterPos:        Vector3.Zero,
                ammo:              5,
                cooldownRemaining: 0,
                target:            target,
                cooldownTicks:     3);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);

            // Swap buffers so published events are visible to Consume.
            _world.Bus.SwapBuffers();

            var events = _world.Bus.Consume<FireRequestEvent>();
            Assert.Equal(1, events.Length);

            var evt = events[0];
            Assert.Equal(shooter, evt.Shooter);
            Assert.Equal(target,  evt.Target);

            // Direction must be normalised (length ≈ 1).
            Assert.InRange(evt.Direction.Length(), 0.999f, 1.001f);

            // Ammo decremented from 5 to 4.
            Assert.Equal(4, _world.GetComponent<WeaponState>(shooter).Ammo);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When <see cref="WeaponState.CooldownTicksRemaining"/> &gt; 0, Execute must
        /// decrement the cooldown and NOT publish a <see cref="FireRequestEvent"/>.
        /// Status stays Running.
        /// </summary>
        [Fact]
        public void AimAndFire_DoesNotFire_WhenCooldownActive()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(
                shooterPos:        Vector3.Zero,
                ammo:              5,
                cooldownRemaining: 5,
                target:            target,
                cooldownTicks:     5);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            var events = _world.Bus.Consume<FireRequestEvent>();
            Assert.Equal(0, events.Length);

            Assert.Equal(NodeStatus.Running, channel.Status);

            // Cooldown decremented from 5 to 4.
            Assert.Equal(4, _world.GetComponent<WeaponState>(shooter).CooldownTicksRemaining);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When <see cref="WeaponState.Ammo"/> == 0, Execute must report
        /// <see cref="NodeStatus.Failure"/> and publish no event.
        /// </summary>
        [Fact]
        public void AimAndFire_ReportsFailure_WhenAmmoZero()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(
                shooterPos:        Vector3.Zero,
                ammo:              0,
                cooldownRemaining: 0,
                target:            target,
                cooldownTicks:     3);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.Equal(NodeStatus.Failure, channel.Status);

            var events = _world.Bus.Consume<FireRequestEvent>();
            Assert.Equal(0, events.Length);
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When the target entity is not alive (destroyed before Execute), the executor
        /// must report <see cref="NodeStatus.Success"/> (objective achieved) and must NOT
        /// publish a <see cref="FireRequestEvent"/>.
        /// </summary>
        [Fact]
        public void AimAndFire_ReportsSuccess_WhenTargetDead()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(
                shooterPos:        Vector3.Zero,
                ammo:              5,
                cooldownRemaining: 0,
                target:            target,
                cooldownTicks:     3);

            _executor.OnEnter(shooter, ref channel, _world);

            // Destroy the target before Execute is called.
            _world.DestroyEntity(target);

            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.Equal(NodeStatus.Success, channel.Status);

            var events = _world.Bus.Consume<FireRequestEvent>();
            Assert.Equal(0, events.Length);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Multi-tick cooldown gating: a cooldown of 3 means the executor fires on tick 4,
        /// not on ticks 1–3. This test is the most important for proving the gating logic.
        /// </summary>
        [Fact]
        public void AimAndFire_DecrementsCooldown_EachTick_UntilCanFire()
        {
            const int initialCooldown = 3;
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(
                shooterPos:        Vector3.Zero,
                ammo:              5,
                cooldownRemaining: initialCooldown,
                target:            target,
                cooldownTicks:     initialCooldown);

            _executor.OnEnter(shooter, ref channel, _world);

            // Ticks 1, 2, 3: cooldown is still > 0 — no event should be emitted.
            for (int tick = 1; tick <= initialCooldown; tick++)
            {
                _executor.Execute(shooter, ref channel, _world, 0.016f);
                _world.Bus.SwapBuffers();

                var earlyEvents = _world.Bus.Consume<FireRequestEvent>();
                Assert.Equal(0, earlyEvents.Length);
                Assert.Equal(NodeStatus.Running, channel.Status);
            }

            // After the loop the cooldown on the weapon is 0 (decremented by executor).
            Assert.Equal(0, _world.GetComponent<WeaponState>(shooter).CooldownTicksRemaining);

            // Tick 4: cooldown == 0 → executor fires.
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            var fireEvents = _world.Bus.Consume<FireRequestEvent>();
            Assert.Equal(1, fireEvents.Length);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }
    }
}
