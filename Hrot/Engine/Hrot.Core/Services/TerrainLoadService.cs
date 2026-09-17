using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.IG.Components;

namespace Hrot.Map.Common.Services
{
    /// <summary>
    /// One planned unit of terrain work: a zone entity, the footprint hash the work is FOR, and the
    /// world-space bounds to make resident.
    ///
    /// <para>⭐ It carries the hash rather than re-deriving it at stamp time on purpose: in the 2PC path
    /// minutes can pass between plan and commit, and the marker must record the footprint the tiles were
    /// actually built for — not whatever the zone looks like by the time the round lands. A zone edited
    /// mid-round therefore reads STALE afterwards, which is correct.</para>
    /// </summary>
    public readonly record struct ZoneLoadItem(
        Entity Zone, ulong FootprintHash, Vector2 BoundsMin, Vector2 BoundsMax);

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

            var item = PlanOne(repo, entity);
            if (item == null) return false;   // not a zone, or already resident for this footprint

            // Mark in-flight BEFORE building: a load can fail, and a bare flag could not say so.
            // ⚠ This stamp exists only on the LOCAL path. The 2PC path cannot make it — PrepareAsync
            //   must not mutate ECS — so there the marker goes straight to Loaded/Failed at commit.
            StampMarker(repo, entity, LoadPhase.Loading, item.Value.FootprintHash);

            bool ok;
            try
            {
                ok = BuildOne(item.Value);
            }
            catch (Exception)
            {
                StampMarker(repo, entity, LoadPhase.Failed, item.Value.FootprintHash);
                throw;
            }

            // ⭐ A node with nothing to do still STAMPS the marker. If a non-building node left it unset,
            //   its map would read "not loaded" forever — the exact error §9.1 retracts.
            StampMarker(repo, entity, ok ? LoadPhase.Loaded : LoadPhase.Failed, item.Value.FootprintHash);
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
            int worked = 0;
            foreach (var entity in CollectZones(repo))
                if (EnsureLoaded(repo, entity)) worked++;

            return worked;
        }

        // ── The three-part API the 2PC path needs, and that the one-shot path above is built from ──
        //
        // ⭐⭐ Why the split exists: the 2PC round must do its WORK in PrepareAsync (which may not touch
        //    ECS) and its ECS WRITES in Commit. EnsureLoaded cannot be used there — it does both. But
        //    re-deriving "is this zone stale?" in the handler would be a SECOND implementation of the one
        //    branch this whole design turns on (§3, "the idempotency check is the identical branch on
        //    both paths"). So the branch lives in Plan, alone, and both paths call it.

        /// <summary>
        /// ⭐ <b>READ-ONLY.</b> The zones that need work — the idempotency / staleness branch, and the
        /// only implementation of it. A zone already resident for its current footprint is absent from
        /// the result.
        /// </summary>
        /// <param name="zoneFilter">
        /// When non-null, only the zone whose id matches is considered (the one-op-per-zone round, §9.3).
        /// Null plans every zone.
        ///
        /// <para>⚠⚠ <b>The id is the zone entity's <c>NetworkIdentity.Value</c>, as a string — a
        /// DEVIATION from the design, and it is forced.</b> §9.5 draws a "zone name" column sourced from
        /// "the Area entity", but measured: no name component exists. <c>TkbIdentity</c> carries only
        /// <c>TkbType</c>, and nothing else on a zone entity is cluster-wide stable. <c>NetworkIdentity</c>
        /// is, it is already replicated, and the scenario round-trip preserves it (<c>CE-277(e)</c>), so
        /// every node resolves the same id to the same zone. ⛔ A name would be nicer for the operator and
        /// is what the UI section wants — but inventing one here would be a second identity for one thing.
        /// The string form keeps the wire stable if a name component is added later.</para>
        /// </param>
        public IReadOnlyList<ZoneLoadItem> Plan(EntityRepository repo, string? zoneFilter = null)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            var items = new List<ZoneLoadItem>();
            foreach (var entity in CollectZones(repo))
            {
                if (zoneFilter != null && !MatchesZoneId(repo, entity, zoneFilter)) continue;
                var item = PlanOne(repo, entity);
                if (item != null) items.Add(item.Value);
            }
            return items;
        }

        /// <summary>
        /// ⭐ <b>NO ECS.</b> Makes the planned coverage resident. Safe to call off the main thread, which
        /// is what <c>PrepareAsync</c> needs.
        /// </summary>
        /// <returns>The items that FAILED — empty when everything is resident.</returns>
        public IReadOnlyList<ZoneLoadItem> BuildStaged(IReadOnlyList<ZoneLoadItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            List<ZoneLoadItem>? failed = null;
            foreach (var item in items)
            {
                bool ok;
                try   { ok = BuildOne(item); }
                catch (Exception) { ok = false; }   // a throw is a failed zone, not a failed ROUND
                if (!ok) (failed ??= new List<ZoneLoadItem>()).Add(item);
            }
            return (IReadOnlyList<ZoneLoadItem>?)failed ?? Array.Empty<ZoneLoadItem>();
        }

        /// <summary>
        /// ⭐ <b>ECS WRITES ONLY.</b> Stamps the marker for each item. Main thread; this is the commit half.
        /// </summary>
        public void Stamp(EntityRepository repo, IEnumerable<ZoneLoadItem> items, LoadPhase phase)
        {
            if (repo == null)  throw new ArgumentNullException(nameof(repo));
            if (items == null) throw new ArgumentNullException(nameof(items));

            foreach (var item in items)
            {
                if (!repo.IsAlive(item.Zone)) continue;   // the zone was deleted while the round ran
                StampMarker(repo, item.Zone, phase, item.FootprintHash);
            }
        }

        private bool BuildOne(ZoneLoadItem item)
            => _tileLoader.Build(item.BoundsMin, item.BoundsMax, item.FootprintHash);

        /// <summary>The staleness branch for ONE entity. Null ⇒ not a zone, or nothing to do.</summary>
        private static ZoneLoadItem? PlanOne(EntityRepository repo, Entity entity)
        {
            if (!repo.IsAlive(entity)) return null;
            if (!repo.HasManagedComponent<EditablePolyline>(entity)) return null;

            var polyline = ((ISimulationView)repo).GetManagedComponentRO<EditablePolyline>(entity);
            var origin = repo.HasComponent<SimTransform>(entity)
                ? repo.GetComponent<SimTransform>(entity).Position
                : Vector3.Zero;

            ulong current = ZoneFootprint.Compute(origin, polyline?.Points);

            if (repo.HasComponent<TerrainAssetLoadState>(entity))
            {
                var marker = repo.GetComponent<TerrainAssetLoadState>(entity);
                if (marker.Phase == LoadPhase.Loaded && marker.SourceHash == current)
                    return null;   // resident for exactly this footprint — nothing to do
            }

            var (min, max) = FootprintBounds(origin, polyline?.Points);
            return new ZoneLoadItem(entity, current, min, max);
        }

        private static List<Entity> CollectZones(EntityRepository repo)
        {
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
            return zones;
        }

        /// <summary>
        /// The zone's cluster-wide id. See <see cref="Plan"/>'s remarks for why this is
        /// <see cref="NetworkIdentity"/> and not a name.
        /// </summary>
        public static string ZoneIdOf(EntityRepository repo, Entity entity)
            => repo.HasComponent<NetworkIdentity>(entity)
                ? repo.GetComponent<NetworkIdentity>(entity).Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

        private static bool MatchesZoneId(EntityRepository repo, Entity entity, string zoneId)
            => string.Equals(ZoneIdOf(repo, entity), zoneId, StringComparison.Ordinal);

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
