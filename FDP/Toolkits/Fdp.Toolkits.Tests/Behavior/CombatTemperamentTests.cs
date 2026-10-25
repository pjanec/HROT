using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐ AQ89 B/E (R-269) — the temperament NEVER reads zero by accident. 🔒 User: <i>"Trait component needs some defaults to never
    /// come up unfilled/zeroed as that would ruin the behavior."</i> 0 is a legitimate value (a coward), so only a SET bit makes a
    /// field count.
    /// </summary>
    public sealed class CombatTemperamentTests : System.IDisposable
    {
        private readonly EntityRepository _world = new();
        public CombatTemperamentTests() => _world.RegisterComponent<CombatTemperament>();
        public void Dispose() => _world.Dispose();

        [Fact]
        public void NoComponent_ZeroFilled_OrNoRegistration_ReadTheDefaults()
        {
            var none = _world.CreateEntity();
            var zero = _world.CreateEntity();
            _world.AddComponent(zero, new CombatTemperament());
            foreach (var e in new[] { none, zero })
            {
                Assert.Equal(TemperamentOf.DefaultRiskAcceptance, TemperamentOf.RiskAcceptance(_world, e));
                Assert.Equal(TemperamentOf.DefaultObedience, TemperamentOf.Obedience(_world, e));
            }
            using var bare = new EntityRepository();
            var b = bare.CreateEntity();
            Assert.Equal(TemperamentOf.DefaultRiskAcceptance, TemperamentOf.RiskAcceptance(bare, b));
        }

        [Fact]
        public void ASetField_CountsEvenAtZero_AndTheOtherKeepsItsDefault()
        {
            var coward = _world.CreateEntity();
            _world.AddComponent(coward, new CombatTemperament { RiskAcceptance = 0f, Obedience = 0.1f, Set = CombatTemperament.RiskAcceptanceSet });
            Assert.Equal(0f, TemperamentOf.RiskAcceptance(_world, coward));
            Assert.Equal(TemperamentOf.DefaultObedience, TemperamentOf.Obedience(_world, coward));   // not set ⇒ its 0.1 is ignored
        }

        /// <summary>AQ89 B/E — the ordinary soldier holds on his own (0.5 &lt; 0.6) and assaults when ordered (0.8 ≥ 0.6); a brave one
        /// closes on his own; a disobedient one refuses even the order.</summary>
        [Theory]
        [InlineData(null, null, false, false)]
        [InlineData(null, null, true, true)]
        [InlineData(0.8f, null, false, true)]
        [InlineData(null, 0.3f, true, false)]
        [InlineData(1.5f, null, false, true)]   // clamped, still willing
        public void Willing_IsTheThreshold_OnBraveryOrObedience(float? risk, float? obey, bool ordered, bool willing)
        {
            var e = _world.CreateEntity();
            var t = new CombatTemperament();
            if (risk is { } r) { t.RiskAcceptance = r; t.Set |= CombatTemperament.RiskAcceptanceSet; }
            if (obey is { } o) { t.Obedience = o; t.Set |= CombatTemperament.ObedienceSet; }
            _world.AddComponent(e, t);
            Assert.Equal(willing, TemperamentOf.Willing(_world, e, ordered));
        }

        // ── ⭐ CE-2269 TS-A (R-2034) — Nerve and Resolve, the type layer, the spread, obedience tempering ──

        /// <summary>TS-1 — the new traits read their neutral 0.5 when unset, count at 0 when set, and a type's value sits under an authored one.</summary>
        [Fact]
        public void CE2269_NerveAndResolve_DefaultUnset_CountAtZero_AuthoredBeatsType()
        {
            var none = _world.CreateEntity();
            Assert.Equal(TemperamentOf.DefaultNerve, TemperamentOf.Nerve(_world, none));
            Assert.Equal(TemperamentOf.DefaultResolve, TemperamentOf.Resolve(_world, none));

            var e = _world.CreateEntity();
            _world.AddComponent(e, new CombatTemperament
            {
                Nerve = 0f, Set = CombatTemperament.NerveSet,
                TypeNerve = 0.9f, TypeResolve = 0.1f, TypeObedience = 0.3f,
                TypeSet = CombatTemperament.NerveSet | CombatTemperament.ResolveSet | CombatTemperament.ObedienceSet,
            });
            Assert.Equal(0f, TemperamentOf.Nerve(_world, e));              // authored 0 beats the type's 0.9
            Assert.Equal(0.1f, TemperamentOf.Resolve(_world, e));          // the type's
            Assert.Equal(0.3f, TemperamentOf.Obedience(_world, e));        // the type's — AQ89's raw reader sees it too
            Assert.Equal(TemperamentOf.DefaultRiskAcceptance, TemperamentOf.RiskAcceptance(_world, e));
        }

        /// <summary>TS-2 (R-2076) — <c>Effective = trained + (1 − Obedience) × (value − trained)</c>, trained nerve 0.75 · resolve 0.6 · bravery
        /// 0.5; AQ89's <c>Willingness</c> stays raw.</summary>
        [Theory]
        [InlineData(1.0f, 0.5f, 0.875f, 0.8f)]    // obedience 0.5 halves the step from the trained standard
        [InlineData(0.1f, 0.9f, 0.685f, 0.55f)]   // a disciplined coward acts close to the trained standard
        [InlineData(0.1f, 0.0f, 0.1f, 0.1f)]      // an undisciplined one acts himself out
        public void CE2269_Effective_IsTemperedByObedience_TowardTheTrainedStandard(float trait, float obedience, float nerve, float resolve)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new CombatTemperament
            {
                Nerve = trait, Obedience = obedience, RiskAcceptance = 0.9f, Resolve = trait,
                Set = CombatTemperament.NerveSet | CombatTemperament.ObedienceSet | CombatTemperament.RiskAcceptanceSet | CombatTemperament.ResolveSet,
            });
            Assert.Equal(nerve, TemperamentOf.Effective(_world, e, TemperamentTrait.Nerve), 4);
            Assert.Equal(resolve, TemperamentOf.Effective(_world, e, TemperamentTrait.Resolve), 4);
            Assert.Equal(0.5f + (1f - obedience) * 0.4f, TemperamentOf.Effective(_world, e, TemperamentTrait.RiskAcceptance), 4);
            Assert.Equal(0.9f, TemperamentOf.Willingness(_world, e, orderedAssault: false), 4);   // ⛔ never blended
        }

        /// <summary>⭐ TS-2 (R-2076) — the ordinary man (no temperament: raw nerve / resolve 0.5, obedience 0.8) is held near the TRAINED
        /// standard — steadier than his raw self — and his bravery stays the average, so TS-5's damping is neutral for him.</summary>
        [Fact]
        public void CE2269_TheOrdinaryMan_IsSteadierThanTheAverage_ButNoBolder()
        {
            var plain = _world.CreateEntity();
            Assert.Equal(TemperamentOf.DefaultNerve, TemperamentOf.Nerve(_world, plain), 4);              // raw: the average man
            Assert.Equal(0.70f, TemperamentOf.Effective(_world, plain, TemperamentTrait.Nerve), 4);       // 0.75 + 0.2 × (0.5 − 0.75)
            Assert.Equal(0.58f, TemperamentOf.Effective(_world, plain, TemperamentTrait.Resolve), 4);     // 0.6 + 0.2 × (0.5 − 0.6)
            Assert.Equal(TemperamentOf.DefaultRiskAcceptance, TemperamentOf.Effective(_world, plain, TemperamentTrait.RiskAcceptance), 4);
        }

        /// <summary>TS-3 — a type stamps only the fields it authors, ± its spread per man: the same man draws the same value every time,
        /// men differ, and every value stays within the spread.</summary>
        [Fact]
        public void CE2269_TheTypeStamp_OnlyAuthoredFields_SpreadDeterministicPerMan()
        {
            var dto = new Fdp.Toolkit.Tkb.Domain.TemperamentDto { Obedience = 0.3f, Nerve = 0.5f };
            var values = new System.Collections.Generic.HashSet<float>();
            for (int i = 0; i < 20; i++)
            {
                var e = _world.CreateEntity();
                TemperamentOf.StampType(_world, e, dto);
                var t = _world.GetComponentRO<CombatTemperament>(e);
                Assert.Equal(0, t.Set);                                                           // nothing authored
                Assert.Equal(CombatTemperament.ObedienceSet | CombatTemperament.NerveSet, t.TypeSet);
                float o = TemperamentOf.Obedience(_world, e);
                Assert.InRange(o, 0.3f - TemperamentOf.DefaultSpread - 1e-4f, 0.3f + TemperamentOf.DefaultSpread + 1e-4f);
                Assert.Equal(TemperamentOf.DefaultRiskAcceptance, TemperamentOf.RiskAcceptance(_world, e));   // not the type's to say
                Assert.Equal(TemperamentOf.DefaultResolve, TemperamentOf.Resolve(_world, e));
                TemperamentOf.StampType(_world, e, dto);                                          // again: the same man, the same draw
                Assert.Equal(o, TemperamentOf.Obedience(_world, e));
                values.Add(o);
            }
            Assert.True(values.Count > 10, $"men are not clones: {values.Count} distinct of 20");

            var flat = _world.CreateEntity();
            TemperamentOf.StampType(_world, flat, new Fdp.Toolkit.Tkb.Domain.TemperamentDto { Obedience = 0.3f, Spread = 0f });
            Assert.Equal(0.3f, TemperamentOf.Obedience(_world, flat));
        }

        /// <summary>TS-3 — an authored value set BEFORE the stamp survives it, and a type that authors nothing adds nothing.</summary>
        [Fact]
        public void CE2269_AnAuthoredValue_SurvivesTheStamp_AndAnEmptyDtoAddsNothing()
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new CombatTemperament { Obedience = 0.95f, Set = CombatTemperament.ObedienceSet });
            TemperamentOf.StampType(_world, e, Fdp.Toolkit.Tkb.Domain.TemperamentDto.Insurgent());
            Assert.Equal(0.95f, TemperamentOf.Obedience(_world, e));

            var bare = _world.CreateEntity();
            TemperamentOf.StampType(_world, bare, new Fdp.Toolkit.Tkb.Domain.TemperamentDto());
            Assert.False(_world.HasComponent<CombatTemperament>(bare));
        }
    }
}
