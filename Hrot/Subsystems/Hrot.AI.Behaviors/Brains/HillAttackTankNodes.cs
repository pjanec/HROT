using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication.Services;
using Hrot.AI.Behaviors.Logging;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2230</c> (hill attack §0, HA-1) — what <c>HullDownAttackRun.btree.json</c> binds: the ONE node of the run, the vehicle
    /// exposure cycle in its LANE mode (<see cref="VehicleFightFromCoverNodes.VehicleFightFromCover"/>). The asset owns the tree, this
    /// C# owns the contract (<see cref="HillAttackTankNodes.ResolveHullDownAttackRun"/>), as for <c>DangerCrossing</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HullDownRunConfig
    {
        public VehicleFightFromCoverParams fight;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2230</c> — one tank's hill-attack run, rebuilt on the vehicle exposure cycle (docs/designs/hill-attack/DESIGN.md §0):
    /// from its baseline slot (the HIDE) out along its lane — the firing slot, then the attack direction — stopping the first tick it
    /// SEES its target (hull-down: only the turret clears the crest, R-246), ONE round chosen by the weapon brain, then a REVERSE back
    /// into the baseline slot, and the run ends (🔒 R-2022: <i>"firing just one round and then moving back to cover"</i>); the next
    /// pair goes. ⛔ HISTORY: its five bespoke nodes (<c>Condition_HasTarget</c>, <c>Action_CreepToAndBeyondSlot</c>,
    /// <c>Action_AimAndFireSpecific</c>, <c>Action_ReverseToBaseline</c>, <c>Action_AbortEngagement</c>) are retired — the creep looked
    /// for the assigned target only near the slot, and the fire node bypassed the weapon brain.
    /// </summary>
    public static class HillAttackTankNodes
    {
        /// <summary>The run's reverse speed back into the baseline slot (m/s) — the old retreat's 12, under the tank's reverse limit.</summary>
        public const float ReverseSpeed = 12f;

        /// <summary>The wait in the baseline slot before the drive out (s) — the commander staged it already.</summary>
        public const float WaitSeconds = 0.5f;

        /// <summary>
        /// ⭐ <c>CE-2230</c> — the commander's payload (<see cref="HullDownAttackParams"/>, sent as the <c>HullDownAttack</c> intent) into
        /// the lane run: the baseline slot is the given hide, the firing slot and the attack direction the lane, the assigned target the
        /// cycle's named one (resolved from its network id; 0 when it has not replicated yet — the cycle then fights the top threat),
        /// one round, one exposure, reverse. An empty payload keeps the asset's bake.
        /// </summary>
        [Hrot.Map.Definitions.Behavior.Behavior("HullDownAttackRun", Params = typeof(HullDownAttackParams),
            Categories = Hrot.Map.Definitions.Behavior.BehaviorCategory.MilitaryApc)]
        [BehaviorResolver("HullDownAttackRun")]
        public static void ResolveHullDownAttackRun(in HullDownAttackParams authored, ref HullDownRunConfig block, EntityRepository world, Entity self)
        {
            var dir = new Vector2(authored.AttackDirX, authored.AttackDirY);
            if (dir.LengthSquared() > 1e-6f) dir = Vector2.Normalize(dir);
            long target = 0;
            if (authored.TargetNetworkId != 0)
            {
                var map = world.HasSingletonManaged<NetworkEntityMap>() ? world.GetSingletonManaged<NetworkEntityMap>() : null;
                if (map != null && map.TryGetEntity(authored.TargetNetworkId, out var e)) target = (long)e.PackedValue;
                else BehaviorLog.Warn(self, world, "TargetNetworkId=" + authored.TargetNetworkId + " not in the entity map; the run fights its top threat.");
            }
            block.fight = new VehicleFightFromCoverParams
            {
                WithdrawMode = WithdrawMode.Reverse,
                RoundsPerExposure = 1,                                    // 🔒 R-2022 — one round, then back
                MaxExposures = 1,                                         // one run per wave (the commander alternates the pairs)
                HideSecondsMin = WaitSeconds, HideSecondsMax = WaitSeconds,
                Speed = ReverseSpeed,
                UseGivenHide = 1,
                GivenHide = new Vector2(authored.BaselineX, authored.BaselineY),
                LaneStart = new Vector2(authored.SlotX, authored.SlotY),
                LaneDir = dir,
                ApproachSpeed = authored.ApproachSpeed,
                CreepSpeed = authored.CreepSpeed,
                DesignatePacked = target,
            };
        }
    }
}
