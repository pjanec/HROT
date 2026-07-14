using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Compile-time stand-in for a blueprint-authored AiPrimitive's <b>generated</b> output, used to
    /// prove the I2/I3 composition path — a blueprint AiPrimitive action composed as a host-BTree node
    /// (<c>DelegateShape = AiPrimitiveTickCore</c>) with its WorkingState living on a
    /// <c>BlueprintBlackboard*</c> partition slot rather than the fixed <c>Blackboard1024+8</c> rail.
    ///
    /// <para>
    /// It mirrors <b>exactly</b> the shape <c>AiPrimitiveEmitter</c> emits for a real blueprint: a
    /// nested blittable <see cref="Params"/> + <see cref="WorkingState"/> and a static
    /// <see cref="TickCore"/> with the signature
    /// <c>(ref Params, ref WorkingState, Entity self, EntityRepository world, float time)</c>. A real
    /// editor-authored AiPrimitive generates this same shape at hot-reload; this hand-written twin
    /// lets the BTree source generator resolve the types at compile time so the composition demo is
    /// buildable and testable without the (not-yet-built) editor cross-compile path.
    /// </para>
    /// </summary>
    public static class DemoAiPrimitiveNodes
    {
        /// <summary>Authored parameters (bin-packed into the host BTree's BrainBlackboard).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Params
        {
            /// <summary>Number of ticks the action stays Running before it returns Success.</summary>
            public int RunsNeeded;
        }

        /// <summary>Per-entity working state — lives in a partition slot, persists across ticks.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WorkingState
        {
            /// <summary>Ticks elapsed since activation. Proves the partition slot persists.</summary>
            public int Ticks;
        }

        /// <summary>
        /// Blueprint <c>TickCore</c> shape. Increments the partition-slot <see cref="WorkingState"/>
        /// each tick and returns <see cref="NodeStatus.Running"/> until it has ticked
        /// <see cref="Params.RunsNeeded"/> times, then <see cref="NodeStatus.Success"/> — an
        /// observable multi-tick effect that proves WorkingState persists in the partition slot
        /// (not re-zeroed each tick) and that Params is read from the bin-packed blackboard.
        /// </summary>
        public static NodeStatus TickCore(
            ref Params p,
            ref WorkingState ws,
            Entity self,
            EntityRepository world,
            float time)
        {
            ws.Ticks++;
            return ws.Ticks >= p.RunsNeeded ? NodeStatus.Success : NodeStatus.Running;
        }
    }
}
