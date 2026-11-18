using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.BodyParts;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (vehicle appearance VA-7, VA-S1; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §3a) — THE one rule for HOW a vehicle
    /// was destroyed, decided once by the Health owner on the hit that kills it (<c>HealthApplicationSystem</c>): the killing hit's part in the type's
    /// ammunition stowage ⇒ <see cref="DestructionKind.Catastrophic"/>; in its fuel stowage ⇒ <see cref="DestructionKind.BurntOut"/>; any other hit (a part
    /// outside both, or no part: a blast) ⇒ <see cref="DestructionKind.KnockedOut"/>. A person (or a body with no resolved body) ⇒
    /// <see cref="DestructionKind.None"/>. Pure; 0 B.
    /// </summary>
    public static class DestructionKinds
    {
        /// <param name="body">The target's resolved body (<see cref="BodyZones.Of(Fdp.Core.EntityRepository, Fdp.Core.Entity)"/>), null when it has none.</param>
        /// <param name="hasPart">Did the killing hit land on a part?</param>
        /// <param name="part">The part it landed on (when <paramref name="hasPart"/>).</param>
        public static DestructionKind Decide(ResolvedBody? body, bool hasPart, byte part)
        {
            if (body == null || body.Plan == BodyPlan.Person) return DestructionKind.None;
            if (hasPart && part < 32)
            {
                uint bit = 1u << part;
                if ((body.AmmoStowageMask & bit) != 0) return DestructionKind.Catastrophic;
                if ((body.FuelStowageMask & bit) != 0) return DestructionKind.BurntOut;
            }
            return DestructionKind.KnockedOut;
        }
    }
}
