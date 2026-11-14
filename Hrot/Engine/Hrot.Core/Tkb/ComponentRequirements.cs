using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-4350</c> (docs/DESIGN_Orders_Params_Entity_Classes.md §14) — THE one "does this entity carry every required component"
    /// AND: the Details tabs' <c>Requires</c> (<c>DetailsViewRegistry</c>) and a behaviour's <c>[Behavior(Requires = …)]</c>
    /// (<see cref="BehaviorFit"/>) both ask it. ⚠ Lives in <c>Hrot.Core</c> (the design drew it in <c>Hrot.Common</c>): <see cref="BehaviorFit"/>
    /// is in Core and Core cannot see Common.
    /// </summary>
    public static class ComponentRequirements
    {
        /// <summary>True when <paramref name="probe"/> says <paramref name="entity"/> carries every type in <paramref name="requires"/>
        /// (an empty list is met). No allocation: index loops over the list.</summary>
        public static bool Met(Func<Entity, Type, bool> probe, Entity entity, IReadOnlyList<Type> requires)
        {
            for (int r = 0; r < requires.Count; r++)
                if (!probe(entity, requires[r])) return false;
            return true;
        }
    }
}
