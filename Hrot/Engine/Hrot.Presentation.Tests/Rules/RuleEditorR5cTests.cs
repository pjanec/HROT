using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Presentation.Edits;
using Hrot.Presentation.Windows;
using Hrot.UI.Common.Rules;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐⭐ CE-4221 / CE-4222 (rules view R4 + R5c; docs/DESIGN_Rules_View_UI.md §6d · §7.8) — the rules view's use of the panel's connectors (DependsOn / ActsOn), Expected bars and clock pins; RE-9 (drag to connect, A2 by kind, And-wrap, undo, refused in live / macro);
/// RE-8 (<c>+ Add</c>: the kinds, the creation request, the new row selected); the Bindings tab (list, edit through the one bind context, A1).
/// </summary>
public sealed class RuleEditorR5cTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly EditHistory _history = new();
    private readonly List<(string Name, string Json)> _applied = new();

    private const string Trig = """{"common":{"title":"Push harder","subject":{"NetworkId":900}},"cooldown":10}""";
    private const string TrigWhen = """{"common":{"title":"Push harder"},"when":{"$type":"OnEntity","ref":{"NetworkId":77},"predicate":{"$type":"PropertyMatch","ComponentType":"ObjectiveStatus","PropertyPath":"State","Operator":"Equals","Predicate":{"$type":"Numeric","MinValue":1,"MaxValue":1}}}}""";

    public RuleEditorR5cTests()
    {
        _ = typeof(Hrot.AI.Behaviors.Brains.Objectives.ObjectiveStateKeepers).Assembly;
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<ObjectiveStatus>();
        _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterComponent<MacroOrigin>();
        _world.RegisterManagedComponent<MacroInstance>();
        _world.RegisterManagedComponent<BehaviorStartRecord>();
        _world.RegisterManagedComponent<ParamBindings>();
    }

    public void Dispose() => _world.Dispose();

    private Entity Rule(int id, string kind, string json, int macroInstance = 0)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.SetManagedComponent(e, new BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 });
        if (macroInstance != 0)
        {
            var inst = _world.CreateEntity(); _world.AddComponent(inst, new NetworkIdentity(macroInstance)); _world.SetManagedComponent(inst, new MacroInstance { Macro = "test/fire-team" });
            _world.AddComponent(e, new MacroOrigin { Instance = new EntityRef(macroInstance) });
        }
        return e;
    }

    private RuleDetails Details(string? blocked = null)
    {
        var svc = new RuleEditorServices(() => ComponentEditServices.CreateBuilder().Build(), () => null,
            (entity, name, json) => { _applied.Add((name, json)); _world.SetManagedComponent(entity, new BehaviorStartRecord { BehaviorName = name, JsonParams = json, InstanceId = 1 }); return true; },
            _history, _ => Array.Empty<RuleJournalEntry>());
        return new RuleDetails(svc, () => _world, () => blocked, _ => { }, (_, _) => { });
    }

    private RuleRow RowOf(int id) => RulesModel.Build(_world, new RulesOptions(), null, 10).Find(id)!;
    private string Params(Entity e) => ((Fdp.ModuleHost.Abstractions.ISimulationView)_world).GetManagedComponentRO<BehaviorStartRecord>(e)!.JsonParams;
    private static JsonNode When(string paramsJson) => JsonNode.Parse(paramsJson)!["when"]!;

    // ── RE-9 + A2: what a drop writes ──────────────────────────────────────────────────────────────────────────────

    /// <summary>A2: the predicate follows the dropped row's KIND — an objective / measure ⇒ <c>State == Achieved</c>, a trigger ⇒ <c>TriggerState == Fired</c>. ✅ Red-proof: the kind test inverted ⇒ red.</summary>
    [Fact]
    public void A2_ThePredicate_FollowsTheDroppedRowsKind()
    {
        Rule(10, "ReachArea", """{"common":{"title":"Reach"}}""");
        Rule(11, "Trigger", """{"common":{"title":"Briefing"}}""");
        Rule(12, "Count", """{"common":{"title":"Count"}}""");
        var obj = RuleConnect.DonePredicateOf(RowOf(10)); var trg = RuleConnect.DonePredicateOf(RowOf(11)); var mes = RuleConnect.DonePredicateOf(RowOf(12));
        Assert.Equal(("State", (double)(int)ObjectiveState.Achieved), (obj["predicate"]!["PropertyPath"]!.ToString(), (double)obj["predicate"]!["Predicate"]!["MinValue"]!));
        Assert.Equal(("TriggerState", (double)(int)TriggerState.Fired), (trg["predicate"]!["PropertyPath"]!.ToString(), (double)trg["predicate"]!["Predicate"]!["MinValue"]!));
        Assert.Equal("State", mes["predicate"]!["PropertyPath"]!.ToString());                              // a measure is an objective-like row
        Assert.Equal((10, "OnEntity", "PropertyMatch", "ObjectiveStatus"), ((int)obj["ref"]!["NetworkId"]!, obj["$type"]!.ToString(), obj["predicate"]!["$type"]!.ToString(), obj["predicate"]!["ComponentType"]!.ToString()));
    }

    [Fact]
    public void TheDrop_IsTheOnlyConditionWhenThereIsNoWhen_AndAnAndCompoundWhenThereIsOne()
    {
        Rule(10, "ReachArea", """{"common":{"title":"Reach"}}""");
        var x = RowOf(10);
        var none = RuleConnect.AddedTo(Trig, x, out _)!;
        Assert.Equal("OnEntity", When(none)["$type"]!.ToString());                                         // no when: just the condition
        var wrapped = RuleConnect.AddedTo(TrigWhen, x, out _)!;
        var w = When(wrapped);
        Assert.Equal(("Compound", "And"), (w["$type"]!.ToString(), w["Operator"]!.ToString()));
        Assert.Equal(new[] { 77, 10 }, w["Conditions"]!.AsArray().Select(c => (int)c!["ref"]!["NetworkId"]!));   // the old one first, the new one ANDed after it
        Assert.Equal("Push harder", JsonNode.Parse(wrapped)!["common"]!["title"]!.ToString());              // the rest of the params untouched
        var again = RuleConnect.AddedTo(wrapped, RowOf(10), out string? why);
        Assert.Null(again); Assert.Contains("already waits", why);                                         // dropping the same row twice is refused
        Rule(13, "ReachArea", """{"common":{"title":"Other"}}""");
        var three = When(RuleConnect.AddedTo(wrapped, RowOf(13), out _)!);                                 // an AND compound grows, it is not nested
        Assert.Equal(3, three["Conditions"]!.AsArray().Count);
    }

    /// <summary>Found LIVE: a scenario's stored params are camelCase (<c>"operator"</c>, <c>"conditions"</c>) — the existing AND compound must still be recognised (appended to), not wrapped inside another.</summary>
    [Fact]
    public void AnExistingAndCompound_InTheScenariosCamelCase_IsAppendedTo_NotWrapped()
    {
        Rule(10, "ReachArea", """{"common":{"title":"Reach"}}""");
        const string stored = """{"common":{"title":"Push"},"when":{"$type":"Compound","operator":"And","conditions":[{"$type":"OnEntity","ref":{"NetworkId":1033,"Ref":"b41d6e00-0000-4000-8000-000000000034"},"predicate":{"$type":"ScenarioTime"}},{"$type":"ScenarioTime","fromSeconds":0,"toSeconds":1500}]}}""";
        var w = When(RuleConnect.AddedTo(stored, RowOf(10), out _)!);
        Assert.Equal(3, w["conditions"]!.AsArray().Count);                                                 // the two it had + the new one, in the ONE compound
        Assert.Equal(1033, (int)w["conditions"]![0]!["ref"]!["NetworkId"]!);
        Assert.Null(RuleConnect.AddedTo(stored.Replace("1033", "10"), RowOf(10), out string? why)); Assert.Contains("already waits", why);   // an authored {NetworkId, Ref} link is read by its id
    }

    /// <summary>RE-9 through the form: the drop stages the new <c>when</c> in the SAME form document (a pending edit, the When tab), <b>Apply</b> writes it as ONE undo entry, Undo restores the old params.</summary>
    [Fact]
    public void TheDrop_IsAPendingEditInTheForm_ApplyWritesIt_UndoRestoresIt()
    {
        var x = Rule(10, "ReachArea", """{"common":{"title":"Reach"}}""");
        var t = Rule(20, "Trigger", TrigWhen);
        var d = Details(); var row = RowOf(20);
        Assert.Null(d.ConnectInto(row, RowOf(10)));
        Assert.Equal(RuleTab.When, d.CurrentTab(row));
        d.Prepare(row, 10);
        Assert.True(d.Probe.Dirty);                                                                        // pending until Apply
        Assert.Equal(TrigWhen, Params(t));
        Assert.True(d.Apply(row));
        Assert.Equal(1, _history.Count);
        var applied = When(Params(t));
        Assert.Equal("And", applied["Operator"]!.ToString());
        Assert.True(applied["Conditions"]!.AsArray().Any(c => (int)c!["Ref"]! == 10), Params(t));
        Assert.Contains("already waits", RuleConnect.AddedTo(Params(t), RowOf(10), out string? why) is null ? why : "");   // the form's own serialisation is still recognised
        Assert.True(d.Undo());
        Assert.Equal(TrigWhen, Params(t));
    }

    [Fact]
    public void TheDrop_IsRefused_ForALiveRun_ARecording_AMacrosRule_AndANonTrigger()
    {
        Rule(10, "ReachArea", """{"common":{"title":"Reach"}}""");
        Rule(20, "Trigger", Trig);
        Rule(30, "Trigger", Trig, macroInstance: 500);
        Assert.Equal("a recording is read-only", Details("a recording is read-only").ConnectInto(RowOf(20), RowOf(10)));
        Assert.StartsWith("from macro test/fire-team", Details().ConnectInto(RowOf(30), RowOf(10)));       // A1
        Assert.Equal("only a trigger waits on another rule", Details().ConnectInto(RowOf(10), RowOf(20)));
        Assert.Equal("a rule cannot wait on itself", Details().ConnectInto(RowOf(20), RowOf(20)));
        var live = Rule(40, "Trigger", Trig);
        _world.AddComponent(live, new ObjectiveStatus { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active });
        _world.AddComponent(live, new ObjectiveLinks());
        Assert.Equal("connecting changes the scenario: edit mode only", Details().ConnectInto(RowOf(40), RowOf(10)));
        Assert.Empty(_applied);
    }

    // ── R4: the rules view's connectors, expected bars and pins ──────────────────────────────────────────────────

    private static string Dep(int net) => "{\"$type\":\"OnEntity\",\"ref\":{\"NetworkId\":" + net + "}}";

    [Fact]
    public void TheRulesView_LinksAWaitedOnRuleToTheTriggerThatWaitsOnIt_AndATriggerToWhatItStarts()
    {
        Rule(10, "ReachArea", """{"common":{"title":"Reach"}}""");
        Rule(20, "Trigger", "{\"common\":{\"title\":\"Counter\"},\"when\":" + Dep(10) + ",\"do\":[{\"act\":\"SetObjectiveActive\",\"args\":{\"Target\":{\"NetworkId\":30},\"Active\":true}}]}");
        Rule(30, "HoldArea", """{"common":{"title":"Hold"}}""");
        var window = new RulesWindow(() => _world, () => null, null, "r5c_links", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 0d);
        window.Rebuild();
        var tracks = window.TracksForRail.OfType<RuleTrack>().Where(t => t.Row != null).ToDictionary(t => t.Row!.NetworkId);
        var from10 = Assert.Single(tracks[10].Links);
        Assert.Equal(("r:20", "a"), (from10.ToTrackId, from10.ToItemId));                                  // 10 -> 20: 20's when names 10
        var from20 = Assert.Single(tracks[20].Links);
        Assert.Equal("r:30", from20.ToTrackId);                                                            // 20 -> 30: what it starts
        Assert.Empty(tracks[30].Links);
    }

    [Fact]
    public void ADeadlineAndAScenarioTimeCondition_ArePinnedDiamonds_AndADeadlineIsAnExpectedStretch()
    {
        Rule(10, "ReachArea", """{"common":{"title":"Reach","deadlineSeconds":120}}""");
        Rule(20, "Trigger", """{"common":{"title":"Clocked"},"when":{"$type":"Compound","Operator":"And","Conditions":[{"$type":"ScenarioTime","FromSeconds":300,"ToSeconds":900}]}}""");
        var r10 = RowOf(10); var r20 = RowOf(20);
        Assert.Equal(new[] { 120d }, r10.Pins!.Select(p => p.At));
        Assert.Equal(new[] { 300d }, r20.Pins!.Select(p => p.At));
        var hooks = new RuleTrackHooks(_ => { }, (_, _) => { }, _ => { }, null);
        var t10 = new RuleTrack(r10, 0, false, hooks, 10);
        Assert.Contains(t10.Items, i => i.Id == "x" && i.Style == TimelineTrackItemStyle.Expected && i.To == 120d);   // the dashed stretch to the deadline
        Assert.Contains(t10.Items, i => i.Id == "p0" && i.Style == TimelineTrackItemStyle.Pinned && i.From == 120d);
        var t20 = new RuleTrack(r20, 0, false, hooks, 10);
        Assert.Contains(t20.Items, i => i.Id == "p0" && i.Style == TimelineTrackItemStyle.Pinned && i.From == 300d);
        var coming = ComingUp.Of(new ITimelineTrack[] { t10, t20 }, 10, 5);
        Assert.Equal(new[] { 120d, 300d }, coming.Select(c => c.At));   // the deadline once (its stretch and its diamond are one moment), then the clock pin
    }

    // ── RE-8: + Add ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheAddMenu_OffersTheTriggerFirst_ThenEveryKindWhoseParamsCarryCommon()
    {
        var kinds = RuleAdd.Kinds();
        Assert.Equal("Trigger", kinds[0]);
        Assert.All(new[] { "Trigger", "ReachArea", "HoldArea", "Counter", "WeightedMean", "NoHarm" }, k => Assert.Contains(k, kinds));
        Assert.Equal(kinds.Count, kinds.Distinct().Count());
        Assert.Equal((8861L, 8860L, 8860L), (RuleAdd.TkbOf("Trigger"), RuleAdd.TkbOf("ReachArea"), RuleAdd.TkbOf("Count")));
        var json = JsonNode.Parse(RuleAdd.NewParamsJson("HoldArea", 1042))!;
        Assert.Equal(("New HoldArea", 1042), (json["common"]!["title"]!.ToString(), (int)json["common"]!["parent"]!["NetworkId"]!));
        Assert.Null(JsonNode.Parse(RuleAdd.NewParamsJson("Trigger", 0))!["common"]!["parent"]);            // no selected row ⇒ no parent
    }

    /// <summary>+ Add asks the host's creation path for the right TKB type with the title and the SELECTED row as parent, only in edit mode; when the entity appears the window selects it and opens its tab. ✅ Red-proof: the TKB of a trigger given as 8860 ⇒ red.</summary>
    [Fact]
    public void AddRule_RequestsTheEntity_WithTheTitleAndTheSelectedRowAsParent_AndSelectsItWhenItAppears()
    {
        Rule(10, "WeightedMean", """{"common":{"title":"Mission"}}""");
        var asked = new List<(long Tkb, string Kind, string Json)>();
        var svc = new RuleEditorServices(() => ComponentEditServices.CreateBuilder().Build(), () => null, (_, _, _) => true, _history, _ => Array.Empty<RuleJournalEntry>(),
            CreateRule: (tkb, kind, json) => { asked.Add((tkb, kind, json)); return true; });
        var window = new RulesWindow(() => _world, () => null, null, "r5c_add", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 0d, editor: svc);
        window.SelectNetworkId(10); window.Rebuild();
        Assert.Null(window.AddRefusal());
        Assert.True(window.AddRule("Trigger"));
        var (tkb, kind, json) = Assert.Single(asked);
        Assert.Equal((8861L, "Trigger", "New Trigger", 10), (tkb, kind, JsonNode.Parse(json)!["common"]!["title"]!.ToString(), (int)JsonNode.Parse(json)!["common"]!["parent"]!["NetworkId"]!));
        window.Rebuild();
        Assert.Equal(10, window.SelectedNetworkId);                                                        // not yet: the entity has not appeared
        Rule(11, "Trigger", json);                                                                         // the creation system's answer
        window.Rebuild();
        Assert.Equal(11, window.SelectedNetworkId);                                                        // selected, its form opened
        // a host with no creation path, a recording and a live run do not offer it
        var none = new RulesWindow(() => _world, () => null, null, "r5c_add2", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 0d,
            editor: new RuleEditorServices(() => ComponentEditServices.CreateBuilder().Build(), () => null, (_, _, _) => true, _history, _ => Array.Empty<RuleJournalEntry>()));
        none.Rebuild();
        Assert.Equal("this host cannot create entities", none.AddRefusal());
        var live = Rule(12, "Trigger", Trig);
        _world.AddComponent(live, new ObjectiveStatus { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active }); _world.AddComponent(live, new ObjectiveLinks());
        window.Rebuild();
        Assert.Equal("adding a rule changes the scenario: edit mode only", window.AddRefusal());
        Assert.False(window.AddRule("HoldArea"));
        Assert.Single(asked);
    }

    // ── the Bindings tab ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheBindingsTab_ListsTheRuleEntitysParamBindings_AndEditsThroughTheOneBindContext()
    {
        var e = Rule(20, "Trigger", Trig);
        _world.SetManagedComponent(e, new ParamBindings { Bindings = new Dictionary<string, string> { ["Behavior.Params.Cooldown"] = "cd", ["Behavior.Params.HoldFor"] = "hold", ["EntityInfo.ForceId"] = "side" } });
        var d = Details(); var row = RowOf(20);
        Assert.Equal(new[] { new RuleDetails.BindingRow("Cooldown", "cd"), new RuleDetails.BindingRow("HoldFor", "hold") }, d.BindingsOf(row));   // the rule's params only
        d.RequestTab(RuleTab.Bindings);
        d.Prepare(row, 10);
        var ctx = d.BindsOf(row)!;
        Assert.Equal("cd", ctx.BoundTo("Cooldown"));
        ctx.Bind("Delay", "wait"); ctx.Unbind("Cooldown");                                                 // the SAME writes the AI section's chip makes
        Assert.Equal(new[] { new RuleDetails.BindingRow("Delay", "wait"), new RuleDetails.BindingRow("HoldFor", "hold") }, d.BindingsOf(RowOf(20)));
        d.Prepare(RowOf(20), 10);
        Assert.Equal("side", ((Fdp.ModuleHost.Abstractions.ISimulationView)_world).GetManagedComponentRO<ParamBindings>(e)!.Bindings["EntityInfo.ForceId"]);   // the other entries are kept
    }

    [Fact]
    public void TheBindingsTab_OfARuleFromAMacro_IsReadOnly_A1()
    {
        Rule(30, "Trigger", Trig, macroInstance: 500);
        var d = Details(); var row = RowOf(30);
        Assert.StartsWith("from macro test/fire-team", d.ReadOnlyReason(row));
        Assert.Equal(500, row.MacroInstance);
    }
}
