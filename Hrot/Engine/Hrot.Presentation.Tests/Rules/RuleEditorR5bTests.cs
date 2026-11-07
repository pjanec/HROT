using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.Map.Definitions.Behavior;
using Hrot.Presentation.Behavior;
using Hrot.Presentation.Edits;
using Hrot.UI.Common.Rules;
using StructEdit.Core;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>A catalogue action no trigger step can run (its params hold an <c>Entity</c>, CE-4162): the picker must list it DISABLED with its reason.</summary>
public struct R5bRefusedParams { public Entity Who; }
public struct R5bNoState { }
public static class R5bActions
{
    [SharedAiAction]
    public static NodeStatus R5bRefused(ref R5bRefusedParams p, ref R5bNoState s, Entity self, EntityRepository world) => NodeStatus.Success;
}

/// <summary>
/// ⭐⭐ CE-4220 (rules view R5b; docs/DESIGN_Rules_View_UI.md §7 RE-4 · RE-7 live · RE-3 live) — the do list's edits and what they serialise to, the standard leaves (the step list, the <c>Type</c> field), LIVE Apply
/// (<c>RuleCommands.Edit</c>: complete params, origin Operator, pending while Acting, the refusal shown, a kind with no keeper read-only, never in the scenario's undo history) and the Live strip (matched by label).
/// </summary>
public sealed class RuleEditorR5bTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly EditHistory _history = new();
    private readonly List<RuleJournalEntry> _journal = new();
    private readonly List<(string Json, int Origin)> _written = new();
    private readonly List<bool> _liveValues = new();
    private int _writeSeq;

    private const string TriggerJson = """{"common":{"title":"Counter-attack","subject":{"NetworkId":900}},"cooldown":10,"do":[{"act":"Message","args":{"Text":"hello"}},{"act":"Message","args":{"Text":"second"}}]}""";

    public RuleEditorR5bTests()
    {
        _ = typeof(Hrot.AI.Behaviors.Brains.Objectives.ObjectiveStateKeepers).Assembly;   // the shipped actions (Message …) are in the catalogue's scan
        ActionCatalogue.Discover(typeof(R5bActions).Assembly);
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<ObjectiveStatus>();
        _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterComponent<RuleLeaves>();
        _world.RegisterManagedComponent<BehaviorStartRecord>();
    }

    public void Dispose() => _world.Dispose();

    private Entity Rule(int id, string kind, string json, ObjectiveRole role = ObjectiveRole.Trigger, TriggerState trig = TriggerState.Armed, byte cur = 0, byte count = 0, string reason = "")
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.SetManagedComponent(e, new BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 });
        _world.AddComponent(e, new ObjectiveStatus { Role = role, State = ObjectiveState.Active, Title = new FixedString64("Counter-attack"), TriggerState = trig, StepCur = cur, StepCount = count, Reason = new FixedString64(reason) });
        _world.AddComponent(e, new ObjectiveLinks());
        return e;
    }

    private static string? EditRefusal(string kind) => kind == BehaviorNames.Trigger ? null : "not editable live, restart the rule";

    private RuleDetails Live(bool withLive = true)
    {
        var svc = new RuleEditorServices(() => ComponentEditServices.CreateBuilder().Build(), () => null, (_, _, _) => true, _history, _ => _journal.ToList(),
            Live: withLive ? new RuleLiveServices(EditRefusal, on => _liveValues.Add(on)) : null);
        var d = new RuleDetails(svc, () => _world, () => null, _ => { }, (_, _) => { });
        d.LiveWriter = Write;
        return d;
    }

    private Guid _request;
    private void Outcome(Fdp.Toolkit.Replication.Edits.EditStatus status, string? reason = null)
        => Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(_world).Add(Fdp.Toolkit.Replication.Edits.EditOutcome.Create(_request, 20, status, reason, 1, 12d, fromNetwork: false));

    private Fdp.Toolkit.Replication.Patching.EntityWriteRoute Write(EntityRepository w, Entity e, string json, int origin, ref int last, out int seq, out Guid requestId)
    {
        seq = ++last; _written.Add((json, origin)); requestId = _request = Guid.NewGuid();
        return Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct;
    }

    private RuleRow RowOf(int id) => RulesModel.Build(_world, new RulesOptions(), null, 10).Find(id)!;

    // ── the do list's edits → the JSON the runtime parses ─────────────────────────────────────────────────────────────

    /// <summary>
    /// RE-4: add / reorder / remove each build a NEW list, and the list serialises to exactly what <c>ActionList.Get</c> parses (order and acts) and <c>ActionList.Validate</c> accepts.
    /// ✅ Red-proof: <c>Moved</c> moving the step the wrong way ⇒ the parsed order differs ⇒ red.
    /// </summary>
    [Fact]
    public void TheDoList_AddReorderRemove_SerialiseToWhatTheRuntimeParses()
    {
        var message = ActionCatalogue.All.Single(a => a.ShortName == "Message");
        var list = new List<ActionStep> { new() { Act = "Message", Args = new JsonObject { ["Text"] = "one" } } };
        list = ActionListEdit.Added(list, message);                                                       // + Add step ⇒ a second Message with empty args
        list = ActionListEdit.WithArgs(list, 1, new JsonObject { ["Text"] = "two" });
        var wait = ActionCatalogue.All.First(a => a.Refusal == null && a.ShortName != "Message");
        list = ActionListEdit.Added(list, wait);
        Assert.Equal(new[] { "Message", "Message", ActionListEdit.ActName(wait) }, ActionList.Get(ActionStep.ToJson(list)).Select(s => s.Act));

        var moved = ActionListEdit.Moved(list, 2, -1);                                                    // the third up one
        Assert.Equal(new[] { "Message", ActionListEdit.ActName(wait), "Message" }, ActionList.Get(ActionStep.ToJson(moved)).Select(s => s.Act));
        Assert.Equal(new[] { "one", "two" }, ActionList.Get(ActionStep.ToJson(moved)).Where(s => s.Act == "Message").Select(s => (string)JsonNode.Parse(s.ArgsJson)!["Text"]!));   // the args travelled with their step
        Assert.Equal(list.Count, ActionListEdit.Moved(list, 0, -1).Count);                                // the ends do not move
        Assert.Equal("Message", ActionListEdit.Moved(list, 0, -1)[0].Act);

        var removed = ActionListEdit.Removed(moved, 1);
        Assert.Equal(new[] { "Message", "Message" }, ActionList.Get(ActionStep.ToJson(removed)).Select(s => s.Act));
        Assert.NotSame(list, removed);                                                                    // a new list each time (an ActionStep is immutable)
        Assert.Equal(3, list.Count);

        // a Message step with real args passes the runtime's own strict check
        var ok = new List<ActionStep> { new() { Act = "Message", Args = new JsonObject { ["Text"] = "hello" } } };
        Assert.Single(ActionList.Validate(ActionStep.ToJson(ok)));
    }

    /// <summary>RE-4: a step's args are a params document of the action's ParamsType: the form's JSON, laid over the original args, keeps a key the type does not know (a nested <c>params</c>) and reads back strictly.</summary>
    [Fact]
    public void AStepsArgs_RoundTripThroughItsParamsType_AndKeepAnUnknownKey()
    {
        var message = ActionCatalogue.All.Single(a => a.ShortName == "Message");
        var service = ComponentEditServices.CreateBuilder().Build();
        using var session = BehaviorParamsForm.Open(service, message.ParamsType, """{"Text":"hello","Seconds":7}""");
        string form = BehaviorParamsForm.CommitToJson(session, message.ParamsType);
        Assert.Contains("hello", form);
        var merged = ActionListEdit.MergeArgs(new JsonObject { ["Text"] = "old", ["Extra"] = 5 }, form);
        Assert.Equal("hello", (string?)merged["Text"]);
        Assert.Equal(5, (int?)merged["Extra"]);                                                           // never silently dropped
        Assert.Equal(7f, (float?)merged["Seconds"]);
        // the params type reads the serialised args back (the runtime's strict check, minus the unknown key)
        merged.Remove("Extra");
        ActionCatalogue.RunnerFor("Message").CheckArgs(merged.ToJsonString());
    }

    /// <summary>RE-4: the ONE picker lists the catalogue (ShortName, full name as the detail); an action a trigger step cannot run is listed DISABLED with the reason.</summary>
    [Fact]
    public void ThePickerRows_ListTheCatalogue_AndARefusedActionIsDisabledWithItsReason()
    {
        var rows = ActionListEdit.PickerEntries();
        Assert.Equal(ActionCatalogue.All.Count, rows.Count);
        var message = rows.Single(r => r.Name == "Message");
        Assert.True(message.IsEnabled);
        Assert.Contains("ScenarioVerbs.Message", message.Description);                                    // the full name is the detail
        var refused = rows.Single(r => r.Name == "R5bRefused");
        Assert.False(refused.IsEnabled);
        Assert.Contains("Entity field", refused.Description);                                             // …and says why
        Assert.Equal("rules.add-step", ActionListEdit.Request().ContextKey);
        Assert.Equal(rows.Count, ActionListEdit.Request().ItemsProvider().Count());
    }

    // ── the standard leaves ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The standard services give the do list ONE leaf (a Custom node holding the whole list) and a `Type` field a Custom leaf — not a container drawn as "RuntimeType" with an empty combo. ✅ Red-proof: the `Type` registration dropped ⇒ red.</summary>
    [Fact]
    public void TheStandardServices_GiveTheDoListAndATypeFieldTheirLeaves()
    {
        var svc = ComponentEditServices.CreateBuilder().Build();
        EditNode? Find(EditNode n, string name) => n.Name == name ? n : n.Children.Select(c => Find(c, name)).FirstOrDefault(x => x != null);
        var dto = new TriggerParamsJsonDto { Do = new List<ActionStep> { new() { Act = "Message" } } };
        using (var s = svc.Open(dto, typeof(TriggerParamsJsonDto), null))
        {
            var d = Find(s.Document.Root, "Do")!;
            Assert.Equal((EditNodeKind.Custom, typeof(List<ActionStep>)), (d.Kind, d.ClrType));
            Assert.Empty(d.Children);                                                                      // one leaf, not a row per step
        }
        var pm = new PropertyMatchDto { ComponentType = typeof(ObjectiveStatus), PropertyPath = "State", Predicate = new NumericPredicateDto() };
        using (var s = svc.Open(pm, typeof(PropertyMatchDto), null))
            Assert.Equal(EditNodeKind.Custom, Find(s.Document.Root, "ComponentType")!.Kind);
    }

    // ── RE-7 live: Apply ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A LIVE trigger is editable (its kind has a keeper) and Apply writes the COMPLETE params through <c>RuleCommands.Edit</c> as an Operator, pushes NOTHING to the scenario's undo history, and shows "sent" until the owner answers.
    /// ✅ Red-proof: the origin sent as Superior (not Operator) ⇒ red.
    /// </summary>
    [Fact]
    public void LiveApply_WritesTheCompleteParams_AsAnOperator_AndNeverTouchesTheScenariosUndo()
    {
        var e = Rule(20, "Trigger", TriggerJson);
        var d = Live(); var row = RowOf(20);
        d.Prepare(row, 10);
        Assert.True(d.Probe.Editable);
        string edited = TriggerJson.Replace("\"cooldown\":10", "\"cooldown\":25");
        d.StagePending(edited); d.Prepare(row, 10);
        Assert.True(d.Probe.Dirty);
        Assert.True(d.Apply(row));
        Assert.Equal((edited, (int)BehaviorOrigin.Operator), _written.Single());                          // complete JSON, origin Operator
        Assert.Equal(0, _history.Count);                                                                  // a live edit is this run only
        d.Prepare(row, 11);
        Assert.Equal("sent - waiting for the owner", d.Probe.LiveEdit);
    }

    /// <summary>A trigger that is ACTING holds the edit until its list ends: the editor shows "pending (acting)" from its own Seq until the journal says applied — then "applied — the rule's state is kept".</summary>
    [Fact]
    public void LiveApply_OnAnActingTrigger_IsPending_UntilTheJournalShowsApplied()
    {
        Rule(20, "Trigger", TriggerJson, trig: TriggerState.Acting, cur: 2, count: 3);
        var d = Live(); var row = RowOf(20);
        d.Prepare(row, 10); d.StagePending(TriggerJson.Replace("\"cooldown\":10", "\"cooldown\":25")); d.Prepare(row, 10);
        Assert.True(d.Apply(row));
        d.Prepare(row, 11);
        Assert.StartsWith("sent - waiting", d.Probe.LiveEdit);                                            // nothing heard yet
        Outcome(Fdp.Toolkit.Replication.Edits.EditStatus.Pending);
        d.Prepare(row, 12);
        Assert.StartsWith("pending (acting)", d.Probe.LiveEdit);                                          // the owner holds it until the do list ends
        Outcome(Fdp.Toolkit.Replication.Edits.EditStatus.Applied);
        d.Prepare(row, 13);
        Assert.StartsWith("applied", d.Probe.LiveEdit);
        Assert.Contains("this run only", d.Probe.LiveEdit);
    }

    /// <summary>
    /// ⭐ CE-2380 part 2 (RE-7, ⑤) — a refusal shows the owner's WHOLE reason (the status <c>Reason</c> is cut to 55 characters; the OUTCOME is not), read from the edit-outcome log by the request id; no outcome for 5 s ⇒ "the owner did not answer".
    /// ✅ Red-proof: the reason cut to 55 characters ⇒ the full-text assertion fails; the 5 s branch removed ⇒ the last one fails.
    /// </summary>
    [Fact]
    public void LiveApply_Refused_ShowsTheWholeReason_AndNoAnswerSaysSo()
    {
        var e = Rule(20, "Trigger", TriggerJson);
        var d = Live(); var row = RowOf(20);
        var clock = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc); d.Now = () => clock;
        d.Prepare(row, 10); d.StagePending(TriggerJson.Replace("\"cooldown\":10", "\"cooldown\":-1")); d.Prepare(row, 10);
        Assert.True(d.Apply(row));
        Outcome(Fdp.Toolkit.Replication.Edits.EditStatus.Pending);
        d.Prepare(RowOf(20), 11);
        Assert.StartsWith("sent - the owner is judging", d.Probe.LiveEdit);
        string whole = "edit refused: do step 2 'Teleport': unknown action 'Teleport' (not in the catalogue) - cooldown must be >= 0 and a number, got -1";
        Outcome(Fdp.Toolkit.Replication.Edits.EditStatus.Refused, whole);
        d.Prepare(RowOf(20), 12);
        Assert.Equal("refused - " + whole, d.Probe.LiveEdit);                                             // > 55 characters, whole

        // no outcome at all: waits, then says the owner did not answer
        var d2 = Live(); d2.Now = () => clock;
        d2.Prepare(row, 10); d2.StagePending(TriggerJson.Replace("\"cooldown\":10", "\"cooldown\":25")); d2.Prepare(row, 10);
        Assert.True(d2.Apply(row));
        clock += RuleDetails.OwnerAnswerWait - TimeSpan.FromSeconds(1); d2.Prepare(RowOf(20), 11);
        Assert.StartsWith("sent - waiting", d2.Probe.LiveEdit);
        clock += TimeSpan.FromSeconds(2); d2.Prepare(RowOf(20), 12);
        Assert.Equal("the owner did not answer", d2.Probe.LiveEdit);
    }

    /// <summary>
    /// ⭐ CE-2380 part 2 (RV-5) — a refused Fire / Disarm / Re-arm / Reset says why in the owner's WHOLE words, read by the request id the control write returned; a Pending one says nothing yet; an Applied one reads done.
    /// ✅ Red-proof: the Refused branch of <c>PollControl</c> not copying <c>o.Reason</c> ⇒ red.
    /// </summary>
    [Fact]
    public void AControlRequest_ShowsTheOwnersWholeRefusal()
    {
        var window = new Hrot.Presentation.Windows.RulesWindow(() => _world, () => null, null, "t_ctl", "Scenario", System.Numerics.Vector4.One, _ => { }, _ => { }, now: () => 5d);
        _request = Guid.NewGuid();
        window.ExpectControl(_request, "Fire");
        Outcome(Fdp.Toolkit.Replication.Edits.EditStatus.Pending);
        window.PollControl();
        Assert.Null(window.Message);                                                                      // still judging: nothing to say
        string whole = "cannot fire: the trigger is Disarmed - re-arm it first, or it is waiting out its cooldown of 600 s";
        Outcome(Fdp.Toolkit.Replication.Edits.EditStatus.Refused, whole);
        window.PollControl();
        Assert.Equal("Fire: refused - " + whole, window.Message);
    }

    /// <summary>A kind the owner cannot re-parameterise (no state keeper) is READ-ONLY up front, with the owner's own wording; Apply writes nothing. A host with no live services keeps every running rule read-only.</summary>
    [Fact]
    public void ANonEditableKind_IsReadOnlyLive_WithTheReason_AndNothingIsWritten()
    {
        Rule(30, "NoHarm", """{"common":{"title":"No harm"}}""", ObjectiveRole.Objective);
        var d = Live(); var row = RowOf(30);
        d.Prepare(row, 10);
        Assert.False(d.Probe.Editable);
        Assert.Equal("not editable live, restart the rule", d.Probe.ReadOnlyReason);
        d.StagePending("""{"common":{"title":"x"}}""");
        Assert.False(d.Apply(row));
        Assert.Empty(_written);
        Rule(20, "Trigger", TriggerJson);
        Assert.Equal(RuleDetails.LiveReadOnly, Live(withLive: false).ReadOnlyReason(RowOf(20)));
    }

    // ── RE-3 live: the strip ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The owner's <c>RuleLeaves</c> (431) are read, and laid over the form's own leaves BY LABEL (the label the owner stamped, from the same flattening): a matched leaf carries its value, an unmatched form leaf says "not sampled", a sampled leaf the form lacks is flagged.</summary>
    [Fact]
    public void TheLiveStrip_MatchesTheOwnersSamples_ToTheFormsLeaves_ByLabel()
    {
        var e = Rule(20, "Trigger", TriggerJson);
        var pm = new PropertyMatchDto { ComponentType = typeof(ObjectiveStatus), PropertyPath = "State", Operator = SearchOperator.Equals, Predicate = new NumericPredicateDto() };
        var when = new CompoundPredicateDto { Conditions = { pm, new ScenarioTimePredicateDto() } };
        string a = RuleLeafPlan.LabelOf(pm);                                                              // "ObjectiveStatus.State"
        _world.AddComponent(e, new RuleLeaves());
        ref var leaves = ref _world.GetComponentRW<RuleLeaves>(e);
        leaves.Count = 2;
        leaves.Leaves[0] = new RuleLeaf { Label = new FixedString32(a), Value = 1, Measured = 2f };
        leaves.Leaves[1] = new RuleLeaf { Label = new FixedString32("Count Gone"), Value = 0, Measured = 3f };   // the owner still runs a leaf the form no longer has

        var live = LiveLeafOverlay.Read(_world, e);
        Assert.Equal(new[] { a, "Count Gone" }, live.Select(l => l.Label));
        var rows = LiveLeafOverlay.Match(live, when);
        Assert.Equal(new[] { (a, true, true, true), ("ScenarioTime", false, false, true), ("Count Gone", true, false, false) },
                     rows.Select(r => (r.Label, r.Sampled, r.Holds, r.InForm)));
        Assert.Equal(2f, rows[0].Measured);
        Assert.Empty(LiveLeafOverlay.Read(_world, _world.CreateEntity()));                                // no component ⇒ nothing, not a throw
    }

    /// <summary>Found LIVE: the owner's label is a FixedString32, so an OnEntity leaf (the target's authored ref + the inner leaf) arrives CUT — the strip matches on the cut form; two leaves sharing it pair in order.</summary>
    [Fact]
    public void TheLiveStrip_MatchesALongLabelThatTheOwnerCut()
    {
        var pm = new PropertyMatchDto { ComponentType = typeof(ObjectiveStatus), PropertyPath = "AVeryLongPropertyPathThatCannotFit" };
        string full = RuleLeafPlan.LabelOf(pm);
        Assert.True(full.Length > 32);
        string cut = new FixedString32(full).ToString();
        Assert.True(cut.Length < full.Length);
        var when = new CompoundPredicateDto { Conditions = { pm, pm } };
        var rows = LiveLeafOverlay.Match(new[] { new LiveLeaf(cut, true, 1f), new LiveLeaf(cut, false, 2f) }, when);
        Assert.Equal(new[] { (true, 1f), (true, 2f) }, rows.Select(r => (r.Sampled && r.InForm, r.Measured)));
        Assert.Equal(new[] { full, full }, rows.Select(r => r.Label));                                    // the strip shows the full label
    }

    /// <summary>RuleLeafPlan's flattening is ONE function (compound descended, every other predicate a leaf, at most eight) shared by the owner's plan and this strip.</summary>
    [Fact]
    public void TheLeavesOf_AWhen_AreFlattenedThroughCompoundsOnly_AtMostEight()
    {
        var many = new CompoundPredicateDto();
        for (int i = 0; i < 10; i++) many.Conditions.Add(new ScenarioTimePredicateDto());
        var leaves = RuleLeafPlan.LeavesOf(new CompoundPredicateDto { Conditions = { many, new NumericPredicateDto() } }, out bool truncated);
        Assert.Equal(RuleLeafPlan.MaxLeaves, leaves.Count);
        Assert.True(truncated);
        Assert.Empty(RuleLeafPlan.LeavesOf(null, out _));
    }
    // ═══════════════════════ ⭐ CE-4307 (RX5, docs/UX/DESIGN_Rule_Editor_UX.md §2.4, rule-list-visual.svg) — actions as sentences; Order by order kind ═══════════════════════

    private static ActionStep Step(string act, string args) => new() { Act = act, Args = (JsonObject)JsonNode.Parse(args)! };

    /// <summary>⭐ CE-4307 — each catalogue verb reads as words (the ONE renderer the List line and the Do tab share): verb words are grammar, the rest content.
    /// ✅ Red-proof: the switch's <c>"Message"</c> arm removed ⇒ the step reads "Message" alone ⇒ red.</summary>
    [Fact]
    public void ADoStep_ReadsAsASentence_GrammarAndContentApart()
    {
        Assert.Equal("Message everyone: \"Phase 2\"", ActionSentence.Of(Step("Message", """{"Text":"Phase 2"}"""), null));
        Assert.Equal("Bring in red-qrf at @Drop", ActionSentence.Of(Step("InsertMacro", """{"macro":"forces/red-qrf","at":"@Drop"}"""), null));
        Assert.Equal("End the exercise — Friend wins", ActionSentence.Of(Step("EndExercise", """{"reason":"held","winner":"Friend"}"""), null));
        Assert.Equal("End the exercise — a draw", ActionSentence.Of(Step("EndExercise", """{"winner":"Draw"}"""), null));
        Assert.Equal("Take 1 from @Blue_tickets", ActionSentence.Of(Step("AddToCounter", """{"target":"@Blue_tickets","amount":-1}"""), null));
        Assert.Equal("Order @Alpha to (no order yet)", ActionSentence.Of(Step("Order", """{"target":"@Alpha"}"""), null));
        var segs = ActionSentence.Parts(Step("Message", """{"Text":"go"}"""), null);
        Assert.True(segs[0].Grammar); Assert.Equal("Message ", segs[0].Text);
        Assert.Contains(segs, x => !x.Grammar && x.Text == "\"go\"");
        Assert.Equal("Unknown verb", ActionSentence.Of(Step("UnknownVerb", "{}"), null));   // an act the table does not know: its humanised name
    }

    /// <summary>⭐ CE-4307 — the List line carries each step's sentence after the arrow. ✅ Red-proof: <c>RuleSentence</c> back on <c>Humanize(act)</c> ⇒ red.</summary>
    [Fact]
    public void TheListLine_SaysWhatEachStepDoes()
    {
        const string json = """{"common":{"title":"Push"},"when":{"$type":"ScenarioTime","FromSeconds":60},"do":[{"act":"Message","args":{"Text":"Phase 2"}},{"act":"EndExercise","args":{"winner":"Hostile"}}]}""";
        var (text, incomplete) = RuleSentence.Of("Trigger", json, "Push", false, null);
        Assert.False(incomplete);
        Assert.EndsWith("→ Message everyone: \"Phase 2\", End the exercise — Hostile wins", text);
    }

    /// <summary>⭐ CE-4307 — an Order step is set by ORDER KIND (`Order.&lt;kind&gt;`, the receiver's class picks the behaviour); the other args stay; a behaviour name
    /// (Advanced) has no kind. ✅ Red-proof: <c>WithOrderKind</c> writing the bare kind ⇒ <c>OrderKindOf</c> null ⇒ red.</summary>
    [Fact]
    public void AnOrderStep_IsSetByOrderKind_AndReadsAsOne()
    {
        var kinds = Hrot.Common.Orders.OrderMenu.Orders;
        Assert.NotEmpty(kinds);
        string kind = kinds[0].Name;
        var list = new List<ActionStep> { Step("Order", """{"target":"@Alpha","behavior":"MoveToLocation","params":{"x":1}}""") };
        Assert.Null(ActionListEdit.OrderKindOf(list[0]));                                                       // a behaviour (Advanced): no kind
        var set = ActionListEdit.WithOrderKind(list, 0, kind);
        Assert.Equal(kind, ActionListEdit.OrderKindOf(set[0]));
        Assert.Equal("Order." + kind, (string)set[0].Args!["behavior"]!);
        Assert.Equal(1, (int)set[0].Args!["params"]!["x"]!);                                                     // the other args kept
        Assert.Equal("@Alpha", (string)set[0].Args!["target"]!);
        Assert.Equal($"Order @Alpha to {StructEdit.Core.Attributes.DisplayNames.Humanize(kind)}", ActionSentence.Of(set[0], null));
        Assert.True(ActionListEdit.IsOrder("Order")); Assert.False(ActionListEdit.IsOrder("Message"));
    }
}
