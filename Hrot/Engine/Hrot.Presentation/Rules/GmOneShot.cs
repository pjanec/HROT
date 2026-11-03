using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules.Actions;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐⭐ CE-4243 (SR-104 Z3; docs/DESIGN_Rules_View_UI.md §8 GM-1 … GM-4) — the game master's <b>hand-run action</b>, PURE: not a command, a TRANSIENT ONE-SHOT TRIGGER. Its params are <c>when = ScenarioTime(from 0)</c> (always true) +
/// <c>fireIfTrueWhenArmed</c> + <c>maxFires 1</c> + <c>do = [the step]</c> + <c>common.title "GM · &lt;action&gt;"</c>; the host creates it through the ONE creation path (<c>RuleAdd.TkbOf("Trigger")</c>, <c>isTransient</c>), so it fires through the
/// normal step runner, journals itself and shows in the rules view like any rule. ⛔ no second runner.
/// </summary>
public static class GmOneShot
{
    /// <summary>Every game-master rule's title starts with this — the rules view groups the rows by it (GM-4).</summary>
    public const string TitlePrefix = "GM · ";
    public const string GroupName = "Game master (live)";
    public const string EditModeRefusal = "the game master acts on a running exercise";

    public static string TitleOf(string act) => TitlePrefix + (act.Contains('.') ? act[(act.LastIndexOf('.') + 1)..] : act);

    public static bool IsGmTitle(string? title) => title != null && title.StartsWith(TitlePrefix, StringComparison.Ordinal);

    /// <summary>The trigger's params JSON for <paramref name="step"/>.</summary>
    public static string TriggerParamsJson(ActionStep step)
    {
        var stepNode = JsonNode.Parse(ActionStep.ToJson(new[] { step }))!.AsArray()[0]!.DeepClone();
        return new JsonObject
        {
            ["common"] = new JsonObject { ["title"] = TitleOf(step.Act) },
            ["when"] = new JsonObject { ["$type"] = "ScenarioTime", ["fromSeconds"] = 0 },
            ["do"] = new JsonArray(stepNode),
            ["maxFires"] = 1,
            ["fireIfTrueWhenArmed"] = true,
        }.ToJsonString();
    }

    /// <summary>GM-3 — the step of a per-side skill row: <c>SetSkillAmplifier { side, tiers }</c> (the verb is the ONE writer of the amplifier).</summary>
    public static ActionStep SkillStep(ForceId side, int tiers)
        => new() { Act = "SetSkillAmplifier", Args = new JsonObject { ["side"] = side.ToString(), ["tiers"] = Math.Clamp(tiers, -Fdp.Toolkit.Behavior.Components.SkillAmplifier.MaxTiers, Fdp.Toolkit.Behavior.Components.SkillAmplifier.MaxTiers) } };

    /// <summary>
    /// GM-2 — a fresh step's args pre-filled from the selection: a field named <c>target</c> takes the selected entity (its network id as an entity link), a field named <c>side</c> the selection's side. The user can change both; a step whose
    /// params type has neither is left as it is.
    /// </summary>
    public static JsonObject Prefilled(ActionEntry entry, int selectedNetworkId, ForceId selectedSide)
    {
        var args = new JsonObject();
        foreach (var f in entry.ParamsType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (selectedNetworkId != 0 && f.Name.Equals("target", StringComparison.OrdinalIgnoreCase)) args[f.Name.ToLowerInvariant()] = new JsonObject { ["NetworkId"] = selectedNetworkId };
            else if (selectedNetworkId != 0 && f.Name.Equals("side", StringComparison.OrdinalIgnoreCase) && f.FieldType == typeof(ForceId)) args[f.Name.ToLowerInvariant()] = selectedSide.ToString();
        }
        return args;
    }

    /// <summary>The arguments' text for the rail / probe.</summary>
    public static string Describe(ActionStep step) => $"{step.Act} {step.Args?.ToJsonString() ?? "{}"}";
}
