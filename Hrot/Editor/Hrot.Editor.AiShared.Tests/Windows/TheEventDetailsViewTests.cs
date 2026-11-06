using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Replication.Components;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.Scenario;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐ CE-4263 (<c>docs/DESIGN_Director_Timeline_Editor.md</c> §2c-1; 🔒 user, 2026-11-04: <i>"I would expect the detail panel will show the detail about the clicked event"</i>) — the Event view: it applies only while the
/// focused event's SUBJECT is the one selected unit (a stale event cannot show), its rows name the event (time, lane, kind, text, Subject / Other links, values — numbers culture-invariant), and the shared Details bundle offers
/// it FIRST. 🔴 Red-proof (compiles): <c>AppliesTo</c> ignoring the subject match ⇒ the predicate rail goes red.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheEventDetailsViewTests : IDisposable
{
    public TheEventDetailsViewTests() { PanelSnapshot.Clear(); PanelSnapshot.CaptureEnabled = false; }
    public void Dispose() { PanelSnapshot.Clear(); PanelSnapshot.CaptureEnabled = false; }

    private static (EntityRepository World, Entity A, Entity B) World()
    {
        var world = new EntityRepository();
        world.RegisterComponent<NetworkIdentity>();
        Entity Unit(int net) { var e = world.CreateEntity(); world.AddComponent(e, new NetworkIdentity(net)); return e; }
        return (world, Unit(100), Unit(200));
    }

    private static DetailsContext Ctx(params Entity[] selected) => DetailsContext.Empty("Scenario") with { Entities = selected };

    [Fact]
    public void CE4263_TheEventView_AppliesOnlyToTheFocusedEventsSubject()
    {
        var (world, a, b) = World();
        using var _w = world;
        var focus = new TimelineEventFocus();
        var d = EventDetailsViewDescriptor.For(() => world, focus, _ => { });

        Assert.False(d.AppliesTo(Ctx(a)), "no event focused");
        focus.Set(new TimelineEvent(5, "combat.shot", 100, 200, 1f, 2f, 3f));
        Assert.True(d.AppliesTo(Ctx(a)), "the subject is the selected unit");
        Assert.False(d.AppliesTo(Ctx(b)), "another unit is selected: the event is stale");
        Assert.False(d.AppliesTo(Ctx(a, b)), "more than one unit");
        Assert.False(d.AppliesTo(Ctx()), "nothing selected");
        focus.Clear();
        Assert.False(d.AppliesTo(Ctx(a)));
        Assert.True(EventDetailsViewDescriptor.Rank > ScenarioComponentsViewDescriptor.Rank && EventDetailsViewDescriptor.Rank > EntitySummaryViewDescriptor.Rank && EventDetailsViewDescriptor.Rank > RuntimeDetailsViewDescriptor.Rank, "first among the entity views (the offer set is rank-descending)");
    }

    [Fact]
    public void CE4263_TheRows_NameTheEvent_WithLinks_AndInvariantNumbers()
    {
        var saved = CultureInfo.CurrentCulture;
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        try
        {
            CultureInfo.CurrentCulture = comma;
            var focus = new TimelineEventFocus();
            focus.Set(new TimelineEvent(5, "combat.shot", 100, 200, 12.5f, 3f, 0.25f), laneName: "Shots fired", wallText: "0:01:02.5", simText: "0:00:40.0");
            var rows = EventDetailsView.Rows(focus);
            string V(string label) => rows.Single(r => r.Label == label).Value;

            Assert.Equal("0:00:40.0", V("Time (sim)"));
            Assert.Equal("0:01:02.5", V("Time (wall)"));
            Assert.Equal("Shots fired", V("Lane"));
            Assert.Equal("combat.shot", V("Kind"));
            Assert.Equal("Shots fired  unit 100 → 200", V("Event"));
            Assert.Equal(100, rows.Single(r => r.Label == "Subject").LinkNetworkId);   // a link: the host's ONE selection
            Assert.Equal(200, rows.Single(r => r.Label == "Other").LinkNetworkId);
            Assert.Equal("12.5, 3.0", V("Position"));                                     // a comma culture changes nothing
            Assert.Equal("0.25", V("Value"));

            focus.Set(new TimelineEvent(0, "life.spawned", 100, 0, 0f, 0f, 0f));         // no Other, no values, no times: those rows are absent, not zero
            var bare = EventDetailsView.Rows(focus);
            Assert.DoesNotContain(bare, r => r.Label is "Other" or "Position" or "Value" or "Time (sim)" or "Time (wall)");
            focus.Clear();
            Assert.Empty(EventDetailsView.Rows(focus));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void CE4263_TheDetailsBundle_OffersTheEventViewFirst_WhenItsFocusedEventsSubjectIsSelected()
    {
        var (world, a, b) = World();
        using var _w = world;
        var focus = new TimelineEventFocus();
        var windows = new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f));
        var bundle = new Hrot.Editor.AiShared.Windows.EntityDetailsBundle(new Hrot.Editor.AiShared.Windows.EntityDetailsHostServices(
            WindowId: "details_ce4263", Perspective: "Player", World: () => world,
            Inspector: () => new Fdp.Presentation.Panels.EntityInspectorPanel(), Session: () => new Fdp.Presentation.Adapters.RepositoryAdapter(world),
            RunState: () => Hrot.Editor.AiShared.Variables.VariableRunState.Running, Host: () => null,
            EventFocus: focus, SelectNetworkEntity: _ => { }));
        Fdp.Toolkit.Runner.UiBundleHost.Compose(new Fdp.Toolkit.Runner.IUiBundle[] { bundle }, new Fdp.Toolkit.Runner.UiBundleContext(windows));

        Assert.NotEqual(EventDetailsViewDescriptor.ViewId, bundle.Workspace.DetailsViews.Default(Ctx(a))?.Id);   // no event: the usual default
        focus.Set(new TimelineEvent(1, "combat.shot", 100, 0, 0, 0, 0));
        Assert.Equal(EventDetailsViewDescriptor.ViewId, bundle.Workspace.DetailsViews.Default(Ctx(a))?.Id);       // FIRST
        Assert.Contains(bundle.Workspace.DetailsViews.OfferSet(Ctx(a)), d => d.Id == EntitySummaryViewDescriptor.ViewId);   // the unit's own views are still one click away
        Assert.NotEqual(EventDetailsViewDescriptor.ViewId, bundle.Workspace.DetailsViews.Default(Ctx(b))?.Id);   // another unit: gone by construction
    }
}
