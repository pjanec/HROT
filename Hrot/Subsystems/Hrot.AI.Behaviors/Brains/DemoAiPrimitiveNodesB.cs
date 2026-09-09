using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// A <b>second, deliberately different</b> compile-time stand-in for a blueprint-authored
    /// AiPrimitive's generated output — the counterpart to <see cref="DemoAiPrimitiveNodes"/>.
    ///
    /// <para>
    /// Exists for the BP-41 proof (<c>T39_TwoDistinctPrimitives</c>): every prior composition test uses
    /// <em>one</em> primitive type — <c>T20</c> places two hardcoded stateful actions of the same type,
    /// <c>T35</c> places the same <see cref="DemoAiPrimitiveNodes"/> three times. Two <em>different</em>
    /// AiPrimitives on one entity — the case an author actually hits — was covered only by analogy.
    /// </para>
    ///
    /// <para>
    /// The differences from <see cref="DemoAiPrimitiveNodes"/> are the point, not decoration: a
    /// <b>16-byte</b> <see cref="WorkingState"/> against A's 4-byte one, and an <b>8-byte</b>
    /// <see cref="Params"/> against A's 4-byte one. A partition allocator that sized every slot from the
    /// first placement, or a bin-packer that assumed one Params stride, would pass with A alone and
    /// corrupt state here.
    /// </para>
    /// </summary>
    public static class DemoAiPrimitiveNodesB
    {
        /// <summary>Authored parameters (bin-packed into the host BTree's BrainBlackboard). 8 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Params
        {
            /// <summary>Number of ticks the action stays Running before it returns Success.</summary>
            public int RunsNeeded;

            /// <summary>Amount added to <see cref="WorkingState.Accumulator"/> per tick.</summary>
            public int Stride;
        }

        /// <summary>
        /// Per-entity working state — lives in a partition slot, persists across ticks. 16 bytes
        /// (<see cref="long"/> forces 8-byte alignment), four times A's.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WorkingState
        {
            /// <summary>Running total of <see cref="Params.Stride"/>, one addition per tick.</summary>
            public long Accumulator;

            /// <summary>Ticks elapsed since activation.</summary>
            public int Steps;
        }

        /// <summary>
        /// Blueprint <c>TickCore</c> shape. Accumulates by <see cref="Params.Stride"/> each tick so the
        /// slot's contents are distinguishable from <see cref="DemoAiPrimitiveNodes"/>'s plain tick
        /// counter: after N ticks this slot reads <c>Accumulator = Stride*N, Steps = N</c> while A's
        /// reads <c>Ticks = N</c>. Cross-talk between the two slots is therefore visible as a wrong
        /// number rather than as a coincidence.
        /// </summary>
        public static NodeStatus TickCore(
            ref Params p,
            ref WorkingState ws,
            Entity self,
            EntityRepository world,
            float time)
        {
            ws.Steps++;
            ws.Accumulator += p.Stride;
            return ws.Steps >= p.RunsNeeded ? NodeStatus.Success : NodeStatus.Running;
        }
    }
}
