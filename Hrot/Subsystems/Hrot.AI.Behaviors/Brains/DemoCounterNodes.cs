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

        // ── Second demo DTO + action (S1-G) ───────────────────────────────────────

        /// <summary>
        /// Blackboard DTO for the accumulator demo. Blittable, sequential layout.
        /// Placed at a non-zero offset after <see cref="DemoCounterParams"/> in T10.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DemoAccumParams
        {
            /// <summary>Running total; incremented by <see cref="Action_AddStepToSum"/> each tick.</summary>
            public int Sum;

            /// <summary>Amount added per tick. Seeded by the proof test before the first tick.</summary>
            public int Step;
        }

        /// <summary>
        /// Action: adds <c>Step</c> to <c>Sum</c> and returns Success. Purely stateless and
        /// side-effect-free — the result is observable directly through the blackboard.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_AddStepToSum(
            ref DemoAccumParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            p.Sum += p.Step;
            return NodeStatus.Success;
        }

        // ── Stateful cursor demo (S2-1) ───────────────────────────────────────────

        /// <summary>
        /// Params DTO for the cursor demo. Blittable, sequential layout.
        /// Carries the Limit that <see cref="Action_AdvanceCursor"/> counts toward.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DemoCursorParams
        {
            /// <summary>Upper bound; cursor stops (Success) once it reaches this value.</summary>
            public int Limit;
        }

        /// <summary>
        /// Working state for the cursor demo. Lives in a <c>BlueprintBlackboard*</c> partition
        /// slot, NOT in <c>BrainBlackboard.BehaviorParameters</c>, so it persists across ticks
        /// independently per node instance.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DemoCursorState
        {
            /// <summary>Cursor position; incremented by <see cref="Action_AdvanceCursor"/> each tick.</summary>
            public int Cursor;
        }

        /// <summary>
        /// Stateful action: increments <c>Cursor</c> by one each tick, returning
        /// <see cref="NodeStatus.Running"/> until the cursor reaches <c>Limit</c>,
        /// then <see cref="NodeStatus.Success"/>. WorkingState is projected from the
        /// entity's active <c>BlueprintBlackboard*</c> tier partition slot.
        ///
        /// NOT marked [BTreeAction] — this method uses the ThreeParamReusableStateful delegate
        /// shape (4-param with WorkingState) registered by the bridge emitter, not the
        /// standard FbtActionRegistrar auto-registration. The [BTreeAction] attribute only
        /// supports the standard 3-param (ref TDto, ref BehaviorTreeState, ref BTreeContext) shape.
        /// </summary>
        public static NodeStatus Action_AdvanceCursor(
            ref DemoCursorParams p,
            ref DemoCursorState ws,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            ws.Cursor++;
            return ws.Cursor < p.Limit ? NodeStatus.Running : NodeStatus.Success;
        }
    }
}
