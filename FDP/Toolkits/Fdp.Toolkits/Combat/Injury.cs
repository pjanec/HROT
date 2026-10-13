using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-2155</c> (R-263 D1) — the four human states. Health moves one way (nothing heals), so the states do too.</summary>
    public enum InjuryState : byte
    {
        Ok = 0,
        Wounded = 1,
        Incapacitated = 2,
        Dead = 3,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2155</c> (R-263 D1 / R-2011, docs/DESIGN_Injury_States.md) — the injury state, DERIVED: the replicated <see cref="Health"/>
    /// against the type's <see cref="InjuryThresholds"/>. Never stored and never sent, so every node reads the same state (I1). An entity
    /// without thresholds is <see cref="InjuryState.Ok"/> until dead; one without <see cref="Health"/> is always Ok.
    /// </summary>
    public static class Injury
    {
        /// <summary>The state of <paramref name="entity"/> as this node sees its health.</summary>
        public static InjuryState StateOf(ISimulationView view, Entity entity)
        {
            if (!view.HasComponent<Health>(entity)) return InjuryState.Ok;
            return StateOf(view, entity, in view.GetComponentRO<Health>(entity));
        }

        /// <summary>The state <paramref name="health"/> means for <paramref name="entity"/>'s type.</summary>
        public static InjuryState StateOf(ISimulationView view, Entity entity, in Health health)
        {
            if (health.Current <= 0f) return InjuryState.Dead;
            if (health.Max <= 0f || !view.HasComponent<InjuryThresholds>(entity)) return InjuryState.Ok;
            ref readonly var t = ref view.GetComponentRO<InjuryThresholds>(entity);
            float fraction = health.Current / health.Max;
            if (fraction < t.IncapacitatedBelow) return InjuryState.Incapacitated;
            if (fraction < t.WoundedBelow) return InjuryState.Wounded;
            return InjuryState.Ok;
        }

        /// <summary>Down but alive: cannot move or fire, bleeding out, may be carried (R-2009 C3).</summary>
        public static bool IsIncapacitated(ISimulationView view, Entity entity)
            => view.IsAlive(entity) && StateOf(view, entity) == InjuryState.Incapacitated;

        /// <summary>
        /// ⭐ I4 — still a fighter: the entity exists and is neither dead nor incapacitated. The test every "is it a threat / a target"
        /// reader asks. ⚠ Not <see cref="CombatLife.IsAlive"/>: an incapacitated soldier is alive (he can be carried, he bleeds) but out of
        /// the fight.
        /// </summary>
        public static bool IsInTheFight(ISimulationView view, Entity entity)
        {
            if (!view.IsAlive(entity)) return false;
            var state = StateOf(view, entity);
            return state == InjuryState.Ok || state == InjuryState.Wounded;
        }
    }
}
