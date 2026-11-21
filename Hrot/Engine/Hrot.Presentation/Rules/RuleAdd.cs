using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐⭐ CE-4222 (rules view R5c, RE-8; docs/DESIGN_Rules_View_UI.md §7.3) — <b>+ Add</b>, PURE: the rule kinds the menu offers, the TKB type each is created as, and the params a NEW rule starts with. ⭐ The creation itself is the ONE path every author uses
/// (<c>EntityCreation.RequestEntityCreation</c>, the Add Entity picker's own — measured in §7.5): ⛔ no second one here.
/// </summary>
public static class RuleAdd
{
    public const string TriggerKind = "Trigger";

    /// <summary>Every behaviour whose params type carries the <c>common</c> block (<see cref="RuleTabs.IsRuleParams"/>), the trigger first, the rest by name — straight from the declared contracts (<see cref="ParamsDtoRegistry"/>), so a new kind appears here by itself.</summary>
    public static IReadOnlyList<string> Kinds()
        => ParamsDtoRegistry.Default.Behaviors.Where(b => RuleTabs.IsRuleParams(b.ParamsType)).Select(b => b.Name).Distinct()
            .OrderBy(n => n == TriggerKind ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The TKB type a rule of <paramref name="kind"/> is created as: a trigger = 8861, every objective / measure kind = 8860 (a brain with no body that runs one behaviour).</summary>
    public static long TkbOf(string kind) => kind == TriggerKind ? AuthoredRules.TkbTrigger : AuthoredRules.TkbObjective;

    public static string TitleOf(string kind) => $"New {kind}";

    /// <summary>The params a new rule starts with: <c>common.title</c> = <i>New &lt;kind&gt;</i> and <c>common.parent</c> = the selected row, as the STABLE link <see cref="RuleConnect.LinkOf"/> writes (null = no parent).</summary>
    public static string NewParamsJson(string kind, JsonNode? parentLink)
    {
        var common = new JsonObject { ["title"] = TitleOf(kind) };
        if (parentLink != null) common["parent"] = parentLink.DeepClone();
        return new JsonObject { ["common"] = common }.ToJsonString();
    }
}
