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
        /// <summary>
        /// ⭐ <c>CE-4623</c> AB-6 — a MODIFIER bit (13): the mover JUMPS low barriers, so its query filter includes the navmesh's off-mesh jump links
        /// (<see cref="NavPolyFlags.Jump"/>); every other filter excludes them. Set only for a Quadruped whose temperament <c>Jumps</c>.
        /// </summary>
        public const uint JumpModifier = 1u << 13;

        public static uint WoodsFactorBits(float forestFactor)
            => forestFactor <= 0f ? 0u : (uint)Math.Clamp((int)MathF.Round(forestFactor * 15f, MidpointRounding.AwayFromZero), 1, 15) << WoodsFactorShift;

        /// <summary>The woods cost a mask's factor bits ask for (1 / factor), or 0 when the mask carries none.</summary>
        public static float WoodsCostOf(uint layerMask)
        {
            uint q = (layerMask & WoodsFactorMask) >> WoodsFactorShift;
            return q == 0 ? 0f : 15f / q;
        }

        /// <summary>
        /// ⭐ <c>CE-4624</c> MS-T3 (DESIGN_Minefields.md MR-4, KR-9, KR-13; lane decision R-5047) — the mover's SIDE + 1 (0 = no knowledge applied) in bits 14…18 of the mask: the
        /// mask already travels everywhere a route is asked (the request, the wire, <c>RouteQuery.LayerMask</c>, the solver, the EQS path costs, the danger solve), so the side reaches
        /// the navmesh filter with no new parameter. Like the other modifiers, no mesh carries these bits.
        /// </summary>
        public const int SideShift = 14;
        public const uint SideMask = 0x1Fu << SideShift;

        /// <summary>The side bits of <paramref name="force"/> (a <c>ForceId</c>; &lt; 0 ⇒ none).</summary>
        public static uint SideBits(int force) => force < 0 || force > 30 ? 0u : (uint)(force + 1) << SideShift;

        /// <summary>The side + 1 a mask carries (0 = none).</summary>
        public static byte SidePlus1Of(uint layerMask) => (byte)((layerMask & SideMask) >> SideShift);

        /// <summary>The layer mask of <paramref name="entity"/>'s route requests — ⭐ CE-4624 with its SIDE (so its routes are priced by what its side knows of minefields).</summary>
        public static NavLayerMask For(EntityRepository repo, Entity entity, uint explicitMask)
        {
            var mask = (uint)ForMover(repo, entity, explicitMask);
            if ((mask & SideMask) == 0 && repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(entity))
                mask |= SideBits((int)repo.GetComponentRO<EntityInfo>(entity).ForceId);
            return (NavLayerMask)mask;
        }

        private static NavLayerMask ForMover(EntityRepository repo, Entity entity, uint explicitMask)
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
                // ⭐ CE-4623 AB-2 (L-A6) — an ANIMAL walks where a man walks (fields, woods, through gaps) and never prefers the road: the
                //   infantry layer. ⛔ Not the vehicle layer (woods closed, roads preferred). A dedicated animal layer is filed for later.
                if (cls == VehicleClass.Quadruped)
                    return Fdp.Toolkit.Animals.AnimalFear.TemperamentOf(repo, entity) is { Jumps: true }
                        ? (NavLayerMask)((uint)NavLayerMask.Infantry | JumpModifier) : NavLayerMask.Infantry;   // ⭐ AB-6 — a jumper may use the jump links
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
