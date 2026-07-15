using System.Runtime.InteropServices;
using Fbt.Kernel;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Slice 2a-2 demo Category-1 shared struct: a hand-written blittable struct used with the
    /// <c>GetShared</c>/<c>SetShared</c> blueprint graph nodes (NOT a generated <c>_Bp+WorkingState</c>).
    /// Decorated with <see cref="BlackboardDtoStructAttribute"/> so it is a valid Category-1 shared
    /// type for the blackboard authoring type picker, matching the convention used for other
    /// hand-written blackboard DTOs in this project (see <c>HillAttackDtos.cs</c>).
    /// </summary>
    [BlackboardDtoStruct]
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadRallyState
    {
        /// <summary>Accumulates once per tick via the SharedStateRallyDemo blueprint's GetShared/SetShared pair.</summary>
        public int RallyCount;
    }

    /// <summary>
    /// Tiny pure helper used by the SharedStateRallyDemo blueprint (Assets/Blueprints/SharedStateRallyDemo.bp.json)
    /// to increment <see cref="SquadRallyState.RallyCount"/> via a <c>FunctionCall</c> node, since the
    /// blueprint graph has no dedicated "get/set struct field" node for an arbitrary foreign struct.
    /// </summary>
    public static class SquadRallyStateOps
    {
        /// <summary>Returns a copy of <paramref name="state"/> with <c>RallyCount</c> incremented by one.</summary>
        public static SquadRallyState IncrementRallyCount(SquadRallyState state)
        {
            state.RallyCount += 1;
            return state;
        }
    }
}
