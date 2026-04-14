using Fdp.Examples.Common;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Examples.Scenarios.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UrbanCombatValidator"/> introduced by PACK3-U001.
    ///
    /// <para>Each test builds a minimal, self-contained <see cref="EntityRepository"/>
    /// with a <see cref="TkbIdentity"/>-tagged APC / Insurgent entity pair and drives
    /// the validator in isolation — no full scenario pipeline, no CarKinem systems.</para>
    ///
    /// <para>To observe latch state the tests read the validator's public latch properties
    /// (<c>LatchAmbushFired</c>, etc.).</para>
    /// </summary>
    public class UrbanCombatValidatorTests
    {
        // ── TKB constants matching UrbanCombatNewScenario ────────────────────
        private const long TkbMilitaryApc = 2001;
        private const long TkbInsurgent   = 2003;

        // ── Helper: create a minimal repo with required component types ───────
        private static EntityRepository CreateMinimalWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<TkbIdentity>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<Health>();
            return world;
        }

        // ── Unit test 1: _latchAmbushFired fires when WeaponChannel == AimAndFire ──

        [Fact]
        public void EvaluateTick_LatchAmbushFired_WhenInsurgentWeaponChannelIsAimAndFire()
        {
            using var world    = CreateMinimalWorld();
            var validator = new UrbanCombatValidator();

            // Spawn Insurgent with TkbIdentity and active weapon channel.
            var insurgent = world.CreateEntity();
            world.AddComponent(insurgent, new TkbIdentity { TkbType = TkbInsurgent });
            world.AddComponent(insurgent, new WeaponChannel
            {
                ActiveAction = CombatConstants.ActionIdAimAndFire,
            });
            world.AddComponent(insurgent, new Health { Current = 100f, Max = 100f });

            // One tick — latch 1 should fire.
            validator.EvaluateTick(1, world);

            Assert.True(validator.LatchAmbushFired,
                "LatchAmbushFired must be set when Insurgent WeaponChannel.ActiveAction == AimAndFire.");
        }

        // ── Unit test 2: ScenarioFailureException thrown when tick > 600 ──────

        [Fact]
        public void EvaluateTick_Throws_WhenTickExceeds600AndNoLatchesFired()
        {
            using var world    = CreateMinimalWorld();
            var validator = new UrbanCombatValidator();

            // Spawn entities so the world is valid but latches remain unset.
            var apc = world.CreateEntity();
            world.AddComponent(apc, new TkbIdentity { TkbType = TkbMilitaryApc });
            world.AddComponent(apc, new LocomotionChannel { ActiveAction = 1 });

            var insurgent = world.CreateEntity();
            world.AddComponent(insurgent, new TkbIdentity { TkbType = TkbInsurgent });
            world.AddComponent(insurgent, new WeaponChannel { ActiveAction = 0 });
            world.AddComponent(insurgent, new Health { Current = 100f, Max = 100f });

            // Tick 601 — validator must throw.
            Assert.Throws<ScenarioFailureException>(() => validator.EvaluateTick(601, world));
        }

        // ── Unit test 3: returns true after all four latches fire sequentially ─

        [Fact]
        public void EvaluateTick_ReturnsTrue_WhenAllFourLatchesFire()
        {
            using var world    = CreateMinimalWorld();
            var validator = new UrbanCombatValidator();

            var apc = world.CreateEntity();
            world.AddComponent(apc, new TkbIdentity { TkbType = TkbMilitaryApc });
            world.AddComponent(apc, new LocomotionChannel { ActiveAction = 1 }); // moving initially

            var insurgent = world.CreateEntity();
            world.AddComponent(insurgent, new TkbIdentity { TkbType = TkbInsurgent });
            world.AddComponent(insurgent, new WeaponChannel { ActiveAction = 0 });
            world.AddComponent(insurgent, new Health { Current = 100f, Max = 100f });

            // Tick 1: no latch fires yet.
            bool result = validator.EvaluateTick(1, world);
            Assert.False(result);
            Assert.False(validator.LatchAmbushFired);

            // Tick 2: set Insurgent's weapon channel → latch 1 fires.
            ref var weapon = ref world.GetComponentRW<WeaponChannel>(insurgent);
            weapon.ActiveAction = CombatConstants.ActionIdAimAndFire;
            result = validator.EvaluateTick(2, world);
            Assert.False(result);
            Assert.True(validator.LatchAmbushFired);
            Assert.False(validator.LatchApcHalted);

            // Tick 3: halt APC → latch 2 fires.
            ref var loco = ref world.GetComponentRW<LocomotionChannel>(apc);
            loco.ActiveAction = 0;
            result = validator.EvaluateTick(3, world);
            Assert.False(result);
            Assert.True(validator.LatchApcHalted);
            Assert.False(validator.LatchInsurgentHit);

            // Tick 4: damage Insurgent → latch 3 fires.
            ref var hp = ref world.GetComponentRW<Health>(insurgent);
            hp.Current = 50f;
            result = validator.EvaluateTick(4, world);
            Assert.False(result);
            Assert.True(validator.LatchInsurgentHit);
            Assert.False(validator.LatchInsurgentKilled);

            // Tick 5: kill Insurgent → latch 4 fires; EvaluateTick returns true.
            world.DestroyEntity(insurgent);
            result = validator.EvaluateTick(5, world);
            Assert.True(result, "EvaluateTick must return true once InsurgentKilled latch fires.");
            Assert.True(validator.LatchInsurgentKilled);
        }
    }
}
