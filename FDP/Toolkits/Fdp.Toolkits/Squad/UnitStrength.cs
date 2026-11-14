using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Squad.Command;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐⭐ <c>CE-4613</c> (R-4251 R-D4; 📄 docs/designs/aggregate-units/DESIGN_Aggregate_Squad.md §18) — a unit's strength in MEN, at any echelon: <see cref="Present"/>
    /// (the men in the fight — a float: a FusionSquad's pool loses men by fractions later) and <see cref="Authorized"/> (the men it is made of). A soldier is
    /// <see cref="MenOf"/> men (1 today); a squad, a platoon, a company is the SUM over its roster, recursively (≤ <see cref="CombatEffective.MaxDepth"/>).
    /// ⭐ ONE seam: the SITREP colour, the squad's own strength in a fight and the objectives' census read it, so a unit that is not one man per body
    /// plugs in at <see cref="MenOf"/> and nowhere else. Allocation-free.
    /// </summary>
    public readonly struct UnitStrength
    {
        public readonly float Present;
        public readonly int Authorized;
        /// <summary>The roster is known (a unit with no <see cref="UnitRoster"/> yet has no strength to report).</summary>
        public readonly bool Known;

        public UnitStrength(float present, int authorized, bool known) { Present = present; Authorized = authorized; Known = known; }

        /// <summary>Present ÷ Authorized (0 when nothing is authorized).</summary>
        public float Fraction => Authorized > 0 ? Present / Authorized : 0f;

        /// <summary>
        /// The strength colour (CB-37): GREEN ≥ <see cref="StaffComposerSystem.GreenFraction"/> of its men present, AMBER ≥ <see cref="StaffComposerSystem.AmberFraction"/>,
        /// RED while still in the fight, BLACK once it is not (or emptied); unknown without a roster. ⭐ For a squad of soldiers exactly the old members-fighting
        /// share; above it, men — not subunits (R-D4).
        /// </summary>
        public static StatusColour ColourOf(ISimulationView view, Entity unit)
        {
            var s = Of(view, unit);
            if (!s.Known) return StatusColour.ColourUnknown;
            if (s.Authorized == 0 || !CombatEffective.IsInTheFight(view, unit)) return StatusColour.Black;
            float f = s.Fraction;
            return f >= StaffComposerSystem.GreenFraction ? StatusColour.Green : f >= StaffComposerSystem.AmberFraction ? StatusColour.Amber : StatusColour.Red;
        }

        /// <summary><paramref name="unit"/>'s strength: a soldier's own (<see cref="MenOf"/> present when in the fight), a unit's summed over its roster.
        /// <paramref name="standing"/>: count only men still standing (in the fight and not broken, CE-7015).</summary>
        public static UnitStrength Of(ISimulationView view, Entity unit, bool standing = false)
        {
            if (unit.IsNull || !view.IsAlive(unit)) return default;
            if (!CombatEffective.IsUnit(view, unit))
            {
                float men = MenOf(view, unit);
                bool fights = standing ? CombatEffective.IsStanding(view, unit) : CombatEffective.IsInTheFight(view, unit);
                return new UnitStrength(fights ? men : 0f, (int)MathF.Ceiling(men), true);
            }
            if (!view.HasComponent<UnitRoster>(unit)) return default;
            float present = 0f; int authorized = 0;
            Sum(view, unit, 0, standing, ref present, ref authorized);
            return new UnitStrength(present, authorized, true);
        }

        /// <summary>How many men one BODY is: 1 for a soldier. ⭐ The one place a body that stands for several men (a FusionSquad's soldier, R-D8) answers.</summary>
        public static float MenOf(ISimulationView view, Entity body) => 1f;

        /// <summary><see cref="MenOf"/> as whole men (a census counts people).</summary>
        public static int WholeMenOf(ISimulationView view, Entity body) => (int)MenOf(view, body);

        private static void Sum(ISimulationView view, Entity unit, int depth, bool standing, ref float present, ref int authorized)
        {
            ref readonly var roster = ref view.GetComponentRO<UnitRoster>(unit);
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !view.IsAlive(m)) { authorized += 1; continue; }   // a slot whose man is gone still counts against the authorized strength
                if (CombatEffective.IsUnit(view, m))
                {
                    if (!view.HasComponent<UnitRoster>(m)) { authorized += 1; present += 1f; continue; }   // composition unknown: in the fight, as CombatEffective takes it
                    if (depth + 1 < CombatEffective.MaxDepth) Sum(view, m, depth + 1, standing, ref present, ref authorized);
                    continue;
                }
                float men = MenOf(view, m);
                authorized += (int)MathF.Ceiling(men);
                if (standing ? CombatEffective.IsStanding(view, m) : CombatEffective.IsInTheFight(view, m)) present += men;
            }
        }
    }
}
