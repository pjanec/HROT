using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.Scenario;
using Xunit;

#pragma warning disable CS0649

namespace Hrot.Editor.Tests;

/// <summary>⭐ CE-4210 MA-9 — the closure walk of a selection, the plan it yields (params, anchor, header) and the gesture's state machine (a cancelled dialog saves nothing; a macro is never saved with a dangling reference).</summary>
public sealed class SelectionMacroTests : IDisposable
{
    [ComponentId(250)]
    public struct Refs { public EntityLink Follow; public AreaLink Zone; public EntityRef Plain; }

    /// <summary>A behaviour's params contract holding a reference (the shape of an Attack / Order target).</summary>
    public sealed class AttackParams { public EntityLink Target { get; set; } }

    private readonly EntityRepository _w = new();
    private Entity _a, _b, _c, _outside;   // a, b, c are the "men"; outside is the key'd entity none of them is part of

    public SelectionMacroTests()
    {
        _w.RegisterComponent<NetworkIdentity>();
        _w.RegisterComponent<EntityGuid>();
        _w.RegisterComponent<EntityInfo>();
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<RefKey>();
        _w.RegisterComponent<ScenarioIgnoreTag>();
        _w.RegisterComponent<Refs>();
        _w.RegisterManagedComponent<ActiveMissionPlan>();
        Entity Make(int net, string name, float x, float y, float z)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new NetworkIdentity(net));
            _w.AddComponent(e, new EntityGuid(Guid.NewGuid()));
            _w.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, z) });
            return e;
        }
        _a = Make(1, "Alpha", 0f, 0f, 0f); _b = Make(2, "Bravo", 10f, 20f, 3f); _c = Make(3, "Charlie", 20f, 40f, 6f); _outside = Make(4, "Hill", 99f, 99f, 99f);
        _w.AddComponent(_outside, new RefKey("Hill"));
    }

    public void Dispose() => _w.Dispose();

    private static EntityLink To(int net) => new(new EntityRef(net));

    private IReadOnlyList<Entity> Men => new[] { _a, _b };

    [Fact]
    public void TheWalk_ListsEveryReferenceOutsideTheSelection_AndIgnoresTheInsideOnes()
    {
        _w.AddComponent(_a, new Refs { Follow = To(4), Zone = default });                        // a -> the outside entity
        _w.AddComponent(_b, new Refs { Follow = To(1) });                                          // b -> a: inside
        var selected = MacroClosure.Savable(_w, Men);

        var outside = Assert.Single(MacroClosure.Walk(_w, selected));
        Assert.Equal("Alpha", outside.SourceName);
        Assert.Equal("Refs", outside.Component);
        Assert.Equal("Follow", outside.Field, ignoreCase: true);                                   // the walker says WHICH field
        Assert.Equal("Hill", outside.TargetName);
        Assert.Equal("@Hill", outside.TargetKey);
        Assert.Equal("Alpha · Refs.Follow · Hill (@Hill)", outside.Line, ignoreCase: true);
        Assert.Empty(MacroClosure.Walk(_w, MacroClosure.Savable(_w, new[] { _a, _b, _outside })));   // select the target too: closed
    }

    [Fact]
    public void ACloseSelection_HasNoOutsideReferences_AndAParamBoundOneIsNotAReference()
    {
        _w.AddComponent(_a, new Refs { Follow = new EntityLink(default, RefSpec.ByParam("hill")) });   // already a ^param
        Assert.Empty(MacroClosure.Walk(_w, MacroClosure.Savable(_w, Men)));
    }

    [Fact]
    public void MakeParameters_OneParamPerOutsideTarget_DefaultIsItsKey_ElseItsGuid_TypedByTheField()
    {
        _w.AddComponent(_a, new Refs { Follow = To(4) });                                          // the key'd target
        _w.AddComponent(_b, new Refs { Follow = To(3), Zone = default });                          // c has no RefKey: its GUID
        var plan = SelectionMacroPlan.Build(_w, Men, "test/sel");

        Assert.Equal(2, plan.Outside.Count);
        Assert.True(plan.CanMakeParameters);
        Assert.Equal(2, plan.Params.Count);
        var hill = plan.Params.Single(p => p.Default!.ToJsonString() == "\"@Hill\"");
        Assert.Equal("EntityLink", hill.TypeId);
        Assert.Equal("hill", hill.Name);
        var guid = _w.GetComponent<EntityGuid>(_c).Value.ToString();
        Assert.Contains(plan.Params, p => p.Default!.ToJsonString() == $"\"{guid}\"" && p.TypeId == "EntityLink");
        Assert.Equal(2, plan.Params.Select(p => p.Name).Distinct().Count());                       // unique names
    }

    [Fact]
    public void TwoFieldsOfOneTarget_ShareOneParam()
    {
        _w.AddComponent(_a, new Refs { Follow = To(4) });
        _w.AddComponent(_b, new Refs { Follow = To(4) });
        Assert.Single(SelectionMacroPlan.Build(_w, Men, "x").Params);
    }

    [Fact]
    public void APlainEntityRef_CannotBecomeAParam_SoTheSelectionCannotBeSaved_AsIs()
    {
        _w.AddComponent(_a, new Refs { Plain = new EntityRef(4) });
        var plan = SelectionMacroPlan.Build(_w, Men, "x");
        Assert.False(plan.CanMakeParameters);
        Assert.Contains("no parameter form", Assert.Single(plan.Blockers).WhyNot);
    }

    [Fact]
    public void TheHeader_IsAMacro_AnchoredAtTheSelectionsCentroid_HeadingNorth()
    {
        var plan = SelectionMacroPlan.Build(_w, new[] { _a, _b, _c }, "x");
        var header = ScenarioHeaderJson.Read(new System.Text.Json.Nodes.JsonObject { ["Header"] = System.Text.Json.Nodes.JsonNode.Parse(plan.HeaderJson(withParams: false)) })!;
        Assert.Equal("macro", header.Kind);
        Assert.Equal(10f, header.Anchor!.Value.Position.X);                                         // (0 + 10 + 20) / 3
        Assert.Equal(20f, header.Anchor.Value.Position.Y);
        Assert.Equal(3f, header.Anchor.Value.Position.Z);                                           // the mean of z
        Assert.Equal(0f, header.Anchor.Value.Heading.Degrees);
        Assert.Null(header.Params);
    }

    [Fact]
    public void ATransientEntity_IsNotSavable()
    {
        _w.AddComponent(_b, new ScenarioIgnoreTag());
        Assert.Equal(new[] { _a }, MacroClosure.Savable(_w, Men).Select(s => s.Entity));
    }

    // ── the gesture ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Sink { public readonly List<(string Name, Guid[] Guids, string? Header)> Saves = new(); }

    private (SaveSelectionAsMacroFlow Flow, Sink Sink) Flow(params Entity[] selection)
    {
        var sink = new Sink();
        return (new SaveSelectionAsMacroFlow(() => _w, () => selection, (n, g, h) => sink.Saves.Add((n, g, h))), sink);
    }

    [Fact]
    public void AClosedSelection_IsSavedAtOnce_UnderMacrosPath_WithOnlyItsGuids()
    {
        var (flow, sink) = Flow(_a, _b);
        flow.Begin(); flow.Name = "test/sel-w5"; flow.Confirm();

        var save = Assert.Single(sink.Saves);
        Assert.Equal("macros/test/sel-w5", save.Name);
        Assert.Equal(new[] { _w.GetComponent<EntityGuid>(_a).Value, _w.GetComponent<EntityGuid>(_b).Value }, save.Guids);
        Assert.Contains("\"Kind\":\"macro\"", save.Header);
        Assert.DoesNotContain("Params", save.Header);
    }

    [Fact]
    public void AnOpenSelection_AsksFirst_ACancelledDialogSavesNothing()
    {
        _w.AddComponent(_a, new Refs { Follow = To(4) });
        var (flow, sink) = Flow(_a, _b);
        flow.Begin(); flow.Name = "x"; flow.Confirm();
        Assert.Equal(SaveSelectionAsMacroFlow.StepKind.Closure, flow.Step);
        Assert.Empty(sink.Saves);

        flow.Cancel();
        Assert.Equal(SaveSelectionAsMacroFlow.StepKind.Closed, flow.Step);
        Assert.Empty(sink.Saves);                                                                    // ⛔ never a dangling reference, never a silent save
    }

    [Fact]
    public void MakeParameters_SavesWithTheParamsInTheHeader_AndTheWorldIsUntouched()
    {
        _w.AddComponent(_a, new Refs { Follow = To(4) });
        var before = _w.GetComponent<Refs>(_a);
        var (flow, sink) = Flow(_a, _b);
        flow.Begin(); flow.Name = "x"; flow.Confirm(); flow.MakeParameters();

        var save = Assert.Single(sink.Saves);
        Assert.Contains("\"Params\"", save.Header);
        Assert.Contains("\"@Hill\"", save.Header);
        Assert.Equal(before, _w.GetComponent<Refs>(_a));                                             // the live link is still unbound
        Assert.Equal(RefForm.None, _w.GetComponent<Refs>(_a).Follow.Spec.Form);
    }

    [Fact]
    public void ABlockedSelection_CannotBeMadeIntoParameters_AndNothingIsSaved()
    {
        _w.AddComponent(_a, new Refs { Plain = new EntityRef(4) });
        var (flow, sink) = Flow(_a);
        flow.Begin(); flow.Name = "x"; flow.Confirm(); flow.MakeParameters();
        Assert.Empty(sink.Saves);
        Assert.Equal(SaveSelectionAsMacroFlow.StepKind.Closure, flow.Step);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/../b")]
    [InlineData("a//b")]
    [InlineData("bad:name")]
    public void AnUnusableName_IsRefused_NothingSaved(string name)
    {
        var (flow, sink) = Flow(_a);
        flow.Begin(); flow.Name = name; flow.Confirm();
        Assert.Empty(sink.Saves);
        Assert.Equal(SaveSelectionAsMacroFlow.StepKind.Naming, flow.Step);
    }

    [Fact]
    public void NothingSelected_SaysSo_AndNoNameIsAsked()
    {
        var (flow, sink) = Flow();
        flow.Begin();
        Assert.Equal(SaveSelectionAsMacroFlow.StepKind.Done, flow.Step);
        Assert.Contains("Select", flow.Message);
        Assert.Empty(sink.Saves);
    }

    [Theory]
    [InlineData("test/sel", "test/sel")]
    [InlineData("macros/test/sel", "test/sel")]
    [InlineData(" /test\\sel/ ", "test/sel")]
    public void ThePathOfATypedName(string typed, string path) => Assert.Equal(path, SaveSelectionAsMacroFlow.PathOf(typed));

    // ── ⭐ CE-4210 W-M6 — a reference INSIDE a params blob is part of the closure (SR-159: bound as its Param form, like a typed leaf) ─────────────────────

    private static ScenarioBehaviorRemapper Remapper() { var r = new ScenarioBehaviorRemapper(); r.Register<AttackParams>("Attack"); return r; }

    private void GiveMission(Entity e, params string[] taskParams)
    {
        var plan = new DomainMissionPlan();
        foreach (var p in taskParams) plan.Tasks.Add(new DomainMissionTask { TaskId = Guid.NewGuid(), BehaviorName = "Attack", BehaviorParams = p });
        _w.SetManagedComponent(e, new ActiveMissionPlan { Plan = plan });
    }

    [Fact]
    public void W_M6_TheWalk_ListsAReferenceInsideAMissionTasksParamsBlob_LikeATypedLeaf()
    {
        GiveMission(_a, "{\"Target\":4}");                                                       // task 1 attacks the outside entity (network id 4)
        GiveMission(_b, "{\"Target\":1}");                                                       // b's task names a: inside
        var selected = MacroClosure.Savable(_w, Men);

        var outside = Assert.Single(MacroClosure.Walk(_w, selected, Remapper()));
        Assert.Equal("Alpha", outside.SourceName);
        Assert.Equal("MissionPlan", outside.Component);
        Assert.Contains("task 1", outside.Field);                                                  // WHICH task ...
        Assert.EndsWith("Target", outside.Field, StringComparison.OrdinalIgnoreCase);              // ... and which field of its params
        Assert.Equal("Hill", outside.TargetName);
        Assert.True(outside.CanParameterise);                                                      // an EntityLink: Make parameters can bind it

        var plan = SelectionMacroPlan.Build(_w, Men, "x", Remapper());
        Assert.Equal("@Hill", Assert.Single(plan.Params).Default!.GetValue<string>());
        Assert.True(plan.CanMakeParameters);
        Assert.Contains("\"Target\":4", ((Fdp.ModuleHost.Abstractions.ISimulationView)_w).GetManagedComponentRO<ActiveMissionPlan>(_a)!.Plan.Tasks[0].BehaviorParams);   // the walk never writes the world
    }

    [Fact]
    public void W_M6_TheWalk_ListsABlobReference_OnlyWhenGivenTheRemapper_TheSilentDefault()
    {
        GiveMission(_a, "{\"Target\":4}");
        Assert.Empty(MacroClosure.Walk(_w, MacroClosure.Savable(_w, Men)));                        // no remapper: no contract to read the blob by
        Assert.Single(MacroClosure.Walk(_w, MacroClosure.Savable(_w, Men), Remapper()));
    }

    [Fact]
    public void W_M6_TheFlow_GivenTheRemapper_AsksAboutABlobReference()
    {
        GiveMission(_a, "{\"Target\":4}");
        var sink = new List<(string, Guid[], string?)>();
        var flow = new SaveSelectionAsMacroFlow(() => _w, () => Men.ToArray(), (n, g, h) => sink.Add((n, g, h)), Remapper());
        flow.Begin(); flow.Name = "x"; flow.Confirm();
        Assert.Equal(SaveSelectionAsMacroFlow.StepKind.Closure, flow.Step);
        Assert.Equal("MissionPlan", Assert.Single(flow.Plan!.Outside).Component);
        Assert.Empty(sink);
    }
}
