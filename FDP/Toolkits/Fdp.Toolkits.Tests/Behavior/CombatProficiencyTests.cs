using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2273</c> DA-A (DA-1, DA-2; 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8a) — a soldier's
    /// PROFICIENCY: exactly neutral at Regular and with none (no rail or demo moves), the five presets and the line between them, the
    /// three sources in precedence (authored › unit › type › Regular), the unit's value written onto its men, the TKB type default.
    /// </summary>
    public sealed class CombatProficiencyTests : System.IDisposable
    {
        private readonly EntityRepository _world = new();

        public CombatProficiencyTests()
        {
            _world.RegisterComponent<CombatProficiency>();
            _world.RegisterComponent<UnitSubordinate>();
        }

        public void Dispose() => _world.Dispose();

        /// <summary>⭐ The neutral rail: no component, a zero-filled one, Regular, or an unregistered world — every factor is EXACTLY
        /// what the code did before proficiency existed (1.0 / 0, controlled bursts, cover discipline).</summary>
        [Fact]
        public void CE2273_NoneZeroFilledOrRegular_IsExactlyNeutral()
        {
            var none = _world.CreateEntity();
            var zero = _world.CreateEntity();
            _world.AddComponent(zero, new CombatProficiency());
            var regular = _world.CreateEntity();
            _world.AddComponent(regular, new CombatProficiency { Authored = ProficiencyOf.Regular, Set = CombatProficiency.AuthoredSet });
            using var bare = new EntityRepository();
            var b = bare.CreateEntity();
            foreach (var (view, e) in new (ISimulationView, Entity)[] { (_world, none), (_world, zero), (_world, regular), (bare, b) })
            {
                Assert.Equal(1.0f, ProficiencyOf.SpreadFactor(view, e));
                Assert.Equal(1.0f, ProficiencyOf.ExposeFactor(view, e));
                Assert.Equal(0f, ProficiencyOf.ReactionDelaySeconds(view, e));
                Assert.Equal(BurstDiscipline.Controlled, ProficiencyOf.Bursts(view, e));
                Assert.True(ProficiencyOf.KeepsCoverDiscipline(view, e));
                Assert.Equal(ProficiencyTier.Regular, ProficiencyOf.Tier(view, e));
            }
        }

        /// <summary>DA-2's tables at the presets, and linear between them; a value names its nearest tier.</summary>
        [Fact]
        public void CE2273_ThePresets_AndTheLineBetween()
        {
            float[] spread = { 2.5f, 1.6f, 1.0f, 0.8f, 0.65f };
            for (int t = 0; t < 5; t++)
            {
                var e = Authored(ProficiencyOf.ValueOf((ProficiencyTier)t));
                Assert.Equal(spread[t], ProficiencyOf.SpreadFactor(_world, e));
                Assert.Equal((ProficiencyTier)t, ProficiencyOf.Tier(_world, e));
            }
            var mid = Authored(0.7f);                                                   // half-way Regular → Veteran
            Assert.Equal(0.9f, ProficiencyOf.SpreadFactor(_world, mid), 4);
            Assert.Equal(2.5f, ProficiencyOf.SpreadFactor(_world, Authored(0f)));       // flat below Untrained
            Assert.Equal(BurstDiscipline.EmptyTheMagazine, ProficiencyOf.Bursts(_world, Authored(ProficiencyOf.Untrained)));
            Assert.Equal(BurstDiscipline.LongBursts, ProficiencyOf.Bursts(_world, Authored(ProficiencyOf.Militia)));
            Assert.False(ProficiencyOf.KeepsCoverDiscipline(_world, Authored(ProficiencyOf.Militia)));
            Assert.Equal(1.2f, ProficiencyOf.ReactionDelaySeconds(_world, Authored(ProficiencyOf.Untrained)));
        }

        /// <summary>The three sources never overwrite each other; the reader takes authored, else the unit's, else the type's.</summary>
        [Fact]
        public void CE2273_AuthoredBeatsUnit_BeatsTypeDefault()
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new CombatProficiency { TypeDefault = ProficiencyOf.Militia, Set = CombatProficiency.TypeDefaultSet });
            Assert.Equal(ProficiencyOf.Militia, ProficiencyOf.Value(_world, e));
            ref var p = ref _world.GetComponentRW<CombatProficiency>(e);
            p.Unit = ProficiencyOf.Veteran; p.Set |= CombatProficiency.UnitSet;
            Assert.Equal(ProficiencyOf.Veteran, ProficiencyOf.Value(_world, e));
            p = ref _world.GetComponentRW<CombatProficiency>(e);
            p.Authored = 0f; p.Set |= CombatProficiency.AuthoredSet;                      // 0 is a legitimate value
            Assert.Equal(0f, ProficiencyOf.Value(_world, e));
        }

        /// <summary>⭐ The UNIT setting reaches the men: a platoon's authored value fills every man two levels down who has none of his
        /// own; a squad's own value is nearer and wins for its men; the man's own beats both; clearing the unit's takes it back. ✅
        /// Red-proof: the fill writing nothing ⇒ the men read Regular.</summary>
        [Fact]
        public void CE2273_TheUnitsValue_FillsItsMen_TheNearestWins_TheMansOwnBeatsIt()
        {
            var platoon = Authored(ProficiencyOf.Untrained);
            var squadA = Under(platoon);
            var squadB = Under(platoon);
            _world.AddComponent(squadB, new CombatProficiency { Authored = ProficiencyOf.Elite, Set = CombatProficiency.AuthoredSet });
            var a1 = Under(squadA);
            var b1 = Under(squadB);
            var b2 = Under(squadB);
            _world.AddComponent(b2, new CombatProficiency { Authored = ProficiencyOf.Veteran, Set = CombatProficiency.AuthoredSet });

            var fill = new ProficiencyFillSystem();
            fill.Execute(_world, 0.016f);
            Assert.Equal(ProficiencyOf.Untrained, ProficiencyOf.Value(_world, a1));      // two levels up
            Assert.Equal(ProficiencyOf.Elite, ProficiencyOf.Value(_world, b1));           // his squad's is nearer
            Assert.Equal(ProficiencyOf.Veteran, ProficiencyOf.Value(_world, b2));         // his own

            ref var pl = ref _world.GetComponentRW<CombatProficiency>(platoon);
            pl.Set = 0;
            fill.Execute(_world, 0.016f);
            Assert.Equal(ProficiencyOf.Regular, ProficiencyOf.Value(_world, a1));         // taken back
            Assert.Equal(ProficiencyOf.Elite, ProficiencyOf.Value(_world, b1));
        }

        /// <summary>⭐ <c>CE-2269</c> TS-3 — the TKB translator stamps a type's temperament at spawn; a type without a
        /// <c>TemperamentDto</c> stamps nothing (today's soldiers read today's defaults).</summary>
        [Fact]
        public void CE2269_TheTypeTemperament_IsStampedAtSpawn_AndATypeWithoutOneStampsNothing()
        {
            var world = TestWorldFactory.Create();
            world.RegisterComponent<CombatTemperament>();
            var insurgent = new TkbTemplate("Insurgent", 1);
            insurgent.AddDescriptor(new BehaviorProfileDto { BrainTier = BehaviorConstants.BrainTierBTree });
            insurgent.AddDescriptor(TemperamentDto.Insurgent());
            var a = world.CreateEntity();
            new BehaviorTkbTranslator().Inject(world, a, insurgent);
            Assert.InRange(TemperamentOf.Obedience(world, a), 0.15f - 1e-4f, 0.45f + 1e-4f);
            Assert.Equal(0, world.GetComponentRO<CombatTemperament>(a).Set);

            var soldierType = new TkbTemplate("Soldier", 2);
            soldierType.AddDescriptor(new BehaviorProfileDto { BrainTier = BehaviorConstants.BrainTierBTree });
            var s = world.CreateEntity();
            new BehaviorTkbTranslator().Inject(world, s, soldierType);
            Assert.False(world.HasComponent<CombatTemperament>(s));
            Assert.Equal(TemperamentOf.DefaultObedience, TemperamentOf.Obedience(world, s));
            world.Dispose();
        }

        /// <summary>The TKB type's default is projected at spawn into its own field, under an authored value.</summary>
        [Fact]
        public void CE2273_TheTypeDefault_IsProjectedAtSpawn_UnderAnAuthoredValue()
        {
            var world = TestWorldFactory.Create();
            world.RegisterComponent<CombatProficiency>();
            var template = new TkbTemplate("Insurgent", 1);
            template.AddDescriptor(new BehaviorProfileDto { BrainTier = BehaviorConstants.BrainTierBTree, DefaultProficiency = ProficiencyOf.Militia });
            var plain = world.CreateEntity();
            new BehaviorTkbTranslator().Inject(world, plain, template);
            Assert.Equal(ProficiencyOf.Militia, ProficiencyOf.Value(world, plain));

            var authored = world.CreateEntity();
            world.AddComponent(authored, new CombatProficiency { Authored = ProficiencyOf.Elite, Set = CombatProficiency.AuthoredSet });
            new BehaviorTkbTranslator().Inject(world, authored, template);
            Assert.Equal(ProficiencyOf.Elite, ProficiencyOf.Value(world, authored));
            Assert.Equal(ProficiencyOf.Militia, world.GetComponentRO<CombatProficiency>(authored).TypeDefault);

            var regularType = new TkbTemplate("Soldier", 2);
            regularType.AddDescriptor(new BehaviorProfileDto { BrainTier = BehaviorConstants.BrainTierBTree });
            var soldier = world.CreateEntity();
            new BehaviorTkbTranslator().Inject(world, soldier, regularType);
            Assert.False(world.HasComponent<CombatProficiency>(soldier));                // no default ⇒ nothing added
            world.Dispose();
        }

        private Entity Authored(float value)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new CombatProficiency { Authored = value, Set = CombatProficiency.AuthoredSet });
            return e;
        }

        private Entity Under(Entity commander)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new UnitSubordinate { Commander = commander });
            return e;
        }
    }
}
