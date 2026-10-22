using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ AQ89 B (R-269) — a soldier's combat TEMPERAMENT: how much risk he accepts on his own (bravery) and how far he follows
    /// an order that exposes him (obedience), both 0..1. Read through <see cref="TemperamentOf"/> ONLY — never the raw fields.
    /// 📄 <c>docs/blueprints/Architect_Question_89_Suppress_And_Close.md</c>.
    /// <para>⛔ 🔒 User: <i>"Trait component needs some defaults to never come up unfilled/zeroed as that would ruin the
    /// behavior."</i> ⇒ 0 is a LEGITIMATE value here (a coward), so "unset" cannot be 0 the way <see cref="RoeOf"/> reads it: a field
    /// counts only when its <see cref="Set"/> bit is on. A zero-filled component, or none at all, reads the defaults.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CombatTemperament)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct CombatTemperament
    {
        /// <summary>Bravery: how much exposure he accepts on his own initiative (0 = none, 1 = reckless).</summary>
        public float RiskAcceptance;
        /// <summary>How far he follows a superior's order that exposes him (0 = refuses, 1 = always).</summary>
        public float Obedience;
        /// <summary>Which fields were authored: <see cref="RiskAcceptanceSet"/> | <see cref="ObedienceSet"/>. A clear bit ⇒ the default.</summary>
        public byte Set;

        public const byte RiskAcceptanceSet = 1, ObedienceSet = 2;
    }

    /// <summary>⭐ AQ89 (R-269) — reading a unit's temperament with the defaults applied, in one place.</summary>
    public static class TemperamentOf
    {
        /// <summary>An ordinary soldier's bravery (AQ89 B): below the closing threshold — he does not assault on his own.</summary>
        public const float DefaultRiskAcceptance = 0.5f;
        /// <summary>An ordinary soldier's obedience (AQ89 B): above the closing threshold — he assaults when ordered to.</summary>
        public const float DefaultObedience = 0.8f;
        /// <summary>AQ89 E — willing to close under fire at or above this (deterministic, R-212).</summary>
        public const float WillingThreshold = 0.6f;

        /// <summary>The unit's bravery, the default unless authored.</summary>
        public static float RiskAcceptance(ISimulationView view, Entity unit)
        {
            if (!Has(view, unit, out var t) || (t.Set & CombatTemperament.RiskAcceptanceSet) == 0) return DefaultRiskAcceptance;
            return Clamp01(t.RiskAcceptance);
        }

        /// <summary>The unit's obedience, the default unless authored.</summary>
        public static float Obedience(ISimulationView view, Entity unit)
        {
            if (!Has(view, unit, out var t) || (t.Set & CombatTemperament.ObedienceSet) == 0) return DefaultObedience;
            return Clamp01(t.Obedience);
        }

        /// <summary>
        /// ⭐ AQ89 B/D — how willing the unit is to expose itself now: its <see cref="Obedience"/> when the task it runs was ORDERED
        /// as an assault (<paramref name="orderedAssault"/>), else its own <see cref="RiskAcceptance"/>.
        /// </summary>
        public static float Willingness(ISimulationView view, Entity unit, bool orderedAssault)
            => orderedAssault ? Obedience(view, unit) : RiskAcceptance(view, unit);

        /// <summary>AQ89 E — <see cref="Willingness"/> ≥ <see cref="WillingThreshold"/>.</summary>
        public static bool Willing(ISimulationView view, Entity unit, bool orderedAssault)
            => Willingness(view, unit, orderedAssault) >= WillingThreshold;

        private static bool Has(ISimulationView view, Entity unit, out CombatTemperament t)
        {
            t = default;
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<CombatTemperament>()) return false;
            if (!view.HasComponent<CombatTemperament>(unit)) return false;
            t = view.GetComponentRO<CombatTemperament>(unit);
            return true;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
