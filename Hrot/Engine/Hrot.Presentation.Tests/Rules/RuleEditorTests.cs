using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;
using Hrot.Presentation.Edits;
using Hrot.UI.Common.Rules;
using StructEdit.Core;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐⭐ CE-4218 (rules view R5a; docs/DESIGN_Rules_View_UI.md §7 RE-1 … RE-7, A1, A4) — the rule editor's DATA and Apply: <see cref="RuleTabs"/> completeness over EVERY rule DTO, the predicate value leaf as a standard leaf (and what it does NOT reach), the shared
/// <see cref="EditHistory"/>, Apply in edit mode (the entity's params change, ONE history entry, undo restores), and the read-only reasons (A1 macro, live, a recording).
/// </summary>
public sealed class RuleEditorTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly EditHistory _history = new();
    private readonly List<(string Name, string Json)> _applied = new();
    private bool _refuse;

    private const string TriggerJson = """{"common":{"title":"Counter-attack","subject":{"NetworkId":900}},"when":{"$type":"OnEntity","ref":{"NetworkId":10},"predicate":{"$type":"PropertyMatch","ComponentType":"ObjectiveStatus","PropertyPath":"State","Operator":"Equals","Predicate":{"$type":"Numeric","MinValue":2,"MaxValue":2}}},"cooldown":10,"repeat":true}""";

    public RuleEditorTests()
    {
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<ObjectiveStatus>();
        _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterComponent<MacroOrigin>();
        _world.RegisterManagedComponent<MacroInstance>();
        _world.RegisterManagedComponent<BehaviorStartRecord>();
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

    private RuleDetails Details(string? blocked = null, Action<int>? select = null)
    {
        var svc = new RuleEditorServices(
            () => ComponentEditServices.CreateBuilder().Build(), () => null,
            (entity, name, json) =>
            {
                if (_refuse) return false;
                _applied.Add((name, json));
                _world.SetManagedComponent(entity, new BehaviorStartRecord { BehaviorName = name, JsonParams = json, InstanceId = 1 });   // what AssignNow stamps
                return true;
            }, _history, _ => Array.Empty<RuleJournalEntry>());
        return new RuleDetails(svc, () => _world, () => blocked, select ?? (_ => { }), (_, _) => { });
    }

    private RuleRow RowOf(int id) => RulesModel.Build(_world, new RulesOptions(), null, 10).Find(id)!;

    // ── RuleTabs: every field of every rule DTO lands in exactly one tab ────────────────────────────────────────────────────

    /// <summary>✅ Red-proof: <c>TabOfField</c> sending <c>Cooldown</c> nowhere (null = header) ⇒ the field is missing from the tabs ⇒ red.</summary>
    [Fact]
    public void EveryFieldOfEveryRuleDto_LandsInExactlyOneTab_OrTheHeader()
    {
        var dtos = ParamsDtoRegistry.Default.Dtos.Select(d => d.Type).Where(RuleTabs.IsRuleParams).ToList();
        Assert.True(dtos.Count >= 15, $"expected the trigger and every kind DTO, saw {dtos.Count}");
        foreach (var t in dtos)
        {
            var members = RuleTabs.MembersOf(t);
            Assert.Contains(RuleTabs.HeaderField, members);
            var tabsOf = Enum.GetValues<RuleTab>().ToDictionary(tab => tab, tab => RuleTabs.FieldsOf(t, tab));
            foreach (var m in members.Where(m => m != RuleTabs.HeaderField))
                Assert.True(tabsOf.Count(kv => kv.Value.Contains(m)) == 1, $"{t.Name}.{m} must be in exactly one tab, is in {tabsOf.Count(kv => kv.Value.Contains(m))}");
            Assert.All(tabsOf.Values, f => Assert.DoesNotContain(RuleTabs.HeaderField, f));   // the header is drawn on Settings (U-9), never a tab's own field
        }
    }

    [Fact]
    public void ATriggersFields_AreSplitWhenRulesDo_AnObjectivesAreAllSettings()
    {
        var trig = typeof(TriggerParamsJsonDto);
        // ⭐ U-9 (§8.2 ⑧) — When = the condition + its timing; Settings = the header only (drawn there, not a tab's own field)
        Assert.Equal(new[] { "When", "HoldFor", "Delay", "Cooldown", "Repeat", "MaxFires", "FireIfTrueWhenArmed", "WhileTrue" }.OrderBy(x => x), RuleTabs.FieldsOf(trig, RuleTab.When).OrderBy(x => x));
        Assert.Equal(new[] { "Do" }, RuleTabs.FieldsOf(trig, RuleTab.Do));
        Assert.Empty(RuleTabs.FieldsOf(trig, RuleTab.Rules));
        Assert.Empty(RuleTabs.FieldsOf(trig, RuleTab.Settings));
        Assert.Equal(new[] { RuleTab.Overview, RuleTab.When, RuleTab.Do, RuleTab.Settings, RuleTab.Bindings, RuleTab.History }, RuleTabs.TabsFor(ObjectiveRole.Trigger));
        Assert.Equal(new[] { RuleTab.Status, RuleTab.Settings, RuleTab.Results, RuleTab.History }, RuleTabs.TabsFor(ObjectiveRole.Objective));
        Assert.Equal(RuleTabs.TabsFor(ObjectiveRole.Objective), RuleTabs.TabsFor(ObjectiveRole.Measure));
    }

    // ── RE-3 / A4: the predicate value leaf ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A4, measured: of every <c>SearchPredicateDto</c> MEMBER, only the three value leaves of a property match carry <c>[PredicateValue]</c> — never the sub-tree ones (<c>OnEntity.Predicate</c>, <c>CountMatching.Predicate</c>) that the old name gate also caught.</summary>
    [Fact]
    public void A4_TheValueLeafIsDeclaredOnTheThreeValueMembersOnly()
    {
        var members = typeof(SearchPredicateDto).Assembly.GetTypes().Where(t => typeof(SearchPredicateDto).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(p => p.PropertyType == typeof(SearchPredicateDto)).Select(p => (Type: t, Prop: p))).ToList();
        var marked = members.Where(m => m.Prop.GetCustomAttribute<PredicateValueAttribute>() != null).Select(m => m.Type.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { nameof(BehaviorParamPredicateDto), nameof(BlueprintVariablePredicateDto), nameof(PropertyMatchDto) }, marked);
        Assert.Contains(members, m => m.Type == typeof(OnEntityPredicateDto) && m.Prop.GetCustomAttribute<PredicateValueAttribute>() == null);
        Assert.Contains(members, m => m.Type == typeof(CountMatchingPredicateDto) && m.Prop.GetCustomAttribute<PredicateValueAttribute>() == null);
    }

    /// <summary>The leaf comes from the STANDARD services: a form over a property match gets a Custom node for its value; one over OnEntity does not. ✅ Red-proof: the editor gated by name again ⇒ OnEntity.Predicate custom ⇒ red.</summary>
    [Fact]
    public void TheStandardServices_GiveAPropertyMatchsValueTheLeaf_NotAnOnEntitysSubtree()
    {
        var svc = ComponentEditServices.CreateBuilder().Build();
        EditNode? Find(EditNode n, string name) => n.Name == name ? n : n.Children.Select(c => Find(c, name)).FirstOrDefault(x => x != null);
        var pm = new PropertyMatchDto { ComponentType = typeof(Health0), PropertyPath = "X", Predicate = new NumericPredicateDto() };
        using (var s = svc.Open(pm, typeof(PropertyMatchDto), null))
            Assert.Equal(EditNodeKind.Custom, Find(s.Document.Root, "Predicate")!.Kind);
        var oe = new OnEntityPredicateDto { Predicate = new NumericPredicateDto() };
        using (var s = svc.Open(oe, typeof(OnEntityPredicateDto), null))
            Assert.NotEqual(EditNodeKind.Custom, Find(s.Document.Root, "Predicate")!.Kind);
    }

    private struct Health0 { public float X; }

    // ── the one undo history ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheEditHistory_PushesUndoesRedoes_AndANewStepDropsTheRedoTail()
    {
        var h = new EditHistory(); int v = 0;
        h.Do("a", () => v = 1, () => v = 0);
        h.Push("b", () => v = 2, () => v = 1); v = 2;               // already applied by the caller
        Assert.Equal(("b", true, false), (h.UndoLabel, h.CanUndo, h.CanRedo));
        Assert.True(h.Undo()); Assert.Equal(1, v);
        Assert.Equal("b", h.RedoLabel);
        Assert.True(h.Redo()); Assert.Equal(2, v);
        h.Undo(); h.Do("c", () => v = 9, () => v = 1);              // a new step after an undo
        Assert.False(h.CanRedo);
        Assert.Equal(2, h.Count);
        h.Undo(); h.Undo(); Assert.False(h.Undo()); Assert.Equal(0, v);
    }

    // ── Apply (edit mode) ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Apply in EDIT mode: the entity's params change through the host's apply, ONE history entry "edit &lt;title&gt;", undo restores the previous JSON, redo re-applies. ✅ Red-proof: <c>Apply</c> not pushing the history ⇒ red.</summary>
    [Fact]
    public void Apply_InEditMode_ChangesTheEntitysParams_PushesOneEntry_UndoRestores()
    {
        var e = Rule(20, "Trigger", TriggerJson);
        var d = Details(); var row = RowOf(20);
        d.Prepare(row, 10);
        Assert.True(d.Probe.Editable);
        Assert.DoesNotContain(RuleTabs.HeaderField, d.Probe.Fields);   // ⭐ U-9 — the header is drawn on Settings only (the frame rail asserts it there)
        string edited = TriggerJson.Replace("\"cooldown\":10", "\"cooldown\":25");
        d.StagePending(edited);
        d.Prepare(row, 10);
        Assert.True(d.Probe.Dirty);
        Assert.True(d.Apply(row));
        Assert.Equal(edited, Params(e));
        Assert.Equal(1, _history.Count);
        Assert.Equal("edit Counter-attack", _history.UndoLabel);
        Assert.True(d.Undo());
        Assert.Equal(TriggerJson, Params(e));
        Assert.True(d.Redo());
        Assert.Equal(edited, Params(e));
    }

    [Fact]
    public void Apply_RefusedByTheOwner_ChangesNothing_AndPushesNothing()
    {
        var e = Rule(20, "Trigger", TriggerJson); _refuse = true;
        var d = Details(); var row = RowOf(20);
        d.Prepare(row, 10); d.StagePending(TriggerJson.Replace("\"cooldown\":10", "\"cooldown\":99"));
        Assert.False(d.Apply(row));
        Assert.Equal((TriggerJson, 0), (Params(e), _history.Count));
        Assert.Contains("refused", d.Message);
    }

    private string Params(Entity e) => ((Fdp.ModuleHost.Abstractions.ISimulationView)_world).GetManagedComponentRO<BehaviorStartRecord>(e)!.JsonParams;

    // ── read-only ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A1: a rule from a macro instance is READ-ONLY, says whose macro, Apply writes nothing; the jump selects the instance. ✅ Red-proof: the macro clause dropped from <c>ReadOnlyReason</c> ⇒ red.</summary>
    [Fact]
    public void A1_ARuleFromAMacro_IsReadOnly_WithTheMacrosName_ApplyWritesNothing()
    {
        var e = Rule(20, "Trigger", TriggerJson, macroInstance: 500);
        var d = Details(); var row = RowOf(20);
        Assert.Equal("from macro test/fire-team — edit the macro or its args", d.ReadOnlyReason(row));
        Assert.Equal(500, row.MacroInstance);                                   // the jump's target
        d.Prepare(row, 10); d.StagePending(TriggerJson.Replace("\"cooldown\":10", "\"cooldown\":99"));
        Assert.False(d.Apply(row));
        Assert.Equal((TriggerJson, 0), (Params(e), _history.Count));
        Assert.False(d.Probe.Editable);
    }

    /// <summary>
    /// ⭐ CE-4249 — a NEW trigger (<i>+ Add ▸ Trigger</i>) has no <c>when</c>; Apply on its title alone is REFUSED in words that name the cause (the runtime's reason used to be only in the log: "refused: the assignment was not accepted"), nothing is written and no undo entry is made;
    /// once a When is set the edit is written, shows on reselect (the entity's params) and is ONE undo entry. ✅ Red-proof: the <c>when</c> pre-check removed ⇒ the refusal text is the generic one / the title-only edit is written ⇒ red.
    /// </summary>
    [Fact]
    public void ANewTrigger_RefusesATitleOnlyApply_SayingWhy_AndKeepsTheEditOnceItHasAWhen()
    {
        const string fresh = """{"common":{"title":"New Trigger"}}""";
        var e = Rule(30, "Trigger", fresh);
        var d = Details(); var row = RowOf(30);
        d.Prepare(row, 10); d.StagePending("""{"common":{"title":"ManualTest"}}""");
        Assert.False(d.Apply(row));
        Assert.Contains("needs a When", d.Message);
        Assert.Equal((fresh, 0), (Params(e), _history.Count));
        d.StagePending("""{"common":{"title":"ManualTest"},"when":{"$type":"ScenarioTime","FromSeconds":60,"ToSeconds":1e9}}""");
        Assert.True(d.Apply(row));
        Assert.Contains("ManualTest", Params(e));                                  // what a reselect reads
        Assert.Equal(1, _history.Count);
    }

    [Fact]
    public void ARunningRule_IsReadOnlyInLive_AndARecordingIsReadOnlyWithItsReason()
    {
        var e = Rule(20, "Trigger", TriggerJson);
        _world.AddComponent(e, new ObjectiveStatus { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active, Title = new FixedString64("Counter-attack") });
        _world.AddComponent(e, new ObjectiveLinks());
        Assert.Equal(RuleDetails.LiveReadOnly, Details().ReadOnlyReason(RowOf(20)));
        Assert.Equal("a recording is read-only", Details("a recording is read-only").ReadOnlyReason(RowOf(20)));
    }
}
