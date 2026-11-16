using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a minefield is laid with (docs/designs/minefields/DESIGN_Minefields.md MF-1, §6.1).</summary>
    public enum MineType : byte
    {
        None = 0,
        /// <summary>Anti-personnel: pressure, a few kg — any person or vehicle sets it off.</summary>
        AntiPersonnel = 1,
        /// <summary>Anti-tank: a vehicle's weight — a man walks over it; attacks the hull BOTTOM.</summary>
        AntiTank = 2,
        /// <summary>Both, alternating along the rows.</summary>
        Mixed = 3,
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T1 (DESIGN_Minefields.md MF-1, MF-2, MR-8, MR-18, §6.1) — ONE minefield: the parameters of its derived mines and its cleared LANES. The OUTLINE
    /// is the entity's ONE <c>EditablePolyline</c> (never stored here, MR-8); the mines are DERIVED (<see cref="MinePattern"/>), never stored one by one.
    /// Unmanaged, authored, saved, recorded, replicated (<c>dtMinefield</c>). On TKB <see cref="Minefields.TkbType"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Minefield)]
    public unsafe struct Minefield
    {
        /// <summary>How many lanes a field holds.</summary>
        public const int MaxLanes = 8;

        public MineType Type;
        /// <summary>Anti-handling devices fitted (a cleared mine may still go off; MR-15).</summary>
        public byte AntiHandling;
        /// <summary>Lanes in use (≤ <see cref="MaxLanes"/>).</summary>
        public byte LaneCount;
        /// <summary>The distance between two mines in a row, and between two rows (m).</summary>
        public float SpacingM;
        /// <summary>The direction the rows run, degrees (0 = east, counter-clockwise).</summary>
        public float OrientationDeg;
        /// <summary>The seed of the optional small jitter (0 = none — the exact lattice).</summary>
        public uint Seed;
        /// <summary>The sides that know this field at start, a bit per <c>ForceId</c> (own fields are known and marked, MF-15).</summary>
        public ushort KnownAtStartSides;

        // the lanes (MR-18): straight segments, world metres; ClearedBy = the side that cleared it + 1 (0 = authored); KnownToSides a bit per ForceId
        public fixed float LaneAx[MaxLanes];
        public fixed float LaneAy[MaxLanes];
        public fixed float LaneBx[MaxLanes];
        public fixed float LaneBy[MaxLanes];
        public fixed float LaneWidth[MaxLanes];
        public fixed byte LaneClearedBy[MaxLanes];
        public fixed ushort LaneKnownToSides[MaxLanes];

        /// <summary>Lane <paramref name="i"/> (0 ≤ i &lt; <see cref="LaneCount"/>).</summary>
        public readonly MinefieldLane Lane(int i)
        {
            if ((uint)i >= MaxLanes) throw new ArgumentOutOfRangeException(nameof(i));
            return new MinefieldLane(new System.Numerics.Vector2(LaneAx[i], LaneAy[i]), new System.Numerics.Vector2(LaneBx[i], LaneBy[i]), LaneWidth[i], LaneClearedBy[i], LaneKnownToSides[i]);
        }

        /// <summary>Writes lane <paramref name="i"/> (does not change <see cref="LaneCount"/>).</summary>
        public void SetLane(int i, in MinefieldLane lane)
        {
            if ((uint)i >= MaxLanes) throw new ArgumentOutOfRangeException(nameof(i));
            LaneAx[i] = lane.A.X; LaneAy[i] = lane.A.Y; LaneBx[i] = lane.B.X; LaneBy[i] = lane.B.Y; LaneWidth[i] = lane.Width;
            LaneClearedBy[i] = lane.ClearedBySidePlus1; LaneKnownToSides[i] = lane.KnownToSides;
        }
    }

    /// <summary>A cleared lane through a field: a straight segment <see cref="A"/>–<see cref="B"/> of <see cref="Width"/> (MR-18).</summary>
    public readonly record struct MinefieldLane(System.Numerics.Vector2 A, System.Numerics.Vector2 B, float Width, byte ClearedBySidePlus1, ushort KnownToSides);

    /// <summary>The minefield type's fixed ids (§3a).</summary>
    public static class Minefields
    {
        /// <summary>⭐ TKB 8830 <c>Minefield</c>, DIS 5.0.0.91, class <c>"Minefield"</c>.</summary>
        public const long TkbType = 8830;
        /// <summary>The navmesh area of a minefield (door 2, woods 3, crater 4; MR-3 composites built in MS-T3).</summary>
        public const byte NavArea = 5;
        /// <summary>The mines' munitions (§3a): anti-personnel, anti-tank, the line charge.</summary>
        public const int MunitionAntiPersonnel = 6051, MunitionAntiTank = 6052, MunitionLineCharge = 6053;
        /// <summary>⭐ TKB 116 the mine-clearing vehicle, DIS 1.1.*.9.2, class <c>"MineClearer"</c>.</summary>
        public const long ClearerTkbType = 116;
        /// <summary>The DDS ordinals (<c>EDescriptorType</c> in the NED assembly, which this toolkit cannot reference; a rail pins them).</summary>
        public const long MinefieldDescriptorOrdinal = 191, StrikesDescriptorOrdinal = 192, KnowledgeDescriptorOrdinal = 193;
    }
}
