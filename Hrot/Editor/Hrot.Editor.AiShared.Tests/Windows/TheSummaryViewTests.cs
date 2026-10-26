using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.IG.Components;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐ <b><c>CE-1247</c> — the Summary view</b>: the IG's former "Entity Properties" card as a Details view on every host.
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §9. ⭐ Re-homes the claims of the deleted <c>EntityInspectorStateTests</c>
/// (a live entity's id, TKB type, position and side; a null or dead entity has no summary) onto the shared reader.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheSummaryViewTests : IDisposable
{
    public TheSummaryViewTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static EntityRepository World(bool withStyle)
    {
        var world = new EntityRepository();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<EntityInfo>();
        world.RegisterComponent<Health>();
        if (withStyle) world.RegisterComponent<ResolvedStyle>();
        return world;
    }

    private static Entity Tank(EntityRepository world)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new NetworkIdentity(1043));
        world.AddComponent(e, new TkbIdentity { TkbType = 101L });
        world.AddComponent(e, new SimTransform { Position = new Vector3(123.4f, 567.8f, 9f), Rotation = Quaternion.Identity });
        world.AddComponent(e, new EntityInfo { Name = new FixedString64("T-72"), ForceId = ForceId.Hostile });
        world.AddComponent(e, new Health { Current = 30f, Max = 120f });
        return e;
    }

    [Fact]
    public void CE1247_ALiveEntity_ReadsItsSourceComponents()
    {
        using var world = World(withStyle: false);   // ⭐ SimHost / CGF / RB register no ResolvedStyle — the read must not throw
        var s = EntitySummary.Read(world, Tank(world));

        Assert.NotNull(s);
        Assert.Equal("T-72", s!.Name);
        Assert.Equal(1043L, s.NetworkId);
        Assert.Equal(101L, s.TkbType);
        Assert.Equal(new Vector3(123.4f, 567.8f, 9f), s.Position);
        Assert.Equal(ForceId.Hostile, s.Side);
        Assert.Null(s.ShownAs);
        Assert.Equal(0.25f, s.HealthFraction);
    }

    [Fact]
    public void CE1247_AnAbsentComponent_IsAnAbsentRow_NotAZero()
    {
        using var world = World(withStyle: false);
        var bare = world.CreateEntity();

        var s = EntitySummary.Read(world, bare)!;

        Assert.Null(s.Name);
        Assert.Null(s.NetworkId);
        Assert.Null(s.TkbType);
        Assert.Null(s.Position);
        Assert.Null(s.Side);
        Assert.Null(s.HealthFraction);
    }

    [Fact]
    public void CE1247_ANullOrDeadEntity_HasNoSummary()
    {
        using var world = World(withStyle: false);
        var e = Tank(world);
        world.DestroyEntity(e);

        Assert.Null(EntitySummary.Read(world, Entity.Null));
        Assert.Null(EntitySummary.Read(world, e));
    }

    [Fact]
    public void CE1247_TheDrawnAffiliation_IsShownOnlyWhereItDiffers()
    {
        using var world = World(withStyle: true);
        var e = Tank(world);
        var style = ResolvedStyle.CreateDefault();
        style.Affiliation = ForceId.Hostile;
        world.AddComponent(e, style);

        Assert.Null(EntitySummary.Read(world, e)!.ShownAs);

        style.Affiliation = ForceId.Friend;   // an IG symbol override
        world.SetComponent(e, style);
        Assert.Equal(ForceId.Friend, EntitySummary.Read(world, e)!.ShownAs);
    }

    [Fact]
    public void CE1247_TheBundle_OffersSummary_AsTheDefaultOverComponents_AndItDumpsWhatItShows()
    {
        using var world = World(withStyle: false);
        var e = Tank(world);
        var inspector = new Fdp.Presentation.Panels.EntityInspectorPanel();
        var session = new Fdp.Presentation.Adapters.RepositoryAdapter(world);
        var windows = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        var bundle = new EntityDetailsBundle(new EntityDetailsHostServices(
            WindowId: "details_ce1247", Perspective: "IG", World: () => world,
            // ⭐ a real inspector and session, so the Components view ALSO applies — the rank is what picks Summary
            Inspector: () => inspector, Session: () => session, RunState: () => VariableRunState.Running, Host: () => null));
        Fdp.Toolkit.Runner.UiBundleHost.Compose(new Fdp.Toolkit.Runner.IUiBundle[] { bundle }, new Fdp.Toolkit.Runner.UiBundleContext(windows));

        var ctx = DetailsContext.Empty("IG") with { Entities = new[] { e } };
        Assert.Contains(bundle.Workspace.DetailsViews.OfferSet(ctx), d => d.Id == Hrot.Editor.Scenario.ScenarioComponentsViewDescriptor.ViewId);
        Assert.Equal(EntitySummaryViewDescriptor.ViewId, bundle.Workspace.DetailsViews.Default(ctx)?.Id);
        Assert.Null(bundle.Workspace.DetailsViews.Default(DetailsContext.Empty("IG")));   // nothing selected ⇒ no card

        PanelSnapshot.CaptureEnabled = true;
        var view = (EntitySummaryView)bundle.Workspace.DetailsViews.Default(ctx)!.Create();
        view.SimulateDraw(ctx, "details_ce1247");
        var dump = PanelSnapshot.TryGet($"details_ce1247/{EntitySummaryViewDescriptor.ViewId}")!.Dump();
        Assert.Equal("T-72", dump["name"]!.GetValue<string>());
        Assert.Equal("Hostile", dump["side"]!.GetValue<string>());
        Assert.Equal(1043L, dump["networkId"]!.GetValue<long>());
    }
}
