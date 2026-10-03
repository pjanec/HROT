using CarKinem.Core;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐ Which navmesh layer a path request plans on — the ONE rule both of the bridge's request sites use.
    /// <para>📄 <c>docs/designs/navig-2/Navigation_Design_v2_0.md</c> §NavLayerMask: each layer is a separate bake (infantry
    /// 0.3 m radius, vehicle 1.5 m), callers pass exactly one bit, and the default is the entity's
    /// <see cref="NavAgentProfile.PreferredLayerMask"/>.</para>
    /// <para>🔴 Found by the 2026-10-03 live run on <c>test-town</c>: nothing wrote a layer, so every request went out as
    /// <c>0</c> ⇒ "all layers", and <c>DotRecastNavmeshProvider.PlanPath</c> takes the FIRST layer that finds a path —
    /// the infantry mesh. A tank's route then hugged a building at infantry clearance (0.7 m from the wall for a 3.6 m
    /// hull). ⭐ No production writer of <see cref="NavAgentProfile"/> exists yet, so the last step falls back on the entity
    /// KIND: a <see cref="VehicleState"/> entity is a vehicle.</para>
    /// </summary>
    public static class NavLayerSelection
    {
        /// <summary>
        /// ① an explicit mask on the order; ② the entity's <see cref="NavAgentProfile.PreferredLayerMask"/>; ③ Vehicle when
        /// it has <see cref="VehicleState"/>, else Infantry.
        /// </summary>
        public static NavLayerMask For(EntityRepository repo, Entity entity, uint explicitMask)
        {
            if (explicitMask != 0) return (NavLayerMask)explicitMask;
            if (repo.HasComponent<NavAgentProfile>(entity))
            {
                uint preferred = repo.GetComponent<NavAgentProfile>(entity).PreferredLayerMask;
                if (preferred != 0) return (NavLayerMask)preferred;
            }
            return repo.HasComponent<VehicleState>(entity) ? NavLayerMask.Vehicle : NavLayerMask.Infantry;
        }
    }
}
