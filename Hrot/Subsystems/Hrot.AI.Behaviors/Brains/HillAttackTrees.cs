using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;
using static Hrot.AI.Behaviors.Brains.TreeNodeIds;

namespace Hrot.AI.Behaviors.Brains
{
    // ⭐⭐ CE-1410 (R-1042) — the hill-attack demo's trees (docs/designs/hill-attack/DESIGN.md) as C#-built trees: node for node the
    //   retired Assets/BTrees/Demos/HillAttack/{PlatoonHillAttack,HullDownAttackRun}.btree.json, their ids, titles and comments kept.
    //   Their contracts and resolvers are unchanged. ⭐ PlatoonHillAttack's tree is its EXISTING C# twin
    //   (HillAttackCommanderNodes.BuildPlatoonHillAttackTree, CE-430), now the registered one — one implementation, not a second.

    /// <summary>
    /// ⭐ <c>HullDownAttackRun</c> — one tank's run in the hill attack (CE-2230, hill-attack DESIGN §0): the vehicle exposure cycle in its LANE
    /// mode. From the baseline slot out along the lane, stop the first tick it SEES its target (hull-down), ONE round, reverse back into the
    /// slot, done (R-2022). The commander's payload is written into <c>Params.fight</c> by <see cref="HillAttackTankNodes.ResolveHullDownAttackRun"/>.
    /// </summary>
    public static class HullDownAttackRun
    {
        /// <summary>The block: the run's config (the resolver writes it at offset 0), then the cycle's working memory.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public HullDownRunConfig Params;
            public VehicleFightFromCoverState State;
        }

        [BTreeDefinition(BehaviorNames.HullDownAttackRun, AssetId = "2b000000-0000-0000-0000-0000000000dd", Curated = true, Group = "Demos/HillAttack",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .StatefulAction(bb => bb.Params.fight, bb => bb.State, VehicleFightFromCoverNodes.VehicleFightFromCover, Id("2b000000-0000-0000-0000-0000000000b1"))
                .Label("Fight from cover — the lane, one round")
                .Comment("Given hide (baseline) · lane (slot + attack direction) · stop on first sight · one round (the weapon brain picks gun and round) · reverse home · Success after one exposure.");
    }
}
