using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Declarative blueprint assignment for scenario persistence.
/// Stored inside <see cref="Hrot.Common.Serializers.InitialBlueprintsIntent"/>.
/// </summary>
public sealed class BlueprintAssignmentDto
{
    /// <summary>The stable Asset GUID of the Instance Blueprint.</summary>
    public required Guid AssetId { get; init; }

    /// <summary>
    /// ⭐⭐ <c>CE-3044</c> (R-191) — the per-entity params as a JSON object keyed by parameter NAME, holding only the
    /// fields that differ from the blueprint's declared defaults (<see cref="BlueprintDefinition.FormatParams"/> of the
    /// live region). Load feeds it to the same <see cref="BlueprintDefinition.ParseParams"/> an attach uses.
    /// <c>null</c> when every param is at its default, so a default assignment stays <c>{ AssetId }</c> only.
    ///
    /// <para>⛔ SUPERSEDED: the resolved param BYTES plus a <c>ParamsStructureHash</c> layout guard (MX-031). 🔒 User:
    /// <i>"Never saved as bytes to scenario."</i> — a byte region is readable only while the layout is unchanged, so the
    /// guard turned any recompile into losing every authored value; by name, a renamed field loses only itself.
    /// 📄 <c>DESIGN_Blueprint_Param_Persistence.md</c> §10.</para>
    /// </summary>
    public JsonObject? Params { get; init; }
}
