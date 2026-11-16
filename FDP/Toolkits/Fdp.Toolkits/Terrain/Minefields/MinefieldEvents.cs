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

    /// <summary>⭐ <c>CE-4624</c> (§6.1) — a lane through a field was cleared (MS-T4): its mines are gone for every side; the lane is known to the clearing side.</summary>
    [EventId(TerrainEventIds.LaneCleared)]
    [StructLayout(LayoutKind.Sequential)]
    public struct LaneCleared
    {
        public Entity Field;
        public byte Lane;
        public byte SidePlus1;
    }
}
