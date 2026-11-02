using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Xunit;

#pragma warning disable CS0649

namespace Fdp.Presentation.Tests.ImGui.Editing;

/// <summary>⭐ CE-4210 P2 (MA-2, MA-5) — the third home's host context and the scan that counts / renames a param across ALL THREE homes.</summary>
public sealed class ParamUsageTests
{
    [ComponentId(252)] public struct LinkComp { public EntityLink Who; public AreaLink Area; }
    [ComponentId(253)] public sealed class NodeComp : BindableNode { public int Min { get; set; } }

    private static (EntityRepository Repo, RepositoryAdapter S, Entity E) World()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<LinkComp>();
        repo.RegisterManagedComponent<NodeComp>();
        repo.RegisterManagedComponent<ParamBindings>();
        var e = repo.CreateEntity();
        return (repo, new RepositoryAdapter(repo), e);
    }

    private static EntityBindContext Ctx(RepositoryAdapter s, Entity e, string prefix = "EntityInfo")
        => new(s, e, prefix, () => new[] { new ParamInScope("side", "ForceId") });

    [Fact]
    public void Context_BindsUnderPrefixedPath_ReadsBack_NewRecordEachTime()
    {
        var (repo, s, e) = World(); using var _ = repo;
        var c = Ctx(s, e);
        Assert.Null(c.BoundTo("ForceId"));
        c.Bind("ForceId", "side");
        var first = (ParamBindings)s.GetComponent(e, typeof(ParamBindings))!;
        Assert.Equal("side", first.Bindings["EntityInfo.ForceId"]);
        Assert.Equal("side", c.BoundTo("ForceId"));
        c.Bind("Other", "side");
        var second = (ParamBindings)s.GetComponent(e, typeof(ParamBindings))!;
        Assert.NotSame(first, second);           // immutable: a NEW record, the old one untouched
        Assert.Single(first.Bindings);
        c.Unbind("ForceId");
        Assert.Null(c.BoundTo("ForceId"));
        Assert.Equal("side", c.BoundTo("Other"));
    }

    [Fact]
    public void Context_ParamsFormPrefix_StepsIntoTheParamsBlobPath()
    {
        var (repo, s, e) = World(); using var _ = repo;
        Ctx(s, e).WithPrefix("Behavior.Params").Bind("Speed", "side");
        Assert.True(((ParamBindings)s.GetComponent(e, typeof(ParamBindings))!).Bindings.ContainsKey("Behavior.Params.Speed"));
    }

    [Fact]
    public void Context_RefusesAnIndexInAPath()
    {
        var (repo, s, e) = World(); using var _ = repo;
        Assert.NotNull(Ctx(s, e).Problem("Items[2].X"));
        Assert.False(ParamBindModel.View(typeof(float), 1f, "$.Items[2].X", "X", null, Ctx(s, e)).Bindable);
    }

    private static void Seed(RepositoryAdapter s, Entity e)
    {
        s.SetComponent(e, typeof(LinkComp), new LinkComp { Who = new EntityLink(default).BindToParam("side"), Area = AreaLink.OfEntity(default).BindToParam("side") });
        s.SetComponent(e, typeof(NodeComp), new NodeComp { Bind = new Dictionary<string, string> { ["Min"] = "side" } });
        s.SetComponent(e, typeof(ParamBindings), new ParamBindings { Bindings = new Dictionary<string, string> { ["EntityInfo.ForceId"] = "side", ["X.Y"] = "other" } });
    }

    [Fact]
    public void Find_CountsAllThreeHomes_AndNamesThem()
    {
        var (repo, s, e) = World(); using var _ = repo; Seed(s, e);
        var (n, where) = ParamUsage.Find(s, "side");
        Assert.Equal(4, n);                       // 2 link leaves + 1 Bind entry + 1 ParamBindings entry
        Assert.Contains(where, w => w.StartsWith("EntityInfo.ForceId"));
        Assert.Equal(4, ParamUsage.CountAll(s)["side"]);
        Assert.Equal(1, ParamUsage.CountAll(s)["other"]);
    }

    [Fact]
    public void Rename_RewritesEveryHome_AndUndoesBySwapping()
    {
        var (repo, s, e) = World(); using var _ = repo; Seed(s, e);
        ParamUsage.Rename(s, "side", "force");

        var link = (LinkComp)s.GetComponent(e, typeof(LinkComp))!;
        Assert.Equal("force", link.Who.Spec.ParamName);
        Assert.Equal("force", link.Area.Spec.ParamName);
        Assert.Equal("force", ((NodeComp)s.GetComponent(e, typeof(NodeComp))!).Bind!["Min"]);
        var pb = ((ParamBindings)s.GetComponent(e, typeof(ParamBindings))!).Bindings;
        Assert.Equal("force", pb["EntityInfo.ForceId"]);
        Assert.Equal("other", pb["X.Y"]);         // another param's binding is untouched
        Assert.Equal(0, ParamUsage.Find(s, "side").Count);

        ParamUsage.Rename(s, "force", "side");
        Assert.Equal(4, ParamUsage.Find(s, "side").Count);
    }
}
