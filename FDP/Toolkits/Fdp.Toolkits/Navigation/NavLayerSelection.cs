using System;
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
        /// <summary>⭐ CE-4369 — a MODIFIER bit on a request's layer mask (not a layer: no mesh carries it): the mover is tracked, so the
        /// woods are priced for it rather than closed. Every provider tests layers by bit, so the extra bit selects nothing else.</summary>
        public const uint TrackedModifier = 1u << 8;

        /// <summary>
        /// ⭐ <c>CE-4621</c> MC-2a (MV-5) — modifier bits 9…12: the mover TYPE's authored forest factor, quantised to fifteenths (0 = not authored ⇒ the
        /// class rule above). The provider prices a woods polygon at 1 / factor for such a mover — the SAME per-type table the integrator reads, so a
        /// bike may route through a wood it can drive through. Like <see cref="TrackedModifier"/>, no mesh carries these bits.
        /// </summary>
        public const int WoodsFactorShift = 9;
        public const uint WoodsFactorMask = 0xFu << WoodsFactorShift;

        /// <summary>The quantised forest factor (1…15) of <paramref name="forestFactor"/>, 0 when not authored.</summary>
        public static uint WoodsFactorBits(float forestFactor)
            => forestFactor <= 0f ? 0u : (uint)Math.Clamp((int)MathF.Round(forestFactor * 15f, MidpointRounding.AwayFromZero), 1, 15) << WoodsFactorShift;

        /// <summary>The woods cost a mask's factor bits ask for (1 / factor), or 0 when the mask carries none.</summary>
        public static float WoodsCostOf(uint layerMask)
        {
            uint q = (layerMask & WoodsFactorMask) >> WoodsFactorShift;
            return q == 0 ? 0f : 15f / q;
        }

        public static NavLayerMask For(EntityRepository repo, Entity entity, uint explicitMask)
        {
            if (explicitMask != 0) return (NavLayerMask)explicitMask;
            if (repo.HasComponent<NavAgentProfile>(entity))
            {
                uint preferred = repo.GetComponent<NavAgentProfile>(entity).PreferredLayerMask;
                if (preferred != 0) return (NavLayerMask)preferred;
            }
            if (repo.HasComponent<VehicleParams>(entity))
            {
                var vp = repo.GetComponent<VehicleParams>(entity);
                var cls = vp.Class;
                if (cls == VehicleClass.Pedestrian) return NavLayerMask.Infantry;
                uint woods = WoodsFactorBits(vp.OffRoadForest);   // ⭐ CE-4621 MC-2a — the type's own forest factor prices the woods
                // ⭐ CE-4369 (R-4190 Q-T1 / Q-T2) — a TRACKED vehicle plans on the vehicle layer with the tracked modifier: woods open to
                //   it at a high cost, closed to a wheeled one (the provider's per-mover filter). The ONE locomotion rule decides.
                return Fdp.Toolkit.Motion.Locomotions.Of(repo, entity) == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Tracked
                    ? (NavLayerMask)((uint)NavLayerMask.Vehicle | TrackedModifier | woods) : (NavLayerMask)((uint)NavLayerMask.Vehicle | woods);
            }
            return repo.HasComponent<VehicleState>(entity) ? NavLayerMask.Vehicle : NavLayerMask.Infantry;
        }
    }
}
