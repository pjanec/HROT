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
    }
}
