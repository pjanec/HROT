using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-0 ①</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §23.7; RV-5) — the ONE predicate and the ONE
    /// slot walk the four strength readers (<c>UnitStrength</c>, <c>CombatEffective</c>, the staff's <c>AmmoOf</c>, the objectives' census) branch on
    /// BEFORE their roster walk: a STOCHASTIC unit has no roster and no bodies, so its men are its <see cref="UnitSlotTable"/>'s slots. Allocation-free
    /// (index loops over the fixed inline slots; the table is read by reference, never copied). ⛔ Present is never stored (SV-11): it is derived here, every call.
    /// <para>Lives beside the contract (<c>Fdp.Toolkit.Aggregate</c>), not in <c>Squad</c>: it reads only the contract's components, and the Squad readers
    /// depend on this namespace — one direction.</para>
    /// </summary>
    public static class AggregateUnits
    {
        /// <summary>⭐ The stress level at which a SLOT is broken (so not standing): the physical rule's own break level for a man of the
        /// default (trained) nerve — <see cref="Stresses.BreakLevel"/> reads <c>max(effective Nerve, Stresses.BreakFloor)</c>, and a slot has no
        /// per-man nerve, so it takes <see cref="TemperamentOf.TrainedNerve"/>. ⛔ Not a new number.</summary>
        public const float BrokenAt = TemperamentOf.TrainedNerve > Stresses.BreakFloor ? TemperamentOf.TrainedNerve : Stresses.BreakFloor;

        /// <summary>The unit is simulated STOCHASTICALLY: it has an <see cref="AggregateUnit"/> whose <see cref="AggregateUnit.Effective"/> resolution is
        /// <see cref="AggregateResolution.Stochastic"/>. ⛔ Physical, Fusion and unset (= physical) are false: they keep their bodies.</summary>
        public static bool IsStochastic(ISimulationView view, Entity e)
        {
            if (e.IsNull) return false;
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<AggregateUnit>()) return false;
            return view.HasComponent<AggregateUnit>(e) && view.GetComponentRO<AggregateUnit>(e).Effective == AggregateResolution.Stochastic;
        }

        /// <summary>The unit's slots, in men: <paramref name="live"/> = slots up (<see cref="UnitSlotTable.IsUp"/>) — or, with <paramref name="standing"/>,
        /// up AND stress below <see cref="BrokenAt"/> —, <paramref name="total"/> = the authorized slots (<see cref="UnitSlotTable.Authorized"/>),
        /// <paramref name="ammo"/> = the rounds the LIVE slots carry. False when the unit has no table (nothing to judge by).</summary>
        public static bool Slots(ISimulationView view, Entity e, bool standing, out int live, out int total, out long ammo)
        {
            live = total = 0; ammo = 0;
            if (e.IsNull) return false;
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<UnitSlotTable>()) return false;
            if (!view.HasComponent<UnitSlotTable>(e)) return false;
            ref readonly var t = ref view.GetComponentRO<UnitSlotTable>(e);
            for (int i = 0; i < t.Count; i++)
            {
                if (t.Slots[i].Role == SlotRole.Empty) continue;
                total++;
                if (!t.IsUp(i)) continue;
                if (standing && (float)t.Slots[i].Stress >= BrokenAt) continue;
                live++;
                ammo += t.Slots[i].Ammo;
            }
            return true;
        }

        /// <summary>A stochastic unit's men in the fight / standing (<see cref="Slots"/>); 0 for no table.</summary>
        public static int LiveMen(ISimulationView view, Entity e, bool standing)
        {
            Slots(view, e, standing, out int live, out _, out _);
            return live;
        }

        /// <summary>⭐ A stochastic unit is in the fight (standing) by the SAME rule as a roster (<c>CombatEffective.MinEffective</c>): at least
        /// <c>min(4, ⌈n/2⌉)</c> of its n slots live; no table ⇒ composition unknown ⇒ in the fight (as a roster-less unit is).</summary>
        public static bool InTheFight(ISimulationView view, Entity e, bool standing)
        {
            if (!Slots(view, e, standing, out int live, out int total, out _)) return true;
            return total > 0 && live >= Fdp.Toolkit.Squad.CombatEffective.MinEffective(total);
        }
    }
}
