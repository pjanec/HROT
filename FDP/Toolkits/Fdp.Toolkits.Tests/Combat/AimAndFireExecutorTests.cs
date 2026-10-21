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
            Ask(shooter, ref channel, dt);
            TestMuscle.FireAll(_world);   // ⭐ Q94 A-3 — the muscle fires what was asked (the ask/answer rails below call Ask alone)
        }

        /// <summary>⭐ <c>Q94 A-3</c> — one frame with NO muscle: the executor asks, nothing answers (the rounds stay reserved).</summary>
        private void Ask(Entity shooter, ref WeaponChannel channel, float dt)
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
            // ⭐ CE-2323 — each gap is the 10 s scaled by his own draw for that round (8–12.5 s), never the 5 s of the double drain
            for (int k = 1; k < shots.Count; k++)
            {
                float gap = FirePacing.Gap(10f, shooter, k - 1, 1);
                Assert.InRange(shots[k] - shots[k - 1], gap - 0.05f, gap + 0.25f);
            }
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

        /// <summary>
        /// ⭐ <c>CE-2338</c> — a friend LYING DOWN on the line (dead or incapacitated) does not hold the shot: the round flies over him. The same
        /// friend standing (in the fight) holds it, and a wrecked friendly HULL still does (CE-321). 🔴 Red-proof: the down-person skip dropped
        /// ⇒ the lying friend holds the shot.
        /// </summary>
        [Fact]
        public void CE2338_AFriendLyingDownOnTheLine_DoesNotHoldTheShot_AStandingOneAndAWreckDo()
        {
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            _world.RegisterComponent<Health>();
            if (!_world.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>()) _world.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            var target = SpawnTarget(new Vector3(20f, 0f, 0f));
            _world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile });
            var (shooter, _) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);
            _world.AddComponent(shooter, new EntityInfo { ForceId = ForceId.Friend });

            var friend = _world.CreateEntity();
            _world.AddComponent(friend, new SimTransform { Position = new Vector3(2f, 0f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(friend, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(friend, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 0.3f });
            _world.AddComponent(friend, global::CarKinem.Core.VehiclePresets.GetPreset(global::CarKinem.Core.VehicleClass.Pedestrian));
            _world.AddComponent(friend, new Health { Current = 100f, Max = 100f });
            Assert.True(Fdp.Toolkit.Combat.LineOfFire.BlockedByFriendly(_world, shooter, target), "a friend standing on the line holds it");
            _world.GetComponentRW<Health>(friend).Current = 0f;
            Assert.False(Fdp.Toolkit.Combat.LineOfFire.BlockedByFriendly(_world, shooter, target), "a friend lying dead on the line: the round flies over him");
            Assert.False(Fdp.Toolkit.Combat.LineOfFire.BlockedByFriendly(_world, shooter, new Vector3(20f, 0f, 0f)), "…a burst at the point too");

            var wreck = _world.CreateEntity();
            _world.AddComponent(wreck, new SimTransform { Position = new Vector3(8f, 0f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(wreck, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(wreck, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 3f });
            _world.AddComponent(wreck, new Health { Current = 0f, Max = 1000f });
            Assert.True(Fdp.Toolkit.Combat.LineOfFire.BlockedByFriendly(_world, shooter, target), "a wrecked friendly hull still stops the round");
        }

        // ── AW-20 — a neutral in the line or the spread holds the shot ─────────────────────────────────────────────────────

        /// <summary>A NEUTRAL entity at <paramref name="at"/> with a collider; a PERSON (pedestrian kinematics) when <paramref name="person"/>.</summary>
        private Entity Neutral(Vector3 at, bool person, float radius = 0.4f)
        {
            if (!_world.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>()) _world.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new EntityInfo { ForceId = ForceId.Neutral });
            _world.AddComponent(e, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = radius });
            if (person) _world.AddComponent(e, global::CarKinem.Core.VehiclePresets.GetPreset(global::CarKinem.Core.VehicleClass.Pedestrian));
            return e;
        }

        /// <summary>
        /// ⭐ <c>AW-20</c> (R-2026; docs/DESIGN_Civilians_And_Crowd_Control.md §5h) — a soldier never fires at a hostile while a NEUTRAL person
        /// stands on the line: he HOLDS (Running, no intent, no ammo spent, <c>HoldNeutralOnLine</c>), and fires the moment the civilian steps
        /// clear. ⛔ A neutral that is not a person (a wall, a wreck — <see cref="ForceId.Neutral"/> is the enum's default) never holds it.
        /// </summary>
        [Fact]
        public void AW20_AimAndFire_HoldsFire_WhileANeutralPersonIsOnTheLine_ThenFiresWhenHeIsClear()
        {
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>();
            _world.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            var target = SpawnTarget(new Vector3(20f, 0f, 0f));
            _world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile });
            var (shooter, channel) = SpawnShooter(Vector3.Zero, ammo: 5, cooldownRemaining: 0f, target, cooldownSeconds: 0.05f);
            _world.AddComponent(shooter, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(shooter, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            var civilian = Neutral(new Vector3(10f, 0.2f, 0f), person: true);
            Neutral(new Vector3(6f, 0f, 0f), person: false, radius: 2f);   // a neutral wall on the line: never a reason to hold

            EnterAimed(shooter, ref channel);
            Tick(shooter, ref channel, 0.016f);
            _world.Bus.SwapBuffers();
            Assert.Equal(0, _world.Bus.Read<WeaponFireIntent>().Length);
            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldNeutralOnLine,
                _world.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(shooter).Weapon.Reason);

            _world.GetComponentRW<SimTransform>(civilian).Position = new Vector3(10f, 3f, 0f);   // he steps clear
            Tick(shooter, ref channel, 0.016f);
            _world.Bus.SwapBuffers();
            Assert.Equal(1, _world.Bus.Read<WeaponFireIntent>().Length);
            Assert.Equal(4, _world.GetComponent<WeaponState>(shooter).Ammo);
        }

        /// <summary>
        /// ⭐ <c>AW-20</c> — the line test itself: a neutral PERSON just off the line holds the shot only when the weapon's spread at his range
        /// reaches him (σ × his distance along it); one behind the shooter, beyond the target, dead, or of the shooter's own side does not
        /// (his own side is <see cref="LineOfFire.BlockedByFriendly(EntityRepository, Entity, Entity)"/>'s); a NEUTRAL shooter is never held by it.
        /// </summary>
        [Fact]
        public void AW20_LineOfFire_ANeutralPersonInTheSpread_HoldsTheShot_ButNotBehindBeyondOrDead()
        {
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            _world.RegisterComponent<Health>();
            var target = SpawnTarget(new Vector3(100f, 0f, 0f));
            _world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile });
            var (shooter, _) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);
            _world.AddComponent(shooter, new EntityInfo { ForceId = ForceId.Friend });

            var near = Neutral(new Vector3(50f, 1f, 0f), person: true);   // 1 m off the line at 50 m; his radius 0.4
            Assert.False(Fdp.Toolkit.Combat.LineOfFire.BlockedByNeutral(_world, shooter, target, 0f));       // an exact aim misses him
            Assert.True(Fdp.Toolkit.Combat.LineOfFire.BlockedByNeutral(_world, shooter, target, 0.02f));     // 20 mils: ±1 m at 50 m reaches him
            Assert.True(Fdp.Toolkit.Combat.LineOfFire.BlockedByNeutral(_world, shooter, new Vector3(100f, 0f, 0f), 0.02f));   // a burst at the point too

            _world.AddComponent(near, new Health { Current = 0f, Max = 100f });   // dead: no longer held for
            Assert.False(Fdp.Toolkit.Combat.LineOfFire.BlockedByNeutral(_world, shooter, target, 0.02f));

            Neutral(new Vector3(-10f, 0f, 0f), person: true);    // behind the shooter
            Neutral(new Vector3(110f, 0f, 0f), person: true);    // beyond the target
            Assert.False(Fdp.Toolkit.Combat.LineOfFire.BlockedByNeutral(_world, shooter, target, 0.02f));

            var onLine = Neutral(new Vector3(40f, 0f, 0f), person: true);
            Assert.True(Fdp.Toolkit.Combat.LineOfFire.BlockedByNeutral(_world, shooter, target, 0f));
            _world.GetComponentRW<EntityInfo>(shooter).ForceId = ForceId.Neutral;   // a neutral shooter: they are his friends (BlockedByFriendly)
            Assert.False(Fdp.Toolkit.Combat.LineOfFire.BlockedByNeutral(_world, shooter, target, 0f));
            Assert.True(Fdp.Toolkit.Combat.LineOfFire.BlockedByFriendly(_world, shooter, target));
            _ = onLine;
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

        /// <summary>⭐ <c>CE-2318</c> (Injury States I4) — a target DOWN (incapacitated: alive, bleeding, below his type's band) ends the
        /// action too — Success, no round fired. 🔒 User, live: soldiers kept firing at an enemy lying wounded. ✅ Red-proof: the end test
        /// back to <c>CombatLife.IsAlive</c> ⇒ the round is fired ⇒ red.</summary>
        [Fact]
        public void CE2318_AimAndFire_ReportsSuccess_WhenTheTargetIsDown()
        {
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<InjuryThresholds>();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            _world.AddComponent(target, new Health { Current = 20f, Max = 100f });
            _world.AddComponent(target, Fdp.Toolkit.Combat.Translators.InjuryTkbTranslator.Thresholds(new Fdp.Toolkit.Tkb.Domain.InjuryDefDto()));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.05f);

            EnterAimed(shooter, ref channel);
            Tick(shooter, ref channel, 0.016f);
            _world.Bus.SwapBuffers();

            Assert.True(CombatLife.IsAlive(_world, target));
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

        // ── CE-2218 — a shooter never fires into the wall in front of his own muzzle ──────────────────────────────────────────

        /// <summary><c>bt-range</c> (the live case's terrain) on this world, and an RPG man's type whose launcher sits where the live
        /// muzzle was: (−0.64, −0.73, +0.86) from the man, facing north (identity).</summary>
        private Entity RpgManOnBtRange(Vector3 at)
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var folder = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
            var terrain = Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "bt-range.world.geojson")),
                "bt-range", Fdp.Toolkit.Terrain.TerrainAssets.ForFolder(folder));
            _world.SetSingletonManaged(terrain);

            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("RPG man", 9218);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
                { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { TurretIndex = -1, MuzzleOffsetX = -0.64f, MuzzleOffsetY = -0.73f, MuzzleOffsetZ = 0.86f, MuzzleVelocity = 115f } } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var man = _world.CreateEntity();
            _world.AddComponent(man, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(man, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 9218 });
            return man;
        }

        /// <summary>
        /// ⭐ <c>CE-2218</c> — live <c>ua-rpg-window</c>: the RPG man at House A's upper window (107.1, 100.9, 3) aimed steeply down at an IFV
        /// ~10 m off; the rocket left the muzzle (106.46, 100.17, 3.86) for (100.03, 92.85, 1.49), stopped 5 cm out and killed him. That
        /// line is masked; the same weapon aimed across open ground is not. 🔴 Red-proof: <c>MuzzleMasked</c> → false ⇒ the first assert fails.
        /// </summary>
        [Fact]
        public void CE2218_TheRpgIntoHisOwnWindowFrame_IsMasked_AnOpenShotIsNot()
        {
            var man = RpgManOnBtRange(new Vector3(107.1f, 100.9f, 3f));
            Assert.True(Fdp.Toolkit.Combat.LineOfFire.MuzzleMasked(_world, man, 0, 0L, new Vector3(100.03f, 92.85f, 1.49f)),
                "the launcher's line runs into the wall it leans out of");

            _world.SetComponent(man, new SimTransform { Position = new Vector3(149f, 10f, 0f), Rotation = Quaternion.Identity });
            Assert.False(Fdp.Toolkit.Combat.LineOfFire.MuzzleMasked(_world, man, 0, 0L, new Vector3(149f, 27f, 1f)),
                "nothing stands in front of the muzzle in the open");
        }

        // ── Q94 A (R-3018) — the brain ASKS, the muscle answers, the answer spends ─────────────────────────────────────────────

        private void Answer(Entity shooter, uint requestId, FireRefusal refusal)
        {
            if (!_world.Bus.IsRegistered<WeaponFireNotification>()) _world.RegisterEvent<WeaponFireNotification>();
            _world.Bus.Publish(new WeaponFireNotification
            {
                Shooter = shooter, WeaponIndex = 0, RequestId = requestId, Refusal = refusal, Quantity = refusal == FireRefusal.None ? 1 : 0,
            });
            _world.Bus.SwapBuffers();
        }

        private WeaponFireIntent[] Asked() { _world.Bus.SwapBuffers(); return _world.Bus.Read<WeaponFireIntent>().ToArray(); }

        /// <summary>
        /// ⭐⭐ <c>Q94 A-3</c> (R-3018) — the executor ASKS (request 1) and spends NOTHING; the one round left is RESERVED, so it does not
        /// ask again before the answer (and says it is waiting, not reloading); the muscle's "fired" for request 1 — read by the weapon
        /// dispatcher, as in production — spends it, ONCE however often it arrives; then the weapon is empty and the action fails.
        /// 🔴 Red-proof: spend at the ask ⇒ 0 after the first frame; <c>Magazine.Ready</c> without the reservation ⇒ a second ask;
        /// no <c>ShotLedger.Settle</c> in the dispatcher ⇒ still 1 after the answer; no newer-id guard ⇒ the duplicate spends again.
        /// </summary>
        [Fact]
        public void Q94_TheAsk_ReservesTheRound_TheFiredAnswerSpendsIt_Once()
        {
            _world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>();
            _world.RegisterComponent<ActorCapabilityState>();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 1, 0f, target, 0f);
            _world.AddComponent(shooter, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            EnterAimed(shooter, ref channel);

            Ask(shooter, ref channel, 0.016f);
            var asked = Asked();
            Assert.Single(asked);
            Assert.Equal(1u, asked[0].RequestId);
            Assert.Equal(1, _world.GetComponent<WeaponState>(shooter).Ammo);          // reserved, not spent

            Ask(shooter, ref channel, 0.016f);
            Assert.Empty(Asked());                                                  // the one round is on its way
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Cooldown,
                _world.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(shooter).Weapon.Reason);

            var dispatcher = new WeaponDispatcherSystem();
            Answer(shooter, 1, FireRefusal.None);
            dispatcher.Execute(_world, 0f);
            Assert.Equal(0, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(0, ShotLedger.Pending(_world.GetComponent<WeaponState>(shooter)));

            Answer(shooter, 1, FireRefusal.None);                                   // the same answer again (a duplicate sample)
            dispatcher.Execute(_world, 0f);
            Assert.Equal(0, _world.GetComponent<WeaponState>(shooter).Ammo);
            ref var w = ref _world.GetComponentRW<WeaponState>(shooter);
            w.Ammo = 3;
            Answer(shooter, 1, FireRefusal.None);
            dispatcher.Execute(_world, 0f);
            Assert.Equal(3, _world.GetComponent<WeaponState>(shooter).Ammo);         // an answered id never spends twice
        }

        /// <summary>
        /// ⭐⭐ <c>Q94 A-4</c> (R-3018, the live <c>CE-2218</c> case) — the muscle REFUSES the round (masked by the wall at the muzzle): nothing
        /// is spent, the cooldown the ask set is cancelled, and the executor HOLDS — no new ask, still Running, <c>Masked</c> (so the behaviour
        /// may move: the window is burned) — until the refusal hold runs out; then it asks again (request 2). The brain read no terrain.
        /// 🔴 Red-proof: a refusal that does not hold ⇒ the second frame asks again at once; a refusal that spends ⇒ 4.
        /// </summary>
        [Fact]
        public void Q94_AMaskedRefusal_SpendsNothing_HoldsMasked_ThenAsksAgain()
        {
            _world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>();
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.5f);
            _world.AddComponent(shooter, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            EnterAimed(shooter, ref channel);

            Ask(shooter, ref channel, 0.016f);
            Assert.Equal(1u, Asked()[0].RequestId);
            Answer(shooter, 1, FireRefusal.Masked);
            ShotLedger.Settle(_world);
            var w = _world.GetComponent<WeaponState>(shooter);
            Assert.Equal(5, w.Ammo);
            Assert.Equal(0f, w.CooldownSecondsRemaining);

            Ask(shooter, ref channel, 0.016f);
            Assert.Empty(Asked());
            Assert.Equal(NodeStatus.Running, channel.Status);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Masked,
                _world.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(shooter).Weapon.Reason);

            ShotLedger.Tick(ref _world.GetComponentRW<WeaponState>(shooter), ShotLedger.RefusalHoldSeconds);
            Ask(shooter, ref channel, 0.016f);
            var again = Asked();
            Assert.Single(again);
            Assert.Equal(2u, again[0].RequestId);
        }

        /// <summary>
        /// ⭐ <c>Q94 A-3</c> — an ask nobody answers (a lost request or answer) is DROPPED after <see cref="ShotLedger.TimeoutSeconds"/>: the
        /// reservation is freed, nothing spent; a late answer to it changes nothing. 🔴 Red-proof: no timeout ⇒ still pending.
        /// </summary>
        [Fact]
        public void Q94_AnUnansweredAsk_TimesOut_NothingSpent_ALateAnswerIgnored()
        {
            var target = SpawnTarget(new Vector3(10f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 5, 0f, target, 0.5f);
            EnterAimed(shooter, ref channel);
            Ask(shooter, ref channel, 0.016f);
            Assert.Equal(1, ShotLedger.Pending(_world.GetComponent<WeaponState>(shooter)));

            ShotLedger.Tick(ref _world.GetComponentRW<WeaponState>(shooter), ShotLedger.TimeoutSeconds * 0.5f);
            Assert.Equal(1, ShotLedger.Pending(_world.GetComponent<WeaponState>(shooter)));
            ShotLedger.Tick(ref _world.GetComponentRW<WeaponState>(shooter), ShotLedger.TimeoutSeconds * 0.5f);
            Assert.Equal(0, ShotLedger.Pending(_world.GetComponent<WeaponState>(shooter)));

            Answer(shooter, 1, FireRefusal.None);
            ShotLedger.Settle(_world);
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);
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

        /// <summary>⭐ <c>CE-2323</c> — the frame (1-based, at <paramref name="dt"/>) on which this shooter's aim at this target is ready:
        /// its own paced aim time, accumulated exactly as the executor does.</summary>
        private int AimSteps(Entity shooter, Entity target, float dt = 0.1f)
        {
            float need = AimAndFireExecutor.PacedAimSecondsFor(_world, shooter, 0, target), elapsed = 0f;
            int k = 0;
            do { elapsed += dt; k++; } while (elapsed < need);
            return k;
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

            // ⭐ CE-2323 — HIS aim time (the mount's 0.8 s × his own factor), and HIS gap after the round
            int aimed = AimSteps(shooter, target);
            int fired = 0;
            for (int i = 0; i < aimed - 1; i++) fired += Step(shooter, ref channel, 0.1f);   // short of his aim time
            Assert.Equal(0, fired);
            Assert.Equal(5, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);

            Assert.Equal(1, Step(shooter, ref channel, 0.1f));                        // aimed
            float gap = FirePacing.Gap(0.2f, shooter, 0, channel.ActionInstanceId);
            int waits = 0;
            while (Step(shooter, ref channel, 0.1f) == 0) waits++;                     // the cooldown …
            Assert.Equal((int)MathF.Ceiling(gap / 0.1f - 1e-4f) - 1, waits);          // … only the cooldown, no new aim
            // ⭐ CE-2166 re-pinned: it was 0.3 s — the rail drove the executor alone, whose own drain lagged the dispatcher's by a frame
            Assert.Equal(3, _world.GetComponent<WeaponState>(shooter).Ammo);
        }

        /// <summary>⭐ <c>CE-2323</c> (R-2063; Weapon Layers §5j) — four riflemen given the same order on the same frame: each first round
        /// leaves on HIS predicted frame (his own aim time), those frames differ, and their later rounds do not move in step. The
        /// prediction reads only (shooter, target, round) — no run state, no clock — so every run of the scenario repeats it.
        /// 🔴 Red before: all four fired on frame 8, then together every 10 frames.</summary>
        [Fact]
        public void CE2323_FourRiflemenOrderedTogether_FireOutOfStep_EachOnHisOwnFrame()
        {
            RegisterSight();
            var target = SpawnTarget(new Vector3(30f, 0f, 0f));
            var men = new Entity[4];
            var channels = new WeaponChannel[4];
            for (int m = 0; m < 4; m++)
            {
                (men[m], channels[m]) = SpawnShooter(new Vector3(0f, m * 2f, 0f), 30, 0f, target, cooldownSeconds: 1f);
                SetSight(men[m], target, visual: true);
                _executor.OnEnter(men[m], ref channels[m], _world);
            }
            var shots = new System.Collections.Generic.List<int>[4];
            for (int m = 0; m < 4; m++) shots[m] = new System.Collections.Generic.List<int>();
            for (int frame = 1; frame <= 60; frame++)
                for (int m = 0; m < 4; m++)
                    if (Step(men[m], ref channels[m], 0.1f) > 0) shots[m].Add(frame);

            var firsts = new System.Collections.Generic.HashSet<int>();
            for (int m = 0; m < 4; m++)
            {
                Assert.True(shots[m].Count >= 3, $"man {m} fired {shots[m].Count}");
                Assert.Equal(AimSteps(men[m], target), shots[m][0]);
                firsts.Add(shots[m][0]);
            }
            Assert.True(firsts.Count >= 3, $"first rounds on frames {string.Join(",", firsts)}: a volley on one frame");
            var thirds = new System.Collections.Generic.HashSet<int>();
            for (int m = 0; m < 4; m++) thirds.Add(shots[m][2]);
            Assert.True(thirds.Count >= 3, $"third rounds on frames {string.Join(",", thirds)}: back in step");
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
            int aimed = AimSteps(shooter, target);                                    // ⭐ CE-2323 — his aim time, ≥ 0.56 s
            for (int i = 0; i < aimed - 1; i++) fired += Step(shooter, ref channel, 0.1f);   // without the restart it would have gone 5 frames sooner
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
            for (int i = 0; i < AimSteps(shooter, target) - 1; i++) fired += Step(shooter, ref channel, 0.1f);   // ⭐ CE-2323 — his aim time
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
            Assert.Equal(AimAndFireExecutor.PacedAimSecondsFor(_world, shooter, 0, target), Row().Needed, 3);   // ⭐ CE-2323 — HIS aim time
            Assert.InRange(Row().Progress, 0.09f, 0.11f);

            int fired = 0;
            for (int i = 0; i < AimSteps(shooter, target) && fired == 0; i++) fired += Step(shooter, ref channel, 0.1f);
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
            for (int i = 0; i < AimSteps(shooter, target) && fired == 0; i++) fired += Step(shooter, ref channel, 0.1f);   // ⭐ CE-2323
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

        /// <summary>⭐⭐ <c>CE-2233</c> (CIV-0, the safety gate; DESIGN_Civilians_And_Crowd_Control CV-8) — the AI's weapon never fires at a
        /// NEUTRAL, whoever picked it: it holds (<c>HoldNeutral</c>) with the target in sight and in range; once the target's side flips
        /// to Hostile (read live) it fires. A target with no <c>EntityInfo</c> is not neutral (Neutral is the enum's zero). ✅ Red-proof:
        /// the hold removed from <c>HoldOf</c> ⇒ rounds at the neutral ⇒ red.</summary>
        [Fact]
        public void CE2233_TheAiNeverFiresAtANeutral_AndFiresOnceItsSideFlipsToHostile()
        {
            if (!_world.IsComponentTypeRegistered<EntityInfo>()) _world.RegisterComponent<EntityInfo>();
            var rifle = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms };
            var (shooter, ch, civilian) = TypedShooter(rifle, range: 50f);
            _world.AddComponent(civilian, new EntityInfo { ForceId = ForceId.Neutral });
            Assert.Equal(0, ShotsIn(shooter, ref ch, 60));
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldNeutral, AimAndFireExecutor.HoldOf(_world, shooter, civilian, rifle, 0));

            _world.GetComponentRW<EntityInfo>(civilian).ForceId = ForceId.Hostile;              // a hostile act flipped his side
            Assert.True(ShotsIn(shooter, ref ch, 60) > 0);

            var (shooter2, ch2, unknown) = TypedShooter(rifle, range: 50f);                     // no EntityInfo: not neutral
            Assert.True(ShotsIn(shooter2, ref ch2, 60) > 0);
        }

        /// <summary>⭐ <c>CE-3255</c> (BR-2; DESIGN_Building_Clearing CB-6) — a STUNNED shooter of any class (here a plain soldier, not a
        /// civilian) with his target in sight fires nothing (<c>HoldStunned</c>) until the stun ends, then fires. ✅ Red-proof: drop the stun
        /// check in <c>HoldOf</c> ⇒ he fires while stunned.</summary>
        [Fact]
        public void CE3255_AStunnedSoldier_FiresNothing_UntilTheStunEnds()
        {
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.EffectState>()) _world.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            var rifle = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms };
            var (shooter, ch, enemy) = TypedShooter(rifle, range: 50f);
            _world.AddComponent(shooter, new Fdp.Toolkit.Combat.EffectState { StunnedUntil = Fdp.Toolkit.Combat.HitModel.Now(_world) + 1000.0 });

            Assert.Equal(0, ShotsIn(shooter, ref ch, 60));
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldStunned, AimAndFireExecutor.HoldOf(_world, shooter, enemy, rifle, 0));

            _world.GetComponentRW<Fdp.Toolkit.Combat.EffectState>(shooter).StunnedUntil = 0.0;   // the stun ends
            Assert.True(ShotsIn(shooter, ref ch, 60) > 0);
        }

        /// <summary>⭐ <c>CV-13a</c> (R-2024; DESIGN_Civilians_And_Crowd_Control §4, §5h.2) — a HOSTILE showing HANDS UP is held like a
        /// neutral: no round (<c>HoldSurrender</c>) and no danger, his side unchanged; the moment his hands come down (a feint) he is
        /// fired at again and his danger is back. Any other gesture holds nothing.</summary>
        [Fact]
        public void CV13a_TheAiHoldsAtAHostileShowingHandsUp_AndFiresTheMomentTheyComeDown()
        {
            if (!_world.IsComponentTypeRegistered<EntityInfo>()) _world.RegisterComponent<EntityInfo>();
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Gesture>()) _world.RegisterComponent<Fdp.Toolkit.Combat.Gesture>();
            var rifle = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms };
            var (shooter, ch, enemy) = TypedShooter(rifle, range: 50f);
            _world.AddComponent(enemy, new EntityInfo { ForceId = ForceId.Hostile });
            float armed = Fdp.Toolkit.Perception.ThreatDanger.Of(_world, shooter, enemy);
            Assert.True(armed > 0f, $"he is a danger before his hands go up: {armed}");
            Fdp.Toolkit.Combat.Gestures.Set(_world, enemy, Fdp.Toolkit.Combat.GestureKind.HandsUp);

            Assert.Equal(0, ShotsIn(shooter, ref ch, 60));
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldSurrender, AimAndFireExecutor.HoldOf(_world, shooter, enemy, rifle, 0));
            Assert.Equal(0f, Fdp.Toolkit.Perception.ThreatDanger.Of(_world, shooter, enemy));
            Assert.Equal(ForceId.Hostile, _world.GetComponent<EntityInfo>(enemy).ForceId);    // ⛔ hands up never flips the side

            Fdp.Toolkit.Combat.Gestures.Set(_world, enemy, Fdp.Toolkit.Combat.GestureKind.FistRaised);   // the hands come down
            Assert.True(ShotsIn(shooter, ref ch, 60) > 0);
            Assert.Equal(armed, Fdp.Toolkit.Perception.ThreatDanger.Of(_world, shooter, enemy));
        }

        /// <summary>⭐ <c>CE-2239</c> part 1 (CV-8, AW-18; §5j FL-2/FL-3) — the FORCE LEVEL caps the round, at the gate: a NonLethal unit holds
        /// its rifle (<c>HoldForceLevel</c>, nothing spent) at a Hostile, but fires a baton round; Warn holds even the baton round; no level is
        /// lethal (unchanged); and SELF-DEFENCE lifts the cap against THAT man only — he aims a lethal weapon at the shooter, or he committed
        /// a lethal act. ✅ Red-proof: the Permits line dropped from HoldOf ⇒ red; SelfDefence always false ⇒ red; the aimed-at check
        /// without "at me" ⇒ red (the second man).</summary>
        [Fact]
        public void CE2239_TheForceLevel_HoldsALethalRound_NotABatonRound_AndSelfDefenceLiftsItAgainstThatManOnly()
        {
            if (!_world.IsComponentTypeRegistered<EntityInfo>()) _world.RegisterComponent<EntityInfo>();
            if (!_world.IsComponentTypeRegistered<Roe>()) _world.RegisterComponent<Roe>();
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponPosture>()) _world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
            var rifle = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms };
            var (shooter, ch, man) = TypedShooter(rifle, range: 30f);
            _world.AddComponent(man, new EntityInfo { ForceId = ForceId.Hostile });
            Assert.True(ShotsIn(shooter, ref ch, 60) > 0);                                              // no level: lethal, as before

            _world.AddComponent(shooter, new Roe { ForceLevel = RoeForceLevel.NonLethal });
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldForceLevel, AimAndFireExecutor.HoldOf(_world, shooter, man, rifle, 0));
            Assert.Equal(0, ShotsIn(shooter, ref ch, 60));

            var db = (Fdp.Toolkit.Tkb.TkbDatabase)_world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
            var baton = new Fdp.Interfaces.TkbTemplate("Baton round", 9390);
            baton.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto { Kind = Fdp.Toolkit.Tkb.Domain.WarheadKind.Blunt });
            db.Register(baton);
            var launcher = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms, AmmoGuid = 9390 };
            Assert.Null(AimAndFireExecutor.HoldOf(_world, shooter, man, launcher, 0));                 // a baton round: allowed
            _world.GetComponentRW<Roe>(shooter).ForceLevel = RoeForceLevel.Warn;
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldForceLevel, AimAndFireExecutor.HoldOf(_world, shooter, man, launcher, 0));
            _world.GetComponentRW<Roe>(shooter).ForceLevel = RoeForceLevel.NonLethal;

            // ⭐ AW-18 — he aims a lethal weapon at the shooter: the rifle is allowed against HIM
            _world.AddComponent(man, new WeaponState { Ammo = 10, MaxAmmo = 10 });
            _world.AddComponent(man, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Target = shooter });
            Assert.Null(AimAndFireExecutor.HoldOf(_world, shooter, man, rifle, 0));
            Assert.True(ShotsIn(shooter, ref ch, 60) > 0);
            // …and only against him: a second Hostile aiming at someone ELSE stays capped
            var other = SpawnTarget(new Vector3(30f, 5f, 0f));
            _world.AddComponent(other, new EntityInfo { ForceId = ForceId.Hostile });
            _world.AddComponent(other, new WeaponState { Ammo = 10, MaxAmmo = 10 });
            _world.AddComponent(other, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Target = man });
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldForceLevel, AimAndFireExecutor.HoldOf(_world, shooter, other, rifle, 0));
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

        /// <summary>⭐ <c>CE-2199</c> — a burst's interval: asked 0 ⇒ the mount's own 60 / RateOfFire; asked > 0 ⇒ kept as given; a mount
        /// stating no rate keeps 0.</summary>
        [Fact]
        public void CE2199_ACooldownOfZero_IsTheMountsOwnRate_AGivenOneIsKept()
        {
            var gun = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { RateOfFire = 6f };
            Assert.Equal(10f, AimAndFireExecutor.CooldownFor(0f, gun));
            Assert.Equal(0.3f, AimAndFireExecutor.CooldownFor(0.3f, gun));
            Assert.Equal(0f, AimAndFireExecutor.CooldownFor(0f, new Fdp.Toolkit.Tkb.Domain.WeaponMountDto()));
            Assert.Equal(0f, AimAndFireExecutor.CooldownFor(0f, null));
        }

        /// <summary>⭐ <c>CE-2199</c> — the measured case: a tank fighting through its turret with the behaviour's cooldown 0 fires its main
        /// gun (6 / min) once per 10 s — two rounds in 12 s from its first, never a round a frame. ✅ Red-proof: the turret path passing
        /// <c>p.CooldownSeconds</c> as given ⇒ all five rounds within a second ⇒ red.</summary>
        [Fact]
        public void CE2199_ATankGunAskedForCooldownZero_FiresAtItsOwnRate()
        {
            var (shooter, _, channel, _) = TurretedTank(rounds: 5, cooldown: 0f, rateOfFire: 6f);
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            _executor.OnEnter(shooter, ref channel, _world);
            int first = -1, shots = 0;
            for (int f = 0; f < 60 * 20; f++)
            {
                Tick(shooter, ref channel, 1f / 60f);
                muscle.Execute(_world, 1f / 60f);
                _world.Bus.SwapBuffers();
                int n = _world.Bus.Read<WeaponFireIntent>().Length;
                if (n > 0 && first < 0) first = f;
                if (first >= 0 && f <= first + 12 * 60) shots += n;
            }
            Assert.True(first >= 0, "it never fired");
            Assert.Equal(2, shots);
        }

        // ── WL-3 (docs/DESIGN_Weapon_Layers.md §5e) — a turreted mount is fought through its turret ────────────────────────────

        /// <summary>A shooter whose type has a turret and a mount on it, with the turret part built (one world = the editor's one
        /// node: the executor is the weapon brain, <see cref="Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem"/> the muscle).</summary>
        private (Entity Shooter, Entity Turret, WeaponChannel Channel, Entity Target) TurretedTank(int rounds, float cooldown, int ammo = 5,
            Fdp.Toolkit.Tkb.Domain.WeaponCategory category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.TankGun, float muzzleVelocity = 0f,
            float aimSeconds = 0f, float rateOfFire = 0f)
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
                                                              AimSeconds = aimSeconds, RateOfFire = rateOfFire } } });
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
            Assert.True(minGap >= (int)(0.5f * FirePacing.GapMin * 60f) - 1,   // ⭐ CE-2323 — each gap ≥ 0.8 × the 0.5 s cooldown
                $"shots {minGap} frames apart: the 0.5 s cooldown, paced, is at least 24");
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
            TestMuscle.FireAll(_world);   // ⭐ Q94 A-3 — the muscle's answer spends the round
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
            TestMuscle.FireAll(_world);   // ⭐ Q94 A-3 — the muscle's answer spends the round
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
        /// ⭐ <c>CE-2180</c> (Air Ops §8b) — a SLOW brain (it ticks every 6th frame, 10 Hz) sees "now" up to one tick past the release point,
        /// so its round leaves late. The intent stamps the moment the CUE says the release is (<see cref="AimAndFireExecutor.ReleaseAt"/>),
        /// not the tick it noticed it on — the SimHost steps its carrier back to that moment (FireProcessingSystem, <c>BombBallisticsTests.CE2180_*</c>).
        /// </summary>
        [Fact]
        public unsafe void CE2180_ASlowBrain_StampsTheCuesReleaseMoment_NotItsTick()
        {
            const long Plane = 9408;
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("plane", Plane);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, AmmoGuid = 9022, InitialAmmunition = 4, DamagePerHit = 1200 } } });
            db.Register(t);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            const float Dt = 1f / 60f, H = 500f;
            const int BrainEvery = 6;
            var v = new Vector3(150f, 0f, 0f);
            var (impact, _) = BombBallistics.Impact(new Vector3(0f, 0f, H), BombBallistics.Launch(v), null, Dt);
            float releaseX = 3000f - impact.X;
            var start = new Vector3(releaseX - 603f, 0f, H);   // just past a brain tick: the slow brain notices ~5 frames late
            double releaseTime = 603.0 / v.X + Dt;             // frame 0 is t = Dt (the clock starts after one frame)

            var (plane, channel) = SpawnShooter(start, 4, 0f, Entity.Null, 0f);
            _world.AddComponent(plane, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Plane });
            _world.AddComponent(plane, new SimVelocity { Linear = v });   // this node owns the motion here
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), new FireAtPointParams { Point = new Vector3(3000f, 0f, 0f), Rounds = 1 });
            var executor = new FireAtPointExecutor();
            executor.OnEnter(plane, ref channel, _world);

            WeaponFireIntent? fired = null; double tickedAt = 0;
            for (int frame = 0; frame < 60 * 8 && fired == null; frame++)
            {
                double now = (frame + 1) * (double)Dt;
                _world.GetComponentRW<SimTransform>(plane).Position = start + v * (float)(now - Dt);
                _world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now, DeltaTime = Dt, TimeScale = 1f });
                if (frame % BrainEvery == 0) { executor.Execute(plane, ref channel, _world, Dt * BrainEvery); tickedAt = now; }
                _world.Bus.SwapBuffers();
                foreach (var e in _world.Bus.Read<WeaponFireIntent>()) fired = e;
            }

            Assert.NotNull(fired);
            Assert.True(tickedAt - releaseTime > 2 * Dt, $"the slow brain noticed late: ticked {tickedAt:F3} s, the release is {releaseTime:F3} s");
            Assert.InRange(fired!.Value.RaisedAt, releaseTime - Dt, releaseTime + Dt);
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
            Vector3 remembered, float freshness = 500f, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? primary = null,
            Fdp.Toolkit.Tkb.Domain.GuidanceKind? missile = null)
        {
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _world.RegisterComponent<WeaponMountInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
            _world.RegisterComponent<EntityInfo>();
            RegisterSight();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            db.Register(new Fdp.Interfaces.TkbTemplate("M67 grenade", M67) { DisType = new Fdp.Core.DISEntityType { Kind = 2 } });
            if (missile is { } guidance)   // ⭐ CE-2200 — the primary mount's round is a missile (AmmoGuid 9311)
            {
                var round = new Fdp.Interfaces.TkbTemplate("Launcher round", 9311);
                round.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.MissileDto { Guidance = guidance, SpeedMps = 140f, MaxFlightSeconds = 15f });
                db.Register(round);
            }
            var type = new Fdp.Interfaces.TkbTemplate("Grenadier", 77);
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                primary ?? new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Range = 300f },
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
            // ⭐ CE-2323 — the slower blind gap, scaled by his own draw for the first round
            Assert.Equal(FirePacing.Gap(0.5f * AimAndFireExecutor.BlindCooldownFactor, unit, 0, channel.ActionInstanceId) - 0.1f,
                         _world.GetComponent<WeaponState>(unit).CooldownSecondsRemaining, 3);
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

        /// <summary>
        /// ⭐ <c>CE-2200</c> (DESIGN_Missiles_And_Ammo §3, M4 D-D "the lock IS the aim time") — a mount that LOCKS (a guided round, an
        /// aim time) never fires a blind round: hidden, BlindFire asked, the Javelin holds (<c>HoldNotSeen</c>, no round, no ammo spent).
        /// An unguided RPG with the same aim time, and a guided round from a mount with no aim time (it never locks), keep the blind round.
        /// 🔴 Red before (live <c>ua-javelin-lock</c>, 2026-10-16): the blind path skipped the lock clock and hit a hidden tank's side.
        /// </summary>
        [Theory]
        [InlineData(Fdp.Toolkit.Tkb.Domain.GuidanceKind.FireAndForget, 4f, false)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.GuidanceKind.Command, 4f, false)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.GuidanceKind.Unguided, 4f, true)]
        [InlineData(Fdp.Toolkit.Tkb.Domain.GuidanceKind.FireAndForget, 0f, true)]
        public void CE2200_ALockingMount_NeverFiresBlind_AnRpgDoes(Fdp.Toolkit.Tkb.Domain.GuidanceKind guidance, float aimSeconds, bool fires)
        {
            _world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>();
            var launcher = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Range = 300f, AimSeconds = aimSeconds, AmmoGuid = 9311 };
            var (unit, _, channel, target) = Grenadier(new Vector3(0f, 30f, 0f), primary: launcher, missile: guidance);
            _world.AddComponent(unit, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            AskBlind(ref channel);
            _executor.OnEnter(unit, ref channel, _world);
            Assert.Equal(fires ? 1 : 0, Step(unit, ref channel, 0.1f));
            Assert.Equal(fires ? 29 : 30, _world.GetComponent<WeaponState>(unit).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);
            if (!fires)
            {
                var row = _world.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(unit).Weapon;
                Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldNotSeen, row.Reason);
                Assert.Equal(target, row.Target);
            }
            Assert.Equal(guidance != Fdp.Toolkit.Tkb.Domain.GuidanceKind.Unguided && aimSeconds > 0f,
                         AimAndFireExecutor.NeedsLock(_world, launcher, 0));
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
        /// a locked unit with NO point is aimed at his middle (⭐ CE-1126 — with a point, the point: the crosshair is on one of his parts).</summary>
        [Fact]
        public void CE1078_PerShot_ALockedShot_IsOneRoundAtTheUnit_AndTheActionRunsOn()
        {
            var target = SpawnTarget(new Vector3(30f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 10, 0f, Entity.Null, 0.2f);
            AskOperatorShot(ref channel, target, Vector3.Zero, cap: 1);
            _executor.OnEnter(shooter, ref channel, _world);
            int shots = 0;
            for (int f = 0; f < 60; f++)
            {
                Tick(shooter, ref channel, 1f / 60f);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>())
                {
                    Assert.InRange(shot.TargetPoint.X, 29f, 31f);   // at the unit
                    shots++;
                }
            }
            Assert.Equal(1, shots);
            Assert.Equal(9, _world.GetComponent<WeaponState>(shooter).Ammo);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        /// <summary>
        /// ⭐ CE-1125 — a locked shot goes to the point the crosshair put ON the unit (his head, here), not his middle. 🔒 User,
        /// 2026-10-16: "when i aim at character and fire i always hit the belly of a standing figure".
        /// </summary>
        [Fact]
        public void CE1125_ALockedShot_GoesToTheCrosshairPointOnTheUnit_NotHisMiddle()
        {
            var target = SpawnTarget(new Vector3(30f, 0f, 0f));
            var (shooter, channel) = SpawnShooter(Vector3.Zero, 10, 0f, Entity.Null, 0.2f);
            var middle = AimPoint.Middle(_world, target, out _, out _);
            var head = middle + new Vector3(0f, 0f, 0.7f);
            AskOperatorShot(ref channel, target, head, cap: 1);
            _executor.OnEnter(shooter, ref channel, _world);
            int shots = 0;
            for (int f = 0; f < 60; f++)
            {
                Tick(shooter, ref channel, 1f / 60f);
                _world.Bus.SwapBuffers();
                foreach (ref readonly var shot in _world.Bus.Read<WeaponFireIntent>())
                {
                    Assert.Equal(head, shot.TargetPoint);
                    shots++;
                }
            }
            Assert.Equal(1, shots);
        }

        /// <summary>⭐ CE-1125 — the point is carried in the unit's own frame: after he walks on and turns, it is still the same spot on him.</summary>
        [Fact]
        public void CE1125_APointOnAUnit_FollowsHim_AsHeMovesAndTurns()
        {
            var unit = SpawnTarget(new Vector3(30f, 0f, 0f));
            var onHim = new Vector3(30.2f, 0.1f, 1.7f);
            var local = AimPoint.ToUnitFrame(_world, unit, onHim);

            ref var t = ref _world.GetComponentRW<SimTransform>(unit);
            t.Position = new Vector3(35f, 4f, 0f);
            t.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

            var carried = AimPoint.FromUnitFrame(_world, unit, local);
            Assert.Equal(1.7f, carried.Z, 3);
            Assert.Equal(35f - 0.1f, carried.X, 3);   // (0.2, 0.1) turned 90°: (-0.1, 0.2)
            Assert.Equal(4f + 0.2f, carried.Y, 3);
        }

        /// <summary>
        /// ⭐⭐ CE-1127 (docs/DESIGN_Entity_Control.md §8g F-1) — the ONE rule for how an operator's trigger works: a weapon on a turret the
        /// unit has, a thrown / arc weapon and a guided one LOCK; a rifle, a mount with no turret (a pintle MG) and an unguided rocket fire
        /// by SIGHT. 🔒 User, 2026-10-16: <i>"The locking mode is good for grenade throwing and grenade launcher and mortar and howitzer"</i> ·
        /// <i>"locking mode for slow turrets as well"</i> · <i>"Mounted fast weapons: OK"</i> · <i>"RPG-type unguided rockets: SIGHT"</i>.
        /// </summary>
        [Fact]
        public void CE1127_TurretThrownArcAndGuidedWeapons_Lock_RiflesPintleGunsAndRockets_FireBySight()
        {
            var (tank, _, _, _) = TurretedTank(rounds: 0, cooldown: 0.2f);
            var turretGun = CombatTkb.MountOf(_world, tank, 0);
            Assert.Equal(OperatorFireMode.Lock, OperatorFireModes.Of(_world, tank, turretGun));
            Fdp.Toolkit.Tkb.Domain.WeaponMountDto Mount(Fdp.Toolkit.Tkb.Domain.WeaponCategory c, int turret = -1)
                => new() { Category = c, TurretIndex = turret };
            Assert.Equal(OperatorFireMode.Sight, OperatorFireModes.Of(_world, tank, Mount(Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms)));
            Assert.Equal(OperatorFireMode.Sight, OperatorFireModes.Of(_world, tank, Mount(Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun)));   // a pintle MG
            Assert.Equal(OperatorFireMode.Sight, OperatorFireModes.Of(_world, tank, Mount(Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket)));
            Assert.Equal(OperatorFireMode.Sight, OperatorFireModes.Of(_world, tank, Mount(Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun, turret: 3)));   // names a turret he has not
            foreach (var c in new[] { Fdp.Toolkit.Tkb.Domain.WeaponCategory.Grenade, Fdp.Toolkit.Tkb.Domain.WeaponCategory.Mortar,
                                      Fdp.Toolkit.Tkb.Domain.WeaponCategory.Howitzer, Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm,
                                      Fdp.Toolkit.Tkb.Domain.WeaponCategory.Sam, Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb })
                Assert.Equal(OperatorFireMode.Lock, OperatorFireModes.Of(_world, tank, Mount(c)));
            Assert.Equal(OperatorFireMode.Sight, OperatorFireModes.Of(_world, tank, null));
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
        /// ⭐⭐ CE-3203 (DESIGN_Weapon_Systems §5b) — a BROKEN weapon (its health at 0: a fragment or a blast) fires for nobody: CanFire says
        /// <see cref="WeaponHold.Destroyed"/>, it arms its host no more (<see cref="WeaponSystems.WeaponOf"/>), no round leaves and its belt
        /// is untouched — while it stays in the host's station (the others' mount indices hold). ⛔ Before: CanFire never read its health,
        /// so a destroyed M2 kept firing. ✅ Red-proof (run): CanFire without the health check ⇒ rounds leave the broken M2 ⇒ red.
        /// </summary>
        [Fact]
        public void CE3203_ABrokenStationWeapon_DoesNotFire_AndArmsNobody()
        {
            var (pickup, m2, _, channel, _) = Technical(rounds: 3);
            _world.AddComponent(m2, new Health { Current = 0f, Max = 80f });

            Assert.False(WeaponSystems.CanFire(_world, m2, out var why));
            Assert.Equal(WeaponHold.Destroyed, why);
            Assert.True(WeaponSystems.IsBroken(_world, m2));
            Assert.Equal(Entity.Null, WeaponSystems.WeaponOf(_world, pickup));
            Assert.Equal(WeaponSystems.ControlledMountBase, WeaponSystems.IndexOf(_world, pickup, m2));   // still in its station

            _executor.OnEnter(pickup, ref channel, _world);
            Assert.Equal(0, WsRun(pickup, ref channel, 3f, expectShooter: m2));
            Assert.Equal(50, _world.GetComponent<WeaponState>(m2).Ammo);

            _world.GetComponentRW<Health>(m2).Current = 1f;   // a damaged weapon still works
            Assert.True(WeaponSystems.CanFire(_world, m2));
            Assert.Equal(m2, WeaponSystems.WeaponOf(_world, pickup));
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
        /// ⭐ <c>CE-1206</c> (Entity Control §8h U-5) — a POSSESSED vehicle fires its station gun with nobody in the gunner seat (one human
        /// drives and shoots, the crew abstract); unpossessed, the empty seat still silences it. ✅ Red-proof: drop the possession test in
        /// <c>WeaponSystems.CanFire</c> ⇒ NoGunner ⇒ red.
        /// </summary>
        [Fact]
        public void CE1206_APossessedVehicle_FiresItsStationGun_WithNoGunner()
        {
            var (pickup, m2, gunner, _, _) = Technical(rounds: 0);
            _world.GetComponentRW<Health>(gunner).Current = 0f;
            Assert.False(WeaponSystems.CanFire(_world, m2, out var why));
            Assert.Equal(WeaponHold.NoGunner, why);

            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.PossessedBy>())
                _world.RegisterComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>();
            _world.AddComponent(pickup, new Fdp.Toolkit.Behavior.Possession.PossessedBy { StationNodeId = 7 });
            Assert.True(WeaponSystems.CanFire(_world, m2, out why), $"held by {why}");
        }

        /// <summary>
        /// ⭐ <c>CE-1210</c> (docs/DESIGN_Entity_Control.md §8n MS-2, R-1018; amends W-H for POSSESSED gunners only) — the man in a
        /// technical's gunner seat controls its M2 while a station possesses HIM: it is on his keys (<c>Controlled</c>), he may fire though
        /// boarding took his CanShoot (<c>OperatesAStation</c>); released, or while the PICKUP itself is possessed (the driver seat), the
        /// pickup controls it again, as for the AI. ✅ Red-proof: the possessed-gunner branch in ControllerOf removed ⇒ the pickup ⇒ red.
        /// </summary>
        [Fact]
        public void CE1210_APossessedGunner_ControlsTheStationHisSeatOperates_TheAIRuleUnchanged()
        {
            var (pickup, m2, gunner, _, _) = Technical(rounds: 1);
            Assert.Equal(pickup, WeaponSystems.ControllerOf(_world, m2));                   // the AI's rule (W-H)
            Assert.False(WeaponSystems.OperatesAStation(_world, gunner));

            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.PossessedBy>())
                _world.RegisterComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>();
            _world.AddComponent(gunner, new Fdp.Toolkit.Behavior.Possession.PossessedBy { StationNodeId = 7 });
            Assert.Equal(gunner, WeaponSystems.ControllerOf(_world, m2));                   // seat switching: the gunner's gun
            Span<Entity> keys = stackalloc Entity[4];
            Assert.Equal(1, WeaponSystems.Controlled(_world, gunner, keys));
            Assert.Equal(m2, keys[0]);
            Assert.Equal(0, WeaponSystems.Controlled(_world, pickup, keys));
            Assert.True(WeaponSystems.OperatesAStation(_world, gunner));
            Assert.True(WeaponSystems.CanFire(_world, m2, out var why), $"held by {why}");

            _world.AddComponent(pickup, new Fdp.Toolkit.Behavior.Possession.PossessedBy { StationNodeId = 7 });   // the driver seat too
            Assert.Equal(pickup, WeaponSystems.ControllerOf(_world, m2));
            _world.RemoveComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(pickup);
            _world.RemoveComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(gunner);                         // released
            Assert.Equal(pickup, WeaponSystems.ControllerOf(_world, m2));
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
        /// <para>⭐ ui handoff P2 — <paramref name="laid"/>: the tube has a LAYING turret 0 (all round, 45–85°, 30 / 15 °/s — the catalogue's)
        /// and its mount is laid by it.</para>
        private unsafe (Entity Man, Entity Tube, WeaponChannel Channel) MannedMortar(FireAtPointParams p, bool laid = false)
        {
            RegisterWeaponSystems();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var tubeType = new Fdp.Interfaces.TkbTemplate("Mortar", IfMortarType);
            tubeType.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Mortar, AmmoGuid = (ulong)IfBombType, MuzzleVelocity = 100f,
                  Arc = Fdp.Toolkit.Tkb.Domain.IndirectArc.HighOnly, MinElevationDeg = 45f, MaxElevationDeg = 85f, TurretIndex = laid ? 0 : -1 } } });
            if (laid)
                tubeType.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TurretSuiteDto { Turrets = { new Fdp.Toolkit.Tkb.Domain.TurretDto
                    { PivotZ = 0.3f, TraverseMinDeg = -180f, TraverseMaxDeg = 180f, ElevationMinDeg = 45f, ElevationMaxDeg = 85f,
                      TraverseRateDegPerSec = 30f, ElevationRateDegPerSec = 15f } } });
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
            if (laid) TurretParts.Ensure(_world, tube, 0);
            var man = WsSoldier(new Vector3(-1f, 0f, 0f));
            Assert.Equal(Fdp.Toolkit.Behavior.EmbarkResult.Applied, Fdp.Toolkit.Behavior.Embarkation.TryEmbark(_world, man, tube, SeatRole.Gunner));
            Assert.Equal(man, WeaponSystems.ControllerOf(_world, tube));   // premise: he mans it
            // ⭐ IF-4 ① (WS-3) — and the dispatcher lets him fire it: WeaponDispatcherSystem fails a weapon action of a man without CanShoot
            //   before the executor runs, which these rails call directly. ✅ Red-proof: Board stripping CanShoot from a Gunner seat ⇒ red.
            Assert.True(_world.GetComponentRO<ActorCapabilityState>(man).Capabilities.HasFlag(ActorCapabilities.CanShoot));

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
            TestMuscle.FireAll(_world);   // ⭐ Q94 A-3 — the muscle's answer spends the round
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

        // ── ⭐⭐ ui handoff P2 / P4 (HANDOFF_Ui_To_Backend_Presets_Elevation_Attitude; DESIGN_Indirect_Fire §3.9, DESIGN_Weapon_Layers W-13e) ──

        /// <summary>Brain then muscle, one frame at <paramref name="now"/>; the rounds that left.</summary>
        private WeaponFireIntent[] LayStep(Entity unit, Entity weapon, ref WeaponChannel channel, double now, FireAtPointExecutor brain,
                                           Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem muscle)
        {
            const float dt = 1f / 60f;
            _world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now, DeltaTime = dt, TimeScale = 1f });
            brain.Execute(unit, ref channel, _world, dt);
            WeaponDispatcherSystem.DrainCooldown(ref _world.GetComponentRW<WeaponState>(weapon), dt);
            muscle.Execute(_world, dt);
            _world.Bus.SwapBuffers();
            return _world.Bus.Read<WeaponFireIntent>().ToArray();
        }

        /// <summary>
        /// ⭐⭐ ui handoff P2 (§3.7) — a gunner's mortar is LAID before its first round: with <c>FireAt</c> 20 s away the tube's laying
        /// turret is ordered at once (Track at the point, the solution's arc — High), slews, and long before the fire time its replicated
        /// <see cref="TurretPose"/> holds the fire solution's elevation (±0.5°, the handoff's acceptance) and the azimuth to the point.
        /// Nothing leaves before <c>FireAt</c>; then the round leaves with the same solution. ✅ Red-proof (run): the lay removed
        /// (<c>Laying.NoTurret</c>) ⇒ no order, the pose stays at 0 ⇒ red.
        /// </summary>
        [Fact]
        public void P2_AMortar_IsLaid_OnTheSolutionsElevation_BeforeItsFirstRound()
        {
            var point = new Vector3(0f, 500f, 0f);   // due north: 90° left of the tube's +X
            var (man, tube, channel) = MannedMortar(new FireAtPointParams { Point = point, Rounds = 1, CooldownSeconds = 3f, FireAt = 20d }, laid: true);
            var brain = new FireAtPointExecutor();
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            brain.OnEnter(man, ref channel, _world);
            var part = TurretParts.Find(_world, tube, 0);

            var mount = CombatTkb.MountOf(_world, tube, 0)!;
            var db = _world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
            var solution = FireSolution.For(null, Vector3.Zero, point, mount, FireSolution.ChargesOf(db, mount, IfBombType));
            Assert.True(solution.Ok && solution.High);

            int frame = 0;
            for (; frame < 60 * 10; frame++) Assert.Empty(LayStep(man, tube, ref channel, frame / 60d, brain, muscle));   // 10 s, all before FireAt
            Assert.Equal(WeaponTaskMode.Track, _world.GetComponent<WeaponTask>(part).Mode);
            Assert.Equal(ArcPreference.High, _world.GetComponent<WeaponTask>(part).Arc);
            Assert.Equal(WeaponTaskState.OnTarget, _world.GetComponent<WeaponTaskStatus>(part).State);
            var pose = _world.GetComponent<TurretPose>(part);
            Assert.InRange(pose.Elevation * 180f / MathF.PI, solution.Elevation - 0.5f, solution.Elevation + 0.5f);
            Assert.InRange(pose.Azimuth * 180f / MathF.PI, 89.5f, 90.5f);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.AwaitingFireTime, LastWeaponReason(man));
            Assert.Equal(10, _world.GetComponent<WeaponState>(tube).Ammo);

            WeaponFireIntent[] shots = System.Array.Empty<WeaponFireIntent>();
            for (; frame < 60 * 21 && shots.Length == 0; frame++) shots = LayStep(man, tube, ref channel, frame / 60d, brain, muscle);
            var shot = Assert.Single(shots);
            Assert.True(frame / 60d >= 20d, $"the round left at {frame / 60d:F2} s, before FireAt");
            Assert.Equal(tube, shot.Shooter);
            Assert.Equal(solution.Charge, shot.Charge);
            Assert.Equal((byte)1, shot.HighArc);
        }

        /// <summary>
        /// ⭐⭐ ui handoff P2 (§3.7) — a self-propelled gun (its OWN turreted Howitzer mount, Arc Both, the M109's 10 / 5 °/s turret) at a
        /// point 90° to its left, fire when ready: no round while the turret TURNS (the action says Slewing), and the first round leaves
        /// only once the turret reports OnTarget — the azimuth then 90° ±0.5°. ✅ Red-proof (run): the lay removed ⇒ the round leaves on
        /// the first frame with the turret still forward ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void P2_ASelfPropelledGun_TurnsItsTurret_BeforeItFires()
        {
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            _world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _world.RegisterComponent<TurretPose>();
            _world.RegisterComponent<WeaponTask>();
            _world.RegisterComponent<WeaponTaskStatus>();
            _world.RegisterComponent<WeaponMuscleState>();
            _world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var gunType = new Fdp.Interfaces.TkbTemplate("SP gun", 9955);
            gunType.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TurretSuiteDto { Turrets = { new Fdp.Toolkit.Tkb.Domain.TurretDto
                { PivotZ = 2.6f, TraverseMinDeg = -180f, TraverseMaxDeg = 180f, ElevationMinDeg = -3f, ElevationMaxDeg = 75f,
                  TraverseRateDegPerSec = 10f, ElevationRateDegPerSec = 5f } } });
            gunType.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Howitzer, AmmoGuid = (ulong)IfBombType, MuzzleVelocity = 300f, TurretIndex = 0,
                  Arc = Fdp.Toolkit.Tkb.Domain.IndirectArc.Both, MinElevationDeg = -3f, MaxElevationDeg = 75f } } });
            db.Register(gunType);
            var shell = new Fdp.Interfaces.TkbTemplate("if shell", IfBombType);
            shell.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto { Kind = Fdp.Toolkit.Tkb.Domain.WarheadKind.HighExplosive, Indirect = true });
            shell.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.AmmoWeaponBallisticsDto { ChargeSpeedsMps = new() { 150f, 220f, 300f } });
            db.Register(shell);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var gun = _world.CreateEntity();
            _world.AddComponent(gun, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(gun, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 9955 });
            _world.AddComponent(gun, new WeaponState { Ammo = 10, MuzzleVelocity = 300f });
            _world.AddComponent(gun, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            var part = TurretParts.Ensure(_world, gun, 0);
            var channel = new WeaponChannel { Status = NodeStatus.Running };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), new FireAtPointParams { Point = new Vector3(0f, 3000f, 0f), Rounds = 1, CooldownSeconds = 10f });
            _world.AddComponent(gun, channel);
            channel = _world.GetComponent<WeaponChannel>(gun);
            var brain = new FireAtPointExecutor();
            var muscle = new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem();
            brain.OnEnter(gun, ref channel, _world);

            int first = -1;
            bool saidSlewing = false;
            for (int f = 0; f < 60 * 15 && first < 0; f++)
            {
                if (LayStep(gun, gun, ref channel, f / 60d, brain, muscle).Length > 0) { first = f; break; }
                saidSlewing |= LastWeaponReason(gun) == Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Slewing;
            }
            Assert.True(saidSlewing, "the gun never said it was slewing");
            Assert.True(first >= 60 * 8, $"first round at frame {first}: 90° at 10 °/s is 9 s of traverse");
            Assert.InRange(_world.GetComponent<TurretPose>(part).Azimuth * 180f / MathF.PI, 89.5f, 90.5f);
            TestMuscle.FireAll(_world);   // ⭐ Q94 A-3 — the muscle's answer spends the round
            Assert.Equal(9, _world.GetComponent<WeaponState>(gun).Ammo);
        }

        /// <summary>
        /// ⭐ ui handoff P4 (W-13e) — the weapon brain names the mount it fires on the unit's <see cref="WeaponPosture"/>: the pickup's
        /// station M2 (its controlled index 32) through <c>AimAndFire</c>; the gunner's tube (32) through <c>FireAtPoint</c>. Kept when
        /// the weapon is lowered. ✅ Red-proof (run): <c>SetActiveMount</c> writing nothing ⇒ 0 ⇒ both red.
        /// </summary>
        [Fact]
        public void P4_TheWeaponBrain_NamesTheMountItFires_OnThePosture()
        {
            var (pickup, _, _, channel, _) = Technical(rounds: 1);
            _world.RegisterComponent<WeaponPosture>();
            _world.AddComponent(pickup, new WeaponPosture());
            _executor.OnEnter(pickup, ref channel, _world);
            _executor.Execute(pickup, ref channel, _world, 1f / 60f);
            Assert.Equal((byte)WeaponSystems.ControlledMountBase, _world.GetComponent<WeaponPosture>(pickup).ActiveMount);
            Fdp.Toolkit.Combat.WeaponPostureOf.Idle(_world, pickup, 10f);
            Assert.Equal(WeaponPostureKind.AtEase, _world.GetComponent<WeaponPosture>(pickup).Kind);
            Assert.Equal((byte)WeaponSystems.ControlledMountBase, _world.GetComponent<WeaponPosture>(pickup).ActiveMount);
        }

        /// <inheritdoc cref="P4_TheWeaponBrain_NamesTheMountItFires_OnThePosture"/>
        [Fact]
        public void P4_AGunner_FiringTheTubeHeMans_NamesIt_OnThePosture()
        {
            var (man, _, channel) = MannedMortar(new FireAtPointParams { Point = new Vector3(500f, 0f, 0f), Rounds = 1, FireAt = 5d });
            _world.RegisterComponent<WeaponPosture>();
            _world.AddComponent(man, new WeaponPosture());
            new FireAtPointExecutor().OnEnter(man, ref channel, _world);
            FireAtPointStep(man, ref channel, now: 1d);
            Assert.Equal((byte)WeaponSystems.ControlledMountBase, _world.GetComponent<WeaponPosture>(man).ActiveMount);
        }

        /// <summary>
        /// ⭐ <c>CE-1222</c> (ui note §2) — an OPERATOR's fire names the mount he fires on the posture too: the pickup's station M2 (32)
        /// while possessed, not the AI's last choice. ✅ Red-proof: drop the <c>SetActiveMount</c> in <c>OperatorFire</c> ⇒ 0 ⇒ red.
        /// </summary>
        [Fact]
        public void CE1222_AnOperatorsFire_NamesTheMountHePicked_OnThePosture()
        {
            var (pickup, _, _, channel, _) = Technical(rounds: 1);
            _world.RegisterComponent<WeaponPosture>();
            _world.AddComponent(pickup, new WeaponPosture());
            AskOperator(ref channel, new Vector3(40f, 0f, 1f), trigger: 1);
            _executor.OnEnter(pickup, ref channel, _world);
            _executor.Execute(pickup, ref channel, _world, 1f / 60f);
            Assert.Equal((byte)WeaponSystems.ControlledMountBase, _world.GetComponent<WeaponPosture>(pickup).ActiveMount);
        }

        private Fdp.Toolkit.Behavior.Diagnostics.ActionReason LastWeaponReason(Entity unit)
            => _world.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(unit).Weapon.Reason;
    }
}
