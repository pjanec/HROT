using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Possession
{
    /// <summary>
    /// ⭐ <c>CE-1205</c> (docs/DESIGN_Entity_Control.md §8m PK-7, R-1007) — THE ONE RULE for whether a station may possess an entity, read by the
    /// map menu (greyed, with the reason) and by <c>EntityOrders.Possess</c> (refused, with the same words — HTTP / MCP too). ⛔ A WEAPON SYSTEM
    /// is manned, never possessed: possessed, it never fired (the operator behaviour drives a body, and a weapon that needs an operator has
    /// no controller without one — Weapon Systems W-D); a man takes it with the use key (Man / Take, §8h U-5, §8m PK-2).
    /// </summary>
    public static class Possessability
    {
        /// <summary>Why a weapon system cannot be possessed, in the menu's and the refusal's words.</summary>
        public const string WeaponIsManned = "a weapon is manned, not possessed - possess a man and use F (Man / Take)";

        /// <summary>Why <paramref name="entity"/> cannot be possessed, or null when it can (as far as this rule goes).</summary>
        public static string? Refusal(ISimulationView view, Entity entity)
            => view is EntityRepository repo && repo.IsAlive(entity) && repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemParams>()
               && Embarkation.IsWeaponSystem(repo, entity) ? WeaponIsManned : null;
    }
}
