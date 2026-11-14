using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-7006</c> / <c>CE-7028</c> — the one answer to "does this unit's KNOWN type carry a weapon?", read from its TKB template's weapon
    /// suite (static data: no other entity's component is read). An entity with no known type is neither — the caller keeps its default.
    /// </summary>
    public static class TemplateArms
    {
        /// <summary>The unit's known type carries at least one weapon mount.</summary>
        public static bool KnownArmed(EntityRepository world, Entity unit)
            => Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(world, unit, out var t)
               && t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>() is { Mounts.Count: > 0 };

        /// <summary>The unit's known type carries no weapon mount (the recon quadcopter, a truck).</summary>
        public static bool KnownUnarmed(EntityRepository world, Entity unit)
            => Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(world, unit, out var t)
               && t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>() is not { Mounts.Count: > 0 };
    }
}
