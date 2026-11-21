using System.Runtime.InteropServices;
using Fdp.Core;

namespace CarKinem.Core
{
    /// <summary>
    /// ⭐ <c>R-3039</c> S1 — a walker's passage through the next doorway on his path (<c>DoorPassageSystem</c>): which door, whether he is held,
    /// the open command's timing and the replan quiet time. On the walker while a door is ahead; recorded, so a checkpoint restored into a fresh
    /// process carries a half-opened door on (it was a field of the system: lost there, the walker re-waited the whole open time or never let
    /// go). 📄 docs/DESIGN_System_State_Inventory.md §6 #4.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DoorPassage)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DoorPassage
    {
        /// <summary>The terrain door (index into <c>TerrainWorld.Doors</c>) this passage is about.</summary>
        public int DoorIndex;
        /// <summary>1 while the system holds him (<c>NavState.IsBlocked</c> set by it).</summary>
        public byte Holding;
        /// <summary>1 once the open command went out.</summary>
        public byte Sent;
        /// <summary>1 once a replan was asked for this door.</summary>
        public byte Replanned;
        /// <summary>Time spent opening before the command (s).</summary>
        public float Elapsed;
        /// <summary>Time since the open command (s) — the answer's timeout.</summary>
        public float SinceSent;
        /// <summary>Time since the last replan for this door (s).</summary>
        public float SinceReplan;
        /// <summary>The system's frame stamp: a passage not seen this frame belongs to a walker who left the query.</summary>
        public int Seen;
    }
}
