using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.IG.Components;
using Hrot.Map.Common;

namespace Hrot.Presentation.Zones;

/// <summary>What a zones list shows per row — the LOCAL answer, which is the only honest one (§9.1).</summary>
public enum ZoneLoadState
{
    /// <summary>No marker at all: this node has never made the zone's terrain resident.</summary>
    NotLoaded = 0,

    /// <summary>Resident, and resident for the zone's CURRENT shape.</summary>
    Loaded,

    /// <summary>⚠ Resident, but for a DIFFERENT footprint — the zone was moved or reshaped since.</summary>
    Stale,

    /// <summary>A round is in flight for this zone on this node.</summary>
    Loading,

    /// <summary>⛔ The load was attempted and failed. §8.3 N3: broken, not static.</summary>
    Failed,
}

/// <summary>One row of the zones view.</summary>
/// <param name="ZoneId">
///   The zone's cluster-wide id — its <c>NetworkIdentity</c> as a string, which is what the op carries.
///   ⚠ Also the display name today; see <see cref="ZoneStatusReader"/>'s remarks.
/// </param>
/// <param name="Entity">The zone entity, for select / zoom-to.</param>
/// <param name="State">The LOCAL load state.</param>
public readonly record struct ZoneStatusRow(string ZoneId, Entity Entity, ZoneLoadState State);

/// <summary>
/// ⭐⭐⭐ <c>E3</c>'s testable core: turns the world's zone entities into rows with a load state.
///
/// <para>⭐⭐ <b>Deliberately free of ImGui</b>, exactly as <c>SharedContextMenuPopulator</c> is: the
/// interesting logic here is the STATE COMPUTATION — particularly <see cref="ZoneLoadState.Stale"/>,
/// which is derived rather than stored — and that can then be asserted without an active render
/// frame.</para>
///
/// <para>⭐ <b>Staleness is computed, not read.</b> The marker records the footprint hash the data was
/// built FOR; this recomputes the CURRENT footprint and compares. ⇒ reshaping a loaded zone makes its
/// row read <c>Stale</c> with no writer's cooperation, which is why the key is a hash and not a counter
/// (§5.3, §9.7 ③c). ⚠ The identical branch backs the gizmo's stroke (<c>E1</c>) — one policy, two
/// surfaces.</para>
///
/// <para>⚠ <b>The "name" is the id, and that is a stated DEVIATION.</b> §9.5's table wants a zone NAME
/// from the Area entity; measured, no name component exists — <c>TkbIdentity</c> carries only
/// <c>TkbType</c>. ⇒ rows are keyed and labelled by <c>NetworkIdentity</c>, the only cluster-wide stable
/// identifier a zone has. 📄 design §10.2 (AS-BUILT).</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.5, §9.1, §5.3.
/// </summary>
public static class ZoneStatusReader
{
    /// <summary>
    /// Every zone in the world, with its LOCAL load state.
    /// </summary>
    /// <param name="inFlightZoneIds">
    ///   ⭐ Zone ids with a cluster round currently pending, which read <see cref="ZoneLoadState.Loading"/>.
    ///   ⛔⛔ This comes from the caller's own request tracking, NEVER from
    ///   <c>ClusterMaster.HasInFlightTransaction</c> — design §9.4 measured that property answering
    ///   <c>false</c> while rounds are genuinely pending, because <c>_activeTransaction</c> is cleared in
    ///   the same method that sets it. A progress indicator sourced from it would read "idle" throughout
    ///   every load.
    /// </param>
    public static List<ZoneStatusRow> Read(
        EntityRepository repo,
        IReadOnlySet<string>? inFlightZoneIds = null)
    {
        var rows = new List<ZoneStatusRow>();
        if (repo == null) return rows;

        var query = repo.Query()
            .With<TkbIdentity>()
            .WithManaged<EditablePolyline>()
            .Build();

        foreach (var entity in query)
        {
            if (repo.GetComponent<TkbIdentity>(entity).TkbType != TkbEntityTypes.TerrainZone) continue;

            string id = repo.HasComponent<NetworkIdentity>(entity)
                ? repo.GetComponent<NetworkIdentity>(entity).Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            rows.Add(new ZoneStatusRow(id, entity, StateOf(repo, entity, id, inFlightZoneIds)));
        }

        return rows;
    }

    /// <summary>True when at least one row is actionable — what the header's "Load all stale" needs.</summary>
    public static bool AnyStale(IReadOnlyList<ZoneStatusRow> rows)
    {
        foreach (var r in rows)
            if (r.State is ZoneLoadState.Stale or ZoneLoadState.NotLoaded or ZoneLoadState.Failed)
                return true;
        return false;
    }

    private static ZoneLoadState StateOf(
        EntityRepository repo, Entity entity, string zoneId, IReadOnlySet<string>? inFlight)
    {
        // ⭐ In flight wins over the marker: the marker still shows the PREVIOUS outcome, and an operator
        //   watching a round they just started needs to see that it is running.
        if (inFlight != null && zoneId.Length > 0 && inFlight.Contains(zoneId))
            return ZoneLoadState.Loading;

        if (!repo.HasComponent<TerrainAssetLoadState>(entity))
            return ZoneLoadState.NotLoaded;

        var marker = repo.GetComponent<TerrainAssetLoadState>(entity);
        switch (marker.Phase)
        {
            case LoadPhase.Failed:  return ZoneLoadState.Failed;
            case LoadPhase.Loading: return ZoneLoadState.Loading;
            case LoadPhase.Loaded:
            {
                var polyline = ((ISimulationView)repo).GetManagedComponentRO<EditablePolyline>(entity);
                var origin   = repo.HasComponent<SimTransform>(entity)
                    ? repo.GetComponent<SimTransform>(entity).Position
                    : Vector3.Zero;

                ulong current = ZoneFootprint.Compute(origin, polyline?.Points);
                return marker.SourceHash == current ? ZoneLoadState.Loaded : ZoneLoadState.Stale;
            }
            default: return ZoneLoadState.NotLoaded;
        }
    }
}
