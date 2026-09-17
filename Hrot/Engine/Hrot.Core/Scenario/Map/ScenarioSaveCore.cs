using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Serialization;

namespace Hrot.Map.Common.Scenario;

/// <summary>
/// ⭐⭐⭐ <b>The ONE scenario-save implementation — host-neutral, used identically by every host.</b>
///
/// <para>There is NO editor-specific scenario save. Whether a save is triggered from the editor UI, from CGF,
/// from a SimHost or IG node, or from the MCP debug API, the bytes are produced HERE: the gated
/// <see cref="ScenarioSerializer"/> (CE-275 ②: <c>save entity ⇔ IsPrimaryOwner AND NOT ScenarioIgnoreTag</c>)
/// stamps <c>$meta</c> and writes the owned entity slice. <c>HrotScenarioSaveHandler</c> (the cluster
/// fan-out handler on every node) calls this, and the editor's thin <c>ScenarioFileService</c> shim calls
/// the very same method — one code path, no duplication.</para>
///
/// <para>⛔ <b>It no longer attaches a <c>Zones</c> section (F1, retired 2026-09-17).</b> It used to take an
/// <c>IZoneManagerService</c> and append that host's active zone DTOs to the DOM. A zone is now an ordinary
/// authored ENTITY, so the gated serialization above already carries it — appending a second representation
/// would be two producers for one slot (<c>R-132</c>), which is precisely what the retirement removes.
/// ⚠ Nothing was lost: what the section described (road networks) is now declared by the TERRAIN asset, and
/// what it listed (obstacles) is now entities.
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.1, §6.</para>
///
/// <para>⭐ It depends only on low, shared types (<see cref="ScenarioSerializer"/> in Fdp.Toolkits and the
/// JSON options in Hrot.Core), so it lives at the shared engine layer and nothing about it is
/// "presentation" or "editor".</para>
///
/// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4.
/// </summary>
public static class ScenarioSaveCore
{
    /// <summary>
    /// Builds the scenario DOM: the gated entity serialization, which stamps <c>$meta</c>.
    /// </summary>
    public static JsonObject BuildDom(
        ScenarioSerializer serializer,
        EntityRepository   world,
        ScenarioHeader     header)
        => serializer.Serialize(world, header);

    /// <summary>
    /// Builds the DOM (see <see cref="BuildDom"/>) and writes it to <paramref name="filePath"/> as minified,
    /// numeric-array-flattened JSON — the exact on-disk shape the scenario loader reads back.
    /// </summary>
    public static void Write(
        ScenarioSerializer serializer,
        EntityRepository   world,
        string             filePath,
        ScenarioHeader     header)
    {
        var dom = BuildDom(serializer, world, header);

        var minified = new JsonSerializerOptions(HrotSerializerOptions.HrotJsonOptions) { WriteIndented = false };
        var json     = JsonSerializer.Serialize(dom, minified);
        File.WriteAllText(filePath, JsonAestheticFormatter.FlattenNumericArrays(json));
    }
}
