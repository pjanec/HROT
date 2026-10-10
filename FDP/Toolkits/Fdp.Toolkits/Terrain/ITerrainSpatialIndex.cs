using System;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-1035 Q0 (<c>docs/DESIGN_World_Query_Seam.md</c> WQ-D) — narrows which pieces and walkables a segment query on
    /// <see cref="TerrainWorld"/> examines. The stand-in's own spatial index; it is NOT the engine seam (that is
    /// <c>IWorldQuery</c>) and no caller outside the terrain stand-in sees it.
    /// </summary>
    public interface ITerrainSpatialIndex
    {
        /// <summary>
        /// Writes, in ASCENDING order, every piece index (prisms, then door leaves when the world has doors) and every walkable
        /// index whose bounds the segment <paramref name="from"/>→<paramref name="to"/> may touch. A superset of the true
        /// crossings is correct; missing one is a defect. When <paramref name="pieceTotal"/> differs from the layout the index
        /// was built for, it must return every piece.
        /// </summary>
        void Candidates(Vector3 from, Vector3 to, int pieceTotal,
            Span<int> pieces, out int pieceCount, Span<int> walkables, out int walkableCount);
    }
}
