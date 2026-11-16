using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-4622</c> K-6 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-4) — <c>ComplyAtCheckpoint</c>: the checkpoint's "out, hands up"
    /// as his brain takes it — a REACTION the checkpoint sends him (an event, never a write): he gets out of the vehicle (the library disembark node) and
    /// stands with his hands up (<see cref="GestureKind.HandsUp"/> — custody reads him as complying) until the checkpoint lets him go. A man whose ROE
    /// keeps him on his task never takes it (the reaction rules) — the bomber who stays at the wheel.
    /// </summary>
    public static class ComplyAtCheckpointBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ComplyBlackboard
        {
            public DisembarkNodeParams Leave;
        }

        /// <summary>He steps out (the default exit).</summary>
        [BehaviorResolver(BehaviorNames.ComplyAtCheckpoint)]
        public static void Resolve(in global::Hrot.Map.Definitions.Behavior.NoParams authored, ref ComplyBlackboard block, EntityRepository world, Entity self)
            => block.Leave = default;

        [Behavior(BehaviorNames.ComplyAtCheckpoint, Params = typeof(global::Hrot.Map.Definitions.Behavior.NoParams), Categories = BehaviorCategory.Civilian | BehaviorCategory.Insurgent,
            Description = "A stopped driver or passenger answers the checkpoint: gets out and stands with his hands up.")]
        [BTreeDefinition(BehaviorNames.ComplyAtCheckpoint, Curated = true, Group = "Library/Civilian", ParamsType = typeof(ComplyBlackboard))]
        public static BTreeBuilder<ComplyBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ComplyBlackboard, BTreeContext>()
                .Sequence(s => s
                    .Action(bb => bb.Leave, EmbarkNodes.Disembark)
                    .Action(HandsUp));

        /// <summary>Hands up, held (Running) — the checkpoint ends it.</summary>
        [SharedAiAction]
        public static NodeStatus HandsUp(Entity self, EntityRepository world)
        {
            if (Gestures.Of(world, self) != GestureKind.HandsUp) Gestures.Set(world, self, GestureKind.HandsUp);
            return NodeStatus.Running;
        }
    }
}
