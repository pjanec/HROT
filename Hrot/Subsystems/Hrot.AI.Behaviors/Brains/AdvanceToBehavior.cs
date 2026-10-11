using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> — the library's <c>AdvanceTo</c>: ONE shared node, <see cref="PostureNodes.AdvanceAndAttack"/> — move to the
    /// objective firing at the top threat, Success on arrival. The order a squad commander gives a member it wants MOVED
    /// (<see cref="SquadManeuverRunner"/>); a hold is still <c>CombatPosture</c>. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §3.6.
    /// </summary>
    public static class AdvanceToBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct AdvanceToBlackboard
        {
            public AdvanceParams Advance;
            public AdvanceState State;
        }

        [BehaviorResolver(BehaviorNames.AdvanceTo)]
        public static void Resolve(in AdvanceToParamsJsonDto authored, ref AdvanceToBlackboard block, EntityRepository world, Entity self)
        {
            block.Advance = new AdvanceParams
            {
                Objective       = authored.Objective,
                Speed           = authored.Speed > 0f ? authored.Speed : 3f,
                ArrivalRadius   = authored.ArrivalRadius > 0f ? authored.ArrivalRadius : 3f,
                CooldownSeconds = authored.CooldownSeconds > 0f ? authored.CooldownSeconds : 1f,
            };
            block.State = default;
        }

        [BTreeDefinition(BehaviorNames.AdvanceTo, Curated = true, Group = "Library/Infantry", ParamsType = typeof(AdvanceToBlackboard))]
        public static BTreeBuilder<AdvanceToBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<AdvanceToBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Advance, bb => bb.State, PostureNodes.AdvanceAndAttack);
    }
}
