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
    public sealed class SwitchableNavmeshProvider : INavmeshProvider
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
            Volatile.Write(ref _inner, provider ?? Fallback);
            Interlocked.Increment(ref _generation);
        }

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

        // ⭐ R-219 — forward the caller's door table (the default interface methods would DROP it)
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => Current.PlanPath(from, to, waypoints, layerMask, doors);
        public bool PathExists(Vector3 from, Vector3 to, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => Current.PathExists(from, to, layerMask, doors);
        public float PathCost(Vector3 from, Vector3 to, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => Current.PathCost(from, to, layerMask, doors);
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
}
