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
        /// <summary>Which fields were authored: <see cref="RiskAcceptanceSet"/> | <see cref="ObedienceSet"/> | <see cref="NerveSet"/> |
        /// <see cref="ResolveSet"/>. A clear bit ⇒ the type's value, else the default.</summary>
        public byte Set;
        /// <summary>⭐ <c>CE-2269</c> TS-1 — how much danger before he goes to ground or breaks (0 = none, 1 = iron).</summary>
        public float Nerve;
        /// <summary>⭐ <c>CE-2269</c> TS-1 — how unwilling he is to give up (0 = surrenders at once, 1 = fanatic).</summary>
        public float Resolve;

        /// <summary>⭐ <c>CE-2269</c> TS-3 — his TKB type's values (with his own seeded spread), stamped at creation, in their OWN fields
        /// with their own <see cref="TypeSet"/> bits (the <see cref="CombatProficiency"/> rule) — so an authored value set before or after
        /// wins, the scenario's inject never wipes them, and a save never writes them into the file.</summary>
        public float TypeRiskAcceptance, TypeObedience, TypeNerve, TypeResolve;
        /// <summary>Which type fields were stamped — the same bits as <see cref="Set"/>.</summary>
        public byte TypeSet;

        public const byte RiskAcceptanceSet = 1, ObedienceSet = 2, NerveSet = 4, ResolveSet = 8;
    }

    /// <summary>⭐ <c>CE-2269</c> TS-2 — the traits obedience tempers (<see cref="TemperamentOf.Effective"/>).</summary>
    public enum TemperamentTrait : byte
    {
        RiskAcceptance = 0,
        Nerve = 1,
        Resolve = 2,
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

        /// <summary>⭐ <c>CE-2269</c> TS-1 — an ordinary man's nerve and resolve: neutral.</summary>
        public const float DefaultNerve = 0.5f, DefaultResolve = 0.5f;

        /// <summary>The unit's bravery: authored, else his type's, else the default.</summary>
        public static float RiskAcceptance(ISimulationView view, Entity unit)
            => Read(view, unit, CombatTemperament.RiskAcceptanceSet, DefaultRiskAcceptance);

        /// <summary>The unit's obedience: authored, else his type's, else the default.</summary>
        public static float Obedience(ISimulationView view, Entity unit)
            => Read(view, unit, CombatTemperament.ObedienceSet, DefaultObedience);

        /// <summary>⭐ <c>CE-2269</c> — the unit's nerve, RAW (no obedience): authored, else his type's, else the default.</summary>
        public static float Nerve(ISimulationView view, Entity unit)
            => Read(view, unit, CombatTemperament.NerveSet, DefaultNerve);

        /// <summary>⭐ <c>CE-2269</c> — the unit's resolve, RAW (no obedience): authored, else his type's, else the default.</summary>
        public static float Resolve(ISimulationView view, Entity unit)
            => Read(view, unit, CombatTemperament.ResolveSet, DefaultResolve);

        /// <summary>⭐ <c>CE-2269</c> TS-2 (R-2076) — the TRAINED standard discipline holds a man to: steadier and firmer than the average man
        /// (<see cref="DefaultNerve"/> / <see cref="DefaultResolve"/>), bravery as the average (so TS-5's damping stays neutral at 1.0).</summary>
        public const float TrainedNerve = 0.75f, TrainedResolve = 0.6f, TrainedRiskAcceptance = DefaultRiskAcceptance;

        /// <summary>
        /// ⭐⭐ <c>CE-2269</c> TS-2 (R-2076) — a trait as obedience TEMPERS it: <c>trained + (1 − Obedience) × (value − trained)</c> — a
        /// disciplined man acts near the TRAINED standard whatever his character (drill steadies him), an undisciplined one acts it out.
        /// Read by the stress, the break and the damping inputs. ⛔ NOT by AQ89's <see cref="Willingness"/> / <see cref="Willing"/>, which
        /// stay raw: they already apply obedience their own way, and blending under them would drop an authored 0.8 to 0.56, under the 0.6
        /// threshold.
        /// </summary>
        public static float Effective(ISimulationView view, Entity unit, TemperamentTrait trait)
        {
            float d = trait switch { TemperamentTrait.Nerve => TrainedNerve, TemperamentTrait.Resolve => TrainedResolve, _ => TrainedRiskAcceptance };
            float v = trait switch { TemperamentTrait.Nerve => Nerve(view, unit), TemperamentTrait.Resolve => Resolve(view, unit), _ => RiskAcceptance(view, unit) };
            return v + Obedience(view, unit) * (d - v);   // ≡ trained + (1 − O)(v − trained); this form gives an undisciplined man EXACTLY himself
        }

        /// <summary>⭐ <c>CE-2269</c> TS-3 — the default spread a type's value gets per man (±).</summary>
        public const float DefaultSpread = 0.15f;

        /// <summary>
        /// ⭐ <c>CE-2269</c> TS-3 — stamps a TKB type's temperament onto <paramref name="entity"/>: only the fields the type authors, each
        /// ± <see cref="Fdp.Toolkit.Tkb.Domain.TemperamentDto.Spread"/> from the man's own seeded draw (the same every run), into the
        /// TYPE fields only — an authored value, before or after, wins.
        /// </summary>
        public static void StampType(EntityRepository repo, Entity entity, Fdp.Toolkit.Tkb.Domain.TemperamentDto dto)
        {
            if (!repo.IsComponentTypeRegistered<CombatTemperament>()) return;
            var t = repo.HasComponent<CombatTemperament>(entity) ? repo.GetComponentRO<CombatTemperament>(entity) : new CombatTemperament();
            byte before = t.TypeSet;
            if (dto.RiskAcceptance is { } r) { t.TypeRiskAcceptance = Spread(entity, 1, r, dto.Spread); t.TypeSet |= CombatTemperament.RiskAcceptanceSet; }
            if (dto.Obedience is { } o) { t.TypeObedience = Spread(entity, 2, o, dto.Spread); t.TypeSet |= CombatTemperament.ObedienceSet; }
            if (dto.Nerve is { } n) { t.TypeNerve = Spread(entity, 3, n, dto.Spread); t.TypeSet |= CombatTemperament.NerveSet; }
            if (dto.Resolve is { } v) { t.TypeResolve = Spread(entity, 4, v, dto.Spread); t.TypeSet |= CombatTemperament.ResolveSet; }
            if (t.TypeSet == before && !repo.HasComponent<CombatTemperament>(entity)) return;   // a dto authoring nothing adds nothing
            if (repo.HasComponent<CombatTemperament>(entity)) repo.SetComponent(entity, t);
            else repo.AddComponent(entity, t);
        }

        private static float Spread(Entity e, int trait, float value, float spread)
        {
            float u = Fdp.Toolkit.Determinism.SimRng.FromSim(e.Index, SpreadSalt + trait, 0f).NextSingle();
            return Clamp01(value + (2f * u - 1f) * spread);
        }

        private const int SpreadSalt = 0x2269 << 4;

        private static float Read(ISimulationView view, Entity unit, byte bit, float dflt)
        {
            if (!Has(view, unit, out var t)) return dflt;
            if ((t.Set & bit) != 0) return Clamp01(Pick(bit, t.RiskAcceptance, t.Obedience, t.Nerve, t.Resolve));
            if ((t.TypeSet & bit) != 0) return Clamp01(Pick(bit, t.TypeRiskAcceptance, t.TypeObedience, t.TypeNerve, t.TypeResolve));
            return dflt;
        }

        private static float Pick(byte bit, float risk, float obey, float nerve, float resolve) => bit switch
        {
            CombatTemperament.RiskAcceptanceSet => risk,
            CombatTemperament.ObedienceSet => obey,
            CombatTemperament.NerveSet => nerve,
            _ => resolve,
        };

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
