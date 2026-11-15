using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2165</c> V4 — the library behaviour <c>VehicleFightFromCover</c> and the land vehicles' ATTACK order
    /// (<see cref="BehaviorNames.LandPlatformAttack"/>, R-278 <c>&lt;Class&gt;&lt;Order&gt;</c> — more specific than
    /// <see cref="BehaviorNames.PlatformAttack"/>), so a tank, IFV, APC or technical told to attack fights from cover instead of running the
    /// infantry posture. 📄 docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §4.
    /// </summary>
    public static class VehicleFightFromCoverBehavior
    {
        /// <summary>The behaviour's block: an attack order's target (as <see cref="CombatPostureBehavior"/> holds it) and the node.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct VehicleFightFromCoverBlackboard
        {
            public DesignateParams Designate;
            public VehicleFightFromCoverParams Fight;
            public DesignateState DesignateState;
            public VehicleFightFromCoverState FightState;
        }

        /// <summary>
        /// ATTACK for a land vehicle: the named target designated (the threat ranking puts it first, so the cycle fights IT; Success once
        /// it is out of the fight — C-5), the cycle at its defaults.
        /// </summary>
        [BehaviorResolver(BehaviorNames.LandPlatformAttack)]
        public static void ResolveAttack(in AttackOrder order, ref VehicleFightFromCoverBlackboard block, EntityRepository world, Entity self)
        {
            block = default;
            var posture = default(CombatPostureBehavior.CombatPostureBlackboard);
            CombatPostureBehavior.Resolve(new CombatPostureParamsJsonDto { Target = order.Target, Assault = true }, ref posture, world, self);
            block.Designate = posture.Designate;   // one reading of an order's target: the posture's (map lookup + warning)
            block.Fight.DesignatePacked = posture.Designate.TargetPacked;   // ⭐ CE-2232 — the cycle knows the named target too: it waits for it, never ends before it is sensed
        }

        /// <summary>The library behaviour takes nothing: no target named, the cycle at its defaults against the top threat.</summary>
        [BehaviorResolver(BehaviorNames.VehicleFightFromCover)]
        public static void Resolve(in NoParams authored, ref VehicleFightFromCoverBlackboard block, EntityRepository world, Entity self)
            => block = default;

        /// <summary>The cycle beside the target's designation, in one Parallel (RequireOne — the attack ends when the target is down,
        /// or when the cycle ends: nothing left to fight, or no hide).</summary>
        [Behavior(BehaviorNames.VehicleFightFromCover, Conduct = Fdp.Toolkit.Behavior.Conduct.Halts | Fdp.Toolkit.Behavior.Conduct.SeeksCover,   /* CE-4621 M-3 (CD-1) */ Params = typeof(NoParams), Categories = BehaviorCategory.MilitaryApc)]   // it takes nothing (P1): the cycle at its defaults
        [BTreeDefinition(BehaviorNames.VehicleFightFromCover, Curated = true, Group = "Library/Combat", ParamsType = typeof(VehicleFightFromCoverBlackboard))]
        public static BTreeBuilder<VehicleFightFromCoverBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<VehicleFightFromCoverBlackboard, BTreeContext>()
                .Parallel(1, both => both
                    .StatefulAction(bb => bb.Designate, bb => bb.DesignateState, PostureNodes.UntilTargetDown)
                    .StatefulAction(bb => bb.Fight, bb => bb.FightState, VehicleFightFromCoverNodes.VehicleFightFromCover));

        [Behavior(BehaviorNames.LandPlatformAttack, Params = typeof(AttackOrder), Categories = BehaviorCategory.MilitaryApc)]
        [BTreeDefinition(BehaviorNames.LandPlatformAttack, Curated = true, Group = "Library/Orders", ParamsType = typeof(VehicleFightFromCoverBlackboard))]
        public static BTreeBuilder<VehicleFightFromCoverBlackboard, BTreeContext> BuildLandPlatformAttack() => BuildTree();
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2436</c> AU-4 (A-4; 📄 DESIGN_Assault_Under_Fire §5, §9.4) — <c>VehicleSupportByFire</c>: the vehicle drives to its STAND-OFF point
    /// (the base of fire's line, never the objective — ⛔ vehicles never lead across the open: modern anti-tank missiles and drones), then runs the
    /// exposure cycle from there (<see cref="VehicleFightFromCoverNodes.VehicleFightFromCover"/>: hide behind a building, gun on the threat,
    /// out front-first to a firing point, fire, and — Scoot, one exposure a position — RELOCATE to a new hide after EVERY engagement).
    /// 📌 measured: the IFVs had no commander and no order — they did nothing for the attack. ⚠ The cycle fights what the vehicle's own sensors
    /// find from its firing points; blind cannon fire on REPORTED points is not built (§9.4).
    /// </summary>
    public static class VehicleSupportByFireBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct VehicleSupportByFireBlackboard
        {
            public CgfNodes.MoveToLocationParams Move;
            public VehicleFightFromCoverParams Fight;
            public VehicleFightFromCoverState FightState;
        }

        [BehaviorResolver(BehaviorNames.VehicleSupportByFire)]
        public static void Resolve(in VehicleSupportByFireParamsJsonDto authored, ref VehicleSupportByFireBlackboard block, EntityRepository world, Entity self)
        {
            block = default;
            block.Move = new CgfNodes.MoveToLocationParams { X = authored.Position.X, Y = authored.Position.Y, Speed = authored.Speed, ArrivalRadius = 8f };
            block.Fight.WithdrawMode = WithdrawMode.Scoot;      // relocate after each engagement
            block.Fight.ExposuresPerPosition = 1;
            block.Fight.SearchRadius = authored.SearchRadius;
        }

        [Behavior(BehaviorNames.VehicleSupportByFire, Params = typeof(VehicleSupportByFireParamsJsonDto), Categories = BehaviorCategory.MilitaryApc,
                  Description = "Supports an attack by fire from a stand-off point: fights from cover and relocates after each engagement.")]
        [BTreeDefinition(BehaviorNames.VehicleSupportByFire, Curated = true, Group = "Library/Combat", ParamsType = typeof(VehicleSupportByFireBlackboard))]
        public static BTreeBuilder<VehicleSupportByFireBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<VehicleSupportByFireBlackboard, BTreeContext>()
                .Sequence(both => both
                    .Action(bb => bb.Move, CgfNodes.Action_WriteMoveToChannel)
                    .StatefulAction(bb => bb.Fight, bb => bb.FightState, VehicleFightFromCoverNodes.VehicleFightFromCover));
    }
}
