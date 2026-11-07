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
        _world.RegisterComponent<EntityGuid>();
        _world.RegisterComponent<RefKey>();
    }

    private static Guid GuidOf(int id) => new(id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
    private JsonNode Link(int id) => RuleConnect.LinkOf(RowOf(id), AuthoredRules.ScopeOf(_world));

    public void Dispose() => _world.Dispose();

    private Entity Rule(int id, string kind, string json, int macroInstance = 0)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new EntityGuid(GuidOf(id)));
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
        var obj = RuleConnect.DonePredicateOf(RowOf(10), Link(10)); var trg = RuleConnect.DonePredicateOf(RowOf(11), Link(11)); var mes = RuleConnect.DonePredicateOf(RowOf(12), Link(12));
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
        var none = RuleConnect.AddedTo(Trig, x, Link(10), out _)!;
        Assert.Equal("OnEntity", When(none)["$type"]!.ToString());                                         // no when: just the condition
        var wrapped = RuleConnect.AddedTo(TrigWhen, x, Link(10), out _)!;
        var w = When(wrapped);
        Assert.Equal(("Compound", "And"), (w["$type"]!.ToString(), w["Operator"]!.ToString()));
        Assert.Equal(new[] { 77, 10 }, w["Conditions"]!.AsArray().Select(c => (int)c!["ref"]!["NetworkId"]!));   // the old one first, the new one ANDed after it
        Assert.Equal("Push harder", JsonNode.Parse(wrapped)!["common"]!["title"]!.ToString());              // the rest of the params untouched
        var again = RuleConnect.AddedTo(wrapped, RowOf(10), Link(10), out string? why);
        Assert.Null(again); Assert.Contains("already waits", why);                                         // dropping the same row twice is refused
        Rule(13, "ReachArea", """{"common":{"title":"Other"}}""");
        var three = When(RuleConnect.AddedTo(wrapped, RowOf(13), Link(13), out _)!);                                 // an AND compound grows, it is not nested
        Assert.Equal(3, three["Conditions"]!.AsArray().Count);
    }

    /// <summary>Found LIVE: a scenario's stored params are camelCase (<c>"operator"</c>, <c>"conditions"</c>) — the existing AND compound must still be recognised (appended to), not wrapped inside another.</summary>
    [Fact]
    public void AnExistingAndCompound_InTheScenariosCamelCase_IsAppendedTo_NotWrapped()
    {
        Rule(10, "ReachArea", """{"common":{"title":"Reach"}}""");
        const string stored = """{"common":{"title":"Push"},"when":{"$type":"Compound","operator":"And","conditions":[{"$type":"OnEntity","ref":{"NetworkId":1033,"Ref":"b41d6e00-0000-4000-8000-000000000034"},"predicate":{"$type":"ScenarioTime"}},{"$type":"ScenarioTime","fromSeconds":0,"toSeconds":1500}]}}""";
        var w = When(RuleConnect.AddedTo(stored, RowOf(10), Link(10), out _)!);
        Assert.Equal(3, w["conditions"]!.AsArray().Count);                                                 // the two it had + the new one, in the ONE compound
        Assert.Equal(1033, (int)w["conditions"]![0]!["ref"]!["NetworkId"]!);
        Assert.Null(RuleConnect.AddedTo(stored.Replace("b41d6e00-0000-4000-8000-000000000034", GuidOf(10).ToString()), RowOf(10), Link(10), out string? why)); Assert.Contains("already waits", why);   // an authored {NetworkId, Ref} link is the rule its GUID names
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
        Assert.True(applied["Conditions"]!.AsArray().Any(c => c!["Ref"]!.ToJsonString().Contains(GuidOf(10).ToString())), Params(t));   // the stable form survived the form's own serialisation
        Assert.Contains("already waits", RuleConnect.AddedTo(Params(t), RowOf(10), Link(10), out string? why) is null ? why : "");   // the form's own serialisation is still recognised
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
        var window = new RulesWindow(() => _world, () => null, null, "r5c_links", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 0d) { ListView = false };   // U-18: these rails drive the Timeline
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
        var json = JsonNode.Parse(RuleAdd.NewParamsJson("HoldArea", new JsonObject { ["NetworkId"] = 1042 }))!;
        Assert.Equal(("New HoldArea", 1042), (json["common"]!["title"]!.ToString(), (int)json["common"]!["parent"]!["NetworkId"]!));
        Assert.Null(JsonNode.Parse(RuleAdd.NewParamsJson("Trigger", null))!["common"]!["parent"]);            // no selected row ⇒ no parent
    }

    /// <summary>+ Add asks the host's creation path for the right TKB type with the title and the SELECTED row as parent, only in edit mode; when the entity appears the window selects it and opens its tab. ✅ Red-proof: the TKB of a trigger given as 8860 ⇒ red.</summary>
    [Fact]
    public void AddRule_RequestsTheEntity_WithTheTitleAndTheSelectedRowAsParent_AndSelectsItWhenItAppears()
    {
        Rule(10, "WeightedMean", """{"common":{"title":"Mission"}}""");
        var asked = new List<(long Tkb, string Kind, string Json)>();
        var svc = new RuleEditorServices(() => ComponentEditServices.CreateBuilder().Build(), () => null, (_, _, _) => true, _history, _ => Array.Empty<RuleJournalEntry>(),
            CreateRule: (tkb, kind, json) => { asked.Add((tkb, kind, json)); return true; });
        var window = new RulesWindow(() => _world, () => null, null, "r5c_add", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 0d, editor: svc) { ListView = false };   // U-18: these rails drive the Timeline
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

    // ── ⭐ CE-4308 (RX6, DESIGN_Rule_Editor_UX §2.5, rule-list-visual.svg) — the gallery ───────────────────────────────────────────────

    /// <summary>A link's network id: the picker writes the bare id for an entity with no GUID, else <c>{"NetworkId", "Ref"}</c>.</summary>
    private static int IdOf(JsonNode? link) => link is JsonObject o ? (int)o["NetworkId"]! : (int)link!;

    /// <summary>⭐ CE-4308 — the gallery offers the mockup's starting points in its groups; an area adds its own entry first; the search filters; a unit becomes the
    /// rule's subject; a phase gets the next phase number; the area's entry pre-fills the condition editor's own row (<i>is inside the area</i>).
    /// ✅ Red-proof: <c>Rule()</c> not writing <c>common.subject</c> ⇒ red.</summary>
    [Fact]
    public void CE4308_TheGallery_OffersTheMockupsStartingPoints_PreFilledFromWhereTheAuthorCame()
    {
        var all = RuleGallery.For(RuleGallery.Context.Empty);
        Assert.Equal(new[] { "Take and hold an area", "Reach an area", "Destroy", "Protect", "Survive", "Begin a phase when …", "End the exercise when …",
            "Bring in … when …", "Brief Blue when …", "Tell everyone when …", "Hit ratio", "Report latency" }, all.Select(t => t.Label));
        Assert.Equal(RuleGallery.Groups, all.Select(t => t.Group).Distinct());
        Assert.All(all, t => Assert.Contains(t.Kind, RuleAdd.Kinds()));                                      // every starting point is a real kind
        Assert.Equal(new[] { "Brief Blue when …", "Tell everyone when …" }, RuleGallery.For(RuleGallery.Context.Empty, "messag").Select(t => t.Label));

        var unit = new Fdp.Toolkit.Replication.EntityLink(new EntityRef(777));
        var area = new Fdp.Toolkit.Replication.EntityLink(new EntityRef(888));
        var c = new RuleGallery.Context(unit, area, NextPhase: 3);
        var shown = RuleGallery.For(c);
        Assert.Equal("When someone enters this area…", shown[0].Label);
        var protect = JsonNode.Parse(RuleGallery.ParamsJson(all.Single(t => t.Label == "Protect"), c, null))!;
        Assert.Equal(777, IdOf(protect["common"]!["subject"]));                                  // the unit is the subject …
        Assert.Equal(777, IdOf(protect["targets"]![0]));                                          // … and what is protected
        var phase = JsonNode.Parse(RuleGallery.ParamsJson(all.Single(t => t.Label == "Begin a phase when …"), c, new JsonObject { ["NetworkId"] = 5 }))!;
        Assert.Equal((3, "Phase 3", 5), ((int)phase["common"]!["phase"]!, (string)phase["common"]!["title"]!, (int)phase["common"]!["parent"]!["NetworkId"]!));
        var enters = JsonNode.Parse(RuleGallery.ParamsJson(shown[0], c, null))!;
        // the when is the condition editor's own row "any of Blue side is inside the area" (RowProjection.ToDto) — its shape, and the editor reads that shape back as the row
        var w = enters["when"]!;
        Assert.Equal(("CountMatching", 1, int.MaxValue), ((string)w["$type"]!, (int)w["Min"]!, (int)w["Max"]!));
        var conds = w["Predicate"]!["Conditions"]!.AsArray();
        Assert.Equal(("PropertyMatch", "ForceId", 1d), ((string)conds[0]!["$type"]!, (string)conds[0]!["PropertyPath"]!, (double)conds[0]!["Predicate"]!["MinValue"]!));
        Assert.Equal(("SpatialBounding", 888), ((string)conds[1]!["$type"]!, IdOf(conds[1]!["Bounds"])));
        var row = Fdp.Toolkit.ReplayBrowser.Search.Conditions.RowProjection.RowOf(Fdp.Toolkit.ReplayBrowser.Search.Conditions.RowProjection.ToDto(new Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionRow(
            Fdp.Toolkit.ReplayBrowser.Search.Conditions.Subject.OfSide(Fdp.Toolkit.ReplayBrowser.Search.Conditions.SubjectKind.Any, ForceId.Friend), "inside",
            Values: new Fdp.Toolkit.ReplayBrowser.Search.Conditions.FactValues(Area: AreaLink.OfEntity(new EntityRef(888))))));
        Assert.Contains("inside the area", Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionSentence.Of(row, null));
        var brief = JsonNode.Parse(RuleGallery.ParamsJson(all.Single(t => t.Label == "Brief Blue when …"), RuleGallery.Context.Empty, null))!;
        Assert.Equal("Message", (string)brief["do"]![0]!["act"]!);
        Assert.Null(brief["common"]!["subject"]);                                                               // no unit ⇒ no subject
    }

    /// <summary>⭐ CE-4308 — the window creates a gallery choice through the ONE creation path (the right TKB, the template's params, the selected row as parent),
    /// and <c>OpenGallery</c> pre-fills the context from a unit (as the map's <i>Add rule about this unit…</i> does). ✅ Red-proof: <c>AddFromGallery</c> passing
    /// <c>RuleAdd.NewParamsJson</c> ⇒ the title is "New Protect" ⇒ red.</summary>
    [Fact]
    public void CE4308_AGalleryChoice_IsCreatedThroughTheOnePath_AndAUnitPreFillsIt()
    {
        Rule(10, "WeightedMean", """{"common":{"title":"Mission"}}""");
        var asked = new List<(long Tkb, string Kind, string Json)>();
        var svc = new RuleEditorServices(() => ComponentEditServices.CreateBuilder().Build(), () => null, (_, _, _) => true, _history, _ => Array.Empty<RuleJournalEntry>(),
            CreateRule: (tkb, kind, json) => { asked.Add((tkb, kind, json)); return true; });
        var window = new RulesWindow(() => _world, () => null, null, "r5c_gallery", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 0d, editor: svc) { ListView = false };
        window.SelectNetworkId(10); window.Rebuild();
        window.OpenGallery(subjectNetworkId: 777);
        Assert.Equal(777, window.GalleryContext.Subject.Ref.NetworkId);
        Assert.False(window.GalleryContext.HasArea);
        var protect = RuleGallery.All.Single(t => t.Label == "Protect");
        Assert.True(window.AddFromGallery(protect, window.GalleryContext));
        var (tkb, kind, json) = Assert.Single(asked);
        var p = JsonNode.Parse(json)!;
        Assert.Equal((8860L, "Protect", "Protect", 10, 777), (tkb, kind, (string)p["common"]!["title"]!, (int)p["common"]!["parent"]!["NetworkId"]!, IdOf(p["targets"]![0])));
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

    // ── CE-4225: the stable link form, and reading it back ───────────────────────────────────────────────────

    /// <summary>
    /// A connect's <c>ref</c> and a new rule's <c>common.parent</c> are written as the link the entity PICKER writes — <c>{"NetworkId": n, "Ref": "&lt;guid&gt;"}</c> (<c>Exact</c> = the entity's GUID), the shape a scenario's authored triggers carry — never a bare network id,
    /// which is not stable across a save and reload (measured: after one, <c>"Ref": 1038</c> named <i>Clear the crossroads</i>, not <i>Hold the crossroads</i>). An entity with no GUID falls back to the bare id, as the picker does.
    /// ✅ Red-proof: <c>LinkOf</c> writing the bare id ⇒ the Ref assertions red.
    /// </summary>
    [Fact]
    public void AConnectsLinkAndANewRulesParent_AreWrittenInTheStableForm_TheEntitysGuid()
    {
        Rule(10, "ReachArea", """{"common":{"title":"Reach"}}""");
        var link = Link(10);
        Assert.Equal((10, GuidOf(10).ToString()), ((int)link["NetworkId"]!, link["Ref"]!.ToString()));
        var added = JsonNode.Parse(RuleConnect.AddedTo(Trig, RowOf(10), link, out _)!)!;
        Assert.Equal(GuidOf(10).ToString(), added["when"]!["ref"]!["Ref"]!.ToString());                    // the saved file keeps THIS
        var parent = JsonNode.Parse(RuleAdd.NewParamsJson("HoldArea", link))!["common"]!["parent"]!;
        Assert.Equal(GuidOf(10).ToString(), parent["Ref"]!.ToString());
        Assert.NotSame(link, parent);                                                                      // a copy: the one link is not shared between two parents
        // the form still reads it, and a stale id with the right GUID is the same rule
        Assert.Null(RuleConnect.AddedTo(added.ToJsonString(), RowOf(10), link, out string? why)); Assert.Contains("already waits", why);
        var stale = """{"common":{"title":"T"},"when":{"$type":"OnEntity","ref":{"NetworkId":9999,"Ref":"%G%"}}}""".Replace("%G%", GuidOf(10).ToString());
        Assert.Null(RuleConnect.AddedTo(stale, RowOf(10), link, out _));
        // no GUID ⇒ the bare id (what the picker writes for an unsaved helper)
        var bare = _world.CreateEntity(); _world.AddComponent(bare, new NetworkIdentity(50)); _world.SetManagedComponent(bare, new BehaviorStartRecord { BehaviorName = "ReachArea", JsonParams = """{"common":{"title":"NoGuid"}}""", InstanceId = 1 });
        Assert.Equal(50, (int)RuleConnect.LinkOf(RowOf(50), AuthoredRules.ScopeOf(_world))!);
    }

    /// <summary>
    /// ⭐ AuthoredRules read an authored link through the editor's ONE resolver (<c>LiveRefScope</c>): a GUID-only, an <c>"@Key"</c> and an object whose <c>NetworkId</c> is STALE but whose <c>Ref</c> is right all resolve to the rule they name, before any run — the rule view's arrows
    /// (edit mode) and <i>Waits on</i> come from them. ✅ Red-proof: the GUID branch of <c>LinkId</c> removed ⇒ the waits are empty ⇒ red.
    /// </summary>
    [Fact]
    public void AuthoredRules_ResolveAGuidOnly_AKey_AndAStaleIdWithTheRightGuid_BeforeARun()
    {
        var hold = Rule(10, "HoldArea", """{"common":{"title":"Hold"}}""");
        _world.AddComponent(hold, new RefKey("Hold"));
        Rule(11, "ReachArea", """{"common":{"title":"Reach"}}""");
        string On(string refJson) => "{\"$type\":\"OnEntity\",\"ref\":" + refJson + "}";
        Rule(20, "Trigger", "{\"common\":{\"title\":\"ByGuid\"},\"when\":" + On("\"" + GuidOf(10) + "\"") + "}");
        Rule(21, "Trigger", "{\"common\":{\"title\":\"ByKey\"},\"when\":" + On("\"@Hold\"") + "}");
        Rule(22, "Trigger", "{\"common\":{\"title\":\"Stale\"},\"when\":" + On("{\"NetworkId\":11,\"Ref\":\"" + GuidOf(10) + "\"}") + "}");   // the id says Reach, the guid says Hold: the guid wins
        Rule(23, "Trigger", "{\"common\":{\"title\":\"Id\"},\"when\":" + On("11") + "}");
        Rule(24, "Trigger", "{\"common\":{\"title\":\"Nowhere\"},\"when\":" + On("\"@Nothing\"") + "}");
        Assert.Equal(new[] { 10 }, RowOf(20).WaitsOn.Select(l => l.NetworkId));
        Assert.Equal(new[] { 10 }, RowOf(21).WaitsOn.Select(l => l.NetworkId));
        Assert.Equal(new[] { 10 }, RowOf(22).WaitsOn.Select(l => l.NetworkId));
        Assert.Equal(new[] { 11 }, RowOf(23).WaitsOn.Select(l => l.NetworkId));                            // a bare id is still an id
        Assert.Empty(RowOf(24).WaitsOn);                                                                   // a key that names nothing is none, not a crash
    }
}
