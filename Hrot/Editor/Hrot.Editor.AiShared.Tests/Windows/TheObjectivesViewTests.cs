using System;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐ <b><c>CE-4216</c> (rules view R2, RV-7; docs/DESIGN_Rules_View_UI.md)</b> — the Details panel's <b>Objectives</b> tab: <i>"objectives about this unit"</i>. Offered for ONE selected unit that some rule is about and only then (empty ⇒ not drawn);
/// its rows are the rules view's own; and the host's <c>OpenRule</c> reaches the constructed tab (the silent-default rule: a caller that HAS the rules window must PASS it).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheObjectivesViewTests
{
    private static EntityRepository World(out Entity unit, out Entity bystander)
    {
        var world = new EntityRepository();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<ObjectiveStatus>();
        world.RegisterComponent<ObjectiveLinks>();
        unit = world.CreateEntity(); world.AddComponent(unit, new NetworkIdentity(900));
        bystander = world.CreateEntity(); world.AddComponent(bystander, new NetworkIdentity(950));
        var rule = world.CreateEntity();
        world.AddComponent(rule, new NetworkIdentity(10));
        world.AddComponent(rule, new ObjectiveStatus { Title = new FixedString64("Reach the bridge"), State = ObjectiveState.Active, ChangedAt = 0 });
        world.AddComponent(rule, new ObjectiveLinks { Subject = new EntityRef(900) });
        return world;
    }

    private static EntityDetailsBundle Bundle(EntityRepository world, Action<int>? open)
    {
        var inspector = new Fdp.Presentation.Panels.EntityInspectorPanel();
        var session = new Fdp.Presentation.Adapters.RepositoryAdapter(world);
        var windows = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        var bundle = new EntityDetailsBundle(new EntityDetailsHostServices(
            WindowId: "details_ce4216", Perspective: "IG", World: () => world, Inspector: () => inspector, Session: () => session,
            RunState: () => VariableRunState.Running, Host: () => null, OpenRule: open));
        Fdp.Toolkit.Runner.UiBundleHost.Compose(new Fdp.Toolkit.Runner.IUiBundle[] { bundle }, new Fdp.Toolkit.Runner.UiBundleContext(windows));
        return bundle;
    }

    /// <summary>✅ Red-proof: <c>AppliesTo</c> answering true for any single entity ⇒ the bystander is offered the tab ⇒ red.</summary>
    [Fact]
    public void TheTab_IsOfferedForAUnitSomeRuleIsAbout_AndOnlyThen()
    {
        using var world = World(out var unit, out var bystander);
        var bundle = Bundle(world, _ => { });
        var about = DetailsContext.Empty("IG") with { Entities = new[] { unit } };
        var none = DetailsContext.Empty("IG") with { Entities = new[] { bystander } };
        Assert.Contains(bundle.Workspace.DetailsViews.OfferSet(about), d => d.Id == EntityObjectivesViewDescriptor.ViewId);
        Assert.DoesNotContain(bundle.Workspace.DetailsViews.OfferSet(none), d => d.Id == EntityObjectivesViewDescriptor.ViewId);
        Assert.DoesNotContain(bundle.Workspace.DetailsViews.OfferSet(DetailsContext.Empty("IG")), d => d.Id == EntityObjectivesViewDescriptor.ViewId);   // nothing selected
        Assert.Equal(EntitySummaryViewDescriptor.ViewId, bundle.Workspace.DetailsViews.Default(about)?.Id);                                            // it does not displace the default
    }

    [Fact]
    public void TheTab_ListsTheRulesViewsOwnRows_AndCarriesTheHostsOpenRule()
    {
        using var world = World(out var unit, out _);
        Action<int> open = _ => { };
        var ctx = DetailsContext.Empty("IG") with { Entities = new[] { unit } };
        var view = (EntityObjectivesView)Bundle(world, open).Workspace.DetailsViews.OfferSet(ctx).Single(d => d.Id == EntityObjectivesViewDescriptor.ViewId).Create();
        var row = Assert.Single(view.RowsFor(unit));
        Assert.Equal(("Reach the bridge", 10), (row.Title, row.NetworkId));
        Assert.Same(open, view.OpenRule);                         // ⭐ the forwarding rail, on the CONSTRUCTED tab
    }
}
