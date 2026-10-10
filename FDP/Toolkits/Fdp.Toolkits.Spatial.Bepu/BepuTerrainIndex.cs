using System.Numerics;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Spatial.Bepu
{
    /// <summary>
    /// ⭐ CE-1035 Q0 (<c>docs/DESIGN_World_Query_Seam.md</c> WQ-D) — a bounding-volume tree (Bepu's <see cref="Tree"/>) over a
    /// <see cref="TerrainWorld"/>'s pieces (prisms, then door leaves) and walkables, so a segment query visits only the pieces
    /// whose 3-D box the segment touches instead of every piece. It only NARROWS (<see cref="ITerrainSpatialIndex"/>): the
    /// terrain's own per-piece maths still decides, so the answers are identical by construction.
    /// <para>⚠ Thread use: Bepu's <see cref="BufferPool"/> is not thread-safe and a ray cast may take its traversal stack from
    /// it, so every thread gets its own pool (<see cref="t_pool"/>). The tree itself is read-only after the build.</para>
    /// </summary>
    public sealed class BepuTerrainIndex : ITerrainSpatialIndex, IDisposable
    {
        /// <summary>Boxes are grown by this much so a flat slab or a hairline wall is never missed by the box test.</summary>
        public const float Margin = 1e-3f;

        private readonly BufferPool _buildPool = new();
        private Tree _tree;
        private readonly int[] _leafToItem;   // ≥ 0: piece index · < 0: ~walkable index
        private readonly int _pieceTotal;
        private readonly int _walkableTotal;

        [ThreadStatic] private static BufferPool? t_pool;

        private BepuTerrainIndex(TerrainWorld world)
        {
            var leaves = world.Doors.Count > 0 ? world.DoorLeaves : Array.Empty<TerrainPrism>();
            _pieceTotal = world.Prisms.Count + leaves.Count;
            _walkableTotal = world.Walkables.Count;
            int n = _pieceTotal + _walkableTotal;
            _leafToItem = new int[n];
            _tree = new Tree(_buildPool, Math.Max(1, n));
            if (n == 0) return;

            _buildPool.Take<BoundingBox>(n, out var bounds);
            int k = 0;
            for (int pi = 0; pi < _pieceTotal; pi++, k++)
            {
                var p = pi < world.Prisms.Count ? world.Prisms[pi] : leaves[pi - world.Prisms.Count];
                bounds[k] = Box(new Vector3(p.Min, p.BaseZ), new Vector3(p.Max, p.TopZ));
                _leafToItem[k] = pi;
            }
            for (int wi = 0; wi < _walkableTotal; wi++, k++)
            {
                var w = world.Walkables[wi];
                bounds[k] = Box(new Vector3(w.Min, w.MinZ), new Vector3(w.Max, w.MaxZ));
                _leafToItem[k] = ~wi;
            }
            _tree.SweepBuild(_buildPool, bounds.Slice(n));
            _buildPool.Return(ref bounds);
        }

        /// <summary>Builds the index for <paramref name="world"/> and attaches it (<see cref="TerrainWorld.SpatialIndex"/>).</summary>
        public static BepuTerrainIndex AttachTo(TerrainWorld world)
        {
            var index = new BepuTerrainIndex(world);
            world.SpatialIndex = index;
            return index;
        }

        private static BoundingBox Box(Vector3 min, Vector3 max)
            => new(Vector3.Min(min, max) - new Vector3(Margin), Vector3.Max(min, max) + new Vector3(Margin));

        /// <inheritdoc/>
        public unsafe void Candidates(Vector3 from, Vector3 to, int pieceTotal,
            Span<int> pieces, out int pieceCount, Span<int> walkables, out int walkableCount)
        {
            var d = to - from;
            if (pieceTotal != _pieceTotal || d.LengthSquared() < 1e-12f)
            {
                // a layout this index was not built for, or a point query: everything (a superset is always correct)
                for (int i = 0; i < pieceTotal; i++) pieces[i] = i;
                for (int i = 0; i < _walkableTotal; i++) walkables[i] = i;
                pieceCount = pieceTotal; walkableCount = _walkableTotal;
                return;
            }

            fixed (int* pp = pieces) fixed (int* pw = walkables) fixed (int* map = _leafToItem)
            {
                var tester = new Collector { Map = map, Pieces = pp, Walkables = pw };
                float maxT = 1f;   // the direction is the whole segment, so t ∈ [0, 1]
                _tree.RayCast(from, d, ref maxT, t_pool ??= new BufferPool(), ref tester);
                pieceCount = tester.PieceCount;
                walkableCount = tester.WalkableCount;
            }
            pieces[..pieceCount].Sort();         // ⭐ ascending — the order the full scan visits, so products and sorts match exactly
            walkables[..walkableCount].Sort();
        }

        private unsafe struct Collector : IRayLeafTester
        {
            public int* Map;
            public int* Pieces;
            public int* Walkables;
            public int PieceCount;
            public int WalkableCount;

            public void TestLeaf(int leafIndex, RayData* rayData, float* maximumT, BufferPool pool)
            {
                int item = Map[leafIndex];
                if (item >= 0) Pieces[PieceCount++] = item;
                else Walkables[WalkableCount++] = ~item;
            }
        }

        public void Dispose()
        {
            _tree.Dispose(_buildPool);
            _buildPool.Clear();
        }
    }
}
