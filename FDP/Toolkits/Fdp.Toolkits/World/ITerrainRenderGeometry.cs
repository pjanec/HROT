using System.Numerics;

namespace Fdp.Toolkit.World
{
    /// <summary>What a terrain triangle is, for drawing it (<c>docs/DESIGN_Map_3D_Mode.md</c> M4: colour by kind; textures later).</summary>
    public enum TerrainSurfaceKind : byte
    {
        Ground = 0,
        Wall = 1,
        Roof = 2,
        /// <summary>⭐ CE-1033 S4 — a water surface (drawn only; not part of the walkable soup).</summary>
        Water = 3,
    }

    /// <summary>
    /// ⭐ CE-1033 S1 / CE-1035 WQ-G (<c>docs/DESIGN_World_Query_Seam.md</c>) — the terrain as triangles to DRAW, for the editor's 3-D
    /// map. The stand-in builds it from its terrain mesh; a production engine from its scene — the map never sees which. 🔒 R-252:
    /// triangles and a kind per triangle, nothing about how the terrain is modelled.
    /// </summary>
    public interface ITerrainRenderGeometry
    {
        /// <summary>An object that changes whenever the geometry does (the caller rebuilds its mesh then).</summary>
        object Identity { get; }

        /// <summary>The triangles (Z-up, local metres): <paramref name="indices"/> in threes, one <paramref name="kinds"/> entry per triangle.</summary>
        void Build(out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds);

        /// <summary>
        /// ⭐ CE-1033 S4 (docs/DESIGN_Map_3D_Mode.md §6 S4 — "colours by surface and material; water") — <see cref="Build"/> plus a
        /// material name per triangle (null = plain) and the water surfaces as extra <see cref="TerrainSurfaceKind.Water"/> triangles.
        /// Default: <see cref="Build"/> with no materials and no water, so an implementation that knows neither still draws.
        /// </summary>
        void BuildTagged(out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials)
        {
            Build(out vertices, out indices, out kinds);
            materials = new string?[kinds.Length];
        }
    }
}
