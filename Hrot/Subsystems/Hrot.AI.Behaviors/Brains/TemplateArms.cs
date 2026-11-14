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

        /// <summary>⭐ <c>CE-7027</c> — the rounds the unit's known type carries on all its mounts together (0 = unknown, or none).</summary>
        public static int Rounds(EntityRepository world, Entity unit)
        {
            if (!Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(world, unit, out var t)
                || t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>() is not { } suite) return 0;
            int n = 0;
            for (int i = 0; i < suite.Mounts.Count; i++) n += suite.Mounts[i].InitialAmmunition;
            return n;
        }

        /// <summary>The unit's known type carries no weapon mount (the recon quadcopter, a truck).</summary>
        public static bool KnownUnarmed(EntityRepository world, Entity unit)
            => Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(world, unit, out var t)
               && t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>() is not { Mounts.Count: > 0 };
    }
}
