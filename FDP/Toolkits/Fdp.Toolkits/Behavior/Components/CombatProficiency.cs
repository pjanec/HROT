using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-2273</c> DA-A (DA-1, R-2035) — a soldier's combat PROFICIENCY: what he CAN do, 0..1, beside his temperament (what he
    /// is WILLING to do — never merged). Read through <see cref="ProficiencyOf"/> ONLY. 📄
    /// <c>docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md</c> §6 DA-1/DA-2, §8a.
    /// <para>Three sources, each in its own field with its own <see cref="Set"/> bit, so none overwrites another: <see cref="Authored"/>
    /// (the scenario, on the man), <see cref="Unit"/> (his nearest commander's authored value, written by <c>ProficiencyFillSystem</c>),
    /// <see cref="TypeDefault"/> (his TKB type's, written at spawn). The precedence lives in the reader. 0 is a legitimate value, so
    /// "unset" is a clear bit (the <see cref="CombatTemperament"/> rule).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CombatProficiency)]
    public struct CombatProficiency
    {
        /// <summary>Authored for this man (or, on a unit entity, for every man under it).</summary>
        public float Authored;
        /// <summary>His nearest commander's <see cref="Authored"/> — written by the fill system, never authored.</summary>
        public float Unit;
        /// <summary>His TKB type's default — written at spawn.</summary>
        public float TypeDefault;
        /// <summary><see cref="AuthoredSet"/> | <see cref="UnitSet"/> | <see cref="TypeDefaultSet"/>.</summary>
        public byte Set;

        public const byte AuthoredSet = 1, UnitSet = 2, TypeDefaultSet = 4;
    }

    /// <summary>⭐ <c>CE-2273</c> — the five named presets of proficiency (DA-1).</summary>
    public enum ProficiencyTier : byte
    {
        Untrained = 0,
        Militia = 1,
        Regular = 2,
        Veteran = 3,
        Elite = 4,
    }

    /// <summary>⭐ <c>CE-2273</c> — how a man fires a burst (DA-2): Untrained empties the magazine, Militia long bursts, Regular+ aimed.</summary>
    public enum BurstDiscipline : byte
    {
        Controlled = 0,
        LongBursts = 1,
        EmptyTheMagazine = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2273</c> DA-A (DA-2) — reading a man's proficiency with its sources and defaults applied, and what it changes in him, in
    /// ONE place. ⭐ Every factor is EXACTLY neutral (1.0 / 0) at Regular and with no component, so no existing rail or demo moves.
    /// Reads the man's OWN component only — the hit model reads it on whichever node resolves the shot (backend BR-6).
    /// </summary>
    public static class ProficiencyOf
    {
        public const float Untrained = 0.1f, Militia = 0.35f, Regular = 0.6f, Veteran = 0.8f, Elite = 0.95f;

        private static readonly float[] s_presets = { Untrained, Militia, Regular, Veteran, Elite };
        private static readonly float[] s_spread = { 2.5f, 1.6f, 1.0f, 0.8f, 0.65f };
        private static readonly float[] s_expose = { 1.8f, 1.4f, 1.0f, 0.85f, 0.7f };
        private static readonly float[] s_delay  = { 1.2f, 0.6f, 0f, 0f, 0f };

        /// <summary>The man's proficiency: authored, else his unit's, else his type's, else Regular.</summary>
        public static float Value(ISimulationView view, Entity unit)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<CombatProficiency>()) return Regular;
            if (!view.HasComponent<CombatProficiency>(unit)) return Regular;
            ref readonly var p = ref view.GetComponentRO<CombatProficiency>(unit);
            if ((p.Set & CombatProficiency.AuthoredSet) != 0) return Clamp01(p.Authored);
            if ((p.Set & CombatProficiency.UnitSet) != 0) return Clamp01(p.Unit);
            if ((p.Set & CombatProficiency.TypeDefaultSet) != 0) return Clamp01(p.TypeDefault);
            return Regular;
        }

        /// <summary>The preset nearest to <paramref name="value"/> (ties go up).</summary>
        public static ProficiencyTier TierOf(float value)
        {
            int best = 0;
            for (int i = 1; i < s_presets.Length; i++)
                if (System.MathF.Abs(value - s_presets[i]) <= System.MathF.Abs(value - s_presets[best])) best = i;
            return (ProficiencyTier)best;
        }

        public static ProficiencyTier Tier(ISimulationView view, Entity unit) => TierOf(Value(view, unit));

        /// <summary>⭐ <c>CE-2275</c> D-2b (DA-16: "the commander's tier is his unit's median man") — the tier of the median value over
        /// <paramref name="men"/> (the upper middle of an even count); Regular with none.</summary>
        public static ProficiencyTier MedianTier(ISimulationView view, System.ReadOnlySpan<Entity> men)
        {
            if (men.Length == 0) return ProficiencyTier.Regular;
            System.Span<float> values = men.Length <= 32 ? stackalloc float[men.Length] : new float[men.Length];
            for (int i = 0; i < men.Length; i++) values[i] = Value(view, men[i]);
            values.Sort();
            return TierOf(values[men.Length / 2]);
        }

        /// <summary>The preset value of <paramref name="tier"/>.</summary>
        public static float ValueOf(ProficiencyTier tier) => s_presets[(int)tier];

        /// <summary>× on the shooter's spread (DA-2): Untrained 2.5 · Militia 1.6 · Regular 1.0 · Veteran 0.8 · Elite 0.65, linear between.</summary>
        public static float SpreadFactor(ISimulationView view, Entity shooter) => Lerp(s_spread, Value(view, shooter));

        /// <summary>× on the time he stays exposed at a firing post (DA-2): 1.8 … 1.0 at Regular … 0.7.</summary>
        public static float ExposeFactor(ISimulationView view, Entity unit) => Lerp(s_expose, Value(view, unit));

        /// <summary>Seconds before his first shot at a NEW contact (DA-2): +1.2 Untrained … 0 from Regular up.</summary>
        public static float ReactionDelaySeconds(ISimulationView view, Entity unit) => Lerp(s_delay, Value(view, unit));

        /// <summary>How he fires a burst (DA-2).</summary>
        public static BurstDiscipline Bursts(ISimulationView view, Entity unit)
            => Tier(view, unit) switch
            {
                ProficiencyTier.Untrained => BurstDiscipline.EmptyTheMagazine,
                ProficiencyTier.Militia => BurstDiscipline.LongBursts,
                _ => BurstDiscipline.Controlled,
            };

        /// <summary>Regular and up keep cover discipline (DA-2, DA-6): set back from the sill, change window after N exposures.</summary>
        public static bool KeepsCoverDiscipline(ISimulationView view, Entity unit) => Tier(view, unit) >= ProficiencyTier.Regular;

        /// <summary>Piecewise-linear over the five presets, flat beyond the ends.</summary>
        public static float Lerp(float[] atPresets, float value)
        {
            if (value <= s_presets[0]) return atPresets[0];
            for (int i = 1; i < s_presets.Length; i++)
                if (value <= s_presets[i])
                {
                    if (value == s_presets[i]) return atPresets[i];   // exact at a preset: Regular is bit-identical 1.0 / 0
                    float t = (value - s_presets[i - 1]) / (s_presets[i] - s_presets[i - 1]);
                    return atPresets[i - 1] + t * (atPresets[i] - atPresets[i - 1]);
                }
            return atPresets[^1];
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
