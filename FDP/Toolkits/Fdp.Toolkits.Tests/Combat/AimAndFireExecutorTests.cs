using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Core
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Executors;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AimAndFireExecutor"/> (BCS-P5-T2 / BS1-T004).
    /// Each test drives the executor directly without a dispatcher system, using a real
    /// <see cref="EntityRepository"/> for component access and event bus assertions.
    ///
    /// <b>PACK-P003:</b> Executor no longer uses <see cref="Fdp.Toolkit.Replication.Services.NetworkEntityMap"/>.
    /// <see cref="WeaponFireIntent"/> carries local ECS <see cref="Entity"/> handles directly.
    /// The tests verify that the published entity handles match the spawned entities.
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
            _world.RegisterEvent<WeaponFireIntent>();
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
        /// <paramref name="cooldownSeconds"/>.
        /// </summary>
        private unsafe (Entity shooter, WeaponChannel channel)
            SpawnShooter(Vector3 shooterPos, int ammo, float cooldownRemaining,
                         Entity target, float cooldownSeconds)
        {
            var shooter = _world.CreateEntity();
            _world.AddComponent(shooter, new SimTransform { Position = shooterPos, Rotation = Quaternion.Identity });
            _world.AddComponent(shooter, new WeaponState
            {
                Ammo                       = ammo,
                CooldownSecondsRemaining   = cooldownRemaining,
                MuzzleVelocity             = 800f,
            });
            _world.AddComponent(shooter, new WeaponChannel());

            var channel = _world.GetComponent<WeaponChannel>(shooter);
            channel.Status = NodeStatus.Running;

            var p = new AimAndFireParams { Target = target, CooldownSeconds = cooldownSeconds };
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
        /// BS1-T004 SC-1: When ammo > 0 and cooldown == 0 and target is alive, Execute must:
        ///   • Publish exactly one <see cref="WeaponFireIntent"/>.
        ///   • Shooter == local ECS entity handle of the shooter.
        ///   • Target  == local ECS entity handle of the target.
        ///   • WeaponIndex == 0 (single weapon slot POC).
        ///   • channel.Status == NodeStatus.Running after firing (firing is not terminal).
        ///   • WeaponState.Ammo is decremented by 1.
        /// </summary>
        [Fact]
        public void AimAndFire_EmitsWeaponFireIntent_WhenConditionsAreMet()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(
                shooterPos:       Vector3.Zero,
                ammo:             5,
                cooldownRemaining: 0f,
                target:           target,
                cooldownSeconds:  0.05f);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);

            // Swap buffers so published events are visible to Consume.
            _world.Bus.SwapBuffers();

            var intents = _world.Bus.Read<WeaponFireIntent>();
            Assert.Equal(1, intents.Length);

            var intent = intents[0];
            // PACK-P003: assert local ECS Entity handles, not network IDs.
            Assert.Equal(shooter, intent.Shooter);
            Assert.Equal(target,  intent.Target);
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
                cooldownRemaining: 0f,
                target:            target,
                cooldownSeconds:   0.05f);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            var fireRequests = _world.Bus.Read<FireRequestEvent>();
            Assert.Equal(0, fireRequests.Length);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T004 SC-3: When <see cref="WeaponState.CooldownSecondsRemaining"/> > 0, Execute must
        /// decrement the cooldown by dt and NOT publish a <see cref="WeaponFireIntent"/>.
        /// Status stays Running.
        /// </summary>
        [Fact]
        public void AimAndFire_DoesNotFire_WhenCooldownActive()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(
                shooterPos:        Vector3.Zero,
                ammo:              5,
                cooldownRemaining: 0.5f,
                target:            target,
                cooldownSeconds:   0.5f);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            var intents = _world.Bus.Read<WeaponFireIntent>();
            Assert.Equal(0, intents.Length);

            Assert.Equal(NodeStatus.Running, channel.Status);

            // Cooldown decremented by dt (0.5 - 0.016 = 0.484).
            Assert.True(_world.GetComponent<WeaponState>(shooter).CooldownSecondsRemaining < 0.5f);
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
                cooldownRemaining: 0f,
                target:            target,
                cooldownSeconds:   0.05f);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.Equal(NodeStatus.Failure, channel.Status);

            var intents = _world.Bus.Read<WeaponFireIntent>();
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
                cooldownRemaining: 0f,
                target:            target,
                cooldownSeconds:   0.05f);

            _executor.OnEnter(shooter, ref channel, _world);

            // Destroy the target before Execute is called.
            _world.DestroyEntity(target);

            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.Equal(NodeStatus.Success, channel.Status);

            var intents = _world.Bus.Read<WeaponFireIntent>();
            Assert.Equal(0, intents.Length);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// <summary>
        /// BS1-T004 SC-3 (cooldown drain): A cooldown of 0.1 s drains to zero after enough
        /// dt steps, then the executor fires on the next call with no remaining cooldown.
        /// Ammo is decremented exactly once on the firing step.
        /// </summary>
        [Fact]
        public void AimAndFire_DrainsCooldown_ByDt_UntilCanFire()
        {
            const float cooldownSec = 0.1f;
            const float dt         = 0.016f; // ~60 Hz frame

            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(
                shooterPos:        Vector3.Zero,
                ammo:              5,
                cooldownRemaining: cooldownSec,
                target:            target,
                cooldownSeconds:   cooldownSec);

            _executor.OnEnter(shooter, ref channel, _world);

            // Drain cooldown step by step; no intent should fire while cooldown > 0.
            int drainSteps = (int)System.Math.Ceiling(cooldownSec / dt);
            for (int i = 0; i < drainSteps; i++)
            {
                _executor.Execute(shooter, ref channel, _world, dt);
                _world.Bus.SwapBuffers();

                var earlyIntents = _world.Bus.Read<WeaponFireIntent>();
                Assert.Equal(0, earlyIntents.Length);
                Assert.Equal(NodeStatus.Running, channel.Status);
            }

            // Cooldown is now <= 0 (fully drained).
            Assert.True(_world.GetComponent<WeaponState>(shooter).CooldownSecondsRemaining <= 0f);

            // Next Execute fires.
            _executor.Execute(shooter, ref channel, _world, dt);
            _world.Bus.SwapBuffers();

            var fireIntents = _world.Bus.Read<WeaponFireIntent>();
            Assert.Equal(1, fireIntents.Length);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        // ── CE-321 ────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <c>CE-321</c> — a unit never fires through a friendly: with a same-force collider on the line it HOLDS
        /// (Running, no intent, no ammo spent); once the friendly moves off the line it fires.
        /// </summary>
        [Fact]
        public void AimAndFire_HoldsFire_WhileAFriendlyIsOnTheLine_CE321()
        {
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();

            var target = SpawnTarget(new Vector3(20f, 0f, 0f));
            _world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile });
            var (shooter, channel) = SpawnShooter(Vector3.Zero, ammo: 5, cooldownRemaining: 0f, target, cooldownSeconds: 0.05f);
            _world.AddComponent(shooter, new EntityInfo { ForceId = ForceId.Friend });

            var apc = _world.CreateEntity();
            _world.AddComponent(apc, new SimTransform { Position = new Vector3(8f, 1f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(apc, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(apc, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 3.5f });

            // A HOSTILE collider on the same line does not hold fire — only friends do.
            var cover = _world.CreateEntity();
            _world.AddComponent(cover, new SimTransform { Position = new Vector3(12f, 0f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(cover, new EntityInfo { ForceId = ForceId.Hostile });
            _world.AddComponent(cover, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 3.5f });

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();
            Assert.Equal(0, _world.Bus.Read<WeaponFireIntent>().Length);
            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);

            // The APC moves off the line (5 m to the side, radius 3.5) ⇒ the shot goes.
            _world.GetComponentRW<SimTransform>(apc).Position = new Vector3(8f, 5f, 0f);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();
            Assert.Equal(1, _world.Bus.Read<WeaponFireIntent>().Length);
            Assert.Equal(4, _world.GetComponent<WeaponState>(shooter).Ammo);
        }

        /// <summary>⭐ <c>CE-321</c> — a target at 0 HP that is still in the world (CE-267: the body stays) ends the action
        /// — Success, no round fired.</summary>
        [Fact]
        public void AimAndFire_ReportsSuccess_WhenTargetIsCombatDead_ButStillInTheWorld_CE321()
        {
            _world.RegisterComponent<Health>();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            _world.AddComponent(target, new Health { Current = 0f, Max = 100f });
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.True(_world.IsAlive(target));
            Assert.Equal(NodeStatus.Success, channel.Status);
            Assert.Equal(0, _world.Bus.Read<WeaponFireIntent>().Length);
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);
        }

        /// <summary>
        /// ⭐ <c>CE-2075</c> (R-200) — the ROE is enforced here: HoldFire never fires; ReturnFire fires only within the window
        /// after a hit; FireAtWill (and no ROE) fires. A held shot spends no round and keeps the action Running.
        /// </summary>
        [Theory]
        [InlineData(RoeFire.HoldFire,   null,  false)]
        [InlineData(RoeFire.HoldFire,   1.0,   false)]   // even when hit
        [InlineData(RoeFire.ReturnFire, null,  false)]   // never hit
        [InlineData(RoeFire.ReturnFire, 1.0,   true)]    // hit 1 s ago
        [InlineData(RoeFire.ReturnFire, 6.0,   false)]   // hit 6 s ago — outside the 5 s window
        [InlineData(RoeFire.FireAtWill, null,  true)]
        [InlineData(RoeFire.FireUnset,  null,  true)]    // no ROE given ⇒ fire at will
        public void AimAndFire_EnforcesTheUnitsRoe_CE2075(RoeFire fire, double? hitSecondsAgo, bool fires)
        {
            _world.RegisterComponent<Roe>();
            _world.RegisterComponent<RecentSenses>();
            const double now = 100.0;
            _world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now, DeltaTime = 0.016f, TimeScale = 1f });

            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);
            _world.AddComponent(shooter, new Roe { Fire = fire });
            var senses = new RecentSenses();
            if (hitSecondsAgo is double ago) senses.Record(Fdp.Toolkit.Perception.Events.SensorChange.Hit, now - ago);
            _world.AddComponent(shooter, senses);

            _executor.OnEnter(shooter, ref channel, _world);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.Equal(fires ? 1 : 0, _world.Bus.Read<WeaponFireIntent>().Length);
            Assert.Equal(fires ? 4 : 5, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        /// <summary>⭐ <c>CE-2075</c> — a world without the ROE component types fires as before (fire at will).</summary>
        [Fact]
        public void AimAndFire_FiresAtWill_WhenTheWorldHasNoRoe_CE2075()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, _) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);
            Assert.True(AimAndFireExecutor.RoePermitsFire(_world, shooter));
        }

        /// <summary>⭐ <c>CE-321</c> — a friendly BEHIND the shooter or BEYOND the target is not on the line of fire.</summary>
        [Fact]
        public void LineOfFire_IgnoresFriendliesBehindTheShooter_AndBeyondTheTarget_CE321()
        {
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            var target = SpawnTarget(new Vector3(20f, 0f, 0f));
            var (shooter, _) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);
            _world.AddComponent(shooter, new EntityInfo { ForceId = ForceId.Friend });

            foreach (var x in new[] { -6f, 26f })
            {
                var f = _world.CreateEntity();
                _world.AddComponent(f, new SimTransform { Position = new Vector3(x, 0f, 0f), Rotation = Quaternion.Identity });
                _world.AddComponent(f, new EntityInfo { ForceId = ForceId.Friend });
                _world.AddComponent(f, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 3.5f });
            }

            Assert.False(Fdp.Toolkit.Combat.LineOfFire.BlockedByFriendly(_world, shooter, target));
        }
    }
}

