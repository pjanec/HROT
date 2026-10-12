using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>WL-4</c> L-8 (📄 docs/DESIGN_Weapon_Layers.md §5f; §5e "its node is NOT built") — <c>TrackTarget</c>, a curated C# tree:
    /// keep the gun on the unit's top threat without firing (the shared <see cref="TrackTargetNodes.TrackTarget"/> node, re-aimed when
    /// the top threat changes). The <c>FireAtPoint</c> precedent: the vocabulary for behaviour authors, deciding nothing itself.
    /// </summary>
    public static class TrackTargetBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct TrackTargetBlackboard
        {
            public TrackTargetNodeParams Track;
        }

        [BehaviorResolver(BehaviorNames.TrackTarget)]
        public static void Resolve(in TrackTargetParamsJsonDto authored, ref TrackTargetBlackboard block, EntityRepository world, Entity self)
            => block.Track = new TrackTargetNodeParams { Mount = (byte)authored.Mount };

        /// <summary>The top threat (the starter ranking), then the shared node. Failure while there is none.</summary>
        [Fbt.Kernel.SharedAiAction]
        public static NodeStatus TrackTopThreat(ref TrackTargetNodeParams p, Entity self, EntityRepository world)
        {
            if (!EqsTacticsNodes.TopThreat(world, self, p.Target, out var threat)) return NodeStatus.Failure;
            p.Target = threat;
            return TrackTargetNodes.TrackTarget(ref p, self, world);
        }
[Behavior(BehaviorNames.TrackTarget, Params = typeof(global::Hrot.Map.Definitions.Behavior.TrackTargetParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.TrackTarget, Curated = true, Group = "Library/Basic", ParamsType = typeof(TrackTargetBlackboard))]
        public static BTreeBuilder<TrackTargetBlackboard, BTreeContext> BuildTrackTargetTree()
            => new BTreeBuilder<TrackTargetBlackboard, BTreeContext>()
                .Action(bb => bb.Track, TrackTopThreat);
    }
}
