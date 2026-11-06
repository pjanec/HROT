using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>The tabs of the rule editor (docs/DESIGN_Rules_View_UI.md §7, RE-1): a trigger has the first six, an objective / measure the last three plus History.</summary>
public enum RuleTab { Overview, When, Rules, Do, Bindings, History, Status, Settings, Results }

/// <summary>
/// ⭐⭐ CE-4218 (rules view R5a, RE-1 / RE-2) — <b>which fields of the ONE params document each tab draws</b>, pure. ⛔ No per-tab DTO and no second form: the rule entity's behaviour params are one document (<c>TriggerParamsJsonDto</c> or its kind's DTO),
/// drawn by <c>BehaviorParamsForm</c>; a tab only CHOOSES fields. The header (<c>common</c>: title, subject, parent, required, scored, weight, points, deadline, startActive, visible to, group, pass) sits above every tab.
/// A trigger: <b>When</b> = <c>when</c> · <b>Do</b> = <c>do</c> (the list editor is R5b: a placeholder) · <b>Rules</b> = every other field (hold for, delay, cooldown, repeat, max fires, fire if true when armed, while true).
/// An objective / measure: <b>Settings</b> = every field but <c>common</c> (the kind's own: area, min men, hold seconds …). <b>Overview / Status / Results / History / Bindings</b> draw no params field.
/// </summary>
public static class RuleTabs
{
    /// <summary>The name of the header field every rule DTO carries (the CLR member, as the params form names its nodes).</summary>
    public const string HeaderField = nameof(TriggerFieldNames.Common);

    // only here for nameof: the trigger DTO's three named fields
    private static class TriggerFieldNames { public const string Common = "Common", When = "When", Do = "Do"; }

    private static readonly RuleTab[] TriggerTabs = { RuleTab.Overview, RuleTab.When, RuleTab.Rules, RuleTab.Do, RuleTab.Bindings, RuleTab.History };
    private static readonly RuleTab[] ObjectiveTabs = { RuleTab.Status, RuleTab.Settings, RuleTab.Results, RuleTab.History };

    /// <summary>The tabs of a role (cached arrays — no allocation per call; the editor asks several times a frame).</summary>
    public static IReadOnlyList<RuleTab> TabsFor(ObjectiveRole role) => role == ObjectiveRole.Trigger ? TriggerTabs : ObjectiveTabs;

    private static readonly string[] Titles = System.Enum.GetNames(typeof(RuleTab));
    private static readonly string[] Ids = System.Array.ConvertAll(Titles, n => "rt" + n);

    public static string TitleOf(RuleTab tab) => Titles[(int)tab];

    /// <summary>⭐ U-2 / CE-4286 — the constant ImGui id of the tab (stable when the title text changes).</summary>
    public static string IdOf(RuleTab tab) => Ids[(int)tab];

    /// <summary>⭐ U-2 (Colour Icons §3.1) — the key in <c>NodeEditor.Core.Widgets.Icons</c> of the tab's coloured icon.</summary>
    public static string IconKeyOf(RuleTab tab) => tab switch
    {
        RuleTab.Overview => "tab/overview", RuleTab.Status => "tab/status", RuleTab.When => "tab/when", RuleTab.Rules => "tab/rules", RuleTab.Do => "tab/do",
        RuleTab.Bindings => "tab/bindings", RuleTab.History => "tab/history", RuleTab.Settings => "tab/settings", RuleTab.Results => "tab/results",
        _ => "doc/other",
    };

    /// <summary>The members of <paramref name="paramsType"/> that are fields of the document: public instance fields and properties, by CLR name.</summary>
    public static IReadOnlyList<string> MembersOf(Type paramsType)
        => paramsType.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m is FieldInfo || m is PropertyInfo { CanRead: true, CanWrite: true })
                     .Select(m => m.Name).ToList();

    /// <summary>The ONE tab a field belongs to (<c>null</c> = the header, on every tab). A trigger's DTO is told by its <c>When</c> / <c>Do</c> members.</summary>
    public static RuleTab? TabOfField(Type paramsType, string member)
    {
        if (member == TriggerFieldNames.Common) return null;
        bool trigger = IsTrigger(paramsType);
        if (!trigger) return RuleTab.Settings;
        return member == TriggerFieldNames.When ? RuleTab.When : member == TriggerFieldNames.Do ? RuleTab.Do : RuleTab.Rules;
    }

    public static bool IsTrigger(Type paramsType) => paramsType.GetMember(TriggerFieldNames.When).Length > 0 && paramsType.GetMember(TriggerFieldNames.Do).Length > 0;

    /// <summary>The fields <paramref name="tab"/> draws for <paramref name="paramsType"/> (without the header).</summary>
    public static IReadOnlyList<string> FieldsOf(Type paramsType, RuleTab tab)
        => MembersOf(paramsType).Where(m => TabOfField(paramsType, m) == tab).ToList();

    /// <summary>Whether <paramref name="paramsType"/> is a rule's params DTO: it carries the <c>common</c> block.</summary>
    public static bool IsRuleParams(Type t)
        => t.GetMember(TriggerFieldNames.Common).OfType<FieldInfo>().Any(f => f.FieldType == typeof(ObjectiveCommon))
        || t.GetMember(TriggerFieldNames.Common).OfType<PropertyInfo>().Any(p => p.PropertyType == typeof(ObjectiveCommon));
}
