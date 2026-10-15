using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Core
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
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

        /// <summary>
        /// One frame as production runs it: the executor, then <see cref="WeaponDispatcherSystem.DrainCooldown"/> — the dispatcher's one
        /// drain, which the next frame applies before it runs the executor again (⭐ <c>CE-2166</c>: the executor no longer drains).
        /// </summary>
        private void Tick(Entity shooter, ref WeaponChannel channel, float dt)
        {
            _executor.Execute(shooter, ref channel, _world, dt);
            if (_world.HasComponent<WeaponState>(shooter))
                WeaponDispatcherSystem.DrainCooldown(ref _world.GetComponentRW<WeaponState>(shooter), dt);
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
            Tick(shooter, ref channel, 0.016f);

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
            Tick(shooter, ref channel, 0.016f);
            _world.Bus.SwapBuffers();

            var fireRequests = _world.Bus.Read<FireRequestEvent>();
            Assert.Equal(0, fireRequests.Length);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// BS1-T004 SC-3: When <see cref="WeaponState.CooldownSecondsRemaining"/> > 0, the executor must NOT publish a
        /// <see cref="WeaponFireIntent"/>; the frame's drain (the dispatcher's, <c>CE-2166</c>) takes dt off the cooldown.
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
            Tick(shooter, ref channel, 0.016f);
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
            Tick(shooter, ref channel, 0.016f);
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

            Tick(shooter, ref channel, 0.016f);
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
                Tick(shooter, ref channel, dt);
                _world.Bus.SwapBuffers();

                var earlyIntents = _world.Bus.Read<WeaponFireIntent>();
                Assert.Equal(0, earlyIntents.Length);
                Assert.Equal(NodeStatus.Running, channel.Status);
            }

            // Cooldown is now <= 0 (fully drained).
            Assert.True(_world.GetComponent<WeaponState>(shooter).CooldownSecondsRemaining <= 0f);

            // Next Execute fires.
            Tick(shooter, ref channel, dt);
            _world.Bus.SwapBuffers();

            var fireIntents = _world.Bus.Read<WeaponFireIntent>();
            Assert.Equal(1, fireIntents.Length);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2166</c> — through the real <see cref="WeaponDispatcherSystem"/>: a 10 s per-shot cooldown fires every 10 s. 🔴 Red-proof:
        /// the executor drained the cooldown a second time while it aimed, so the rounds left every ~5 s (9 shots in 41 s, not 5).
        /// </summary>
        [Fact]
        public unsafe void CE2166_ThroughTheDispatcher_A10SecondCooldown_FiresEvery10Seconds()
        {
            _world.RegisterComponent<ActorCapabilityState>();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, ammo: 50, cooldownRemaining: 0f, target: target, cooldownSeconds: 10f);
            _world.AddComponent(shooter, new ActorCapabilityState { Capabilities = ActorCapabilities.CanShoot });
            channel.ActiveAction = Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire;
            channel.ActionInstanceId = 1;
            _world.SetComponent(shooter, channel);
            var dispatcher = new WeaponDispatcherSystem();
            dispatcher.RegisterExecutor(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, _executor);

            const float dt = 0.1f;
            var shots = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 410; i++)
            {
                dispatcher.Execute(_world, dt);
                _world.Bus.SwapBuffers();
                if (_world.Bus.Read<WeaponFireIntent>().Length > 0) shots.Add(i * dt);
            }

            Assert.True(shots.Count >= 3, $"shots at {string.Join(", ", shots)}");
            for (int k = 1; k < shots.Count; k++)
                Assert.InRange(shots[k] - shots[k - 1], 9.9f, 10.3f);
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
            Tick(shooter, ref channel, 0.016f);
            _world.Bus.SwapBuffers();
            Assert.Equal(0, _world.Bus.Read<WeaponFireIntent>().Length);
            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);

            // The APC moves off the line (5 m to the side, radius 3.5) ⇒ the shot goes.
            _world.GetComponentRW<SimTransform>(apc).Position = new Vector3(8f, 5f, 0f);
            Tick(shooter, ref channel, 0.016f);
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
            Tick(shooter, ref channel, 0.016f);
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
            Tick(shooter, ref channel, 0.016f);
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
            Tick(shooter, ref channel, 0.016f);
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
            Tick(shooter, ref channel, 0.016f);
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
            Tick(shooter, ref channel, dt);
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
            Assert.Equal(1, Step(shooter, ref channel, 0.1f));                        // … 0.2 s later the next round, no new aim
            // ⭐ CE-2166 re-pinned: it was 0.3 s — the rail drove the executor alone, whose own drain lagged the dispatcher's by a frame
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
        [InlineData(0, 8, NodeStatus.Running)]   // 8 steps, a round every step: a 0.05 s cooldown is gone by the next 0.1 s frame (⭐ CE-2166
                                                  //   re-pinned from 4 — the rail drove the executor alone; through the dispatcher it was every step)
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

        // ── WL-6 (docs/DESIGN_Weapon_Layers.md W-13, R-272) — the weapon brain's posture ───────────────────────────────────────

        /// <summary>
        /// ⭐ WL-6 (W-13b) — the weapon brain writes the unit's <see cref="WeaponPosture"/> where it decides: hidden ⇒ Ready (raised,
        /// not aimed); seen and aiming ⇒ Aimed at the target; firing stays Aimed; then, with no weapon action running, Aimed drops to
        /// Ready at once and Ready to AtEase after <see cref="Fdp.Toolkit.Combat.WeaponPostureOf.LowerAfterSeconds"/>.
        /// </summary>
        [Fact]
        public void WL6_TheWeaponBrainWritesThePosture_ReadyWhenHidden_AimedWhenAiming_AtEaseAfterIdle()
        {
            RegisterSight();
            _world.RegisterComponent<WeaponPosture>();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 60, 0f, target, cooldownSeconds: 0.2f);
            _world.AddComponent(shooter, new WeaponPosture());
            WeaponPosture Posture() => _world.GetComponent<WeaponPosture>(shooter);
            Assert.Equal(WeaponPostureKind.AtEase, Posture().Kind);

            SetSight(shooter, target, visual: false);
            _executor.OnEnter(shooter, ref channel, _world);
            Step(shooter, ref channel, 0.1f);
            Assert.Equal(WeaponPostureKind.Ready, Posture().Kind);

            SetSight(shooter, target, visual: true);
            Step(shooter, ref channel, 0.1f);
            Assert.Equal(WeaponPostureKind.Aimed, Posture().Kind);
            Assert.Equal(target, Posture().Target);
            int fired = 0;
            for (int i = 0; i < 10 && fired == 0; i++) fired += Step(shooter, ref channel, 0.1f);
            Assert.Equal(1, fired);
            Assert.Equal(WeaponPostureKind.Aimed, Posture().Kind);

            // the action ends: no weapon action runs — the dispatcher's idle branch
            Fdp.Toolkit.Combat.WeaponPostureOf.Idle(_world, shooter, 0.1f);
            Assert.Equal(WeaponPostureKind.Ready, Posture().Kind);
            for (int i = 0; i < 45; i++) Fdp.Toolkit.Combat.WeaponPostureOf.Idle(_world, shooter, 0.1f);   // 4.6 s
            Assert.Equal(WeaponPostureKind.Ready, Posture().Kind);
            for (int i = 0; i < 5; i++) Fdp.Toolkit.Combat.WeaponPostureOf.Idle(_world, shooter, 0.1f);
            Assert.Equal(WeaponPostureKind.AtEase, Posture().Kind);
            Assert.True(Posture().Target.IsNull);
        }

        /// <summary>⭐ WL-6 (W-13b) — every weapon-brain status maps to a posture (or explicitly to none), so a new status cannot be
        /// silently drawn as "at ease" in the middle of a fight.</summary>
        [Fact]
        public void WL6_EveryWeaponStatusHasAPosture()
        {
            foreach (Fdp.Toolkit.Behavior.Diagnostics.ActionReason r in System.Enum.GetValues(typeof(Fdp.Toolkit.Behavior.Diagnostics.ActionReason)))
            {
                var kind = Fdp.Toolkit.Combat.WeaponPostureOf.For(r);
                bool says = r is not (Fdp.Toolkit.Behavior.Diagnostics.ActionReason.None or Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Moving);
                Assert.True(says == kind.HasValue, $"{r}: {(kind?.ToString() ?? "none")}");
            }
            Assert.Equal(WeaponPostureKind.Aimed, Fdp.Toolkit.Combat.WeaponPostureOf.For(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.BlindFire));
            Assert.Equal(WeaponPostureKind.Ready, Fdp.Toolkit.Combat.WeaponPostureOf.For(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Reloading));
        }

        // ── ⭐ M8 (DESIGN_Missiles_And_Ammo §2) — the round per target, a switch costs one reload ─────────────────────────────

        /// <summary>A tank-gun shooter (TKB type 9100, <see cref="M8Rounds.TankGun"/>) with its loadout component, aimed at a rifleman.</summary>
        private (Entity Shooter, WeaponChannel Channel, Entity Target) LoadedTank(Fdp.Toolkit.Tkb.Domain.WeaponMountDto gun, float reloadSeconds)
        {
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<AmmoLoadout>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            M8Rounds.Register(db);
            var clone = new Fdp.Interfaces.TkbTemplate("APFSDS-B", 9004);
            clone.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.AmmoWeaponBallisticsDto { PenetrationMm = 600, Damage = 1100 });
            db.Register(clone);
            var type = new Fdp.Interfaces.TkbTemplate("Tank", M8Rounds.TankGunType);
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { gun } });
            db.Register(type);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var target = SpawnTarget(new Vector3(300, 0, 0));
            _world.AddComponent(target, new Health { Current = 100, Max = 100 });
            var (shooter, channel) = SpawnShooter(Vector3.Zero, AmmoLoadout.From(gun.Loadout).Total, 0f, target, 1f);
            _world.AddComponent(shooter, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = M8Rounds.TankGunType });
            _world.GetComponentRW<WeaponState>(shooter).ReloadSeconds = reloadSeconds;
            _world.AddComponent(shooter, AmmoLoadout.From(gun.Loadout));
            EnterAimed(shooter, ref channel);
            return (shooter, channel, target);
        }

        /// <summary>The frame and the round of the first shot (−1 / 0 when none within <paramref name="frames"/>).</summary>
        private (int Frame, long Ammo) FirstShot(Entity shooter, ref WeaponChannel channel, int frames)
        {
            const float dt = 1f / 60f;
            for (int f = 0; f < frames; f++)
            {
                Tick(shooter, ref channel, dt);
                WeaponDispatcherSystem.DrainSwitch(ref _world.GetComponentRW<AmmoLoadout>(shooter), dt);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>()) return (f, shot.AmmoGuid);
            }
            return (-1, 0);
        }

        /// <summary>
        /// ⭐⭐ M8 (D-I) — against a rifleman the tank gun switches APFSDS → HE (it also sprays the room): the switch costs ONE reload
        /// (4 s here — no round before it, the action says Reloading), then the HE round leaves, carried on the fire intent, and the
        /// HE count drops. A TIE (two rounds of equal value and area) keeps the loaded round: it fires at once, no switch.
        /// </summary>
        [Fact]
        public void M8_ASwitch_CostsOneReload_ATie_KeepsTheLoadedRound()
        {
            var (shooter, channel, _) = LoadedTank(M8Rounds.TankGun(), reloadSeconds: 4f);
            var (frame, ammo) = FirstShot(shooter, ref channel, 60 * 6);
            Assert.InRange(frame, 4 * 60 - 2, 4 * 60 + 2);
            Assert.Equal(M8Rounds.He, ammo);
            var load = _world.GetComponentRO<AmmoLoadout>(shooter);
            Assert.Equal(M8Rounds.He, load.LoadedAmmo);
            Assert.Equal(9, load.RoundsAt(2));     // HE 10 → 9
            Assert.Equal(20, load.RoundsAt(0));    // APFSDS untouched
            Assert.Equal(39, _world.GetComponentRO<WeaponState>(shooter).Ammo);
        }

        /// <inheritdoc cref="M8_ASwitch_CostsOneReload_ATie_KeepsTheLoadedRound"/>
        [Fact]
        public void M8_ATie_KeepsTheLoadedRound_AndFiresAtOnce()
        {
            var gun = M8Rounds.TankGun() with { Loadout = new()
            {
                new Fdp.Toolkit.Tkb.Domain.AmmoLoadDto { AmmoGuid = unchecked((ulong)M8Rounds.Apfsds), Rounds = 5 },
                new Fdp.Toolkit.Tkb.Domain.AmmoLoadDto { AmmoGuid = 9004, Rounds = 5 },
            } };
            var (shooter, channel, _) = LoadedTank(gun, reloadSeconds: 4f);
            var (frame, ammo) = FirstShot(shooter, ref channel, 60);
            Assert.Equal(0, frame);
            Assert.Equal(M8Rounds.Apfsds, ammo);
        }

        // ── ⭐ M4 / D-L (DESIGN_Missiles_And_Ammo §3–§4) — the missile's minimum range, the turret lock, firing on the move ─────────

        /// <summary>A per-shot shooter of TKB type 9300 with <paramref name="mount"/>, aimed at a target <paramref name="range"/> m east,
        /// moving at <paramref name="speed"/> m/s. The missile munition 9301 (min range 65 m) is registered.</summary>
        private (Entity Shooter, WeaponChannel Channel, Entity Target) TypedShooter(Fdp.Toolkit.Tkb.Domain.WeaponMountDto mount, float range, float speed = 0f)
        {
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>())
                _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            if (!_world.IsComponentTypeRegistered<SimVelocity>()) _world.RegisterComponent<SimVelocity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var missile = new Fdp.Interfaces.TkbTemplate("Javelin", 9301);
            missile.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.MissileDto { Guidance = Fdp.Toolkit.Tkb.Domain.GuidanceKind.FireAndForget, MinRangeM = 65 });
            db.Register(missile);
            var type = new Fdp.Interfaces.TkbTemplate("Shooter", 9300);
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { mount } });
            db.Register(type);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var target = SpawnTarget(new Vector3(range, 0, 0));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 10, 0f, target, 1f);
            _world.AddComponent(shooter, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 9300 });
            _world.AddComponent(shooter, new SimVelocity { Linear = new Vector3(0, speed, 0) });
            EnterAimed(shooter, ref channel);
            return (shooter, channel, target);
        }

        private int ShotsIn(Entity shooter, ref WeaponChannel channel, int frames)
        {
            int shots = 0;
            for (int f = 0; f < frames; f++)
            {
                Tick(shooter, ref channel, 1f / 60f);
                _world.Bus.SwapBuffers();
                shots += _world.Bus.Read<WeaponFireIntent>().Length;
            }
            return shots;
        }

        /// <summary>⭐ M4 — below the missile's minimum range (65 m) the weapon brain holds (TooClose: it cannot arm or turn in time);
        /// at 100 m it fires.</summary>
        [Fact]
        public void M4_BelowMinRange_TheWeaponHolds()
        {
            var launcher = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm, AmmoGuid = 9301 };
            var (near, nearCh, nearTarget) = TypedShooter(launcher, range: 40f);
            Assert.Equal(0, ShotsIn(near, ref nearCh, 30));
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.TooClose, AimAndFireExecutor.HoldOf(_world, near, nearTarget, launcher, 0));
            var (far, farCh, _) = TypedShooter(launcher, range: 100f);
            Assert.True(ShotsIn(far, ref farCh, 30) > 0);
        }

        /// <summary>
        /// ⭐ D-L — an ATGM does not fire on the move (the weapon holds: HoldMoving) — a machine gun does, at twice the spread (the same
        /// <see cref="HitModel.IsMoving"/> test doubles σ). Standing, the ATGM fires.
        /// </summary>
        [Fact]
        public void DL_AnAtgm_HoldsFireWhileMoving_AnHmg_FiresAtDoubleSpread()
        {
            var atgm = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm };
            var (movingAtgm, ch1, _) = TypedShooter(atgm, range: 300f, speed: 5f);
            Assert.Equal(0, ShotsIn(movingAtgm, ref ch1, 30));
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldMoving, AimAndFireExecutor.HoldOf(_world, movingAtgm, Entity.Null, atgm, 0));

            var (standingAtgm, ch2, _) = TypedShooter(atgm, range: 300f, speed: 0f);
            Assert.True(ShotsIn(standingAtgm, ref ch2, 30) > 0);

            var hmg = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun, DispersionMils = 2f };
            var (movingHmg, ch3, _) = TypedShooter(hmg, range: 300f, speed: 5f);
            Assert.True(ShotsIn(movingHmg, ref ch3, 30) > 0);
            _world.GetComponentRW<SimVelocity>(movingHmg).Linear = Vector3.Zero;
            float still = HitModel.Sigma(_world, movingHmg, hmg, 0);
            _world.GetComponentRW<SimVelocity>(movingHmg).Linear = new Vector3(0, 5, 0);
            Assert.Equal(2f * still, HitModel.Sigma(_world, movingHmg, hmg, 0), 5);
        }

        /// <summary>
        /// ⭐ M4 (D-D: the lock IS the aim time) — a turret ATGM that declares a 4 s aim time grants its first round only after the
        /// target was seen continuously 4 s (the slew alone is 2.25 s); the same mount without one fires as soon as the slew ends.
        /// </summary>
        [Fact]
        public void M4_TheTurretPath_TimesTheLock()
        {
            int FirstShot(float aimSeconds)
            {
                var (shooter, _, channel, _) = TurretedTank(rounds: 1, cooldown: 0.5f, category: Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm,
                                                            aimSeconds: aimSeconds);
                var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
                _executor.OnEnter(shooter, ref channel, _world);
                for (int f = 0; f < 60 * 8; f++)
                {
                    Tick(shooter, ref channel, 1f / 60f);
                    muscle.Execute(_world, 1f / 60f);
                    _world.Bus.SwapBuffers();
                    if (_world.Bus.Read<WeaponFireIntent>().Length > 0) return f;
                }
                return -1;
            }
            int unlocked = FirstShot(0f);
            Assert.InRange(unlocked, 2 * 60, 3 * 60);          // the slew only
            Assert.InRange(FirstShot(4f), 4 * 60, 4 * 60 + 5); // the lock
        }

        // ── WL-3 (docs/DESIGN_Weapon_Layers.md §5e) — a turreted mount is fought through its turret ────────────────────────────

        /// <summary>A shooter whose type has a turret and a mount on it, with the turret part built (one world = the editor's one
        /// node: the executor is the weapon brain, <see cref="Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem"/> the muscle).</summary>
        private (Entity Shooter, Entity Turret, WeaponChannel Channel, Entity Target) TurretedTank(int rounds, float cooldown, int ammo = 5,
            Fdp.Toolkit.Tkb.Domain.WeaponCategory category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.TankGun, float muzzleVelocity = 0f,
            float aimSeconds = 0f)
        {
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _world.RegisterComponent<TurretPose>();
            _world.RegisterComponent<WeaponTask>();
            _world.RegisterComponent<WeaponTaskStatus>();
            _world.RegisterComponent<WeaponMuscleState>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var type = new Fdp.Interfaces.TkbTemplate("Tank", 42);
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TurretSuiteDto { Turrets = { new Fdp.Toolkit.Tkb.Domain.TurretDto { PivotZ = 2f } } });
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
                { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = category, TurretIndex = 0, MuzzleVelocity = muzzleVelocity,
                                                              AimSeconds = aimSeconds } } });
            db.Register(type);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var target = SpawnTarget(new Vector3(0f, 100f, 2f));   // 90° left of the hull
            var (shooter, channel) = SpawnShooter(Vector3.Zero, ammo, 0f, target, cooldown);
            _world.AddComponent(shooter, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 42 });
            unsafe
            {
                var p = new AimAndFireParams { Target = target, CooldownSeconds = cooldown, Rounds = rounds };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }
            return (shooter, TurretParts.Ensure(_world, shooter, 0), channel, target);
        }

        // ── ⭐ CE-2165 V8 (handoff 2 C4; DESIGN_Vehicle_Exposure_Cycle §8, §10) — who can shoot at a drone ─────────────────────────

        /// <summary>A shooter with ONE turreted mount of <paramref name="category"/> on <paramref name="turret"/>, aiming at a target at
        /// <paramref name="targetAt"/> — <see cref="TurretedTank"/>'s world with the turret's arcs and the target's place chosen.</summary>
        private (Entity Shooter, Entity Turret, WeaponChannel Channel, Entity Target) Turreted(Fdp.Toolkit.Tkb.Domain.TurretDto turret,
            Fdp.Toolkit.Tkb.Domain.WeaponCategory category, Vector3 targetAt, int rounds)
        {
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _world.RegisterComponent<TurretPose>();
            _world.RegisterComponent<WeaponTask>();
            _world.RegisterComponent<WeaponTaskStatus>();
            _world.RegisterComponent<WeaponMuscleState>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var type = new Fdp.Interfaces.TkbTemplate("Shooter", 43);
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TurretSuiteDto { Turrets = { turret } });
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
                { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = category, TurretIndex = 0 } } });
            db.Register(type);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var target = SpawnTarget(targetAt);
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 50, 0f, target, 0.1f);
            _world.AddComponent(shooter, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 43 });
            unsafe
            {
                var p = new AimAndFireParams { Target = target, CooldownSeconds = 0.1f, Rounds = rounds };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }
            return (shooter, TurretParts.Ensure(_world, shooter, 0), channel, target);
        }

        /// <summary>
        /// ⭐⭐ <c>C4</c> (handoff 2) — the weapon-brain half of <c>ua-drone-shelter</c>: a drone 40 m up and 30 m out sits ~52° above the
        /// guns. The technical's pintle HMG (B9, TKB 104: all round, −10…+70°) is granted rounds and fires them; the tank's main gun (the
        /// M1's −10…+20°) cannot reach it — its task ends <see cref="WeaponTaskFailure.OutOfArc"/>
        /// (<c>TurretSlew.InArc</c> in <c>WeaponTaskDispatcherSystem</c>) and no round leaves. The behaviour half (the brain retries an
        /// out-of-arc failure later) is <c>ShelterFromAirNodesTests.V8_ASeenDrone_IsFiredAt_AndAnOutOfArcFailure_IsRetriedLater</c>.
        /// </summary>
        [Theory]
        [InlineData("pintle HMG", true)]
        [InlineData("tank gun", false)]
        public void V8_AnHmg_FiresAtASeenDrone_ATankGun_IsOutOfArc(string who, bool reaches)
        {
            var turret = reaches
                ? new Fdp.Toolkit.Tkb.Domain.TurretDto { PivotX = -1.3f, PivotZ = 1.9f, TrunnionX = 0.4f, TraverseMinDeg = -180f, TraverseMaxDeg = 180f,
                                                          ElevationMinDeg = -10f, ElevationMaxDeg = 70f, TraverseRateDegPerSec = 90f, ElevationRateDegPerSec = 45f }
                : new Fdp.Toolkit.Tkb.Domain.TurretDto { PivotX = -0.55f, PivotZ = 1.9f, TrunnionX = 1.65f,
                                                          ElevationMinDeg = -10f, ElevationMaxDeg = 20f, TraverseRateDegPerSec = 40f, ElevationRateDegPerSec = 10f };
            var category = reaches ? Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun : Fdp.Toolkit.Tkb.Domain.WeaponCategory.TankGun;
            var (shooter, part, channel, drone) = Turreted(turret, category, new Vector3(30f, 0f, 40f), rounds: 5);
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            _executor.OnEnter(shooter, ref channel, _world);

            const float dt = 1f / 60f;
            int shots = 0; bool outOfArc = false;
            for (int f = 0; f < 60 * 6 && channel.Status == NodeStatus.Running; f++)
            {
                Tick(shooter, ref channel, dt);
                muscle.Execute(_world, dt);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>())
                {
                    Assert.Equal(drone, shot.Target);
                    shots++;
                }
                if (_world.HasComponent<WeaponTaskStatus>(part) && _world.GetComponent<WeaponTaskStatus>(part).Reason == WeaponTaskFailure.OutOfArc)
                    outOfArc = true;
            }

            if (reaches)
            {
                Assert.True(shots >= 5, $"the {who} fired {shots} of 5 granted rounds at the drone");
                Assert.False(outOfArc, $"the {who} reported OutOfArc");
            }
            else
            {
                Assert.Equal(0, shots);
                Assert.True(outOfArc, $"the {who}'s task never ended OutOfArc (status {_world.GetComponent<WeaponTaskStatus>(part).State})");
            }
        }

        /// <summary>
        /// ⭐ WL-3 — the brain grants, the muscle aims and fires: no round while the turret slews (no AimSeconds on this path — the
        /// slew IS the aim), then exactly the rounds asked, spaced by the cooldown; every round the muscle reports is spent ONCE on
        /// the brain's WeaponState; done ⇒ Success and the turret's task back to Idle.
        /// </summary>
        [Fact]
        public void WL3_ATurretedMount_IsFoughtThroughItsTurret_TheBrainGrants_TheMuscleFires()
        {
            var (shooter, turret, channel, target) = TurretedTank(rounds: 3, cooldown: 0.5f);
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            _executor.OnEnter(shooter, ref channel, _world);

            const float dt = 1f / 60f;
            int shots = 0, firstShotFrame = -1, lastShotFrame = -1, minGap = int.MaxValue;
            for (int f = 0; f < 60 * 8 && channel.Status == NodeStatus.Running; f++)
            {
                Tick(shooter, ref channel, dt);
                muscle.Execute(_world, dt);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>())
                {
                    Assert.Equal(shooter, shot.Shooter);
                    Assert.Equal(target, shot.Target);
                    if (firstShotFrame < 0) firstShotFrame = f;
                    if (lastShotFrame >= 0) minGap = Math.Min(minGap, f - lastShotFrame);
                    lastShotFrame = f;
                    shots++;
                }
            }

            Assert.Equal(NodeStatus.Success, channel.Status);
            Assert.Equal(3, shots);
            Assert.True(firstShotFrame >= 60 * 2, $"first shot at frame {firstShotFrame}: 90° at 40°/s is 2.25 s of slewing");
            Assert.True(minGap >= 29, $"shots {minGap} frames apart: the 0.5 s cooldown is 30");
            Assert.Equal(2, _world.GetComponent<WeaponState>(shooter).Ammo);   // 5 − 3, spent once each
            Assert.Equal(WeaponTaskMode.Idle, _world.GetComponent<WeaponTask>(turret).Mode);
        }

        /// <summary>⭐ WL-3 — the grant is the magazine: an empty magazine withdraws it (Track — the gun keeps following) until the
        /// reload ends, then fire resumes under the SAME epoch.</summary>
        [Fact]
        public void WL3_AnEmptyMagazine_DropsTheTaskToTrack_UntilTheReload()
        {
            var (shooter, turret, channel, _) = TurretedTank(rounds: 0, cooldown: 0.1f, ammo: 10);
            ref var w = ref _world.GetComponentRW<WeaponState>(shooter);
            Fdp.Toolkit.Combat.Magazine.Load(ref w, 2, 1f);   // two rounds per magazine, a one-second reload
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            _executor.OnEnter(shooter, ref channel, _world);

            const float dt = 1f / 60f;
            int shots = 0;
            uint epoch = 0;
            for (int f = 0; f < 60 * 4 && shots < 2; f++)
            {
                Tick(shooter, ref channel, dt);
                muscle.Execute(_world, dt);
                _world.Bus.SwapBuffers();
                shots += _world.Bus.Read<WeaponFireIntent>().Length;
                epoch = _world.GetComponent<WeaponTask>(turret).Epoch;
            }
            Assert.Equal(2, shots);
            Tick(shooter, ref channel, dt);   // accounts the second round: the magazine is empty
            Assert.Equal(WeaponTaskMode.Track, _world.GetComponent<WeaponTask>(turret).Mode);
            Assert.True(Fdp.Toolkit.Combat.Magazine.Reloading(_world.GetComponent<WeaponState>(shooter)));

            ref var reloading = ref _world.GetComponentRW<WeaponState>(shooter);
            Fdp.Toolkit.Combat.Magazine.Tick(ref reloading, 1.1f);   // the weapon dispatcher runs the reload; here by hand
            Tick(shooter, ref channel, dt);
            var task = _world.GetComponent<WeaponTask>(turret);
            Assert.Equal(WeaponTaskMode.Engage, task.Mode);
            Assert.Equal(epoch, task.Epoch);
            Assert.Equal(4, task.RoundsGranted);   // two fired + two in the new magazine
        }

        /// <summary>⭐ WL-3 (W-1) — TrackTarget keeps the gun on the target and fires nothing; the target down ⇒ Success and Idle.</summary>
        [Fact]
        public void WL3_TrackTarget_FollowsWithoutFiring_AndEndsWhenTheTargetIsDown()
        {
            var (shooter, turret, channel, target) = TurretedTank(rounds: 0, cooldown: 0.1f);
            unsafe { Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), new TrackTargetParams { Target = target }); }
            var track = new TrackTargetExecutor();
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            track.OnEnter(shooter, ref channel, _world);
            int shots = 0;
            for (int f = 0; f < 60 * 4; f++)
            {
                track.Execute(shooter, ref channel, _world, 1f / 60f);
                muscle.Execute(_world, 1f / 60f);
                _world.Bus.SwapBuffers();
                shots += _world.Bus.Read<WeaponFireIntent>().Length;
            }
            Assert.Equal(0, shots);
            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(WeaponTaskState.OnTarget, _world.GetComponent<WeaponTaskStatus>(turret).State);
            Assert.Equal(90f * MathF.PI / 180f, _world.GetComponent<TurretPose>(turret).Azimuth, 3);

            _world.DestroyEntity(target);
            track.Execute(shooter, ref channel, _world, 1f / 60f);
            Assert.Equal(NodeStatus.Success, channel.Status);
            Assert.Equal(WeaponTaskMode.Idle, _world.GetComponent<WeaponTask>(turret).Mode);
        }
            // ── CE-3158 G3 — a BURST never flies through a friend; an indirect warhead does ─────────────────────────────────────

        /// <summary>A burst-firing unit at the origin with a <see cref="FireAtPointParams"/> action at <paramref name="point"/>, a friend
        /// (radius 3.5) at (8, 1) on the line; optionally the unit's type fires <paramref name="ammoName"/> from mount 0.</summary>
        private unsafe (Entity shooter, Entity friend) BurstWithAFriendOnTheLine(Vector3 point, string? ammoName = null)
        {
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            var shooter = _world.CreateEntity();
            _world.AddComponent(shooter, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(shooter, new WeaponState { Ammo = 5 });
            _world.AddComponent(shooter, new WeaponChannel());
            _world.AddComponent(shooter, new EntityInfo { ForceId = ForceId.Friend });
            if (ammoName != null)
            {
                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                db.Register(new Fdp.Interfaces.TkbTemplate(ammoName, 900));
                var type = new Fdp.Interfaces.TkbTemplate("burst unit", 901);
                type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = new System.Collections.Generic.List<Fdp.Toolkit.Tkb.Domain.WeaponMountDto>
                    { new() { AmmoGuid = 900 } } });
                db.Register(type);
                _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
                _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
                _world.AddComponent(shooter, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 901 });
            }
            var friend = _world.CreateEntity();
            _world.AddComponent(friend, new SimTransform { Position = new Vector3(8f, 1f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(friend, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(friend, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 3.5f });

            ref var channel = ref _world.GetComponentRW<WeaponChannel>(shooter);
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), new FireAtPointParams { Point = point, CooldownSeconds = 0.05f, Rounds = 3 });
            new FireAtPointExecutor().OnEnter(shooter, ref channel, _world);
            return (shooter, friend);
        }

        private int BurstStep(Entity shooter)
        {
            ref var channel = ref _world.GetComponentRW<WeaponChannel>(shooter);
            new FireAtPointExecutor().Execute(shooter, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();
            return _world.Bus.Read<WeaponFireIntent>().Length;
        }

        /// <summary>
        /// ⭐⭐ <c>G3_R1</c> — A DIRECT BURST HOLDS while a friend is on the line to its POINT (Running, no round, no ammo spent) and
        /// fires once the friend moves off. 🔴 Red-proof: before G3 the burst had no friendly check (BS-1 §5.1a guarded aimed fire
        /// only) and the first step fired through him.
        /// </summary>
        [Fact]
        public void G3_R1_ADirectBurst_HoldsWhileAFriendIsOnTheLineToItsPoint()
        {
            var (shooter, friend) = BurstWithAFriendOnTheLine(new Vector3(20f, 0f, 1f));
            Assert.Equal(0, BurstStep(shooter));
            Assert.Equal(NodeStatus.Running, _world.GetComponentRO<WeaponChannel>(shooter).Status);
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);

            _world.GetComponentRW<SimTransform>(friend).Position = new Vector3(8f, 5f, 0f);
            Assert.Equal(1, BurstStep(shooter));
            Assert.Equal(4, _world.GetComponent<WeaponState>(shooter).Ammo);
        }

        /// <summary>⭐⭐ <c>G3_R2</c> — an INDIRECT warhead (an 81 mm bomb, from the reference library) arcs over friends: the same
        /// burst over the same friend FIRES.</summary>
        [Fact]
        public void G3_R2_AnIndirectWarhead_FiresOverAFriend()
        {
            var (shooter, _) = BurstWithAFriendOnTheLine(new Vector3(20f, 0f, 0f), "81mm mortar HE");
            Assert.Equal(1, BurstStep(shooter));
        }

        /// <summary>⭐ <c>G3_R3</c> — a KINETIC round (no warhead) is direct fire: held, as without a TKB.</summary>
        [Fact]
        public void G3_R3_AKineticRound_IsDirectFire_AndHolds()
        {
            var (shooter, _) = BurstWithAFriendOnTheLine(new Vector3(20f, 0f, 0f), "5.56x45 ball");
            Assert.Equal(0, BurstStep(shooter));
        }
    
        // ── WL-5 W-12b/c (docs/DESIGN_Weapon_Layers.md §5g) — which categories use the turret task; the ATGM one round at a time ──

        /// <summary>⭐ W-12b (§5g G-1) — the table: small arms, grenades, mortars per shot; the ATGM guided; the rest (and an untagged
        /// mount) through the turret when one is named.</summary>
        [Theory]
        [InlineData(Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms, false, 0)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.WeaponCategory.Grenade, false, 0)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.WeaponCategory.Mortar, false, 0)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.WeaponCategory.TankGun, true, 0)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon, true, 0)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun, true, 0)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.WeaponCategory.Unknown, true, 0)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm, true, 1)]
        public void WL5_W12b_TheProfileTable(Fdp.Toolkit.Tkb.Domain.WeaponCategory category, bool turret, int inFlight)
        {
            var (shooter, _, _, _) = TurretedTank(rounds: 1, cooldown: 0.5f, category: category);
            var profile = WeaponBrainProfiles.For(category);
            Assert.Equal(turret, profile.UsesTurretTask(_world, shooter, CombatTkb.MountOf(_world, shooter, 0)));
            Assert.Equal(inFlight, profile.RoundsInFlight);
        }

        /// <summary>⭐ W-12b — a small-arms mount that names a turret is still fought per shot: the turret gets no Engage task.</summary>
        [Fact]
        public void WL5_W12b_ASmallArmsMountOnATurret_FiresPerShot()
        {
            var (shooter, turret, channel, _) = TurretedTank(rounds: 1, cooldown: 0.5f, category: Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms);
            _executor.OnEnter(shooter, ref channel, _world);
            for (int f = 0; f < 600 && channel.Status == NodeStatus.Running; f++) Step(shooter, ref channel, 1f / 60f);
            Assert.Equal(NodeStatus.Success, channel.Status);   // the aimed shot left through the per-shot path
            Assert.False(_world.HasComponent<WeaponTask>(turret) && _world.GetComponent<WeaponTask>(turret).Mode == WeaponTaskMode.Engage,
                "the turret is never tasked for a per-shot mount");
        }

        /// <summary>
        /// ⭐⭐ W-12c (§5g G-2) — an ATGM at 100 m flying 200 m/s: ONE round is granted, and the next only after its estimated flight
        /// (0.5 s + 0.5 s margin), so three rounds leave ≥ 1 s apart although the cooldown is 0.5 s. 🔴 Red before: the magazine was
        /// granted and the rounds left on the cooldown, 0.5 s apart — two missiles in the air to guide.
        /// </summary>
        [Fact]
        public void WL5_W12c_AnAtgm_FiresOneRoundPerFlight()
        {
            var (shooter, _, channel, target) = TurretedTank(rounds: 3, cooldown: 0.5f, category: Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm,
                                                             muzzleVelocity: 200f);
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            _executor.OnEnter(shooter, ref channel, _world);
            const float dt = 1f / 60f;
            int shots = 0, last = -1, minGap = int.MaxValue;
            for (int f = 0; f < 60 * 10 && channel.Status == NodeStatus.Running; f++)
            {
                Tick(shooter, ref channel, dt);
                muscle.Execute(_world, dt);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>())
                {
                    if (last >= 0) minGap = Math.Min(minGap, f - last);
                    last = f;
                    shots++;
                }
            }
            Assert.Equal(NodeStatus.Success, channel.Status);
            Assert.Equal(3, shots);
            float flight = AimAndFireExecutor.GuidedFlightSeconds(_world, shooter, target, CombatTkb.MountOf(_world, shooter, 0), 0f);
            Assert.Equal(1f, flight, 2);
            Assert.True(minGap >= 59, $"rounds {minGap} frames apart: one flight is 60 frames, the cooldown only 30");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2170</c> (Air Ops handoff E1; Missiles §4a) — a TOW at 2 km holds the launcher for its real flight: the estimate
        /// reads the MISSILE's own speed (280 m/s ⇒ 2000 / 280 + 0.5 ≈ 7.6 s), not the mount's generated muzzle speed (a BDC mount's
        /// range × 0.5 = 1875 m/s ⇒ 1.6 s). 🔴 Red before: 1.57 s — the `Guiding` hold ended while the round still needed the sight.
        /// The loaded round wins over the mount's own ammo (M8).
        /// </summary>
        [Fact]
        public void CE2170_ATowAtTwoKm_HoldsTheLauncherForItsFlight()
        {
            const long Tow = 6904, Heat = 6905;
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var tow = new Fdp.Interfaces.TkbTemplate("TOW", Tow);
            tow.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.MissileDto { SpeedMps = 280f, MaxFlightSeconds = 15f });
            db.Register(tow);
            db.Register(new Fdp.Interfaces.TkbTemplate("not a missile", Heat));
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var target = SpawnTarget(new Vector3(2000f, 0f, 0f));
            var (shooter, _) = SpawnShooter(Vector3.Zero, 5, 0f, target, 1f);
            var mount = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm, AmmoGuid = unchecked((ulong)Tow), MuzzleVelocity = 1875f };

            float hold = AimAndFireExecutor.GuidedFlightSeconds(_world, shooter, target, mount, 0f);
            Assert.True(hold >= 7f, $"the TOW holds the launcher {hold:F2} s for a 2 km flight at 280 m/s");
            Assert.Equal(2000f / 280f + AimAndFireExecutor.GuidedFlightMarginSeconds, hold, 2);
            Assert.Equal(hold, AimAndFireExecutor.GuidedFlightSeconds(_world, shooter, target, mount with { AmmoGuid = 0 }, 0f, loadedAmmo: Tow), 3);
            // no missile ⇒ the mount's speed, as before
            Assert.Equal(2000f / 1875f + AimAndFireExecutor.GuidedFlightMarginSeconds,
                AimAndFireExecutor.GuidedFlightSeconds(_world, shooter, target, mount, 0f, loadedAmmo: Heat), 3);
        }

        /// <summary>
        /// ⭐ Air Ops slice 4 (docs/designs/air-attack/DESIGN_Air_Operations.md §8, A6) — the RELEASE profile: an aircraft on a level pass
        /// with a bomb mount holds (<c>AwaitingRelease</c>, nothing spent) until <see cref="BombBallistics"/> says the impact reaches the
        /// target, then releases at once — no sight, no aim clock — and a one-round action is done. A target 15 m off the track is never
        /// released on.
        /// </summary>
        [Fact]
        public unsafe void A6_TheReleaseProfile_WaitsForTheCue()
        {
            const long Plane = 9405;
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("plane", Plane);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, AmmoGuid = 9022, InitialAmmunition = 4, DamagePerHit = 1200 } } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            const float Dt = 1f / 60f, H = 500f;
            var v = new Vector3(150f, 0f, 0f);
            var (impact, _) = BombBallistics.Impact(new Vector3(0f, 0f, H), BombBallistics.Launch(v), null, Dt);
            var target = SpawnTarget(new Vector3(3000f, 0f, 0f));
            float releaseX = 3000f - impact.X;

            (Entity Plane, WeaponChannel Channel) Pass(Entity at)
            {
                var (shooter, channel) = SpawnShooter(new Vector3(releaseX - 600f, 0f, H), 4, 0f, at, 0f);
                _world.AddComponent(shooter, new SimVelocity { Linear = v });
                _world.AddComponent(shooter, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Plane });
                var p = Unsafe.Read<AimAndFireParams>(Unsafe.AsPointer(ref channel.Params[0]));
                p.Rounds = 1;
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
                _executor.OnEnter(shooter, ref channel, _world);
                return (shooter, channel);
            }

            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.AwaitingRelease,
                AimAndFireExecutor.HoldOf(_world, Pass(target).Plane, target, CombatTkb.MountOf(_world, Pass(target).Plane, 0), 0));

            var (plane, channel) = Pass(target);
            float releasedAt = float.NaN; int released = 0;
            for (int frame = 0; frame < 60 * 8; frame++)
            {
                Tick(plane, ref channel, Dt);
                _world.Bus.SwapBuffers();
                if (_world.Bus.Read<WeaponFireIntent>().Length > 0 && released++ == 0) releasedAt = _world.GetComponent<SimTransform>(plane).Position.X;
                _world.GetComponentRW<SimTransform>(plane).Position += v * Dt;
            }
            Assert.Equal(1, released);
            Assert.InRange(releasedAt, releaseX - v.X * Dt, releaseX + v.X * Dt);
            Assert.Equal(NodeStatus.Success, channel.Status);
            Assert.Equal(3, _world.GetComponent<WeaponState>(plane).Ammo);

            // off the track: never released
            var aside = SpawnTarget(new Vector3(3000f, 15f, 0f));
            var (wide, wideChannel) = Pass(aside);
            for (int frame = 0; frame < 60 * 8; frame++)
            {
                Tick(wide, ref wideChannel, Dt);
                _world.Bus.SwapBuffers();
                Assert.Equal(0, _world.Bus.Read<WeaponFireIntent>().Length);
                _world.GetComponentRW<SimTransform>(wide).Position += v * Dt;
            }
        }

        /// <summary>
        /// ⭐ CE-2173 slice 5 (docs/designs/air-attack/DESIGN_Air_Operations.md §8, A6) — a BOMB fired at a POINT (a strike on a place,
        /// <see cref="FireAtPointExecutor"/>) waits for the same release cue as the aimed path: it leaves exactly where a bomb dropped
        /// then lands on the point, once, and nothing leaves on a pass wide of it. ✅ Red-proof: without the gate the first frame drops
        /// (600 m early) ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE2173_ABombAtAPoint_WaitsForTheReleaseCue()
        {
            const long Plane = 9406;
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("plane", Plane);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, AmmoGuid = 9022, InitialAmmunition = 4, DamagePerHit = 1200 } } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            const float Dt = 1f / 60f, H = 500f;
            var v = new Vector3(150f, 0f, 0f);
            var (impact, _) = BombBallistics.Impact(new Vector3(0f, 0f, H), BombBallistics.Launch(v), null, Dt);
            float releaseX = 3000f - impact.X;

            (Entity Plane, int Released, float At) Run(Vector3 point)
            {
                var (plane, channel) = SpawnShooter(new Vector3(releaseX - 600f, 0f, H), 4, 0f, Entity.Null, 0f);
                _world.AddComponent(plane, new SimVelocity { Linear = v });
                _world.AddComponent(plane, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Plane });
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), new FireAtPointParams { Point = point, Rounds = 1 });
                var executor = new FireAtPointExecutor();
                executor.OnEnter(plane, ref channel, _world);
                int released = 0; float at = float.NaN;
                for (int frame = 0; frame < 60 * 8; frame++)
                {
                    executor.Execute(plane, ref channel, _world, Dt);
                    _world.Bus.SwapBuffers();
                    if (_world.Bus.Read<WeaponFireIntent>().Length > 0 && released++ == 0) at = _world.GetComponent<SimTransform>(plane).Position.X;
                    _world.GetComponentRW<SimTransform>(plane).Position += v * Dt;
                }
                return (plane, released, at);
            }

            var hit = Run(new Vector3(3000f, 0f, 0f));
            Assert.Equal(1, hit.Released);
            Assert.InRange(hit.At, releaseX - v.X * Dt, releaseX + v.X * Dt);
            Assert.Equal(3, _world.GetComponent<WeaponState>(hit.Plane).Ammo);

            Assert.Equal(0, Run(new Vector3(3000f, 15f, 0f)).Released);                     // 15 m off the track: never released
        }

        /// <summary>
        /// ⭐ CE-2173 (Air Ops §9c — the live <c>ua-air-strike</c> miss) — the release cue on the BRAIN node, where the plane's motion is
        /// another node's: no <see cref="SimVelocity"/> is refreshed there (dead reckoning skips an entity that is neither owned nor a
        /// ghost — <c>SpatialCoreTkbTranslator</c>'s zero one stays zero) and <see cref="SimTransform"/> jumps only when a network sample arrives (here every 6th frame, with its
        /// <see cref="Fdp.Toolkit.Replication.Components.NetworkVelocity"/> and stamp). The cue reads the motion as this node knows it
        /// (<see cref="Fdp.Toolkit.Replication.ReplicatedMotion"/>) and releases once, where a bomb dropped lands on the point.
        /// ✅ Red-proof: reading <see cref="SimVelocity"/> (the old cue) ⇒ a standing plane ⇒ never released ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE2173_TheReleaseCue_OnTheBrainNode_ReadsTheReplicatedMotion()
        {
            const long Plane = 9408;
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkTransform>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkVelocity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("plane", Plane);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, AmmoGuid = 9022, InitialAmmunition = 4, DamagePerHit = 1200 } } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            const float Dt = 1f / 60f, H = 500f;
            const int SampleEvery = 6;
            var v = new Vector3(150f, 0f, 0f);
            var (impact, _) = BombBallistics.Impact(new Vector3(0f, 0f, H), BombBallistics.Launch(v), null, Dt);
            float releaseX = 3000f - impact.X;
            var start = new Vector3(releaseX - 600f, 0f, H);

            var (plane, channel) = SpawnShooter(start, 4, 0f, Entity.Null, 0f);
            _world.AddComponent(plane, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Plane });
            _world.AddComponent(plane, new SimVelocity());      // SpatialCoreTkbTranslator gives every entity a zero one, on every node
            _world.SetAuthority<SimTransform>(plane, false);   // SimHost owns the motion
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), new FireAtPointParams { Point = new Vector3(3000f, 0f, 0f), Rounds = 1 });
            var executor = new FireAtPointExecutor();
            executor.OnEnter(plane, ref channel, _world);

            int released = 0; float at = float.NaN;
            for (int frame = 0; frame < 60 * 8; frame++)
            {
                double now = frame * Dt;
                var truth = start + v * (float)now;
                _world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now, DeltaTime = Dt, TimeScale = 1f });
                if (frame % SampleEvery == 0)   // a GeoSpatial sample: the ingress writes the shadow, the velocity and the position
                {
                    _world.SetComponent(plane, new Fdp.Toolkit.Replication.Components.NetworkTransform { LastPosition = truth, LastRotation = Quaternion.Identity, SimStamp = now });
                    _world.SetComponent(plane, new Fdp.Toolkit.Replication.Components.NetworkVelocity { Value = v });
                    _world.GetComponentRW<SimTransform>(plane).Position = truth;
                }
                executor.Execute(plane, ref channel, _world, Dt);
                _world.Bus.SwapBuffers();
                if (_world.Bus.Read<WeaponFireIntent>().Length > 0 && released++ == 0) at = truth.X;
            }

            Assert.Equal(Vector3.Zero, _world.GetComponent<SimVelocity>(plane).Linear);   // never refreshed on this node
            Assert.Equal(1, released);
            Assert.InRange(at, releaseX - v.X * Dt, releaseX + v.X * Dt);
        }

        /// <summary>
        /// ⭐ Air Ops slice 6a F1 (docs/designs/air-attack/DESIGN_Air_Operations.md §7a, AW-1) — a FIXED-forward mount with a nose cone (the
        /// A-10's cannon, 5°) fires only when the target lies inside the cone round the body's 3-D forward: a level plane 300 m up holds a
        /// ground target 1 km ahead (16.7° below the nose) as <c>OutOfArc</c>, nothing spent; nosed down onto it, the round leaves. A target
        /// 3° off the nose fires; a point off the nose holds the AI's round at a point too — the operator's never (M-2). A turret mount and a
        /// mount with no cone are never held. ✅ Red-proof: without the hold the level pass fires at once ⇒ red.
        /// </summary>
        [Fact]
        public void F1_AFixedForwardMount_FiresOnlyInsideItsNoseCone()
        {
            const long Plane = 9407;
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("plane", Plane);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon, FixedConeDeg = 5f, InitialAmmunition = 100, DamagePerHit = 30 } } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var target = SpawnTarget(new Vector3(1000f, 0f, 0f));
            var (plane, channel) = SpawnShooter(new Vector3(0f, 0f, 300f), 100, 0f, target, 0.05f);
            _world.AddComponent(plane, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Plane });
            EnterAimed(plane, ref channel);
            var mount = CombatTkb.MountOf(_world, plane, 0);

            // level: the target is 16.7° under the nose — held, nothing spent
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.OutOfArc, AimAndFireExecutor.HoldOf(_world, plane, target, mount, 0));
            for (int i = 0; i < 10; i++) Assert.Equal(0, Step(plane, ref channel, 1f / 60f));
            Assert.Equal(100, _world.GetComponent<WeaponState>(plane).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);

            // nosed down onto it (a dive): it fires
            float dive = MathF.Atan2(300f, 1000f);
            _world.GetComponentRW<SimTransform>(plane).Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, dive);
            Assert.Null(AimAndFireExecutor.HoldOf(_world, plane, target, mount, 0));
            Assert.Equal(1, Step(plane, ref channel, 1f / 60f));

            // 3° off the nose: inside the 5° cone; 8° off: outside
            _world.GetComponentRW<SimTransform>(plane).Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, dive + 3f * MathF.PI / 180f);
            Assert.Null(AimAndFireExecutor.HoldOf(_world, plane, target, mount, 0));
            _world.GetComponentRW<SimTransform>(plane).Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, dive + 8f * MathF.PI / 180f);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.OutOfArc, AimAndFireExecutor.HoldOf(_world, plane, target, mount, 0));

            // at a POINT: the AI's round is held outside the cone, the operator's is not
            _world.GetComponentRW<SimTransform>(plane).Rotation = Quaternion.Identity;
            ref var weapon = ref _world.GetComponentRW<WeaponState>(plane);
            weapon.CooldownSecondsRemaining = 0f;
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.OutOfArc,
                FireAtPointExecutor.ShootAtPoint(_world, plane, 0, ref weapon, new Vector3(1000f, 0f, 0f), 0f, 1f / 60f, obeyRoe: true, obeyArc: true));
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Firing,
                FireAtPointExecutor.ShootAtPoint(_world, plane, 0, ref weapon, new Vector3(1000f, 0f, 0f), 0f, 1f / 60f, obeyRoe: false, obeyArc: false));

            // no cone, or a turret: never held
            Assert.False(BodyAim.OutsideFixedCone(_world, plane, mount! with { FixedConeDeg = 0f }, new Vector3(0f, 1000f, 0f)));
            Assert.False(BodyAim.OutsideFixedCone(_world, plane, mount with { TurretIndex = 0 }, new Vector3(0f, 1000f, 0f)));
            Assert.True(BodyAim.OutsideFixedCone(_world, plane, mount, new Vector3(0f, 1000f, 300f)));
        }

        // ── WL-4 W-12e (docs/DESIGN_Weapon_Layers.md §5f, L-2/L-3) — a hidden target gets a grenade at its last-known spot ────

        private const long M67 = 6001;

        /// <summary>A grenadier (mount 0 rifle, mount 1 an M67 thrown at 15 m/s, range 25 m — the shipped Grenadier's suite), a
        /// target his memory places at <paramref name="remembered"/>, not seen now, on Auto.</summary>
        private unsafe (Entity Unit, Entity Grenade, WeaponChannel Channel, Entity Target) Grenadier(
            Vector3 remembered, float freshness = 500f)
        {
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _world.RegisterComponent<WeaponMountInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
            _world.RegisterComponent<EntityInfo>();
            RegisterSight();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            db.Register(new Fdp.Interfaces.TkbTemplate("M67 grenade", M67) { DisType = new Fdp.Core.DISEntityType { Kind = 2 } });
            var type = new Fdp.Interfaces.TkbTemplate("Grenadier", 77);
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Range = 300f },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { MuzzleVelocity = 15f, Range = 25f, AmmoGuid = unchecked((ulong)M67) },
            } });
            db.Register(type);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var target = SpawnTarget(remembered);
            _world.AddComponent(target, new EntityInfo { ForceId = (Fdp.Core.ForceId)2 });
            var (unit, channel) = SpawnShooter(Vector3.Zero, 30, 0f, target, 0.5f);
            _world.AddComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 77 });
            _world.AddComponent(unit, new EntityInfo { ForceId = (Fdp.Core.ForceId)1 });
            var mem = new Fdp.Toolkit.Perception.Components.TargetMemory { Count = 1 };
            mem.EntityIds[0] = (long)target.PackedValue;
            mem.PositionsX[0] = remembered.X; mem.PositionsY[0] = remembered.Y; mem.PositionsZ[0] = remembered.Z;
            mem.Freshness[0] = freshness;
            _world.AddComponent(unit, mem);
            SetSight(unit, target, visual: false);

            var grenade = _world.CreateEntity();
            _world.AddComponent(grenade, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = unit, InstanceId = 1 });
            _world.AddComponent(grenade, new WeaponMountInfo { MountIndex = 1 });
            _world.AddComponent(grenade, new WeaponState { Ammo = 4, MuzzleVelocity = 15f });

            var p = new AimAndFireParams { Target = target, CooldownSeconds = 0.5f, Mount = AimAndFireParams.MountAuto };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            return (unit, grenade, channel, target);
        }

        /// <summary>
        /// ⭐ W-12e — not seen, remembered 18 m away (inside the 25 m range and the throw's reach): ONE grenade goes to the
        /// remembered point from mount 1, it counts as a round, the mount spends one and cools down 6 s (no second throw while it
        /// does). 🔴 Red before: the executor held (<c>HoldNotSeen</c>) and nothing ever threw at a hidden target.
        /// </summary>
        [Fact]
        public void WL4_W12e_AHiddenTargetInReach_GetsOneGrenadeAtItsLastKnownSpot()
        {
            var remembered = new Vector3(0f, 18f, 0f);
            var (unit, grenade, channel, target) = Grenadier(remembered);
            _executor.OnEnter(unit, ref channel, _world);
            Tick(unit, ref channel, 0.1f);
            _world.Bus.SwapBuffers();
            var intents = _world.Bus.Read<WeaponFireIntent>();
            Assert.Equal(1, intents.Length);
            Assert.True(intents[0].AtPoint);
            Assert.Equal(remembered, intents[0].TargetPoint);
            Assert.Equal(1, intents[0].WeaponIndex);
            Assert.Equal(3, _world.GetComponent<WeaponState>(grenade).Ammo);
            Assert.Equal(HiddenTargetThrow.CooldownSeconds, _world.GetComponent<WeaponState>(grenade).CooldownSecondsRemaining);
            Assert.Equal(30, _world.GetComponent<WeaponState>(unit).Ammo);   // the rifle untouched
            Assert.Equal(1, AimAndFireExecutor.RoundsFiredOf(ref channel));

            for (int i = 0; i < 10; i++) Assert.Equal(0, Step(unit, ref channel, 0.1f));   // cooling down: no second grenade
            Assert.Equal(NodeStatus.Running, channel.Status);
            _ = target;
        }

        /// <summary>⭐ W-12e — the cases that must NOT throw: beyond the throw (30 m), a forgotten sighting, a friendly beside the
        /// target (inside the M67's lethal radius), the thrower's own spot, and a target that is SEEN (the rifle's business).</summary>
        [Theory]
        [InlineData("too far")]
        [InlineData("stale")]
        [InlineData("friendly near the burst")]
        [InlineData("seen")]
        public void WL4_W12e_NoThrow_WhenAConditionFails(string why)
        {
            var at = why == "too far" ? new Vector3(0f, 30f, 0f) : new Vector3(0f, 18f, 0f);
            var (unit, _, channel, target) = Grenadier(at, freshness: why == "stale" ? 0f : 500f);
            if (why == "friendly near the burst")
            {
                var friend = SpawnTarget(at + new Vector3(0.5f, 0f, 0f));
                _world.AddComponent(friend, new EntityInfo { ForceId = (Fdp.Core.ForceId)1 });
            }
            if (why == "seen") SetSight(unit, target, visual: true);
            _executor.OnEnter(unit, ref channel, _world);
            Assert.Equal(0, Step(unit, ref channel, 0.1f));
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        // ── AQ89 A (R-269) — Suppress at a hidden target: blind rounds at the last-known spot, not an idle hold ────────────────

        private static unsafe void AskBlind(ref WeaponChannel channel)
        {
            var p = Unsafe.Read<AimAndFireParams>(Unsafe.AsPointer(ref channel.Params[0]));
            p.BlindFire = 1;
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
        }

        /// <summary>
        /// ⭐ AQ89 A — hidden, 30 m away (beyond a throw), BlindFire asked: the RIFLE fires at the remembered point raised to a crouched
        /// man's middle, cools down <see cref="AimAndFireExecutor.BlindCooldownFactor"/>× slower than aimed fire, and the round counts.
        /// 🔴 Red before: Suppress at a hidden target held (<c>HoldNotSeen</c>) every frame until the memory faded.
        /// </summary>
        [Fact]
        public void AQ89_A_AHiddenTargetBeyondAThrow_GetsBlindRoundsAtItsLastKnownSpot()
        {
            var remembered = new Vector3(0f, 30f, 0f);
            var (unit, grenade, channel, _) = Grenadier(remembered);
            AskBlind(ref channel);
            _executor.OnEnter(unit, ref channel, _world);
            Tick(unit, ref channel, 0.1f);
            _world.Bus.SwapBuffers();
            var intents = _world.Bus.Read<WeaponFireIntent>();
            Assert.Equal(1, intents.Length);
            Assert.True(intents[0].AtPoint);
            Assert.Equal(0, intents[0].WeaponIndex);
            Assert.Equal(remembered + new Vector3(0f, 0f, AimAndFireExecutor.BlindAimHeightMetres), intents[0].TargetPoint);
            Assert.Equal(29, _world.GetComponent<WeaponState>(unit).Ammo);
            Assert.Equal(4, _world.GetComponent<WeaponState>(grenade).Ammo);   // nothing thrown
            // set on the shot, then the frame's one drain (⭐ CE-2166 — the dispatcher's) takes the 0.1 s off
            Assert.Equal(0.5f * AimAndFireExecutor.BlindCooldownFactor - 0.1f, _world.GetComponent<WeaponState>(unit).CooldownSecondsRemaining, 3);
            Assert.Equal(1, AimAndFireExecutor.RoundsFiredOf(ref channel));
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        /// <summary>⭐ AQ89 A — no blind round without a LIVE memory of the target, nor when BlindFire is not asked (today's hold).</summary>
        [Theory]
        [InlineData(true, 0f)]
        [InlineData(false, 500f)]
        public void AQ89_A_NoBlindRound_WhenForgottenOrNotAsked(bool asked, float freshness)
        {
            var (unit, _, channel, _) = Grenadier(new Vector3(0f, 30f, 0f), freshness);
            if (asked) AskBlind(ref channel);
            _executor.OnEnter(unit, ref channel, _world);
            Assert.Equal(0, Step(unit, ref channel, 0.1f));
            Assert.Equal(30, _world.GetComponent<WeaponState>(unit).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        // ── CE-3172 S4d (docs/DESIGN_Entity_Control.md §8b M-8, C-2) — operator fire ─────────────────────────────────────────

        private static unsafe void AskOperator(ref WeaponChannel channel, Vector3 point, byte trigger, float cooldown = 0.2f)
            => Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]),
                   new AimAndFireParams { Operator = 1, Trigger = trigger, Point = point, CooldownSeconds = cooldown });

        /// <summary>⭐ <c>CE-3172</c> S4d — a per-shot mount fires at the operator's POINT while the trigger is held: no target, no
        /// sight, no aim time, and HoldFire does not hold it (C-2); the cooldown still spaces the rounds; released ⇒ no round.</summary>
        [Fact]
        public void CE3172_OperatorFire_PerShot_FiresAtThePoint_OverridingRoe_OnlyWhileTheTriggerIsHeld()
        {
            _world.RegisterComponent<Roe>();
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 10, 0f, Entity.Null, 0.2f);
            _world.AddComponent(shooter, new Roe { Fire = RoeFire.HoldFire });
            var point = new Vector3(40f, 5f, 1f);

            AskOperator(ref channel, point, trigger: 0);
            _executor.OnEnter(shooter, ref channel, _world);
            Tick(shooter, ref channel, 0.016f);
            _world.Bus.SwapBuffers();
            Assert.Empty(_world.Bus.Read<WeaponFireIntent>().ToArray());
            Assert.Equal(NodeStatus.Running, channel.Status);

            AskOperator(ref channel, point, trigger: 1);
            int shots = 0;
            for (int f = 0; f < 30; f++)   // 0.5 s at 60 Hz, a 0.2 s cooldown ⇒ three rounds
            {
                Tick(shooter, ref channel, 1f / 60f);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>())
                {
                    Assert.True(shot.AtPoint);
                    Assert.Equal(point, shot.TargetPoint);
                    Assert.True(shot.Target.IsNull);
                    shots++;
                }
            }
            Assert.Equal(3, shots);
            Assert.Equal(7, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);   // never ends on its own
        }

        /// <summary>⭐ <c>CE-3172</c> S4d — the AI's aimed fire is untouched: Operator = 0 still obeys HoldFire.</summary>
        [Fact]
        public void CE3172_AiFire_StillObeysHoldFire()
        {
            _world.RegisterComponent<Roe>();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);
            _world.AddComponent(shooter, new Roe { Fire = RoeFire.HoldFire });
            EnterAimed(shooter, ref channel);
            Tick(shooter, ref channel, 0.016f);
            _world.Bus.SwapBuffers();
            Assert.Empty(_world.Bus.Read<WeaponFireIntent>().ToArray());
        }

        /// <summary>⭐ <c>CE-3172</c> S4d — a turreted mount: released, the turret TRACKS the operator's point; held, the brain grants
        /// rounds and the muscle fires them AT the point; a moved point re-aims without a new epoch (rounds in flight keep counting).</summary>
        [Fact]
        public void CE3172_OperatorFire_Turret_TracksThePoint_AndFiresWhileHeld()
        {
            var (shooter, turret, channel, _) = TurretedTank(rounds: 0, cooldown: 0.2f, ammo: 10);
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            var point = new Vector3(60f, 0f, 1f);   // dead ahead: no slew

            AskOperator(ref channel, point, trigger: 0);
            _executor.OnEnter(shooter, ref channel, _world);
            Tick(shooter, ref channel, 1f / 60f);
            var task = _world.GetComponent<WeaponTask>(turret);
            Assert.Equal(WeaponTaskMode.Track, task.Mode);
            Assert.True(task.Target.IsNull);
            Assert.Equal(point, task.Point);
            uint epoch = task.Epoch;

            AskOperator(ref channel, point, trigger: 1);
            int shots = 0;
            for (int f = 0; f < 120; f++)
            {
                if (f == 60) AskOperator(ref channel, point + new Vector3(0f, 3f, 0f), trigger: 1);   // the operator sweeps
                Tick(shooter, ref channel, 1f / 60f);
                muscle.Execute(_world, 1f / 60f);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>()) { Assert.True(shot.AtPoint); shots++; }
            }
            Assert.True(shots >= 5, $"fired {shots} rounds in 2 s at a 0.2 s cooldown");
            task = _world.GetComponent<WeaponTask>(turret);
            Assert.Equal(epoch, task.Epoch);
            Assert.Equal(point + new Vector3(0f, 3f, 0f), task.Point);
            Tick(shooter, ref channel, 1f / 60f);   // account the last reported round
            Assert.Equal(10 - shots, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        // ── ⭐ CE-1078 / CE-1079 (docs/DESIGN_Entity_Control.md §8e) — a press locks ONE shot; the operator's round switch ───────────

        private static unsafe void AskOperatorShot(ref WeaponChannel channel, Entity target, Vector3 point, int cap, float cooldown = 0.2f)
            => Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]),
                   new AimAndFireParams { Operator = 1, Trigger = 1, Target = target, Point = point, Rounds = cap, CooldownSeconds = cooldown });

        /// <summary>
        /// ⭐⭐ CE-1078 (D-1) — a LOCKED shot on a unit 90° off the gun: the turret task names the UNIT (the muscle follows it), no
        /// round leaves while it slews, exactly ONE leaves once it bears (the cap), and the action keeps running for the next press.
        /// 🔒 User, 2026-10-14: "when i press fire trigger the tank tries aiming to the clicked target and fire once the barrel is aimed".
        /// </summary>
        [Fact]
        public void CE1078_ALockedShot_TurnsTheTurretToTheUnit_FiresOneRoundOnceItBears_AndTheActionRunsOn()
        {
            var (shooter, turret, channel, target) = TurretedTank(rounds: 0, cooldown: 0.2f, ammo: 10);
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            AskOperatorShot(ref channel, target, new Vector3(60f, 0f, 1f), cap: 1);
            _executor.OnEnter(shooter, ref channel, _world);
            int shots = 0, first = -1;
            for (int f = 0; f < 600; f++)
            {
                Tick(shooter, ref channel, 1f / 60f);
                muscle.Execute(_world, 1f / 60f);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>())
                {
                    Assert.Equal(target, shot.Target);
                    Assert.False(shot.AtPoint);
                    shots++;
                    if (first < 0) first = f;
                }
            }
            Assert.Equal(1, shots);
            Assert.True(first > 10, $"the round left at frame {first}, before a 90° traverse");
            Assert.Equal(target, _world.GetComponent<WeaponTask>(turret).Target);
            Assert.Equal(9, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(1, AimAndFireExecutor.RoundsFiredOf(ref channel));
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        /// <summary>⭐ CE-1078 — per shot too: in operator mode <c>Rounds</c> CAPS the rounds (a press = one) and never ends the action;
        /// a locked unit is aimed at (its middle), not the point the press was made at.</summary>
        [Fact]
        public void CE1078_PerShot_ALockedShot_IsOneRoundAtTheUnit_AndTheActionRunsOn()
        {
            var target = SpawnTarget(new Vector3(30f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 10, 0f, Entity.Null, 0.2f);
            AskOperatorShot(ref channel, target, new Vector3(5f, 40f, 0f), cap: 1);
            _executor.OnEnter(shooter, ref channel, _world);
            int shots = 0;
            for (int f = 0; f < 60; f++)
            {
                Tick(shooter, ref channel, 1f / 60f);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>())
                {
                    Assert.InRange(shot.TargetPoint.X, 29f, 31f);   // at the unit, not at (5, 40)
                    shots++;
                }
            }
            Assert.Equal(1, shots);
            Assert.Equal(9, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        /// <summary>⭐ CE-1079 (D-3) — the operator's round switch holds the trigger like a reload; once it is done the round that
        /// leaves is the LOADED one (the per-shot path now carries it too) and its type's count drops.</summary>
        [Fact]
        public void CE1079_ARoundSwitch_HoldsTheOperatorsTrigger_ThenTheLoadedRoundLeaves()
        {
            _world.RegisterComponent<AmmoLoadout>();
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 10, 0f, Entity.Null, 0.2f);
            var load = new AmmoLoadout { Count = 2, Ammo0 = 6010, Rounds0 = 5, Ammo1 = 6011, Rounds1 = 5, Loaded = 1, SwitchRemaining = 1f };
            _world.AddComponent(shooter, load);
            AskOperator(ref channel, new Vector3(40f, 0f, 0f), trigger: 1);
            _executor.OnEnter(shooter, ref channel, _world);
            for (int f = 0; f < 30; f++) { Tick(shooter, ref channel, 1f / 60f); _world.Bus.SwapBuffers(); Assert.Empty(_world.Bus.Read<WeaponFireIntent>().ToArray()); }

            _world.GetComponentRW<AmmoLoadout>(shooter).SwitchRemaining = 0f;
            Tick(shooter, ref channel, 1f / 60f);
            _world.Bus.SwapBuffers();
            var round = Assert.Single(_world.Bus.Read<WeaponFireIntent>().ToArray());
            Assert.Equal(6011L, round.AmmoGuid);
            Assert.Equal(4, _world.GetComponentRO<AmmoLoadout>(shooter).Rounds1);
        }

            // ── ⭐ CE-3195 (R-3003, docs/DESIGN_Weapon_Layers.md W-14a) — a person's round waits for his body ─────────────────────

        /// <summary>The shooter becomes a PERSON (pedestrian kinematics) with a weapon posture; optionally embarked.</summary>
        private void MakePerson(Entity shooter, bool embarked = false)
        {
            if (!_world.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>()) _world.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            if (!_world.IsComponentTypeRegistered<WeaponPosture>()) _world.RegisterComponent<WeaponPosture>();
            _world.AddComponent(shooter, global::CarKinem.Core.VehiclePresets.GetPreset(global::CarKinem.Core.VehicleClass.Pedestrian));
            _world.AddComponent(shooter, new WeaponPosture());
            if (embarked)
            {
                _world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
                _world.AddComponent(shooter, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag());
            }
        }

        private void Face(Entity unit, float yawDegrees)
            => _world.GetComponentRW<SimTransform>(unit).Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yawDegrees * MathF.PI / 180f);

        /// <summary>
        /// ⭐⭐ <c>CE-3195</c> — a soldier facing EAST with his target WEST of him holds the round (Running, nothing spent) and stays
        /// AIMED at it (so the gait turns him — <c>CarKinematicsSystem</c> rails); turned so it is 60° off (inside the 75° twist) he fires
        /// at once — a snap shot, the aim clock did not restart. 🔴 Red-proof: before CE-3195 the first step fired out of his back.
        /// </summary>
        [Fact]
        public void CE3195_APerson_HoldsTheRoundAtATargetBehindHim_ThenSnapsItOffOnceInsideHisTwist()
        {
            var target = SpawnTarget(new Vector3(-10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, cooldownSeconds: 0.2f);
            MakePerson(shooter);
            EnterAimed(shooter, ref channel);

            Assert.Equal(0, Step(shooter, ref channel, 0.1f));
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);
            var posture = _world.GetComponent<WeaponPosture>(shooter);
            Assert.Equal(WeaponPostureKind.Aimed, posture.Kind);
            Assert.Equal(target, posture.Target);

            Face(shooter, 120f);   // the target (180°) is now 60° off his body
            Assert.Equal(1, Step(shooter, ref channel, 0.1f));
            Assert.Equal(4, _world.GetComponent<WeaponState>(shooter).Ammo);
        }

        /// <summary>⭐ <c>CE-3195</c> — the arc is the PERSON's: a vehicle's fixed gun and an embarked passenger (he sits at the vehicle's
        /// heading; the gait does not turn him) fire behind as before; and a person exactly at the twist (75°) still fires.</summary>
        [Theory]
        [InlineData("vehicle", 180f)]
        [InlineData("embarked", 180f)]
        [InlineData("person", 74f)]
        public void CE3195_TheArcHoldsOnlyAPersonsRoundBeyondHisTwist(string who, float targetBearingDegrees)
        {
            float r = targetBearingDegrees * MathF.PI / 180f;
            var target = SpawnTarget(new Vector3(10f * MathF.Cos(r), 10f * MathF.Sin(r), 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, cooldownSeconds: 0.2f);
            if (who != "vehicle") MakePerson(shooter, embarked: who == "embarked");
            EnterAimed(shooter, ref channel);
            Assert.Equal(1, Step(shooter, ref channel, 0.1f));
        }

        /// <summary>⭐ <c>CE-3195</c> — a person's BURST at a point behind him (a grenade, a blind burst) holds the same way and keeps the
        /// posture aimed at the POINT; facing it, the round goes.</summary>
        [Fact]
        public void CE3195_APersonsBurstAtAPointBehindHim_Holds_UntilHeFacesIt()
        {
            var (shooter, friend) = BurstWithAFriendOnTheLine(new Vector3(-20f, 0f, 1f));
            _world.GetComponentRW<SimTransform>(friend).Position = new Vector3(8f, 50f, 0f);   // nobody on the line
            MakePerson(shooter);
            Assert.Equal(0, BurstStep(shooter));
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);
            var posture = _world.GetComponent<WeaponPosture>(shooter);
            Assert.Equal(WeaponPostureKind.Aimed, posture.Kind);
            Assert.Equal(new Vector3(-20f, 0f, 1f), posture.Point);

            Face(shooter, 180f);
            Assert.Equal(1, BurstStep(shooter));
        }

        // ── ⭐⭐ Weapon systems WS-3 (W-D, W-E, W-H, W-I; docs/designs/weapons/DESIGN_Weapon_Systems.md §4a, §4c, §6) ─────────────────

        private const long WsM2Type = 9950, WsPickupType = 9951, WsSentryType = 9952;

        private void RegisterWeaponSystems()
        {
            void Reg<T>() where T : unmanaged { if (!_world.IsComponentTypeRegistered<T>()) _world.RegisterComponent<T>(); }
            Reg<Fdp.Toolkit.Replication.Components.PartMetadata>();
            Reg<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            Reg<TurretPose>();
            Reg<WeaponTask>();
            Reg<WeaponTaskStatus>();
            Reg<WeaponMuscleState>();
            Reg<PassengerBuffer>();
            Reg<IsEmbarkedTag>();
            Reg<CarrierSeats>();
            Reg<CarrierStations>();
            Reg<OpenPassengerCabin>();
            Reg<PersonCarrier>();
            Reg<ActorCapabilityState>();
            Reg<Health>();
            Reg<WeaponSystemParams>();
            Reg<WeaponSystemState>();
        }

        /// <summary>The TKB of the WS-3 rails: an M2 (its own pintle turret and mount 0), a bare pickup (driver, gunner, a bed station the
        /// gunner operates, NeedsGunner), and a sentry gun (automatic, or manual when <paramref name="sentryAutomatic"/> is false).</summary>
        private void WeaponSystemTypes(bool sentryAutomatic = true)
        {
            RegisterWeaponSystems();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            foreach (var (name, id, ws) in new[]
            {
                ("M2", WsM2Type, new Fdp.Toolkit.Tkb.Domain.WeaponSystemDto
                    { MountKinds = Fdp.Toolkit.Tkb.Domain.MountKinds.Ground | Fdp.Toolkit.Tkb.Domain.MountKinds.Pintle }),
                ("Sentry", WsSentryType, new Fdp.Toolkit.Tkb.Domain.WeaponSystemDto { NeedsOperator = !sentryAutomatic }),
            })
            {
                var t = new Fdp.Interfaces.TkbTemplate(name, id);
                t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TurretSuiteDto { Turrets = { new Fdp.Toolkit.Tkb.Domain.TurretDto
                    { PivotZ = 0.3f, TraverseMinDeg = -180f, TraverseMaxDeg = 180f, ElevationMinDeg = -10f, ElevationMaxDeg = 60f,
                      TraverseRateDegPerSec = 180f, ElevationRateDegPerSec = 90f } } });
                t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
                    { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun, TurretIndex = 0 } } });
                t.AddDescriptor(ws);
                db.Register(t);
            }
            var pickup = new Fdp.Interfaces.TkbTemplate("Pickup", WsPickupType);
            pickup.AddDescriptor(PickupSeats());
            db.Register(pickup);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
        }

        private static Fdp.Toolkit.Tkb.Domain.CarrierDefDto PickupSeats() => new()
        {
            Exposure = PassengerExposure.Open,
            Seats =
            {
                new Fdp.Toolkit.Tkb.Domain.SeatDto { Role = SeatRole.Driver },
                new Fdp.Toolkit.Tkb.Domain.SeatDto { Role = SeatRole.Gunner },
                new Fdp.Toolkit.Tkb.Domain.SeatDto
                {
                    Role = SeatRole.Station, Accepts = Fdp.Toolkit.Tkb.Domain.MountKinds.Pintle, OperatedBy = 1, NeedsGunner = true,
                    X = -1.5f, Z = 1.4f,
                },
            },
        };

        /// <summary>A weapon-system entity of <paramref name="type"/> at <paramref name="at"/>: its own WeaponState and turret part.</summary>
        private Entity WeaponSystemOf(long type, Vector3 at)
        {
            var w = _world.CreateEntity();
            _world.AddComponent(w, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(w, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = type });
            _world.AddComponent(w, new WeaponState { Ammo = 50, MuzzleVelocity = 800f });
            var t = _world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>()!.GetByType(type);
            new WeaponSystemTkbTranslator().Inject(_world, w, t);
            TurretParts.Ensure(_world, w, 0);
            return w;
        }

        private Entity WsSoldier(Vector3 at)
        {
            var s = _world.CreateEntity();
            _world.AddComponent(s, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(s, new Health { Current = 100f, Max = 100f });
            _world.AddComponent(s, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            return s;
        }

        /// <summary>A unit (no weapon of its own) with an AimAndFire channel at <paramref name="target"/>, mount Auto.</summary>
        private unsafe (Entity Unit, WeaponChannel Channel) WsUnit(Entity unit, Entity target, int rounds)
        {
            var channel = new WeaponChannel { Status = NodeStatus.Running };
            var p = new AimAndFireParams { Target = target, CooldownSeconds = 0.1f, Rounds = rounds, Mount = AimAndFireParams.MountAuto };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            _world.AddComponent(unit, channel);
            channel = _world.GetComponent<WeaponChannel>(unit);
            return (unit, channel);
        }

        /// <summary>The technical: a bare pickup at the origin facing east, an M2 in its bed station, a gunner (when <paramref name="gunner"/>)
        /// in its Gunner seat, a target 100 m to its left; the pickup's channel aims at it.</summary>
        private (Entity Pickup, Entity M2, Entity Gunner, WeaponChannel Channel, Entity Target) Technical(int rounds, bool gunner = true)
        {
            WeaponSystemTypes();
            var pickup = _world.CreateEntity();
            _world.AddComponent(pickup, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(pickup, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = WsPickupType });
            var pt = new Fdp.Interfaces.TkbTemplate("Pickup", WsPickupType);
            pt.AddDescriptor(PickupSeats());
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(_world, pickup, pt);
            var m2 = WeaponSystemOf(WsM2Type, new Vector3(-1.5f, 0f, 1.4f));
            Assert.Equal(Fdp.Toolkit.Behavior.EmbarkResult.Applied, Fdp.Toolkit.Behavior.Embarkation.TryEmbark(_world, m2, pickup));
            var man = Entity.Null;
            if (gunner)
            {
                man = WsSoldier(Vector3.Zero);
                Assert.Equal(Fdp.Toolkit.Behavior.EmbarkResult.Applied,
                    Fdp.Toolkit.Behavior.Embarkation.TryEmbark(_world, man, pickup, SeatRole.Gunner));
            }
            var target = SpawnTarget(new Vector3(0f, 100f, 1f));
            var (_, channel) = WsUnit(pickup, target, rounds);
            return (pickup, m2, man, channel, target);
        }

        /// <summary>Runs <paramref name="seconds"/> of brain + muscle for <paramref name="unit"/>; the rounds that left, by shooter.</summary>
        private int WsRun(Entity unit, ref WeaponChannel channel, float seconds, Entity expectShooter, Action<int>? atFrame = null)
        {
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            const float dt = 1f / 60f;
            int shots = 0;
            for (int f = 0; f < seconds * 60 && channel.Status == NodeStatus.Running; f++)
            {
                atFrame?.Invoke(f);
                _executor.Execute(unit, ref channel, _world, dt);
                muscle.Execute(_world, dt);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>())
                {
                    Assert.Equal(expectShooter, shot.Shooter);
                    Assert.Equal(0, shot.WeaponIndex);   // the weapon's own mount 0
                    shots++;
                }
            }
            return shots;
        }

        /// <summary>
        /// ⭐⭐ WS-3 (W-D, W-H; §4a) — the pickup's weapon brain fires its STATION weapon: the bare pickup has no weapon of its own, its
        /// mount list holds the M2 its station carries (the one controller is the host), and the rounds leave the M2 — its own mount 0,
        /// its own turret part (granted and slewed there, not on the pickup), its own belt. ✅ Red-proof: <see cref="WeaponSystems.Controlled"/>
        /// returning nothing ⇒ the pickup has nothing to fire (Failure, 0 rounds) ⇒ red.
        /// </summary>
        [Fact]
        public void WS_ThePickupFiresItsStationHmg_OnTheHmgsTurret()
        {
            var (pickup, m2, _, channel, _) = Technical(rounds: 3);
            Assert.False(_world.HasComponent<WeaponState>(pickup));
            Span<Entity> mounts = stackalloc Entity[8];
            Assert.Equal(1, WeaponMountQuery.EnumerateFiringMounts(_world, pickup, mounts));
            Assert.Equal(m2, mounts[0]);
            _executor.OnEnter(pickup, ref channel, _world);

            int shots = WsRun(pickup, ref channel, 4f, expectShooter: m2);

            Assert.Equal(NodeStatus.Success, channel.Status);
            Assert.Equal(3, shots);
            Assert.Equal(47, _world.GetComponent<WeaponState>(m2).Ammo);   // the M2's belt
            var part = TurretParts.Find(_world, m2, 0);
            Assert.NotEqual(0f, _world.GetComponent<TurretPose>(part).Azimuth);   // the M2's turret slewed to the target on its left
            Assert.Equal(WeaponTaskMode.Idle, _world.GetComponent<WeaponTask>(part).Mode);   // released when done
        }

        /// <summary>
        /// ⭐⭐ WS-3 (W-D) — <c>TrackTarget</c> on a BARE host (mount 0, the primary) slews its station weapon's turret, as AimAndFire fires
        /// it. 📌 Found by the split-cluster rail: the technical's tree tracks before it fires, and the tracker looked for a mount on the
        /// pickup itself — it failed every frame, so the station M2 never fired. ✅ Red-proof: the tracker without the platform routing ⇒
        /// Failure, the M2's turret never ordered ⇒ red.
        /// </summary>
        [Fact]
        public void WS_TrackTarget_OnABareHost_SlewsItsStationWeaponsTurret()
        {
            var (pickup, m2, _, channel, target) = Technical(rounds: 3);
            var tracker = new TrackTargetExecutor();
            unsafe
            {
                *(TrackTargetParams*)channel.Params = new TrackTargetParams { Target = target, Mount = 0 };
            }
            tracker.OnEnter(pickup, ref channel, _world);
            tracker.Execute(pickup, ref channel, _world, 0.1f);

            Assert.Equal(NodeStatus.Running, channel.Status);
            var part = TurretParts.Find(_world, m2, 0);
            Assert.True(_world.HasComponent<WeaponTask>(part), "the M2's turret is ordered");
            Assert.Equal(WeaponTaskMode.Track, _world.GetComponent<WeaponTask>(part).Mode);
            Assert.Equal(target, _world.GetComponent<WeaponTask>(part).Target);
            Assert.Equal(0, _world.GetComponent<WeaponTask>(part).MountIndex);   // the weapon's own mount 0
        }

        /// <summary>
        /// ⭐⭐ WS-4 — a BARE host is armed with its station weapon for every reader that asks "is this unit armed, with what?"
        /// (<see cref="WeaponSystems.WeaponOf"/>): the utility inputs read the M2's belt and readiness, and its own strength is not
        /// "unarmed". 📌 Found by the split-cluster rail: the engage decision scored 0 (WeaponHasAmmo read the pickup, which has no
        /// WeaponState), so the technical never fought. ✅ Red-proof: the inputs reading ctx.Self only ⇒ 0 ⇒ red.
        /// </summary>
        [Fact]
        public void WS_ABareHost_IsArmedWithItsStationWeapon_ForTheUtilityInputs_AndItsStrength()
        {
            var (pickup, m2, _, _, target) = Technical(rounds: 3);
            Assert.Equal(m2, WeaponSystems.WeaponOf(_world, pickup));
            var ctx = new Fdp.Toolkit.Utility.UtilityInputCtx { Repo = _world, Self = pickup, Context = target };
            Assert.Equal(1f, Fdp.Toolkit.Utility.StandardInputs.WeaponHasAmmo(in ctx));
            var belt = _world.GetComponent<WeaponState>(m2);   // the M2's own belt, read through the pickup
            Assert.Equal(belt.MaxAmmo == 0 ? 0f : System.Math.Clamp((float)belt.Ammo / belt.MaxAmmo, 0f, 1f),
                         Fdp.Toolkit.Utility.StandardInputs.AmmoFraction(in ctx), 3);
            Assert.True(Fdp.Toolkit.Utility.StandardInputs.RoundsLeft(in ctx) > 0f);
            Assert.Equal(1f, Fdp.Toolkit.Perception.ThreatDanger.OwnStrength(_world, pickup), 3);
            Assert.True(Fdp.Toolkit.Perception.ThreatDanger.Of(_world, target, pickup) > Fdp.Toolkit.Perception.ThreatDanger.Unarmed,
                "to its enemy the bare pickup is armed (its M2), not an unarmed contact");
        }

        /// <summary>⭐ WS-3 (§6 CanFire, W-I) — a NeedsGunner station whose gunner seat is EMPTY: the pickup's brain holds; no round, the
        /// M2's belt untouched, its turret idle. ✅ Red-proof: CanFire without the NeedsGunner check ⇒ the empty pickup fires ⇒ red.</summary>
        [Fact]
        public void WS_AStationWeapon_WithAnEmptyGunnerSeat_DoesNotFire()
        {
            var (pickup, m2, _, channel, _) = Technical(rounds: 3, gunner: false);
            _executor.OnEnter(pickup, ref channel, _world);
            Assert.Equal(0, WsRun(pickup, ref channel, 3f, expectShooter: m2));
            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(50, _world.GetComponent<WeaponState>(m2).Ammo);
            var part = TurretParts.Find(_world, m2, 0);
            Assert.False(_world.HasComponent<WeaponTask>(part) && _world.GetComponent<WeaponTask>(part).Mode != WeaponTaskMode.Idle);   // never ordered
        }

        /// <summary>
        /// ⭐⭐ WS-3 (W-I, STEER_…_Crew_Presets) — the gunner is an ordinary man: killing him SILENCES the gun (the turret is released at
        /// once, so no granted round leaves after his death), and it fires again once another soldier boards the Gunner seat.
        /// ✅ Red-proof: the executor without the weapon-system hold ⇒ rounds keep leaving after the death ⇒ red.
        /// </summary>
        [Fact]
        public void WS_AStationWeapon_FallsSilent_WhenItsGunnerDies()
        {
            var (pickup, m2, gunner, channel, _) = Technical(rounds: 0);
            _executor.OnEnter(pickup, ref channel, _world);
            Assert.True(WsRun(pickup, ref channel, 3f, expectShooter: m2) > 0, "the manned M2 fires");

            _world.GetComponentRW<Health>(gunner).Current = 0f;
            Assert.Equal(0, WsRun(pickup, ref channel, 3f, expectShooter: m2));
            Assert.Equal(NodeStatus.Running, channel.Status);   // the action holds, it does not end
            Assert.False(WeaponSystems.CanFire(_world, m2, out var why));
            Assert.Equal(WeaponHold.NoGunner, why);

            Assert.True(Fdp.Toolkit.Behavior.Embarkation.Disembark(_world, gunner, pickup));   // the body is taken out
            var second = WsSoldier(Vector3.Zero);
            Assert.Equal(Fdp.Toolkit.Behavior.EmbarkResult.Applied,
                Fdp.Toolkit.Behavior.Embarkation.TryEmbark(_world, second, pickup, SeatRole.Gunner));
            Assert.True(WsRun(pickup, ref channel, 3f, expectShooter: m2) > 0, "a new gunner in the seat: it fires again");
        }

        /// <summary>
        /// ⭐⭐ WS-3 (W-E; §4c) — an AUTOMATIC weapon system nobody operates is its own controller: its own brain fires its mount 0. A
        /// MANUAL one nobody mans has no controller: its brain holds (W-D — a brain on the weapon never fights its operator's).
        /// ✅ Red-proof: ControllerOf returning the weapon for a manual one too ⇒ the manual sentry fires ⇒ red.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void WS_AnAutomaticTurret_FiresOnItsOwn_AManualOneDoesNot(bool automatic)
        {
            WeaponSystemTypes(sentryAutomatic: automatic);
            var sentry = WeaponSystemOf(WsSentryType, Vector3.Zero);
            var target = SpawnTarget(new Vector3(60f, 20f, 1f));
            var (_, channel) = WsUnit(sentry, target, rounds: 3);
            _executor.OnEnter(sentry, ref channel, _world);

            int shots = WsRun(sentry, ref channel, 3f, expectShooter: sentry);

            Assert.Equal(automatic ? sentry : Entity.Null, WeaponSystems.ControllerOf(_world, sentry));
            Assert.Equal(automatic ? 3 : 0, shots);
        }

        /// <summary>
        /// ⭐ WS-3 (§6) — the M2 fires the round, but the PICKUP shakes it: its spread doubles while the pickup drives, and its sight line
        /// starts at the pickup's eye (HitModel.BodyOf) — so a technical's numbers do not move when its gun became an entity.
        /// ✅ Red-proof: Sigma reading the shooter itself ⇒ the M2 (no velocity of its own) never counts as moving ⇒ red.
        /// </summary>
        [Fact]
        public void WS_AStationWeapon_ShakesWithItsHost()
        {
            var (pickup, m2, _, _, _) = Technical(rounds: 1);
            if (!_world.IsComponentTypeRegistered<SimVelocity>()) _world.RegisterComponent<SimVelocity>();
            if (!_world.IsComponentTypeRegistered<UnderFire>()) _world.RegisterComponent<UnderFire>();
            var mount = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { DispersionMils = 4f };
            Assert.Equal(pickup, HitModel.BodyOf(_world, m2));
            Assert.Equal(pickup, HitModel.BodyOf(_world, pickup));   // not a weapon system: itself
            float still = HitModel.Sigma(_world, m2, mount, 0d);
            _world.AddComponent(pickup, new SimVelocity { Linear = new Vector3(10f, 0f, 0f) });
            Assert.Equal(still * HitModel.MovingFactor, HitModel.Sigma(_world, m2, mount, 0d), 5);
        }

        // ── ⭐⭐ Indirect fire IF-1 (I6; docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.2, FM-4, IF-C) ─────────────────────────

        private const long IfMortarType = 9953, IfBombType = 9954;

        /// <summary>A ground mortar (a weapon system, HighOnly 45–85°, one Gunner seat) at the origin, its bomb with charges 60 / 80 / 100
        /// m/s, and a soldier manning it; the soldier's channel holds <paramref name="p"/>. The tube is the weapon; he is its controller.</summary>
        private unsafe (Entity Man, Entity Tube, WeaponChannel Channel) MannedMortar(FireAtPointParams p)
        {
            RegisterWeaponSystems();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var tubeType = new Fdp.Interfaces.TkbTemplate("Mortar", IfMortarType);
            tubeType.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Mortar, AmmoGuid = (ulong)IfBombType, MuzzleVelocity = 100f,
                  Arc = Fdp.Toolkit.Tkb.Domain.IndirectArc.HighOnly, MinElevationDeg = 45f, MaxElevationDeg = 85f } } });
            tubeType.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSystemDto
                { NeedsOperator = true, MountKinds = Fdp.Toolkit.Tkb.Domain.MountKinds.Ground });
            tubeType.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CarrierDefDto
                { Exposure = PassengerExposure.Open, Seats = { new Fdp.Toolkit.Tkb.Domain.SeatDto { Role = SeatRole.Gunner } } });
            db.Register(tubeType);
            var bomb = new Fdp.Interfaces.TkbTemplate("if bomb", IfBombType);
            bomb.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto { Kind = Fdp.Toolkit.Tkb.Domain.WarheadKind.HighExplosive, Indirect = true });
            bomb.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.AmmoWeaponBallisticsDto { ChargeSpeedsMps = new() { 60f, 80f, 100f } });
            db.Register(bomb);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var tube = _world.CreateEntity();
            _world.AddComponent(tube, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(tube, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = IfMortarType });
            _world.AddComponent(tube, new WeaponState { Ammo = 10, MuzzleVelocity = 100f });
            new WeaponSystemTkbTranslator().Inject(_world, tube, tubeType);
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(_world, tube, tubeType);
            var man = WsSoldier(new Vector3(-1f, 0f, 0f));
            Assert.Equal(Fdp.Toolkit.Behavior.EmbarkResult.Applied, Fdp.Toolkit.Behavior.Embarkation.TryEmbark(_world, man, tube, SeatRole.Gunner));
            Assert.Equal(man, WeaponSystems.ControllerOf(_world, tube));   // premise: he mans it

            var channel = new WeaponChannel { Status = NodeStatus.Running };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            _world.AddComponent(man, channel);
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>())
                _world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>();
            _world.AddComponent(man, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            channel = _world.GetComponent<WeaponChannel>(man);
            return (man, tube, channel);
        }

        private WeaponFireIntent[] FireAtPointStep(Entity man, ref WeaponChannel channel, double now)
        {
            _world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now, DeltaTime = 1f / 60f, TimeScale = 1f });
            new FireAtPointExecutor().Execute(man, ref channel, _world, 1f / 60f);
            _world.Bus.SwapBuffers();
            return _world.Bus.Read<WeaponFireIntent>().ToArray();
        }

        /// <summary>
        /// ⭐⭐ IF-1 (I6, §3.2) — a gunner fires the GROUND MORTAR HE MANS through <c>FireAtPoint</c> (mount 0: he has no weapon of his own,
        /// so his primary is the tube): the round leaves the TUBE (its mount 0, its ammunition), carrying the brain's fire solution — the
        /// lowest charge that reaches (80 m/s for 500 m), on the HIGH arc a HighOnly mortar must use. ⭐ FM-4: laid on the point, nothing
        /// leaves before <c>FireAt</c> (Running, <c>AwaitingFireTime</c>). ✅ Red-proof: the executor's old MountEntity path (the owner) ⇒
        /// the man has no WeaponState ⇒ Failure, no round ⇒ red; the FireAt hold removed ⇒ a round at t = 1 s ⇒ red.
        /// </summary>
        [Fact]
        public void IF1_AGunnerFiresTheMortarHeMans_AsTheTube_WithTheSolution_AndNotBeforeFireAt()
        {
            var (man, tube, channel) = MannedMortar(new FireAtPointParams { Point = new Vector3(500f, 0f, 0f), Rounds = 1, CooldownSeconds = 3f, FireAt = 5d });
            new FireAtPointExecutor().OnEnter(man, ref channel, _world);

            Assert.Empty(FireAtPointStep(man, ref channel, now: 1d));
            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(10, _world.GetComponent<WeaponState>(tube).Ammo);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.AwaitingFireTime, LastWeaponReason(man));

            var shots = FireAtPointStep(man, ref channel, now: 5d);
            var shot = Assert.Single(shots);
            Assert.Equal(tube, shot.Shooter);
            Assert.Equal(0, shot.WeaponIndex);
            Assert.True(shot.AtPoint);
            Assert.Equal((byte)2, shot.Charge);   // 60 m/s reaches ≈ 367 m; 80 m/s ≈ 652 m — the lowest that reaches 500 m
            Assert.Equal((byte)1, shot.HighArc);
            Assert.Equal(9, _world.GetComponent<WeaponState>(tube).Ammo);
            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        /// <summary>⭐ IF-1 (I6, IF-C) — no solution, no round: a point beyond the top charge fails <c>OutOfRange</c>; one inside the
        /// mortar's 85° limit fails <c>TooClose</c>. Nothing spent. ✅ Red-proof: the solution gate removed ⇒ the round leaves on the
        /// legacy one-speed arc ⇒ red.</summary>
        [Theory]
        [InlineData(1500f, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.OutOfRange)]
        [InlineData(10f, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.TooClose)]
        public void IF1_NoFireSolution_Fails_WithWhy_AndSpendsNothing(float metres, Fdp.Toolkit.Behavior.Diagnostics.ActionReason why)
        {
            var (man, tube, channel) = MannedMortar(new FireAtPointParams { Point = new Vector3(metres, 0f, 0f), Rounds = 1 });
            new FireAtPointExecutor().OnEnter(man, ref channel, _world);
            Assert.Empty(FireAtPointStep(man, ref channel, now: 1d));
            Assert.Equal(NodeStatus.Failure, channel.Status);
            Assert.Equal(why, LastWeaponReason(man));
            Assert.Equal(10, _world.GetComponent<WeaponState>(tube).Ammo);
        }

        /// <summary>⭐ IF-1 (I6) — <see cref="FireAtPointParams"/> still fits the 32-byte <see cref="WeaponChannel.Params"/> with the
        /// arc preference and the fire time added.</summary>
        [Fact]
        public unsafe void IF1_FireAtPointParams_StillFitTheChannel()
            => Assert.True(sizeof(FireAtPointParams) <= 32, $"{sizeof(FireAtPointParams)} bytes");

        private Fdp.Toolkit.Behavior.Diagnostics.ActionReason LastWeaponReason(Entity unit)
            => _world.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(unit).Weapon.Reason;
    }
}
