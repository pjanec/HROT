using System;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-2</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §23.5; RV-7, R-8311) — the seam through which a STOCHASTIC unit's
    /// SOP is the (class, resolution) table's <c>Sop</c> row, not the template's physical default (<c>BasicSquadSop</c>, whose plays need a roster).
    /// <see cref="BehaviorIngressSystem"/> — the ONE place every SOP is started — asks <see cref="Resolver"/> when a TEMPLATE-DEFAULT SOP
    /// (<see cref="BehaviorOrigin.Sop"/>, from <c>BehaviorTkbTranslator</c>) is assigned to a unit whose resolution is stochastic; a name it answers (and that is registered)
    /// replaces the default. ⛔ An explicit assignment (an author, a commander, a saved one) is never replaced.
    /// <para>⚠ The silent-default pattern: the table lives in Hrot.Core, which Fdp.Toolkits cannot see, so the HOST sets <see cref="Resolver"/> — null means the template's SOP
    /// stands. Hrot's <c>CgfLogicPack</c> (every brain host builds one) sets it; a forwarding rail pins that.</para>
    /// </summary>
    public static class StochasticSopRouting
    {
        /// <summary>(world, unit, isBehaviour) → the SOP behaviour for the unit at its resolution, or null for none. Set by the host that holds the routing table.</summary>
        public static Func<EntityRepository, Entity, Func<string, bool>, string?>? Resolver { get; set; }
    }
}
