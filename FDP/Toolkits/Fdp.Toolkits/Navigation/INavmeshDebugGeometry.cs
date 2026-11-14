using System;
using System.Numerics;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐ <c>CE-3133</c> — a navmesh provider that can hand out its baked polygons for DRAWING (the map's Navmesh layer). Plain
    /// engine-space vertices, so a drawer needs no navmesh library. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5c N1.
    /// </summary>
    public interface INavmeshDebugGeometry
    {
        /// <summary>The polygons of <paramref name="layer"/> (one single-bit layer) as baked now, or null when that layer has no mesh.
        /// The same object until the navmesh changes.</summary>
        NavmeshDebugMesh? DebugMesh(NavLayerMask layer);
    }

    /// <summary>
    /// ⭐ <c>CE-3133</c> — one layer's baked navmesh polygons, immutable: polygon <c>i</c>'s outline is
    /// <c>Vertices[PolyStart[i] .. PolyStart[i + 1])</c> (engine X east, Y north, Z up), <see cref="DoorIndex"/>[i] is the door its
    /// doorway polygon belongs to (an index into <c>TerrainWorld.Doors</c>, like <c>DoorStates</c>) or −1 for ground.
    /// </summary>
    public sealed class NavmeshDebugMesh
    {
        public NavmeshDebugMesh(NavLayerMask layer, uint version, Vector3[] vertices, int[] polyStart, int[] doorIndex)
        {
            if (polyStart.Length != doorIndex.Length + 1)
                throw new ArgumentException("polyStart needs one entry more than there are polygons", nameof(polyStart));
            Layer = layer; Version = version; Vertices = vertices; PolyStart = polyStart; DoorIndex = doorIndex;
        }

        public NavLayerMask Layer { get; }
        /// <summary>The navmesh version it was exported from.</summary>
        public uint Version { get; }
        public Vector3[] Vertices { get; }
        public int[] PolyStart { get; }
        public int[] DoorIndex { get; }
        public int PolyCount => DoorIndex.Length;

        /// <summary>Polygon <paramref name="i"/>'s outline.</summary>
        public ReadOnlySpan<Vector3> Polygon(int i) => Vertices.AsSpan(PolyStart[i], PolyStart[i + 1] - PolyStart[i]);
    }
}
