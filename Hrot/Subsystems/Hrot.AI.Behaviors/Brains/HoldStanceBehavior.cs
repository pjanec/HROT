using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ Buildings Stage 6 (<c>CE-1032</c>; 📄 docs/DESIGN_Building_Interiors.md §3k) — <c>HoldStance</c>, a curated TEST tree (R-223):
    /// request the posture (<c>StanceRequest.Set</c>, the logical stance the area effect and sight read) and hold it.
    /// </summary>
    public static class HoldStanceBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct HoldStanceParams { public StanceId Stance; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HoldStanceBlackboard { public HoldStanceParams Hold; }

        /// <summary>The time a posture change takes (s).</summary>
        public const float BlendSeconds = 1.0f;

        [BehaviorResolver(BehaviorNames.HoldStance)]
        public static void Resolve(in HoldStanceParamsJsonDto authored, ref HoldStanceBlackboard block, EntityRepository world, Entity self)
            => block.Hold = new HoldStanceParams { Stance = authored.Stance };

        [SharedAiAction]
        public static NodeStatus Hold(ref HoldStanceParams p, Entity self, EntityRepository world)
        {
            Hrot.MuscleCharacter.Animation.Stance.StanceRequest.Set(world, self, p.Stance, BlendSeconds);
            return NodeStatus.Running;
        }

        [BTreeDefinition(BehaviorNames.HoldStance, Curated = true, ParamsType = typeof(HoldStanceBlackboard))]
        public static BTreeBuilder<HoldStanceBlackboard, BTreeContext> BuildHoldStanceTree()
            => new BTreeBuilder<HoldStanceBlackboard, BTreeContext>().Action(bb => bb.Hold, Hold);
    }
}
