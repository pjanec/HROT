using System;
using System.Collections.Generic;
using System.Text.Json;
using Hrot.Core.Network;
using Hrot.IG.Systems;
using Fdp.Kernel.Logging;

namespace Hrot.IG.Services;

/// <summary>
/// One-shot startup service that publishes an <see cref="IGCapabilitiesAnnounce"/>
/// DDS message so the ExCon can dynamically populate its layer-control UI.
///
/// <para>Invoked once by <c>IgApplication</c> immediately after the DDS participant
/// is created and all modules are registered.  Because capabilities are static for
/// the lifetime of the session, no subsequent re-publication is needed.</para>
///
/// <para>The <see cref="IGCapabilitiesAnnounce.LayerTreeJson"/> field contains a
/// JSON array of standardised layer-name strings derived directly from
/// <see cref="MapLayerRegistry.All"/>, ensuring the ExCon always reflects the IG's
/// current layer set without requiring a rebuild.</para>
/// </summary>
public static class IgCapabilitiesPublisher
{
    /// <summary>
    /// Invokes the network adapter to publish IG capabilities.
    /// </summary>
    /// <param name="adapter">Network adapter; no-op when null.</param>
    /// <param name="mapId">The IG instance ID.</param>
    public static void Publish(IIgNetworkAdapter? adapter, int mapId)
    {
        if (adapter == null) return;

        try
        {
            string layerTreeJson  = BuildLayerTreeJson();
            string configSchemas  = BuildConfigSchemasJson();
            adapter.PublishCapabilities(mapId, layerTreeJson, configSchemas);

            FdpLog<Log>.Info(
                "[Node-{0}] IGCapabilitiesAnnounce published. Layers={1}",
                mapId,
                layerTreeJson);
        }
        catch (Exception ex)
        {
            FdpLog<Log>.Warn(
                "[Node-{0}] Failed to publish IGCapabilitiesAnnounce: {1}", mapId, ex.Message);
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
