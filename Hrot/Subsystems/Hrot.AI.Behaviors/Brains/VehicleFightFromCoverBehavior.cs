using System.Numerics;
using Fbt.Kernel;
using Fdp.Toolkit.Combat;
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
        /// <summary>⭐ AU-4 blind fire — a burst's rounds, and the pause between bursts (s).</summary>
        public const int BlindRounds = 5;
        public const float BlindIntervalSeconds = 15f;

        [StructLayout(LayoutKind.Sequential)]
        public struct SupportParams
        {
            public VehicleFightFromCoverParams Fight;
            /// <summary>The reported posts' centre; zero = none.</summary>
            public Vector3 Posts;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SupportState
        {
            public VehicleFightFromCoverState Fight;
            public FireAtPointNodeParams Blind;
            public SimInstant NextBurst;
            public byte Firing;
            /// <summary>Blind bursts fired so far.</summary>
            public byte Bursts;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VehicleSupportByFireBlackboard
        {
            public CgfNodes.MoveToLocationParams Move;
            public SupportParams Support;
            public SupportState State;
        }

        [BehaviorResolver(BehaviorNames.VehicleSupportByFire)]
        public static void Resolve(in VehicleSupportByFireParamsJsonDto authored, ref VehicleSupportByFireBlackboard block, EntityRepository world, Entity self)
        {
            block = default;
            block.Move = new CgfNodes.MoveToLocationParams { X = authored.Position.X, Y = authored.Position.Y, Speed = authored.Speed, ArrivalRadius = 8f };
            block.Support.Fight.WithdrawMode = WithdrawMode.Scoot;      // relocate after each engagement
            block.Support.Fight.ExposuresPerPosition = 1;
            block.Support.Fight.SearchRadius = authored.SearchRadius;
            block.Support.Posts = authored.Posts;
        }

        [Behavior(BehaviorNames.VehicleSupportByFire, Params = typeof(VehicleSupportByFireParamsJsonDto), Categories = BehaviorCategory.MilitaryApc,
                  Description = "Supports an attack by fire from a stand-off point: fights from cover and relocates after each engagement; fires on the reported posts it cannot see.")]
        [BTreeDefinition(BehaviorNames.VehicleSupportByFire, Curated = true, Group = "Library/Combat", ParamsType = typeof(VehicleSupportByFireBlackboard))]
        public static BTreeBuilder<VehicleSupportByFireBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<VehicleSupportByFireBlackboard, BTreeContext>()
                .Sequence(both => both
                    .Action(bb => bb.Move, CgfNodes.Action_WriteMoveToChannel)
                    .StatefulAction(bb => bb.Support, bb => bb.State, FightOrBlindFire));

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-4 (A-4 as designed; 📄 DESIGN_Assault_Under_Fire §9.4) — the exposure cycle while the vehicle's own sensors give it a
        /// threat; while they give it NONE and the company reported posts, BLIND FIRE: a <see cref="BlindRounds"/>-round cannon burst
        /// (<see cref="FireAtPointNodes.FireAtPoint"/>, mount 0) on the posts' centre every <see cref="BlindIntervalSeconds"/> — a burst always ends
        /// before the cycle takes the gun again. 📌 Before, the cycle with nothing in sight returned Success at once and the IFV's support ended.
        /// Without posts, the cycle alone, as before.
        /// </summary>
        public static NodeStatus FightOrBlindFire(ref SupportParams p, ref SupportState s, Entity self, EntityRepository world)
        {
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            if (s.Firing != 0)
            {
                if (FireAtPointNodes.FireAtPoint(ref s.Blind, self, world) == NodeStatus.Running) return NodeStatus.Running;
                s.Firing = 0;
                s.NextBurst = (SimInstant)(now + BlindIntervalSeconds);
            }
            bool blind = p.Posts != Vector3.Zero
                && !EqsTacticsNodes.TopAim(world, self, new ThreatAim(s.Fight.Threat, s.Fight.HeardId, s.Fight.HeardPoint), out _);
            if (!blind)
            {
                var r = VehicleFightFromCoverNodes.VehicleFightFromCover(ref p.Fight, ref s.Fight, self, world);
                return p.Posts != Vector3.Zero && r == NodeStatus.Success ? NodeStatus.Running : r;   // with posts, the support goes on
            }
            if (VehicleFightFromCoverNodes.Disarmed(world, self)) return NodeStatus.Running;
            if (now < (double)s.NextBurst) return NodeStatus.Running;
            s.Blind = new FireAtPointNodeParams { Point = p.Posts, Rounds = BlindRounds, Mount = 0 };
            s.Firing = 1;
            if (s.Bursts < byte.MaxValue) s.Bursts++;
            FireAtPointNodes.FireAtPoint(ref s.Blind, self, world);
            return NodeStatus.Running;
        }

        /// <summary>Leaving: the cycle's sensors, fire, tracking and move stop (as its own deactivator does).</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.VehicleSupportByFireBehavior.FightOrBlindFire")]
        public static void Deactivate_FightOrBlindFire(ref SupportParams p, ref SupportState s, Entity self, EntityRepository world)
            => VehicleFightFromCoverNodes.Deactivate_VehicleFightFromCover(ref p.Fight, ref s.Fight, self, world);
    }
}
