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
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace FDP.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AimAndFireExecutor"/> (BCS-P5-T2 / BS1-T004).
    /// Each test drives the executor directly without a dispatcher system, using a real
    /// <see cref="EntityRepository"/> for component access and event bus assertions.
    ///
    /// <b>BS1-T004:</b> Executor now publishes <see cref="WeaponFireIntent"/> (stable
    /// network entity IDs) instead of <see cref="FireRequestEvent"/> (local ECS handles).
    /// Tests use a real <see cref="NetworkEntityMap"/> to register entities and validate
    /// the IDs embedded in the published intent.
    /// </summary>
    public class AimAndFireExecutorTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;
        private readonly AimAndFireExecutor _executor;

        public AimAndFireExecutorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<WeaponChannel>();
            _world.RegisterEvent<FireRequestEvent>();
            _world.RegisterEvent<WeaponFireIntent>();
            _world.RegisterEvent<HitEvent>();

            _entityMap = new NetworkEntityMap();
            _executor  = new AimAndFireExecutor(_entityMap);
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
        /// Registers the entity in <see cref="_entityMap"/> under
        /// <paramref name="shooterNetId"/>.
        /// </summary>
        private unsafe (Entity shooter, WeaponChannel channel)
            SpawnShooter(Vector3 shooterPos, int ammo, int cooldownRemaining,
                         Entity target, int cooldownTicks, long shooterNetId = 100L)
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

            _entityMap.Register(shooterNetId, shooter);

            var channel = _world.GetComponent<WeaponChannel>(shooter);
            channel.Status = NodeStatus.Running;

            var p = new AimAndFireParams { Target = target, CooldownTicks = cooldownTicks };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            _world.SetComponent(shooter, channel);
            channel = _world.GetComponent<WeaponChannel>(shooter);

            return (shooter, channel);
        }

        /// <summary>
        /// Spawns a target entity at <paramref name="pos"/> and registers it in
        /// <see cref="_entityMap"/> under <paramref name="targetNetId"/>.
        /// </summary>
        private Entity SpawnTarget(Vector3 pos, long targetNetId = 200L)
        {
            var target = _world.CreateEntity();
            _world.AddComponent(target, new SimTransform { Position = pos, Rotation = Quaternion.Identity });
            _entityMap.Register(targetNetId, target);
            return target;
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T004 SC-1: When ammo > 0 and cooldown == 0 and target is alive, Execute must:
        ///   • Publish exactly one <see cref="WeaponFireIntent"/>.
        ///   • ShooterEntityId == network ID of the shooter entity.
        ///   • TargetEntityId  == network ID of the target entity.
        ///   • WeaponIndex     == 0 (single weapon slot POC).
        ///   • channel.Status  == NodeStatus.Running after firing (firing is not terminal).
        ///   • WeaponState.Ammo is decremented by 1.
        /// </summary>
        [Fact]
        public void AimAndFire_EmitsWeaponFireIntent_WhenConditionsAreMet()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f), targetNetId: 200L);
            var (shooter, channel) = SpawnShooter(
                shooterPos:        Vector3.Zero,
                ammo:              5,
                cooldownRemaining: 0,
                target:            target,
                cooldownTicks:     3,
                shooterNetId:      100L);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);

            // Swap buffers so published events are visible to Consume.
            _world.Bus.SwapBuffers();

            var intents = _world.Bus.Consume<WeaponFireIntent>();
            Assert.Equal(1, intents.Length);

            var intent = intents[0];
            Assert.Equal(100L, intent.ShooterEntityId);
            Assert.Equal(200L, intent.TargetEntityId);
            // Batch-01 review fix (Issue 3): assert WeaponIndex matches POC contract.
            Assert.Equal(0, intent.WeaponIndex);

            // Batch-01 review fix (Issue 3): firing is not terminal — status stays Running.
            var channelAfter = _world.GetComponent<WeaponChannel>(shooter);
            Assert.Equal(NodeStatus.Running, channelAfter.Status);

            // Ammo decremented from 5 to 4.
            Assert.Equal(4, _world.GetComponent<WeaponState>(shooter).Ammo);
        }

        /// <summary>
        /// BS1-T004 SC-2: After the refactor, Execute must NOT publish any
        /// <see cref="FireRequestEvent"/> (CQRS chain uses <see cref="WeaponFireIntent"/>).
        /// </summary>
        [Fact]
        public void AimAndFire_DoesNotEmitFireRequestEvent_WhenConditionsAreMet()
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
            _world.Bus.SwapBuffers();

            var fireRequests = _world.Bus.Consume<FireRequestEvent>();
            Assert.Equal(0, fireRequests.Length);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T004 SC-3: When <see cref="WeaponState.CooldownTicksRemaining"/> > 0, Execute must
        /// decrement the cooldown and NOT publish a <see cref="WeaponFireIntent"/>.
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

            var intents = _world.Bus.Consume<WeaponFireIntent>();
            Assert.Equal(0, intents.Length);

            Assert.Equal(NodeStatus.Running, channel.Status);

            // Cooldown decremented from 5 to 4.
            Assert.Equal(4, _world.GetComponent<WeaponState>(shooter).CooldownTicksRemaining);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T004 SC-4: When <see cref="WeaponState.Ammo"/> == 0, Execute must report
        /// <see cref="NodeStatus.Failure"/> and publish no <see cref="WeaponFireIntent"/>.
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

            var intents = _world.Bus.Consume<WeaponFireIntent>();
            Assert.Equal(0, intents.Length);
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When the target entity is not alive (destroyed before Execute), the executor
        /// must report <see cref="NodeStatus.Success"/> (objective achieved) and must NOT
        /// publish a <see cref="WeaponFireIntent"/>.
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

            var intents = _world.Bus.Consume<WeaponFireIntent>();
            Assert.Equal(0, intents.Length);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T004 SC-3 (ammo/cooldown preserved): Multi-tick cooldown gating — a cooldown
        /// of 3 means the executor fires (publishes WeaponFireIntent) on tick 4, not on
        /// ticks 1–3.  Ammo is decremented exactly once on the firing tick.
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

            // Ticks 1, 2, 3: cooldown is still > 0 — no intent should be emitted.
            for (int tick = 1; tick <= initialCooldown; tick++)
            {
                _executor.Execute(shooter, ref channel, _world, 0.016f);
                _world.Bus.SwapBuffers();

                var earlyIntents = _world.Bus.Consume<WeaponFireIntent>();
                Assert.Equal(0, earlyIntents.Length);
                Assert.Equal(NodeStatus.Running, channel.Status);
            }

            // After the loop the cooldown on the weapon is 0 (decremented by executor).
            Assert.Equal(0, _world.GetComponent<WeaponState>(shooter).CooldownTicksRemaining);

            // Tick 4: cooldown == 0 → executor fires.
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            var fireIntents = _world.Bus.Consume<WeaponFireIntent>();
            Assert.Equal(1, fireIntents.Length);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }
    }
}

