using System.Runtime.InteropServices;
using Fbt;
using Fdp.Toolkit.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Minimal, side-effect-free demo nodes for proving the JSON BTree pipeline end-to-end
    /// (condition + action + decorator + editor-managed blackboard variable), analogous to
    /// the blueprint <c>CountingDemo</c>. These operate purely on a tiny blittable DTO that
    /// is projected onto the start of the runtime blackboard (the engine offset-0 convention),
    /// so they have no dependency on perception/locomotion/ECS — the counter is observable
    /// directly in the blackboard inspector and in unit tests.
    /// </summary>
    public static class DemoCounterNodes
    {
        /// <summary>
        /// Blackboard DTO for the counter demo. Blittable, sequential layout so it maps
        /// cleanly onto the first bytes of <c>BrainBlackboard.BehaviorParameters</c>.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DemoCounterParams
        {
            /// <summary>Incremented by <see cref="Action_IncrementCounter"/> each time it runs.</summary>
            public int Counter;

            /// <summary>Upper bound; <see cref="Condition_CounterBelowThreshold"/> gates on this.</summary>
            public int Threshold;
        }

        /// <summary>
        /// Condition: Success while <c>Counter &lt; Threshold</c>, Failure once the counter
        /// reaches the threshold. Lets a Sequence keep running the increment until the cap.
        /// </summary>
        [BTreeCondition]
        public static NodeStatus Condition_CounterBelowThreshold(
            ref DemoCounterParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            return p.Counter < p.Threshold ? NodeStatus.Success : NodeStatus.Failure;
        }

        /// <summary>
        /// Action: increments <c>Counter</c> by one and returns Success. The simplest
        /// possible observable effect — mirrors the blueprint CountingDemo's "+1".
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_IncrementCounter(
            ref DemoCounterParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            p.Counter++;
            return NodeStatus.Success;
        }
    }
}
