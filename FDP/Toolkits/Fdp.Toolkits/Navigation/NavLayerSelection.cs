using CarKinem.Core;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐ Which navmesh layer a path request plans on — the ONE rule every request site uses (the bridge's two, the Muscle replan,
    /// the EQS context).
    /// <para>📄 <c>docs/designs/navig-2/Navigation_Design_v2_0.md</c> §8.2/§8.3: each layer is a separate bake for one KIND of mover
    /// (<c>RecastNavmeshBaker</c>: infantry 0.3 m radius / 0.4 m step / 60°, vehicle 1.8 m / 0.1 m / 20°), and a request names
    /// exactly one. The entity's TKB data is MAPPED onto one of those fixed layers — layers are never made per entity.</para>
    /// <para>🔒 <c>CE-3112</c> (user, 2026-10-08: <i>"Approved, go with class mapping"</i>): the kind is the TKB's locomotion class,
    /// already on the entity as <see cref="VehicleParams.Class"/> (copied from <c>VehicleParametersDto</c> by the kinematics
    /// translator): <see cref="VehicleClass.Pedestrian"/> ⇒ Infantry, any other class ⇒ Vehicle. ⛔ SUPERSEDED (<c>CE-3025</c>):
    /// "a <see cref="VehicleState"/> entity is a vehicle" — SimHost infantry carries <see cref="VehicleState"/> too (CarKinem moves
    /// it), so every soldier planned on the 1.8 m vehicle mesh and no doorway admitted it (found by the <c>bt-doors</c> live run).
    /// The kind is a CLASS, not a size: a soldier opens doors and climbs stairs, a vehicle does neither, whatever its width.</para>
    /// </summary>
    public static class NavLayerSelection
    {
        /// <summary>
        /// ① an explicit mask on the order (the hook for a runtime change — a prone crawl, a cave); ② the entity's
        /// <see cref="NavAgentProfile.PreferredLayerMask"/> when something set one (no production writer — CE-3112);
        /// ③ the locomotion class in <see cref="VehicleParams"/>: Pedestrian ⇒ Infantry, else Vehicle; ④ with no
        /// <see cref="VehicleParams"/>: Vehicle for a bare <see cref="VehicleState"/> (a harness vehicle), else Infantry.
        /// </summary>
        public static NavLayerMask For(EntityRepository repo, Entity entity, uint explicitMask)
        {
            if (explicitMask != 0) return (NavLayerMask)explicitMask;
            if (repo.HasComponent<NavAgentProfile>(entity))
            {
                uint preferred = repo.GetComponent<NavAgentProfile>(entity).PreferredLayerMask;
                if (preferred != 0) return (NavLayerMask)preferred;
            }
            if (repo.HasComponent<VehicleParams>(entity))
                return repo.GetComponent<VehicleParams>(entity).Class == VehicleClass.Pedestrian ? NavLayerMask.Infantry : NavLayerMask.Vehicle;
            return repo.HasComponent<VehicleState>(entity) ? NavLayerMask.Vehicle : NavLayerMask.Infantry;
        }
    }
}
