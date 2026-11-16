using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ <c>CE-4624</c> (§6.1) — a mine went off under <see cref="By"/>. Raised by the trigger (the deciding node), read by the knowledge writer.</summary>
    [EventId(TerrainEventIds.MineStruck)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MineStruck
    {
        public Entity Field;
        public short Row;
        public short Col;
        public Entity By;
        public float X;
        public float Y;
        public SimInstant At;
    }

    /// <summary>⭐ <c>CE-4624</c> (§6.1) — a recon team FULLY swept cells of a field (MR-7): each cell known-mined or known-clear for <see cref="SidePlus1"/>.
    /// Raised by the recon brain through the batch buffer (behaviors), applied by the serial knowledge writer.</summary>
    [EventId(TerrainEventIds.MinefieldCellsSwept)]
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MinefieldCellsSwept
    {
        public const int MaxCells = 16;
        public Entity Field;
        public byte SidePlus1;
        public byte Count;
        public fixed byte Col[MaxCells];
        public fixed byte Row[MaxCells];
        /// <summary>Per cell: <see cref="MineCellKnowledge.KnownMined"/> or <see cref="MineCellKnowledge.KnownClear"/>.</summary>
        public fixed byte Found[MaxCells];
        public Entity By;
    }

    /// <summary>⭐ <c>CE-4624</c> (§6.1) — <see cref="SidePlus1"/> marked the field (MF-13 ③): known in full to that side, and learnable by any side seeing its edge (MR-13).</summary>
    [EventId(TerrainEventIds.MinefieldMarked)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MinefieldMarked
    {
        public Entity Field;
        public byte SidePlus1;
        public Entity By;
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T4 (MR-15, MR-17) — a mine clearer (a plough, a flail, a line charge) CLEARED mines of <see cref="Field"/>: up to <see cref="MaxMines"/> lattice (row, col) per event. Raised by the serial
    /// trigger (the deciding node), read by <see cref="MinefieldStrikesSystem"/> as <see cref="MineGone.Cleared"/> — a cleared mine goes off for nobody, teaches nobody (no knowledge, no burst of its own).
    /// </summary>
    [EventId(TerrainEventIds.MinesCleared)]
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MinesCleared
    {
        public const int MaxMines = 16;
        public Entity Field;
        public byte Count;
        public fixed short Row[MaxMines];
        public fixed short Col[MaxMines];
    }

    /// <summary>⭐ <c>CE-4624</c> (§6.1) — a lane through a field was cleared (MS-T4): its mines are gone for every side; the lane is known to the clearing side.</summary>
    [EventId(TerrainEventIds.LaneCleared)]
    [StructLayout(LayoutKind.Sequential)]
    public struct LaneCleared
    {
        public Entity Field;
        /// <summary>The authored lane's index when an authored lane was cleared, else 255 (a new runtime lane: the geometry below).</summary>
        public byte Lane;
        public byte SidePlus1;
        // ⭐ MS-T4 (MR-18) — the runtime lane's straight segment and width (world metres); additive to the §6.1 shape
        public float Ax, Ay, Bx, By, Width;
    }
}
