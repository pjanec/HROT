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

        /// <summary>
        /// Enters the action ALREADY AIMED (<c>CE-3136</c> P-3, D3): the tests below are about cooldown, ammo, ROE and the friendly
        /// line, not the aim time — the aim time's own rails (<c>P3_*</c>) enter with <see cref="AimAndFireExecutor.OnEnter"/>.
        /// </summary>
        private unsafe void EnterAimed(Entity shooter, ref WeaponChannel channel)
        {
            _executor.OnEnter(shooter, ref channel, _world);
            var p = Unsafe.Read<AimAndFireParams>(Unsafe.AsPointer(ref channel.Params[0]));
            Unsafe.Write(Unsafe.AsPointer(ref channel.State[sizeof(Entity)]), new AimAndFireExecutor.AimState
                { Target = p.Target, Stance = (byte)Fdp.Toolkit.Tkb.Domain.StanceId.Standing, Ready = 1 });
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

            EnterAimed(shooter, ref channel);
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

            EnterAimed(shooter, ref channel);
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

            EnterAimed(shooter, ref channel);
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

            EnterAimed(shooter, ref channel);
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

            EnterAimed(shooter, ref channel);

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

            EnterAimed(shooter, ref channel);

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

            EnterAimed(shooter, ref channel);
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

            EnterAimed(shooter, ref channel);
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

            EnterAimed(shooter, ref channel);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.Equal(fires ? 1 : 0, _world.Bus.Read<WeaponFireIntent>().Length);
            Assert.Equal(fires ? 4 : 5, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        /// <summary>⭐ <c>CE-3064</c> (R-206) — ReturnFire also answers a NEAR MISS within the window, not only a hit.</summary>
        [Theory]
        [InlineData(1.0, true)]    // near-missed 1 s ago
        [InlineData(6.0, false)]   // outside the 5 s window
        public void AimAndFire_ReturnFire_AnswersANearMiss_CE3064(double secondsAgo, bool fires)
        {
            _world.RegisterComponent<Roe>();
            _world.RegisterComponent<RecentSenses>();
            const double now = 100.0;
            _world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now, DeltaTime = 0.016f, TimeScale = 1f });

            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);
            _world.AddComponent(shooter, new Roe { Fire = RoeFire.ReturnFire });
            var senses = new RecentSenses();
            senses.Record(Fdp.Toolkit.Perception.Events.SensorChange.NearMiss, now - secondsAgo);
            _world.AddComponent(shooter, senses);

            EnterAimed(shooter, ref channel);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.Equal(fires ? 1 : 0, _world.Bus.Read<WeaponFireIntent>().Length);
        }

        /// <summary>⭐ <c>CE-2095</c> — the unit's own ReturnFire window replaces the 5 s default: hit 7 s ago fires under a 10 s
        /// window and not under the default (0 = unset); a 2 s window refuses a hit 3 s ago.</summary>
        [Theory]
        [InlineData(10f, 7.0, true)]
        [InlineData(0f,  7.0, false)]   // unset ⇒ the 5 s default
        [InlineData(2f,  3.0, false)]
        [InlineData(2f,  1.0, true)]
        public void AimAndFire_ReturnFire_UsesTheUnitsOwnWindow_CE2095(float window, double hitSecondsAgo, bool fires)
        {
            _world.RegisterComponent<Roe>();
            _world.RegisterComponent<RecentSenses>();
            const double now = 100.0;
            _world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now, DeltaTime = 0.016f, TimeScale = 1f });

            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);
            _world.AddComponent(shooter, new Roe { Fire = RoeFire.ReturnFire, ReturnFireWindowSeconds = window });
            var senses = new RecentSenses();
            senses.Record(Fdp.Toolkit.Perception.Events.SensorChange.Hit, now - hitSecondsAgo);
            _world.AddComponent(shooter, senses);

            EnterAimed(shooter, ref channel);
            _executor.Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.Equal(fires ? 1 : 0, _world.Bus.Read<WeaponFireIntent>().Length);
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
            // ── CE-3136 P-3 — aim time (D3) and the sight gate (D4) ────────────────────────────────────────────────────────

        /// <summary>A world with perception: the shooter carries <see cref="Fdp.Toolkit.Perception.Components.ActiveSensorTracks"/>.</summary>
        private void RegisterSight() => _world.RegisterComponent<Fdp.Toolkit.Perception.Components.ActiveSensorTracks>();

        private unsafe void SetSight(Entity shooter, Entity target, bool visual)
        {
            var tracks = new Fdp.Toolkit.Perception.Components.ActiveSensorTracks();
            if (visual)
            {
                tracks.EntityIds[0]  = (long)target.PackedValue;
                tracks.Modalities[0] = (byte)Fdp.Toolkit.Perception.Components.SensorModality.Visual;
                tracks.Count = 1;
            }
            if (_world.HasComponent<Fdp.Toolkit.Perception.Components.ActiveSensorTracks>(shooter))
                _world.SetComponent(shooter, tracks);
            else
                _world.AddComponent(shooter, tracks);
        }

        private int Step(Entity shooter, ref WeaponChannel channel, float dt)
        {
            _executor.Execute(shooter, ref channel, _world, dt);
            _world.Bus.SwapBuffers();
            return _world.Bus.Read<WeaponFireIntent>().Length;
        }

        /// <summary>⭐ <c>CE-3136</c> P-3 (D3) — no round before <c>AimSeconds</c> (fallback 0.8 s) of continuous sight; then it
        /// goes, and the next round needs only the cooldown.</summary>
        [Fact]
        public void P3_NoRoundBeforeTheAimTime_ThenOnlyTheCooldown()
        {
            RegisterSight();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, cooldownSeconds: 0.2f);
            SetSight(shooter, target, visual: true);
            _executor.OnEnter(shooter, ref channel, _world);

            int fired = 0;
            for (int i = 0; i < 7; i++) fired += Step(shooter, ref channel, 0.1f);   // 0.7 s < 0.8 s
            Assert.Equal(0, fired);
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);

            Assert.Equal(1, Step(shooter, ref channel, 0.1f));                        // 0.8 s — aimed
            Assert.Equal(0, Step(shooter, ref channel, 0.1f));                        // cooldown 0.2 s …
            Assert.Equal(0, Step(shooter, ref channel, 0.1f));
            Assert.Equal(1, Step(shooter, ref channel, 0.1f));                        // … then the next round, no new aim
            Assert.Equal(3, _world.GetComponent<WeaponState>(shooter).Ammo);
        }

        /// <summary>⭐ <c>CE-3136</c> P-3 (D3 + D4) — a target out of sight is held (Running, no round spent) and losing sight
        /// restarts the aim: 0.5 s seen, a blink, then the full 0.8 s again.</summary>
        [Fact]
        public void P3_LostSightHoldsFire_AndRestartsTheAim()
        {
            RegisterSight();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, cooldownSeconds: 0.2f);
            SetSight(shooter, target, visual: false);
            _executor.OnEnter(shooter, ref channel, _world);

            int fired = 0;
            for (int i = 0; i < 20; i++) fired += Step(shooter, ref channel, 0.1f);  // 2 s hidden
            Assert.Equal(0, fired);
            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);

            SetSight(shooter, target, visual: true);
            for (int i = 0; i < 5; i++) fired += Step(shooter, ref channel, 0.1f);   // 0.5 s seen
            SetSight(shooter, target, visual: false);
            fired += Step(shooter, ref channel, 0.1f);                                // a blink
            SetSight(shooter, target, visual: true);
            for (int i = 0; i < 7; i++) fired += Step(shooter, ref channel, 0.1f);   // 0.7 s — would have been 1.2 s without the restart
            Assert.Equal(0, fired);
            Assert.Equal(1, Step(shooter, ref channel, 0.1f));
        }

        /// <summary>⭐ <c>CE-3136</c> P-3 (D4) — a target HEARD but not seen (no Visual modality) is not an aimed shot.</summary>
        [Fact]
        public unsafe void P3_AHeardTargetIsNotSeen()
        {
            RegisterSight();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, _) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.2f);
            var tracks = new Fdp.Toolkit.Perception.Components.ActiveSensorTracks();
            tracks.EntityIds[0]  = (long)target.PackedValue;
            tracks.Modalities[0] = (byte)Fdp.Toolkit.Perception.Components.SensorModality.Acoustic;
            tracks.Count = 1;
            _world.AddComponent(shooter, tracks);
            Assert.False(Fdp.Toolkit.Perception.SightNow.Sees(_world, shooter, target));
            SetSight(shooter, target, visual: true);
            Assert.True(Fdp.Toolkit.Perception.SightNow.Sees(_world, shooter, target));
        }

        /// <summary>⭐ <c>CE-3136</c> P-3 — a NEW action aims afresh: an aimed shooter re-entered on the same target waits the aim
        /// time again (OnEnter clears the timer, so a stale aim never survives between actions).</summary>
        [Fact]
        public void P3_ANewActionAimsAfresh()
        {
            RegisterSight();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, cooldownSeconds: 0.05f);
            SetSight(shooter, target, visual: true);
            EnterAimed(shooter, ref channel);
            Assert.Equal(1, Step(shooter, ref channel, 0.1f));

            _executor.OnEnter(shooter, ref channel, _world);
            int fired = 0;
            for (int i = 0; i < 7; i++) fired += Step(shooter, ref channel, 0.1f);
            Assert.Equal(0, fired);
        }
            /// <summary>⭐ <c>CE-3136</c> B7 — <see cref="AimAndFireParams.Rounds"/> = 3 fires three aimed rounds and ends in Success;
        /// 0 keeps firing (today's behaviour).</summary>
        [Theory]
        [InlineData(3, 3, NodeStatus.Success)]
        [InlineData(0, 4, NodeStatus.Running)]   // 8 steps, a round every second step (cooldown 0.05 s drains in one)
        public unsafe void B7_RoundsEndsTheActionAfterThatMany(int rounds, int expectedFired, NodeStatus expectedStatus)
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 10, 0f, target, cooldownSeconds: 0.05f);
            ref var p = ref Unsafe.AsRef<AimAndFireParams>(Unsafe.AsPointer(ref channel.Params[0]));
            p.Rounds = rounds;
            EnterAimed(shooter, ref channel);

            int fired = 0;
            for (int i = 0; i < 8 && channel.Status == NodeStatus.Running; i++) fired += Step(shooter, ref channel, 0.1f);
            Assert.Equal(expectedFired, fired);
            Assert.Equal(expectedStatus, channel.Status);
            Assert.Equal(10 - fired, _world.GetComponent<WeaponState>(shooter).Ammo);
        }
            /// <summary>
        /// ⭐ <c>CE-3136</c> P-5 (peek-and-fire D9, R-239) — a 30-round magazine of a 150-round load: 30 rounds go, the 31st waits
        /// for the 3 s reload (held — Running, no round), then fire resumes. <see cref="WeaponState.Ammo"/> stays the TOTAL.
        /// The dispatcher runs the reload; here <see cref="Fdp.Toolkit.Combat.Magazine.Tick"/> stands in for it.
        /// </summary>
        [Fact]
        public void P5_TheThirtyFirstRound_WaitsForTheReload()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 150, 0f, target, cooldownSeconds: 0f);
            ref var w = ref _world.GetComponentRW<WeaponState>(shooter);
            Fdp.Toolkit.Combat.Magazine.Load(ref w, 30, 3f);
            EnterAimed(shooter, ref channel);

            int StepWithReload(float dt)
            {
                Fdp.Toolkit.Combat.Magazine.Tick(ref _world.GetComponentRW<WeaponState>(shooter), dt);
                return Step(shooter, ref channel, dt);
            }

            int fired = 0;
            for (int i = 0; i < 30; i++) fired += StepWithReload(0.1f);
            Assert.Equal(30, fired);
            var after30 = _world.GetComponent<WeaponState>(shooter);
            Assert.Equal(120, after30.Ammo);                 // the total
            Assert.Equal(0, after30.MagazineRounds);
            Assert.True(Fdp.Toolkit.Combat.Magazine.Reloading(after30));

            int steps = 0;
            while (fired == 30 && steps < 50) { fired += StepWithReload(0.1f); steps++; }
            Assert.Equal(31, fired);
            Assert.InRange(steps * 0.1f, 2.95f, 3.15f);       // held for the 3 s reload (±1 step), Running all along
            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(29, _world.GetComponent<WeaponState>(shooter).MagazineRounds);
        }

        /// <summary>⭐ <c>CE-3136</c> P-5 — a mount with NO magazine (size 0) fires its whole load, as before P-5.</summary>
        [Fact]
        public void P5_NoMagazine_FiresTheWholeLoad()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 40, 0f, target, cooldownSeconds: 0f);
            EnterAimed(shooter, ref channel);
            int fired = 0;
            for (int i = 0; i < 40; i++) fired += Step(shooter, ref channel, 0.1f);
            Assert.Equal(40, fired);
            Step(shooter, ref channel, 0.1f);
            Assert.Equal(NodeStatus.Failure, channel.Status);   // out of ammunition altogether
        }
            /// <summary>
        /// ⭐ <c>CE-3136</c> (T1) — the executor SAYS why it is not firing, on the unit's <see cref="Fdp.Toolkit.Behavior.Diagnostics.ActionStatus"/>
        /// weapon row: hidden ⇒ "hold: not seen"; seen ⇒ "aiming 0.x of 0.8 s"; then "firing"; an empty magazine ⇒ "reloading".
        /// </summary>
        [Fact]
        public void T1_TheExecutorSaysWhy_InTheWeaponRow()
        {
            RegisterSight();
            _world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 60, 0f, target, cooldownSeconds: 0f);
            _world.AddComponent(shooter, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            ref var w = ref _world.GetComponentRW<WeaponState>(shooter);
            Fdp.Toolkit.Combat.Magazine.Load(ref w, 1, 2f);   // one round per magazine
            Fdp.Toolkit.Behavior.Diagnostics.ActionStatusRow Row() => _world.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(shooter).Weapon;

            SetSight(shooter, target, visual: false);
            _executor.OnEnter(shooter, ref channel, _world);
            Step(shooter, ref channel, 0.1f);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldNotSeen, Row().Reason);
            Assert.Equal(target, Row().Target);

            SetSight(shooter, target, visual: true);
            Step(shooter, ref channel, 0.1f);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Aiming, Row().Reason);
            Assert.Equal(0.8f, Row().Needed, 3);
            Assert.InRange(Row().Progress, 0.09f, 0.11f);

            int fired = 0;
            for (int i = 0; i < 7 && fired == 0; i++) fired += Step(shooter, ref channel, 0.1f);
            Assert.Equal(1, fired);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Firing, Row().Reason);

            Step(shooter, ref channel, 0.1f);   // the one-round magazine is empty
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Reloading, Row().Reason);
            Assert.Equal(2f, Row().Needed, 3);
        }
    }
}
