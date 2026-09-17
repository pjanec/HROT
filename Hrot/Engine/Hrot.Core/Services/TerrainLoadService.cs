using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.IG.Components;

namespace Hrot.Map.Common.Services
{
    /// <summary>
    /// THE one implementation of "make this zone's terrain data resident", used by BOTH invocation
    /// paths — the local scenario-load call and the cluster 2PC round.
    ///
    /// <para><b>⭐ One implementation is the point.</b> The 2PC round is an <i>invocation wrapper</i> the
    /// scenario-load path deliberately does not use (it is already inside the cluster's own load
    /// transaction, and a nested 2PC would deadlock). Because both paths land here, the idempotency check
    /// is the identical branch on both, so "reload" and "load on scenario open" cannot drift apart.</para>
    ///
    /// <para><b>⭐ Idempotency and staleness are ONE check, and it needs no writer's cooperation.</b> The
    /// marker carries the footprint hash the resident data was loaded for; this recomputes the entity's
    /// CURRENT footprint and compares. Equal ⇒ skip. Different ⇒ rebuild. ⛔ Not a counter: points are
    /// relative, so a MOVE leaves them byte-identical, and the one counter that exists
    /// (<c>EditablePolyline.Version</c>) is never incremented and is reset by the edit tool.</para>
    ///
    /// <para>⚠ <b>DEVIATION from the design's signature, argued:</b> the design draws
    /// <c>EnsureLoaded(view, entity)</c>. This takes an <see cref="EntityRepository"/>, because the
    /// service must WRITE the marker and <c>ISimulationView</c> is read-only apart from a deferred
    /// command buffer — staging the marker would make it invisible to the very next iteration of
    /// <see cref="EnsureAllLoaded"/> and break idempotency WITHIN one call. Both callers are main-thread
    /// with the live repository, so nothing is lost.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §3, §3.2, §9.7.
    /// </summary>
    public sealed class TerrainLoadService
    {
        private readonly IZoneTileLoader _tileLoader;

        /// <param name="tileLoader">
        /// What actually makes coverage resident. In slice 1 this is the announcing fake
        /// (<see cref="AnnouncingZoneTileLoader"/>), which logs its stub-ness on every round.
        /// </param>
        public TerrainLoadService(IZoneTileLoader tileLoader)
            => _tileLoader = tileLoader ?? throw new ArgumentNullException(nameof(tileLoader));

        /// <summary>
        /// Makes one zone entity's terrain data resident, if it is not already resident for the zone's
        /// CURRENT footprint.
        /// </summary>
        /// <returns>
        /// <c>true</c> when work was done (the zone was missing or stale), <c>false</c> when the call was
        /// a no-op because the marker already matched. ⚠ <c>false</c> is the SUCCESS case for a repeat
        /// call — it is what "idempotent" means here, not a failure.
        /// </returns>
        public bool EnsureLoaded(EntityRepository repo, Entity entity)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (!repo.IsAlive(entity)) return false;
            if (!repo.HasManagedComponent<EditablePolyline>(entity)) return false;

            var polyline = ((ISimulationView)repo).GetManagedComponentRO<EditablePolyline>(entity);
            var origin = repo.HasComponent<SimTransform>(entity)
                ? repo.GetComponent<SimTransform>(entity).Position
                : Vector3.Zero;

            ulong current = ZoneFootprint.Compute(origin, polyline?.Points);

            // ── The idempotency / staleness branch, identical on both invocation paths ──
            if (repo.HasComponent<TerrainAssetLoadState>(entity))
            {
                var marker = repo.GetComponent<TerrainAssetLoadState>(entity);
                if (marker.Phase == LoadPhase.Loaded && marker.SourceHash == current)
                    return false;   // resident for exactly this footprint — nothing to do
            }

            // Mark in-flight BEFORE building: a load can fail, and a bare flag could not say so.
            StampMarker(repo, entity, LoadPhase.Loading, current);

            var (min, max) = FootprintBounds(origin, polyline?.Points);
            bool ok;
            try
            {
                ok = _tileLoader.Build(min, max, current);
            }
            catch (Exception)
            {
                StampMarker(repo, entity, LoadPhase.Failed, current);
                throw;
            }

            // ⭐ A node with nothing to do still STAMPS the marker. If a non-building node left it unset,
            //   its map would read "not loaded" forever — the exact error §9.1 retracts.
            StampMarker(repo, entity, ok ? LoadPhase.Loaded : LoadPhase.Failed, current);
            return true;
        }

        /// <summary>
        /// Makes every zone entity's terrain data resident.
        /// </summary>
        /// <returns>How many zones actually needed work — <c>0</c> on a repeat call with no edits.</returns>
        public int EnsureAllLoaded(EntityRepository repo)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            // ⚠ Materialise first: EnsureLoaded ADDS a component, which is a structural mutation, and
            //   iterating a query while mutating it is not something to rely on.
            var zones = new List<Entity>();
            var query = repo.Query()
                .With<TkbIdentity>()
                .WithManaged<EditablePolyline>()
                .Build();

            foreach (var entity in query)
            {
                if (repo.GetComponent<TkbIdentity>(entity).TkbType == TkbEntityTypes.TerrainZone)
                    zones.Add(entity);
            }

            int worked = 0;
            foreach (var entity in zones)
                if (EnsureLoaded(repo, entity)) worked++;

            return worked;
        }

        private static void StampMarker(EntityRepository repo, Entity entity, LoadPhase phase, ulong hash)
        {
            var marker = new TerrainAssetLoadState { Phase = phase, SourceHash = hash };
            if (repo.HasComponent<TerrainAssetLoadState>(entity))
                repo.SetComponent(entity, marker);
            else
                repo.AddComponent(entity, marker);
        }

        /// <summary>
        /// The zone's world-space AABB — <c>origin + relative points</c>. An empty polyline degenerates to
        /// the origin, which is correct: a zone with no shape covers nothing.
        /// </summary>
        private static (Vector2 Min, Vector2 Max) FootprintBounds(
            Vector3 origin, IReadOnlyList<Vector2>? points)
        {
            var o = new Vector2(origin.X, origin.Y);
            if (points == null || points.Count == 0) return (o, o);

            var min = o + points[0];
            var max = min;
            for (int i = 1; i < points.Count; i++)
            {
                var p = o + points[i];
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return (min, max);
        }
    }
}
