using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Serialization;
using Hrot.Map.Common.Services;

namespace Hrot.Map.Common.Scenario;

/// <summary>
/// ⭐⭐⭐ <b>The ONE scenario-save implementation — host-neutral, used identically by every host.</b>
///
/// <para>There is NO editor-specific scenario save. Whether a save is triggered from the editor UI, from CGF,
/// from a SimHost or IG node, or from the MCP debug API, the bytes are produced HERE: the gated
/// <see cref="ScenarioSerializer"/> (CE-275 ②: <c>save entity ⇔ IsPrimaryOwner AND NOT ScenarioIgnoreTag</c>)
/// stamps <c>$meta</c> and writes the owned entity slice, then this host's active zones are attached to the
/// same DOM. <c>HrotScenarioSaveHandler</c> (the cluster fan-out handler on every node) calls this, and the
/// editor's thin <c>ScenarioFileService</c> shim calls the very same method — one code path, no duplication.</para>
///
/// <para>⭐ It depends only on low, shared types (<see cref="ScenarioSerializer"/> in Fdp.Toolkits,
/// <see cref="IZoneManagerService"/> and the JSON options in Hrot.Core), so it lives at the shared engine
/// layer and nothing about it is "presentation" or "editor".</para>
///
/// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4 · §6a (globals/zones).
/// </summary>
public static class ScenarioSaveCore
{
    /// <summary>
    /// Builds the scenario DOM: the gated entity serialization (stamps <c>$meta</c>) plus this host's active
    /// zones, if any and if a zone service is present.
    /// </summary>
    public static JsonObject BuildDom(
        ScenarioSerializer   serializer,
        EntityRepository     world,
        ScenarioHeader       header,
        IZoneManagerService? zoneService)
    {
        var dom = serializer.Serialize(world, header);

        var zones = zoneService?.GetActiveZones();
        if (zones != null && zones.Count > 0)
            dom["Zones"] = JsonSerializer.SerializeToNode(zones, HrotSerializerOptions.HrotJsonOptions)!;

        return dom;
    }

    /// <summary>
    /// Builds the DOM (see <see cref="BuildDom"/>) and writes it to <paramref name="filePath"/> as minified,
    /// numeric-array-flattened JSON — the exact on-disk shape the scenario loader reads back.
    /// </summary>
    public static void Write(
        ScenarioSerializer   serializer,
        EntityRepository     world,
        string               filePath,
        ScenarioHeader       header,
        IZoneManagerService? zoneService)
    {
        var dom = BuildDom(serializer, world, header, zoneService);

        var minified = new JsonSerializerOptions(HrotSerializerOptions.HrotJsonOptions) { WriteIndented = false };
        var json     = JsonSerializer.Serialize(dom, minified);
        File.WriteAllText(filePath, JsonAestheticFormatter.FlattenNumericArrays(json));
    }
}
