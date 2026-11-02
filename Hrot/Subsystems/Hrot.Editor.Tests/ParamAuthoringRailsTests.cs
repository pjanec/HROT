using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.Scenario;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>⭐ CE-4210 P2 (MA-5, D) — the parameters as a Variables source: add · remove-refused-while-bound · rename follows the bindings in ONE undoable step · declare from the bind popup; and the host forwarding rail.</summary>
public sealed class ParamAuthoringRailsTests
{
    private static (EntityRepository Repo, HeaderParamsSchemaSource Src, Entity E, RepositoryAdapter S) Fixture()
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<ParamBindings>();
        ScenarioDescription.Stamp(repo, "desc", "macro", "h", null, "");
        var e = repo.CreateEntity();
        var src = new HeaderParamsSchemaSource(() => repo, new ParamsEditHistory());
        return (repo, src, e, new RepositoryAdapter(repo));
    }

    private static void Add(HeaderParamsSchemaSource s, string name, Type t) => s.AddVariable(new BlackboardVariableEntry(name, t, null));

    [Fact]
    public void Add_DeclaresInTheResidentHeader_AsANewResident()
    {
        var (repo, src, _, _) = Fixture(); using var _r = repo;
        var before = repo.GetSingletonManaged<ScenarioDescriptionResident>();
        Add(src, "side", typeof(ForceId));
        var after = repo.GetSingletonManaged<ScenarioDescriptionResident>();
        Assert.NotSame(before, after);                                // replaced whole, never mutated in place
        Assert.Equal("desc", after.Text);                              // the rest of the resident is kept
        var p = Assert.Single(ScenarioDescription.HeaderOf(repo)!.Params!);
        Assert.Equal(("side", "ForceId"), (p.Name, p.TypeId));
        Assert.Equal("side", Assert.Single(src.Variables).Name);
    }

    [Fact]
    public void Add_RefusesADuplicate_AndANonParamType()
    {
        var (repo, src, _, _) = Fixture(); using var _r = repo;
        Add(src, "side", typeof(ForceId));
        Add(src, "side", typeof(float));
        Assert.Contains("already declared", src.LastRefusal);
        Add(src, "bad", typeof(Guid));
        Assert.Contains("not a parameter type", src.LastRefusal);
        Assert.Single(src.Decls());
    }

    [Fact]
    public void Remove_IsRefusedWhileBound_NamingTheBinding_ThenAllowed()
    {
        var (repo, src, e, s) = Fixture(); using var _r = repo;
        Add(src, "side", typeof(ForceId));
        s.SetComponent(e, typeof(ParamBindings), new ParamBindings { Bindings = new Dictionary<string, string> { ["EntityInfo.ForceId"] = "side" } });
        src.RemoveVariable("side");
        Assert.Contains("EntityInfo.ForceId", src.LastRefusal);
        Assert.Single(src.Decls());
        s.SetComponent(e, typeof(ParamBindings), new ParamBindings());
        src.RemoveVariable("side");
        Assert.Empty(src.Decls());
    }

    [Fact]
    public void Rename_MovesTheDeclarationAndItsBindingsInOneStep_AndUndoMovesThemBack()
    {
        var (repo, src, e, s) = Fixture(); using var _r = repo;
        Add(src, "side", typeof(ForceId));
        s.SetComponent(e, typeof(ParamBindings), new ParamBindings { Bindings = new Dictionary<string, string> { ["EntityInfo.ForceId"] = "side" } });
        src.RenameVariable("side", "force");
        Assert.Equal("force", Assert.Single(src.Decls()).Name);
        Assert.Equal("force", ((ParamBindings)s.GetComponent(e, typeof(ParamBindings))!).Bindings["EntityInfo.ForceId"]);
        Assert.Equal(1, src.CountNodesReferencingVariable("force"));   // the table's reference count = the binding count

        Assert.True(src.History.Undo());                               // ONE undo undoes both
        Assert.Equal("side", Assert.Single(src.Decls()).Name);
        Assert.Equal("side", ((ParamBindings)s.GetComponent(e, typeof(ParamBindings))!).Bindings["EntityInfo.ForceId"]);
        Assert.True(src.History.Redo());
        Assert.Equal("force", ((ParamBindings)s.GetComponent(e, typeof(ParamBindings))!).Bindings["EntityInfo.ForceId"]);
    }

    [Fact]
    public void Rename_ToAnExistingName_IsRefused()
    {
        var (repo, src, _, _) = Fixture(); using var _r = repo;
        Add(src, "a", typeof(float)); Add(src, "b", typeof(float));
        src.RenameVariable("a", "b");
        Assert.Contains("already declared", src.LastRefusal);
        Assert.Equal(new[] { "a", "b" }, src.Decls().Select(d => d.Name));
    }

    [Fact]
    public void DefaultAndComment_AreEditedAndRoundTrip()
    {
        var (repo, src, _, _) = Fixture(); using var _r = repo;
        Add(src, "speed", typeof(float));
        src.UpdateVariableProperties("speed", new Hrot.Editor.AiShared.Variables.VariablePropertyValues(DefaultValueJson: "3.5", Comment: "m/s"));
        var d = Assert.Single(ScenarioDescription.HeaderOf(repo)!.Params!);
        Assert.Equal("3.5", d.Default!.ToJsonString());
        Assert.Equal("m/s", d.Comment);
    }

    [Fact]
    public void Move_Reorders()
    {
        var (repo, src, _, _) = Fixture(); using var _r = repo;
        Add(src, "a", typeof(float)); Add(src, "b", typeof(int));
        src.MoveVariable(0, 1);
        Assert.Equal(new[] { "b", "a" }, src.Decls().Select(d => d.Name));
    }

    [Fact]
    public void Declare_FromTheBindPopup_AddsAParam_AndIsUniqueByName()
    {
        var (repo, src, _, _) = Fixture(); using var _r = repo;
        var p1 = src.Declare(typeof(float), "speed", 4f);
        var p2 = src.Declare(typeof(float), "speed", 5f);
        Assert.Equal(new ParamInScope("speed", "float"), p1);
        Assert.Equal(new ParamInScope("speed2", "float"), p2);
        Assert.Equal("4", src.Decls()[0].Default!.ToJsonString());
        Assert.Null(src.Declare(typeof(Guid), "g", null));
    }

    // ── the silent-default rule: the unit-detail params form asks for its context from the view's own world ──────────────────────────

    [Fact]
    public void TheAiDetailsView_HandsTheParamsFormItsEntitysBindContext_PerRow()
    {
        var (repo, _, e, s) = Fixture(); using var _r = repo;
        var view = new EntityAiDetailsView(new EntityAiEditModel(() => repo, () => null), () => null, null, () => repo);
        var task = Assert.IsType<EntityBindContext>(view.BindsFor(e, "Behavior.Params"));
        var sop = Assert.IsType<EntityBindContext>(view.BindsFor(e, "Sop.Params"));
        Assert.Equal("Behavior.Params", task.Prefix);
        Assert.Equal("Sop.Params", sop.Prefix);
        Assert.Equal(e, task.Entity);

        var none = new EntityAiDetailsView(new EntityAiEditModel(() => repo, () => null), () => null);
        Assert.Null(none.BindsFor(e, "Behavior.Params"));              // a view built without the world shows no bind button (the descriptor passes it)
    }

    [Fact]
    public void TheAiDetailsDescriptor_PassesTheWorldToTheView()
    {
        var (repo, _, e, _) = Fixture(); using var _r = repo;
        var view = Assert.IsType<EntityAiDetailsView>(EntityAiDetailsViewDescriptor.For(() => repo, () => null, () => null).Create());
        Assert.NotNull(view.BindsFor(e, "Behavior.Params"));
    }
}
