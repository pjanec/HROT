using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-2270</c> TS-B (R-2034 TS-4, behaviors) — a combatant's live STRESS 0–1: what fire, a friend falling, being outnumbered or
    /// alone have done to him, calming with time (faster in cover, faster near his leader). Kept by <c>StressSystem</c> on the brain
    /// node; read by the damping inputs (TS-5) and the break (TS-6). Brain-local (NoScenario, never sent — TS-8), like the civilians'
    /// own state. ⛔ Combatants only (TS-7): a civilian keeps <c>CivilianState</c>; a vehicle has none.
    /// 📄 docs/DESIGN_Temperament_And_Stress.md §4 TS-4.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Stress)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Stress
    {
        /// <summary>0 calm … 1 the most he can bear.</summary>
        public float Level;
        /// <summary>Sim time of his last stimulus (a round, a hit, a friend down).</summary>
        public double LastStimulusAt;
        /// <summary>Sim time of the last pass (the continuous terms run over the time since).</summary>
        public double LastStepAt;
        /// <summary>⭐ TS-6 (<c>CE-2272</c>) — 1 while he is broken.</summary>
        public byte Broken;
        /// <summary>⭐ TS-6 — sim time his level fell under the recovery mark (0 = not under it).</summary>
        public double CalmSince;
    }

    /// <summary>
    /// ⭐ <c>CE-1309</c> (docs/DESIGN_People_Display.md §7e U20; moved from behaviors' <c>StressModel</c>, which forwards here) — the READS of
    /// <see cref="Stress"/>: his level, whether he is broken, his breaking point and how nervous he is. One copy for the brain and for the
    /// 3-D card that draws them. 📄 docs/DESIGN_Temperament_And_Stress.md TS-6, AQ103 H.
    /// </summary>
    public static class Stresses
    {
        /// <summary>The lowest level a break needs, whatever his nerve: a man with none still breaks only once something has happened.</summary>
        public const float BreakFloor = 0.05f;
        /// <summary>⭐ <c>CE-2365</c> / AQ103 H — he is NERVOUS from this share of his break level up (the band the fidget and the head share).</summary>
        public const float NervousFrom = 0.4f;

        /// <summary>His level now; 0 with no <see cref="Stress"/>.</summary>
        public static float LevelOf(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity e)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Stress>()) return 0f;
            return view.HasComponent<Stress>(e) ? view.GetComponentRO<Stress>(e).Level : 0f;
        }

        /// <summary>Is he broken now?</summary>
        public static bool IsBroken(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity e)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Stress>()) return false;
            return view.HasComponent<Stress>(e) && view.GetComponentRO<Stress>(e).Broken != 0;
        }

        /// <summary>The level at which he breaks: his effective nerve (TS-2), never under <see cref="BreakFloor"/>.</summary>
        public static float BreakLevel(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity e)
            => System.MathF.Max(TemperamentOf.Effective(view, e, TemperamentTrait.Nerve), BreakFloor);

        /// <summary>⭐ <c>CE-2365</c> / AQ103 H — how nervous he is: 0 below <see cref="NervousFrom"/> × his break level, rising to 1 at it.
        /// 0 with no <see cref="Stress"/> (a civilian, a vehicle, a calm man).</summary>
        public static float Nervousness(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity e)
        {
            float level = LevelOf(view, e);
            if (level <= 0f) return 0f;
            float b = BreakLevel(view, e);
            return System.Math.Clamp((level - NervousFrom * b) / ((1f - NervousFrom) * b), 0f, 1f);
        }
    }
}
