using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-3089</c> (G7) — which of a unit's weapons to fire at a target: the starter <see cref="WeaponSelectionDecision"/>
    /// ranks the unit's MOUNTS (the owner = mount 0, then the mount children) against the target — effectiveness vs its armour
    /// (CE-3071's one model), range band, ammunition, readiness. 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §12 W3.
    /// <para>Called per shot by <see cref="Executors.AimAndFireExecutor"/> when its params say <see cref="Executors.AimAndFireParams.MountAuto"/>.</para>
    /// </summary>
    public static class WeaponChoice
    {
        [System.ThreadStatic] private static UtilityScorer? _scorer;

        /// <summary>
        /// The mount index to fire at <paramref name="target"/>, and the entity holding that mount's <see cref="WeaponState"/>
        /// (<paramref name="owner"/> for mount 0, a mount child otherwise). Falls back to mount 0 on the owner when the decision
        /// is not registered, the unit has no ranked mount, or every mount scores 0 (out of range / no ammo everywhere).
        /// </summary>
        public static int Choose(EntityRepository repo, Entity owner, Entity target, out Entity mount)
        {
            mount = owner;
            _scorer ??= new UtilityScorer(UtilityDecisionCatalog.Shared);
            if (!_scorer.TopCandidate(repo, owner, WeaponSelectionDecision.Id, target, 0, out var top, out float score)
                || score <= 0f || top.IsNull || !repo.IsAlive(top) || top.Equals(owner))
                return 0;
            if (!repo.IsComponentTypeRegistered<WeaponMountInfo>() || !repo.HasComponent<WeaponMountInfo>(top)
                || !repo.HasComponent<WeaponState>(top))
                return 0;
            mount = top;
            return repo.GetComponentRO<WeaponMountInfo>(top).MountIndex;
        }
    }
}
