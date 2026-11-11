using System;
using System.Numerics;
using System.Threading;
using Fdp.Toolkit.Navigation.EngineBacked;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐⭐ <b>The node's ONE navmesh, swappable when a terrain loads</b> (docs/DESIGN_Terrain_World.md §4.1, W6).
    /// <para>⭐ Mirrors <c>RoadNetworkHolder</c>: the same instance is the <see cref="INavmeshProvider"/> singleton AND
    /// the navmesh the SlowBackground <c>NavigationSolverModule</c> was constructed with — a background module's view
    /// has no singleton API, so a singleton SWAP would be invisible to it. A terrain commit calls
    /// <see cref="Publish"/>; every reader sees the baked mesh from its next query.</para>
    /// <para>⭐ Before any terrain is baked it answers like <see cref="EngineBackedNavmeshProvider"/> (straight lines,
    /// everywhere walkable) — the behaviour every host had before this class.</para>
    /// <para>⚠ <see cref="QueryVersion"/> advances on every publish, so a path planned against the old mesh is
    /// recognisably stale.</para>
    /// </summary>
    public sealed class SwitchableNavmeshProvider : INavmeshProvider, INavmeshDebugGeometry, INavmeshTileVersions
    {
        private static readonly INavmeshProvider Fallback = new EngineBackedNavmeshProvider();

        private INavmeshProvider _inner = Fallback;
        private uint _generation;

        /// <summary>The provider answering now (the fallback until a terrain is published).</summary>
        public INavmeshProvider Current => Volatile.Read(ref _inner);

        /// <summary>True once a baked navmesh has been published.</summary>
        public bool HasBakedMesh => !ReferenceEquals(Current, Fallback);

        /// <summary>Installs <paramref name="provider"/>; null reverts to the straight-line fallback (terrain unload).</summary>
        public void Publish(INavmeshProvider? provider)
        {
            if (ReferenceEquals(provider, this)) throw new ArgumentException("A switchable provider cannot wrap itself.");
            // ⭐ CE-4391 S-3 (P3-a) — per-tile versions: a tile whose content key is the one of the previous publish keeps its version; a new or changed tile gets the next sequence.
            //   ⛔ Computed BEFORE the swap so a reader of the new provider never sees a stale tile table. One writer (the commit on the main thread).
            Volatile.Write(ref _tiles, NextTiles(Volatile.Read(ref _tiles), provider as INavmeshTileKeys));
            Volatile.Write(ref _inner, provider ?? Fallback);
            Interlocked.Increment(ref _generation);
        }

        private sealed class TileTable
        {
            public readonly float Metres;
            public readonly uint Sequence;
            public readonly System.Collections.Generic.IReadOnlyDictionary<long, ulong> Keys;
            public readonly System.Collections.Generic.Dictionary<long, uint> Versions;
            public TileTable(float metres, uint sequence, System.Collections.Generic.IReadOnlyDictionary<long, ulong> keys, System.Collections.Generic.Dictionary<long, uint> versions)
            { Metres = metres; Sequence = sequence; Keys = keys; Versions = versions; }
        }

        private static readonly TileTable NoTiles = new(0f, 0u, new System.Collections.Generic.Dictionary<long, ulong>(), new System.Collections.Generic.Dictionary<long, uint>());
        private TileTable _tiles = NoTiles;

        private static TileTable NextTiles(TileTable old, INavmeshTileKeys? keys)
        {
            if (keys == null || keys.TileMetres <= 0f) return new TileTable(0f, old.Sequence, NoTiles.Keys, NoTiles.Versions);   // a provider with no tile grid: versions fall away
            bool sameGrid = old.Metres == keys.TileMetres;
            uint seq = old.Sequence;
            var versions = new System.Collections.Generic.Dictionary<long, uint>(keys.TileKeys.Count);
            bool changed = false;
            foreach (var (tile, key) in keys.TileKeys)
            {
                if (sameGrid && old.Keys.TryGetValue(tile, out var before) && before == key && old.Versions.TryGetValue(tile, out uint v)) { versions[tile] = v; continue; }
                if (!changed) { seq++; changed = true; }   // one sequence step per publish: every tile this rebake changed shares it
                versions[tile] = seq;
            }
            if (sameGrid)   // a tile that vanished (it had polygons, now none) also changed what a path through it can be
                foreach (var tile in old.Keys.Keys)
                    if (!keys.TileKeys.ContainsKey(tile)) { if (!changed) { seq++; changed = true; } versions[tile] = seq; }
            return new TileTable(keys.TileMetres, seq, keys.TileKeys, versions);
        }

        /// <inheritdoc/>
        public float TileMetres => Volatile.Read(ref _tiles).Metres;
        /// <inheritdoc/>
        public uint ChangeSequence => Volatile.Read(ref _tiles).Sequence;
        /// <inheritdoc/>
        public uint TileVersion(int tileX, int tileZ)
            => Volatile.Read(ref _tiles).Versions.TryGetValue(((long)tileX << 32) | (uint)tileZ, out uint v) ? v : 0u;

        public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF) => Current.IsWalkable(position, layerMask);

        public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
            => Current.ProjectToNavmesh(position, out snapped, layerMask);

        public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF)
            => Current.SampleNavmeshPoints(center, radius, results, layerMask);

        public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => Current.PathExists(from, to, layerMask);

        public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => Current.PathCost(from, to, layerMask);

        /// <summary>The inner version, offset by the publish count so a swap is always a new version.</summary>
        public uint QueryVersion() => unchecked((Volatile.Read(ref _generation) << 16) + Current.QueryVersion());

        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
            => Current.PlanPath(from, to, waypoints, layerMask);

        /// <summary>⭐ <c>CE-3133</c> — the published provider's polygons (null before a bake, or when it cannot export).</summary>
        public NavmeshDebugMesh? DebugMesh(NavLayerMask layer) => (Current as INavmeshDebugGeometry)?.DebugMesh(layer);

        // ⭐ R-219 — forward the caller's door table (the default interface methods would DROP it)
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => Current.PlanPath(from, to, waypoints, layerMask, doors);
        public bool PathExists(Vector3 from, Vector3 to, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => Current.PathExists(from, to, layerMask, doors);
        public float PathCost(Vector3 from, Vector3 to, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => Current.PathCost(from, to, layerMask, doors);
        // ⭐ CE-3279 (b) — and the one-search batch (the default would fall back to a path per point)
        public int PathCosts(Vector3 from, ReadOnlySpan<Vector3> to, Span<float> costs, float reach, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => Current.PathCosts(from, to, costs, reach, layerMask, doors);
    }

    /// <summary>
    /// ⭐ <b>Builds a navmesh from the terrain world</b> — the seam that keeps DotRecast out of <c>Hrot.Core</c>
    /// (docs/DESIGN_Terrain_World.md §3). Implemented by <c>RecastNavmeshFactory</c> in
    /// <c>Fdp.Toolkits.Navigation.Recast</c>; called OFF the main thread from <c>TerrainResidency.Prepare</c>.
    /// </summary>
    public interface INavmeshFactory
    {
        /// <summary>Bakes <paramref name="world"/>; null when it has nothing walkable. Throws on a bake failure.</summary>
        INavmeshProvider? Build(TerrainWorld world);
    }

    /// <summary>
    /// ⭐ O-7 A (docs/DESIGN_Terrain_Authoring.md §18e) — a factory that can take WHERE the world changed since its previous bake and skip the tiles outside it. <see cref="BakeSerial"/> counts its bakes, so a caller
    /// that remembers the serial of the bake it committed knows the factory's "previous" still describes the world it is diffing against (an abandoned staged bake moves the serial ⇒ a full bake, never a wrong one).
    /// </summary>
    public interface IIncrementalNavmeshFactory : INavmeshFactory
    {
        long BakeSerial { get; }

        /// <summary><paramref name="dirty"/>: engine XY rectangles that changed; null = unknown = a full bake.</summary>
        INavmeshProvider? Build(TerrainWorld world, System.Collections.Generic.IReadOnlyList<(System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)>? dirty);
    }
}
