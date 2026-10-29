using System;
using System.Collections.Generic;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Terrain;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ <c>AQ107 C</c> (R-4094) — <b>the master publishes the origin of the NED wire's local frame.</b> 🔒 User, 2026-10-27: <i>"there could be
/// multiple CGF nodes and only one needs to publish the georeference, so it must be the orchestrator. And also the orchestrator can load the
/// terrain header which contains the georeference."</i> 📄 docs/blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md §4 row C, §5a.
/// <para>⭐ Two sources, ONE publisher: <b>a LOAD</b> — the loaded terrain's declared origin, read from the same <see cref="TerrainCatalog"/> the nodes
/// resolve through (locate + parse are the catalog's, ⛔ no second parser); <b>a REPLAY entry</b> — the recording's own origin, which each node's
/// prepare result carries (<see cref="ReplayPrepareResult.GeoOrigin"/>, aggregated by <see cref="ReplayConsensusAggregator"/>). Both go through
/// <see cref="PublishGeoReference"/>, which keeps <c>Generation</c> (+1 per DIFFERENT origin) and writes nothing for an unchanged sample.</para>
/// <para>⭐ The load's origin is published when the load's round SUCCEEDS (the terrain is committed), never at the fan-out — a load that fails
/// must not leave a published origin the nodes never adopted.</para>
/// </summary>
public sealed partial class ClusterMaster
{
    /// <summary>A load's terrain origin, parked until its round completes — keyed by the transition's transaction id.</summary>
    private readonly Dictionary<Guid, (TerrainGeoOrigin Origin, string TerrainName)> _pendingGeoReferences = new();

    private bool _geoReferencePublished;
    private TerrainGeoOrigin _lastGeoOrigin;
    private string _lastGeoTerrainName = string.Empty;
    private int _geoReferenceGeneration;

    /// <summary>⭐ AQ107 C — the <c>Generation</c> of the last published <c>GeoReference</c> (0 = none yet). Test/diagnostic surface.</summary>
    public int GeoReferenceGeneration => _geoReferenceGeneration;

    /// <summary>
    /// ⭐ Called while a load is fanned out (a world-boundary load only — a branch from a replay creates no world and keeps its origin):
    /// reads the named terrain's declared origin and parks it for <see cref="OnRoundSucceededForGeoReference"/>. A scenario naming no terrain
    /// parks 0,0,0 (R-229: <i>"Missing geo = zeros"</i>, the nodes' own rule — <c>TerrainResidency.ApplyOriginAhead</c>).
    /// ⛔ A terrain the master cannot read must NOT fail the load (the nodes resolve their own copy): it logs and publishes nothing.
    /// </summary>
    private void NoteLoadGeoReference(Guid transactionId, string? terrainName)
    {
        if (string.IsNullOrWhiteSpace(terrainName))
        {
            _pendingGeoReferences[transactionId] = (TerrainGeoOrigin.Zero, string.Empty);
            return;
        }

        try
        {
            var definition = TerrainCatalog.ForNode(localStagingRoot: null, nasRoot: _config.NasBasePath).ReadDefinition(terrainName!);
            if (definition == null)
            {
                FdpLog<ClusterMaster>.Warn(
                    "[Orchestrator] AQ107 C: terrain '{0}' was not found on the master (NAS '{1}' or the shipped terrains) — no GeoReference is published for this load; the nodes resolve their own copy.",
                    terrainName, _config.NasBasePath);
                return;
            }
            if (definition.Origin is null)
                FdpLog<ClusterMaster>.Warn(
                    "[Orchestrator] AQ107 C: terrain '{0}' declares no 'origin' — its GeoReference is 0,0,0 (lat, lon, alt).", terrainName);
            _pendingGeoReferences[transactionId] = (definition.Origin ?? TerrainGeoOrigin.Zero, terrainName!);
        }
        catch (Exception ex)
        {
            FdpLog<ClusterMaster>.Error(
                "[Orchestrator] AQ107 C: could not read the origin of terrain '{0}' ({1}) — no GeoReference is published for this load.",
                terrainName, ex.Message);
        }
    }

    /// <summary>
    /// ⭐ A round completed successfully: publish the origin its load parked, or — for a replay's prepare round — the recording's origin
    /// the aggregate carries. <paramref name="parked"/> is what <see cref="NoteLoadGeoReference"/> parked under this round, if anything.
    /// </summary>
    private void OnRoundSucceededForGeoReference(
        (TerrainGeoOrigin Origin, string TerrainName)? parked, object? aggregated)
    {
        if (parked is { } load)
            PublishGeoReference(load.Origin, load.TerrainName, "terrain load");
        else if (aggregated is ReplayPrepareResult { GeoOrigin: { } recorded } replay)
            PublishGeoReference(new TerrainGeoOrigin(recorded.Lat, recorded.Lon, recorded.Alt), replay.TerrainName ?? string.Empty, "replay recording");
    }

    /// <summary>
    /// ⭐ THE one place a <c>GeoReference</c> is decided: unchanged ⇒ nothing; a different origin ⇒ <c>Generation</c> + 1; the same origin
    /// under another terrain name ⇒ republished with the SAME generation (a gateway re-bases on a generation change only).
    /// </summary>
    private void PublishGeoReference(TerrainGeoOrigin origin, string terrainName, string source)
    {
        if (_eventBus == null) return;
        bool sameOrigin = _geoReferencePublished && origin == _lastGeoOrigin;
        if (sameOrigin && string.Equals(terrainName, _lastGeoTerrainName, StringComparison.Ordinal)) return;

        if (!sameOrigin) _geoReferenceGeneration++;
        _geoReferencePublished = true;
        _lastGeoOrigin = origin;
        _lastGeoTerrainName = terrainName;

        _eventBus.PublishManaged(new GeoReferencePublishedEvent
        {
            OriginLatitudeDeg  = origin.Lat,
            OriginLongitudeDeg = origin.Lon,
            OriginAltitudeM    = origin.Alt,
            TerrainName        = terrainName,
            Generation         = _geoReferenceGeneration,
        });
        FdpLog<ClusterMaster>.Info(
            $"[Orchestrator] AQ107 C: GeoReference published — origin {origin.Lat}, {origin.Lon}, {origin.Alt} "
          + $"(terrain '{terrainName}', from the {source}), generation {_geoReferenceGeneration}.");
    }
}
