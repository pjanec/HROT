using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/DESIGN_Entity_Interactions.md §7.3) — <c>Embark</c>, a library behaviour (curated C# tree, R-258):
    /// walk to the vehicle and board it. One node, <see cref="EmbarkNodes.Embark"/>. Scope: any unit with a body (a UNIT is refused by
    /// the rules — its members board, R-272).
    /// </summary>
    public static class EmbarkBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct EmbarkBlackboard
        {
            public EmbarkNodeParams Board;
        }

        /// <summary>The authored contract in (the vehicle by network id), the node's params out (the vehicle on this node).</summary>
        [BehaviorResolver(BehaviorNames.Embark)]
        public static void Resolve(in EmbarkParamsJsonDto authored, ref EmbarkBlackboard block, EntityRepository world, Entity self)
            => block.Board = new EmbarkNodeParams { Vehicle = authored.Vehicle.Resolve(world), Speed = authored.Speed };

        [BTreeDefinition(BehaviorNames.Embark, Curated = true, Group = "Library/Basic", ParamsType = typeof(EmbarkBlackboard))]
        public static BTreeBuilder<EmbarkBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<EmbarkBlackboard, BTreeContext>()
                .Action(bb => bb.Board, EmbarkNodes.Embark);
    }

    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d — <c>Disembark</c>, a library behaviour: leave the vehicle the unit is aboard (beside the hull, at its seat).
    /// One node, <see cref="EmbarkNodes.Disembark"/>; Success at once when not aboard. The squad <c>SquadDismount</c> gives it to each
    /// member aboard (DESIGN_Squad_Tactics §5a).
    /// </summary>
    public static class DisembarkBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DisembarkBlackboard
        {
            public DisembarkNodeParams Leave;
        }

        [BehaviorResolver(BehaviorNames.Disembark)]
        public static void Resolve(in DisembarkParamsJsonDto authored, ref DisembarkBlackboard block, EntityRepository world, Entity self)
            => block.Leave = default;

        [BTreeDefinition(BehaviorNames.Disembark, Curated = true, Group = "Library/Basic", ParamsType = typeof(DisembarkBlackboard))]
        public static BTreeBuilder<DisembarkBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<DisembarkBlackboard, BTreeContext>()
                .Action(bb => bb.Leave, EmbarkNodes.Disembark);
    }
}
