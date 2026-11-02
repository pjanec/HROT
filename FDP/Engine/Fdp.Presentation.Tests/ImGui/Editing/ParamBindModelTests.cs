using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

#pragma warning disable CS0649

namespace Fdp.Presentation.Tests.ImGui.Editing;

/// <summary>⭐ CE-4210 P2 (MA-1, MA-2) — the PURE bind decision (no ImGui): bindable?, which home, chip vs button, red; each home written and read back.</summary>
public sealed class ParamBindModelTests
{
    private sealed class FakeCtx : IParamBindContext
    {
        public List<ParamInScope> P = new();
        public Dictionary<string, string> Bound = new();
        public IReadOnlyList<ParamInScope> Params => P;
        public string? BoundTo(string path) => Bound.TryGetValue(path, out var n) ? n : null;
        public void Bind(string path, string param) => Bound[path] = param;
        public void Unbind(string path) => Bound.Remove(path);
        public string? Problem(string path) => Bound.ContainsKey(path) && Bound[path] == "bad" ? "validator says no" : null;
    }

    private struct Scal { public float Speed; public int Count; }
    private sealed class Node : BindableNode { public int Min { get; set; } }
    private struct Linky { public EntityLink Who; public AreaLink Area; }

    private static FakeCtx Ctx(params (string, string)[] ps) { var c = new FakeCtx(); foreach (var (n, t) in ps) c.P.Add(new(n, t)); return c; }

    private static EditNode Child(object value, Type t, string name)
    {
        var s = new ComponentEditServiceBuilder().Build().Open(value, t);
        var root = s.Document.Root;
        if (root.Kind == EditNodeKind.SelectionRoot) root = root.Children.First();
        return root.Children.First(c => c.Name == name);
    }

    [Fact]
    public void NoContext_NotBindable() =>
        Assert.False(ParamBindModel.View(typeof(float), 1f, "$.Speed", "Speed", null, null).Bindable);

    [Fact]
    public void NoFittingParam_NotBindable() =>
        Assert.False(ParamBindModel.View(typeof(float), 1f, "$.Speed", "Speed", null, Ctx(("n", "int"))).Bindable);

    [Fact]
    public void FittingParam_UnboundShowsButton_NoChip()
    {
        var v = ParamBindModel.View(typeof(float), 1f, "$.Speed", "Speed", null, Ctx(("sp", "float")));
        Assert.True(v.ShowsButton); Assert.False(v.ShowsChip); Assert.Single(v.Fitting);
    }

    [Theory]
    [InlineData(typeof(EntityLink), BindHome.RefParamForm)]
    [InlineData(typeof(PointLink), BindHome.RefParamForm)]
    [InlineData(typeof(AreaLink), BindHome.RefParamForm)]
    [InlineData(typeof(float), BindHome.ParamBindings)]
    [InlineData(typeof(ForceId), BindHome.ParamBindings)]
    [InlineData(typeof(EntityRef), BindHome.ParamBindings)]
    public void HomeByType(Type t, BindHome home) => Assert.Equal(home, ParamBindModel.HomeOf(t, null));

    [Fact]
    public void PropertyOfBindableNode_HomeIsBindMap() =>
        Assert.Equal(BindHome.BindableNode, ParamBindModel.HomeOf(typeof(int), new Node()));

    [Fact]
    public void Bound_ShowsChip_AndRedOnUndeclared()
    {
        var c = Ctx(("sp", "float")); c.Bound["Speed"] = "gone";
        var v = ParamBindModel.View(typeof(float), 1f, "$.Speed", "Speed", null, c);
        Assert.True(v.ShowsChip); Assert.False(v.ShowsButton); Assert.True(v.ChipIsRed);
        Assert.Contains("gone", v.Problem);
    }

    [Fact]
    public void Bound_Fine_IsNotRed()
    {
        var c = Ctx(("sp", "float")); c.Bound["Speed"] = "sp";
        Assert.False(ParamBindModel.View(typeof(float), 1f, "$.Speed", "Speed", null, c).ChipIsRed);
    }

    [Fact]
    public void Bound_ValidatorReason_Wins()
    {
        var c = Ctx(("bad", "float")); c.Bound["Speed"] = "bad";
        Assert.Equal("validator says no", ParamBindModel.View(typeof(float), 1f, "$.Speed", "Speed", null, c).Problem);
    }

    [Fact]
    public void BoundToWrongType_IsRed()
    {
        var c = Ctx(("n", "int")); c.Bound["Speed"] = "n";
        Assert.True(ParamBindModel.View(typeof(float), 1f, "$.Speed", "Speed", null, c).ChipIsRed);
    }

    [Fact]
    public void HomeThree_WrittenAndReadBack()
    {
        var c = Ctx(("sp", "float"));
        var n = Child(new Scal { Speed = 2f }, typeof(Scal), "Speed");
        Assert.False(ParamBindModel.Bind(n, null, c, "sp"));
        Assert.Equal("sp", c.BoundTo("Speed"));
        ParamBindModel.Unbind(n, null, c);
        Assert.Null(c.BoundTo("Speed"));
    }

    [Fact]
    public void HomeTwo_BindMapWrittenAndReadBack_StructuralChange()
    {
        var c = Ctx(("m", "int")); var container = new Node();
        var n = Child(container, typeof(Node), "Min");
        Assert.True(ParamBindModel.Bind(n, container, c, "m"));
        Assert.Equal("m", container.Bind!["Min"]);
        Assert.Equal("m", ParamBindModel.View(typeof(int), 0, "$.Min", "Min", container, c).BoundParam);
        ParamBindModel.Unbind(n, container, c);
        Assert.Null(container.Bind);
    }

    [Fact]
    public void HomeOne_LinkParamForm_WrittenAndReadBack()
    {
        var c = Ctx(("a", "AreaLink"), ("e", "EntityLink"));
        var n = Child(new Linky(), typeof(Linky), "Area");
        ParamBindModel.Bind(n, null, c, "a");
        var link = (AreaLink)n.Binding!.GetBoxed()!;
        Assert.Equal(RefForm.Param, link.Spec.Form);
        Assert.Equal("a", ParamBindModel.BoundParamOfLink(link));
        ParamBindModel.Unbind(n, null, c);
        Assert.Null(ParamBindModel.BoundParamOfLink(n.Binding!.GetBoxed()));
        var w = Child(new Linky(), typeof(Linky), "Who");
        ParamBindModel.Bind(w, null, c, "e");
        Assert.Equal("e", ParamBindModel.BoundParamOfLink(w.Binding!.GetBoxed()));
    }

    [Fact]
    public void SuggestName_IsUnique()
    {
        Assert.Equal("speed", ParamBindModel.SuggestParamName("Speed", new[] { "x" }));
        Assert.Equal("speed2", ParamBindModel.SuggestParamName("Speed", new[] { "speed" }));
    }
}
