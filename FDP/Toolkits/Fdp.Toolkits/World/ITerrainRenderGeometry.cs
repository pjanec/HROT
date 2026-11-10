using System.Collections.Generic;
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

namespace Fdp.Toolkit.World
{
    /// <summary>
    /// ⭐ CE-3423 (docs/DESIGN_Map_3D_Mode.md §6ah L-1, L-2) — the terrain's chunk grid: square chunks of <see cref="ChunkSize"/> metres from <see cref="Origin"/>,
    /// <see cref="Nx"/> × <see cref="Ny"/> of them; level k draws the ground at <see cref="Cell"/> × 2^k (k &lt; <see cref="Levels"/>). <see cref="MinZ"/> / <see cref="MaxZ"/>
    /// bound everything drawn (the culling box before a chunk is built).
    /// </summary>
    public sealed record TerrainChunkLayout(Vector2 Origin, float ChunkSize, float Cell, int Nx, int Ny, int Levels, float MinZ, float MaxZ)
    {
        public Vector2 ChunkMin(int cx, int cy) => Origin + new Vector2(cx * ChunkSize, cy * ChunkSize);
        public Vector2 ChunkMax(int cx, int cy) => ChunkMin(cx, cy) + new Vector2(ChunkSize, ChunkSize);
    }

    /// <summary>⭐ CE-3423 — the meshes of a chunk: its ground (one per level), what stands on it (prisms, slabs, water — one, any level), and ⭐ CE-4377 (§6ai)
    /// its trees drawn NEAR (one tree per wood cell's density, the lone trees' crowns) or FAR (the wood as a canopy sheet, the lone crowns) — presentation only.</summary>
    public enum TerrainChunkPart : byte { ChunkGround = 0, ChunkObjects = 1, ChunkTrees = 2, ChunkCanopy = 3 }

    /// <summary>
    /// ⭐ CE-3423 (docs/DESIGN_Map_3D_Mode.md §6ah) — a terrain that can hand out its triangles BY CHUNK and level of detail, so the 3-D map and its picker never build the
    /// whole soup (61 M triangles on a 5.5 km terrain at 1 m). Level 0 of a chunk is exactly the whole soup's triangles there; what is outside the chunk grid (the apron) is
    /// <see cref="BuildOutside"/>. ⭐ Same triangle tagging as <see cref="ITerrainRenderGeometry.BuildTagged"/>.
    /// </summary>
    public interface ITerrainChunkedGeometry : ITerrainRenderGeometry
    {
        TerrainChunkLayout Chunks { get; }

        void BuildChunk(int cx, int cy, int level, TerrainChunkPart part, out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials);

        /// <summary>The triangles outside the chunk grid (the apron round the terrain).</summary>
        void BuildOutside(out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials);

        /// <summary>
        /// ⭐ CE-4392 (DESIGN_Map_3D_Mode §6aj X-4) — what this view's damage draws, OPAQUE to the map (R-250/252): a new object only when the drawn damage changed, null when none.
        /// The map compares references and hands it back to <see cref="BuildChunk(int, int, int, TerrainChunkPart, object?, out Vector3[], out int[], out TerrainSurfaceKind[], out string?[])"/>.
        /// </summary>
        object? Damage => null;

        /// <summary>⭐ CE-4392 — the chunk as <paramref name="damage"/> (a <see cref="Damage"/> value) leaves it; a geometry with no damage builds the plain chunk.</summary>
        void BuildChunk(int cx, int cy, int level, TerrainChunkPart part, object? damage, out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials)
            => BuildChunk(cx, cy, level, part, out vertices, out indices, out kinds, out materials);

        /// <summary>⭐ CE-4392 X-3 — the chunks (<c>cy · Nx + cx</c>) whose objects differ between two <see cref="Damage"/> values (a superset is fine). Allocates — on a change only.</summary>
        void ChunksTouchedBy(object? before, object? after, List<int> into) { }
    }
}
