using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>How a mine left the field.</summary>
    public enum MineGone : byte { None = 0, Struck = 1, Cleared = 2 }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T1 (DESIGN_Minefields.md MF-3, MR-1, KR-3) — GROUND TRUTH per FIELD: the mines that went off or were cleared, as a list of lattice (row, col)
    /// indices (a lattice index survives a polygon edit), chunked: past <see cref="Capacity"/> the deciding node opens the next chunk. A runtime record entity the
    /// deciding node owns, recorded, replicated (<c>dtMinefieldStrikes</c>); ⛔ never per side. Mines inside a cleared LANE are cleared by geometry, not listed here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MinefieldStrikes)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct MinefieldStrikes
    {
        public const int Capacity = 96;

        /// <summary>The field's stable id (its <c>EntityGuid</c>).</summary>
        public Guid Field;
        /// <summary>Which chunk of the field's list this is (0, 1, …).</summary>
        public ushort Chunk;
        /// <summary>Entries in use.</summary>
        public byte Count;
        public fixed short Row[Capacity];
        public fixed short Col[Capacity];
        public fixed byte How[Capacity];

        public readonly (short Row, short Col, MineGone How) Get(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return (Row[i], Col[i], (MineGone)How[i]);
        }

        public void Set(int i, short row, short col, MineGone how)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            Row[i] = row; Col[i] = col; How[i] = (byte)how;
        }

        /// <summary>True when (row, col) is in this chunk.</summary>
        public readonly bool Has(short row, short col)
        {
            for (int i = 0; i < Count; i++) if (Row[i] == row && Col[i] == col) return true;
            return false;
        }
    }
}
