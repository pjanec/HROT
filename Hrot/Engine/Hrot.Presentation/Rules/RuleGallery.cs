using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ReplayBrowser.Search.Conditions;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐ CE-4308 (RX6, docs/UX/DESIGN_Rule_Editor_UX.md §2.5, rule-list-visual.svg "New rule — choose a starting point") — the GALLERY: starting points a
/// non-technical author picks instead of an engine kind, each a kind plus the params it starts with, pre-filled from where the author came (the unit as
/// the rule's subject, the area). PURE: the window creates the chosen one through its ONE creation path (<c>RulesWindow.AddRule</c>); the engine's own
/// kinds stay under <i>More…</i> (<see cref="RuleAdd.Kinds"/>). Conditions are built with the condition editor's own rows (<see cref="RowProjection"/>),
/// so a pre-filled <i>when</i> reads in the editor exactly as if typed there.
/// </summary>
public static class RuleGallery
{
    /// <summary>Where the author came from: the unit the rule is about, the area it watches, and the next free phase number.</summary>
    public sealed record Context(EntityLink Subject, EntityLink Area, int NextPhase = 1)
    {
        public static readonly Context Empty = new(EntityLink.None, EntityLink.None);
        public bool HasSubject => !Subject.Ref.IsNone || !Subject.Spec.IsNone;
        public bool HasArea => !Area.Ref.IsNone || !Area.Spec.IsNone;
    }

    /// <summary>One starting point: its group and label as the mockup names them, the rule kind it creates, and the params it starts with.</summary>
    public sealed record Template(string Group, string Label, string Kind, Func<Context, JsonObject> Params);

    /// <summary>The groups, in the mockup's order.</summary>
    public static readonly string[] Groups = { "Objectives", "Phases and timing", "Reinforcements", "Messages", "Scoring" };

    private const string Trigger = RuleAdd.TriggerKind;

    /// <summary>Every starting point, in the mockup's order. An area-only entry (<c>When someone enters this area…</c>) is offered by <see cref="For"/> when an area is given.</summary>
    public static readonly IReadOnlyList<Template> All = new Template[]
    {
        new("Objectives", "Take and hold an area", BehaviorNames.HoldArea, c => Rule("Take and hold", c, ("area", AreaOf(c)), ("holdSeconds", 300))),
        new("Objectives", "Reach an area", BehaviorNames.ReachArea, c => Rule("Reach", c, ("area", AreaOf(c)))),
        new("Objectives", "Destroy", BehaviorNames.DestroyTargets, c => Rule("Destroy", c, ("targets", TargetsOf(c)))),
        new("Objectives", "Protect", BehaviorNames.Protect, c => Rule("Protect", c, ("targets", TargetsOf(c)))),
        new("Objectives", "Survive", BehaviorNames.Protect, c => Rule("Survive", c, ("targets", TargetsOf(c)))),   // ⭐ Survive = protect the unit itself (its own men)
        new("Phases and timing", "Begin a phase when ...", Trigger, c => Phase(c)),
        new("Phases and timing", "End the exercise when ...", Trigger, c => Rule("End the exercise", c, ("do", Steps(("EndExercise", new JsonObject { ["reason"] = "" }))))),
        new("Reinforcements", "Bring in ... when ...", Trigger, c => Rule("Bring in", c, ("do", Steps(("InsertMacro", new JsonObject()))))),
        new("Messages", "Brief Blue when ...", Trigger, c => Rule("Brief Blue", c, ("do", Steps(("Message", new JsonObject { ["ToKind"] = "Side", ["ToValue"] = (int)ForceId.Friend, ["Text"] = "" }))))),
        new("Messages", "Tell everyone when ...", Trigger, c => Rule("Tell everyone", c, ("do", Steps(("Message", new JsonObject { ["ToKind"] = "All", ["Text"] = "" }))))),
        new("Scoring", "Hit ratio", BehaviorNames.HitRatio, c => Rule("Hit ratio", c)),
        new("Scoring", "Report latency", BehaviorNames.ReportLatency, c => Rule("Report latency", c)),
    };

    /// <summary>⭐ The area's own entry (<i>When someone enters this area…</i>): a trigger whose <i>when</i> is the condition editor's row <i>any of Blue side is
    /// inside the area</i> — the author changes WHO in the editor.</summary>
    public static readonly Template EntersArea = new("Area", "When someone enters this area...", Trigger,
        c => Rule("Someone enters the area", c, ("when", WhenInside(c))));

    /// <summary>The starting points to show for <paramref name="c"/>, filtered by <paramref name="search"/> (case-insensitive, on the label and group): the area's
    /// own entry first when an area is given.</summary>
    public static IReadOnlyList<Template> For(Context c, string? search = null)
    {
        var list = new List<Template>(All.Count + 1);
        if (c.HasArea) list.Add(EntersArea);
        list.AddRange(All);
        if (string.IsNullOrWhiteSpace(search)) return list;
        return list.Where(t => t.Label.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Group.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The template's params as JSON (what the window creates the rule with), with <c>common.parent</c> when given.</summary>
    public static string ParamsJson(Template t, Context c, JsonNode? parentLink)
    {
        var p = t.Params(c);
        if (parentLink != null && p["common"] is JsonObject common) common["parent"] = parentLink.DeepClone();
        return p.ToJsonString();
    }

    /// <summary>The title a template's rule starts with (the window watches for it to select the new row).</summary>
    public static string TitleOf(Template t, Context c) => (string?)t.Params(c)["common"]?["title"] ?? RuleAdd.TitleOf(t.Kind);

    /// <summary>The link the entity picker writes for <paramref name="networkId"/> (the stable GUID form), as <see cref="RuleConnect.LinkOf"/> makes it.</summary>
    public static EntityLink LinkTo(int networkId, LiveRefScope? scope)
        => networkId <= 0 ? EntityLink.None : ReferenceLeafDrawers.PickToLink(new EntityRef(networkId), scope);

    // ── the params ───────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static JsonObject Rule(string title, Context c, params (string Key, JsonNode? Value)[] fields)
    {
        var common = new JsonObject { ["title"] = title };
        if (c.HasSubject) common["subject"] = Node(c.Subject);
        var p = new JsonObject { ["common"] = common };
        foreach (var (k, v) in fields) if (v != null) p[k] = v;
        return p;
    }

    private static JsonObject Phase(Context c)
    {
        var p = Rule($"Phase {c.NextPhase}", c);
        ((JsonObject)p["common"]!)["phase"] = c.NextPhase;   // ⭐ CE-4302 — a trigger marked as a phase
        return p;
    }

    private static JsonArray Steps(params (string Act, JsonObject Args)[] steps)
    {
        var a = new JsonArray();
        foreach (var (act, args) in steps) a.Add(new JsonObject { ["act"] = act, ["args"] = args });
        return a;
    }

    private static JsonNode? AreaOf(Context c) => c.HasArea ? JsonSerializer.SerializeToNode(AreaLink.OfEntity(c.Area.Ref, c.Area.Spec), BehaviorParams.JsonOptions) : null;

    private static JsonNode? TargetsOf(Context c) => c.HasSubject ? new JsonArray { Node(c.Subject) } : null;

    private static JsonNode? WhenInside(Context c)
    {
        if (!c.HasArea) return null;
        var row = new ConditionRow(Subject.OfSide(SubjectKind.Any, ForceId.Friend), "inside", Values: new FactValues(Area: AreaLink.OfEntity(c.Area.Ref, c.Area.Spec)));
        return JsonSerializer.SerializeToNode(RowProjection.ToDto(row), BehaviorParams.JsonOptions);
    }

    private static JsonNode? Node(EntityLink link) => JsonSerializer.SerializeToNode(link, BehaviorParams.JsonOptions);
}
