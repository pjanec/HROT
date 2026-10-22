using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>R-3039</c> S1 — a man's fighting-position sense (was <c>SquadSenseChanges._members</c>): the rounds in the current untenable
    /// window, the contact already reported as flanking him, and whether he is already counted down. On the man, on the node driving his
    /// squad; recorded, so a checkpoint restored into a fresh process keeps the edges — 📐 empty, every flank already reported is reported
    /// again and a window two rounds in starts from zero. Absent = <see cref="Fresh"/>. 📄 docs/DESIGN_System_State_Inventory.md §6 #13.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SquadMemberSense)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct SquadMemberSense
    {
        /// <summary>When the untenable window opened (−∞ = none open).</summary>
        public double WindowStart;
        public int Rounds;
        /// <summary>The contact already reported as flanking him (0 = none).</summary>
        public long Flanker;
        public byte Down;

        public static SquadMemberSense Fresh => new() { WindowStart = double.NegativeInfinity };
    }

    /// <summary>
    /// ⭐ <c>R-3039</c> S1 — a squad's sense on its commander (was <c>SquadSenseChanges._units</c>): the contacts already inside its
    /// engagement area (so each is raised once), whether a heard one is, and the "fired on from a structure" survey asked (SC-8). Absent =
    /// <see cref="Fresh"/>. 📄 docs/DESIGN_System_State_Inventory.md §6 #13.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SquadUnitSense)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct SquadUnitSense
    {
        /// <summary>The squad's contact pool holds 16 (<c>SquadContactPool</c>), so the inside set never holds more.</summary>
        public const int Capacity = 16;

        public fixed long Inside[Capacity];
        public byte InsideCount;
        public byte HeardInside;
        /// <summary>SC-8 — the point survey asked (−∞ = none) and when the last was asked.</summary>
        public double AskedAt;
        public double LastAsk;
        /// <summary>SC-8 — where the fire came from.</summary>
        public Vector3 Source;

        public static SquadUnitSense Fresh => new() { AskedAt = double.NegativeInfinity, LastAsk = double.NegativeInfinity };

        public bool Contains(long id)
        {
            for (int i = 0; i < InsideCount; i++) if (Inside[i] == id) return true;
            return false;
        }
    }
}
