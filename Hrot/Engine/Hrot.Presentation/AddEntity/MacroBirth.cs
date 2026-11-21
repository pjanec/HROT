using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Mission;
using Hrot.Map.Common;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ CE-4210 MA-7 — what a placed MACRO instance is BORN with, in ONE place (the sibling of <c>AreaAuthoringArm.BornWith</c> for a Weather Region): the placement JSON carries the macro's path under
/// <see cref="MacroKey"/>; <see cref="Split"/> takes it out (the attribute compiler never sees it) and <see cref="BornWith"/> adds <c>MacroInstance { Macro, ArgsJson = "{}" }</c> to the new entity's components.
/// Both are called by <c>ScenarioSpawnAdapter.ArmPlacement</c> — the point commit every host passes.
/// </summary>
public static class MacroBirth
{
    /// <summary>The placement JSON's key for the macro path (not a component attribute).</summary>
    public const string MacroKey = "$macro";

    public static string PlacementJson(eForceIdentifier side, string macroPath)
        => JsonSerializer.Serialize(new Dictionary<string, string> { ["Affiliation"] = side.ToString(), [MacroKey] = macroPath }, FdpJsonOptionsRegistry.DefaultRelaxed);

    /// <summary>The placement JSON without the macro key, and the macro path it carried (null when none). Unparseable JSON passes through untouched.</summary>
    public static (string? Json, string? Macro) Split(string? placementJson)
    {
        if (string.IsNullOrWhiteSpace(placementJson) || !placementJson.Contains(MacroKey, StringComparison.Ordinal)) return (placementJson, null);
        try
        {
            if (JsonNode.Parse(placementJson) is not JsonObject obj || !obj.Remove(MacroKey, out var node)) return (placementJson, null);
            string? macro = node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
            return (obj.Count == 0 ? null : obj.ToJsonString(), macro);
        }
        catch (JsonException) { return (placementJson, null); }
    }

    /// <summary>Adds the <see cref="MacroInstance"/> to <paramref name="components"/> when the entity is a macro instance (<paramref name="tkbType"/> 8813) placed from <paramref name="macro"/>; never twice.</summary>
    public static void BornWith(long tkbType, string? macro, List<object> components)
    {
        if (macro is null || tkbType != TkbEntityTypes.ScenarioMacro || components.Exists(c => c is MacroInstance)) return;
        components.Add(new MacroInstance { Macro = macro, ArgsJson = "{}" });
    }
}
