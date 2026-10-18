using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3246</c> (docs/designs/brain-split/BS-1-DESIGN.md §6.4a) — THE rule for where a hit is applied: on the node that claims the
    /// entity's <see cref="Health"/> (<c>CE-523</c>: the claim follows the Brain group's grant), or anywhere when the entity has no
    /// <see cref="NetworkAuthority"/> (no network ⇒ this node owns everything). Read by <c>HealthApplicationSystem</c>, which applies a hit
    /// here, and by the hit egress, which sends exactly the hits this node does NOT apply — so a hit is applied once, by its owner.
    /// </summary>
    public static class HealthOwnership
    {
        /// <summary>True when a hit on <paramref name="entity"/> is applied on this node; false when he is gone, has no
        /// <see cref="Health"/> here, or another node claims it.</summary>
        public static bool AppliesHere(EntityRepository repo, Entity entity)
        {
            if (!repo.IsAlive(entity) || !repo.IsComponentTypeRegistered<Health>() || !repo.HasComponent<Health>(entity)) return false;
            return !repo.HasComponent<NetworkAuthority>(entity) || repo.HasAuthority<Health>(entity);
        }
    }
}
