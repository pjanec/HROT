using System;
using System.Collections.Generic;
using System.Text.Json;
using Bagira.BDC.SSTD;
using Bagira.IG.Systems;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;

namespace Bagira.IG.Services;

/// <summary>
/// One-shot startup service that publishes an <see cref="IGCapabilitiesAnnounce"/>
/// DDS message so the IOS can dynamically populate its layer-control UI.
///
/// <para>Invoked once by <c>IgApplication</c> immediately after the DDS participant
/// is created and all modules are registered.  Because capabilities are static for
/// the lifetime of the session, no subsequent re-publication is needed.</para>
///
/// <para>The <see cref="IGCapabilitiesAnnounce.LayerTreeJson"/> field contains a
/// JSON array of standardised layer-name strings derived directly from
/// <see cref="MapLayerRegistry.All"/>, ensuring the IOS always reflects the IG's
/// current layer set without requiring a rebuild.</para>
/// </summary>
public static class IgCapabilitiesPublisher
{
    /// <summary>
    /// Publishes the IG capabilities announcement to the DDS network.
    /// </summary>
    /// <param name="participant">Active DDS participant.</param>
    /// <param name="mapId">The IG instance ID (keyed field).</param>
    public static void Publish(DdsParticipant participant, int mapId)
    {
        ArgumentNullException.ThrowIfNull(participant);

        try
        {
            using var writer = new DdsWriter<IGCapabilitiesAnnounce>(participant, "IGCapabilitiesAnnounce");

            var payload = new IGCapabilitiesAnnounce
            {
                MapId              = mapId,
                LayerTreeJson      = BuildLayerTreeJson(),
                ConfigurationSchemasJson = BuildConfigSchemasJson(),
                OverlayStyleSchemaJson   = string.Empty,
                TkbManifestJson          = string.Empty,
            };

            writer.Write(payload);

            FdpLog<Log>.Info(
                "[IG] IGCapabilitiesAnnounce published. MapId={0}, Layers={1}",
                mapId,
                payload.LayerTreeJson);
        }
        catch (Exception ex)
        {
            FdpLog<Log>.Warn(
                "[IG] Failed to publish IGCapabilitiesAnnounce: {0}", ex.Message);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    // Marker type used as the generic parameter for FdpLog<T>.
    // Static classes cannot be used as type arguments, so a private nested
    // sealed class acts as the category token.
    private sealed class Log { }

    /// <summary>
    /// Serialises <see cref="MapLayerRegistry.All"/> layer names into a JSON array.
    /// Example output: <c>["units_ground","units_air","vehicles","tactical_graphics","road_graphs"]</c>
    /// </summary>
    private static string BuildLayerTreeJson()
    {
        var names = new List<string>(MapLayerRegistry.All.Count);
        foreach (var layer in MapLayerRegistry.All)
            names.Add(layer.Name);

        return JsonSerializer.Serialize(names);
    }

    /// <summary>
    /// Returns a minimal JSON schema listing the supported interactive tools.
    /// </summary>
    private static string BuildConfigSchemasJson()
    {
        return JsonSerializer.Serialize(new
        {
            tools = new[] { "Navigation", "Selection", "Placement", "Measure", "AreaAuthoring" }
        });
    }
}
