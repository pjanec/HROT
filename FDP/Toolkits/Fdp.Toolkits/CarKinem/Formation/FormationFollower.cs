using System.Runtime.InteropServices;
using Fdp.Core;

namespace CarKinem.Formation
{
    /// <summary>
    /// Formation follower component (attached to follower entities).
    /// The tactical command link (who issued the order) is in UnitSubordinate.Commander (Hrot layer).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FormationFollower)]
    public struct FormationFollower
    {
        public ushort SlotIndex;            // Which slot in template (0-15)
        public FormationMemberState State;  // Current formation state
        public byte IsInFormation;          // 1 = active member, 0 = inactive

        // State tracking
        public float SlotDistFiltered;      // Low-pass filtered distance to slot
        public float RejoinTimer;           // Time spent in Rejoining state
    }
}
