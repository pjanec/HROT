using System;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Weather;
using Hrot.Editor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.Scenario;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐ CE-1161 (docs/designs/weather/DESIGN_Weather_And_Time.md §7; backend → ui handoff U1/U2; CE-3217) — an empty-map click shows the
/// SCENARIO view (start time, global weather) and the ZONES view in Details. Railed on the PRODUCTION editor's catalogue (R-67: a
/// hand-built registry cannot see a composition-root defect — BP-542 built the Zones view and nothing registered it).
/// </summary>
public sealed class TheScenarioEnvironmentViewTests
{
    private static EditorSubsystem RealEditor()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        return editor;
    }

    private sealed class Picked : IEntitySelectionSource
    {
        private readonly System.Collections.Generic.IReadOnlyList<Entity> _e;
        public Picked(int count) => _e = Enumerable.Range(0, count).Select(i => new Entity(i + 2, 1)).ToArray();
        System.Collections.Generic.IReadOnlyList<Entity> IEntitySelectionSource.Selected() => _e;
    }

    private static DetailsContext Context(SelectionOrigin focus, int entities)
    {
        var store = new EditorSelectionStore();
        store.NotifySurfaceFocused(focus);
        return DetailsContextBuilder.Build(store, "Scenario", VariableRunState.Planning, new Picked(entities));
    }

    /// <summary>⭐ CE-4346 — ONE Scenario tab on an empty-map click: the Scenario FORM (<c>details.form.scenario</c>); the old raw view (<c>details.scenario</c>,
    /// the World Environment's components) is gone. The Zones view stays.
    /// 🔴 Mutation: the ScenarioEnvironmentView registration back ⇒ two tabs titled "Scenario" ⇒ red.</summary>
    [Fact]
    public void CE4346_TheProductionScenarioCatalogue_OffersOneScenarioTab_TheForm_AndTheZonesView()
    {
        var views = RealEditor().ScenarioWorkspace!.DetailsViews.All;
        var ids = views.Select(d => d.Id).ToArray();
        Assert.Contains("details.form.scenario", ids);
        Assert.DoesNotContain("details.scenario", ids);
        Assert.Single(views, d => d.Title == "Scenario");
        Assert.Contains(ZonesDetailsViewDescriptor.ViewId, ids);
    }

    /// <summary>⭐ CE-4263 — the production editor registers the Event view (first rank) and shares ITS focus with the host's <c>SelectTimelineEvent</c>; with no world nothing is selected, so nothing is focused.</summary>
    [Fact]
    public void CE4263_TheProductionEditor_RegistersTheEventView_AndAClickWithNoWorldFocusesNothing()
    {
        var editor = RealEditor();
        var views = editor.ScenarioWorkspace!.DetailsViews.All;
        Assert.Contains(views, d => d.Id == EventDetailsViewDescriptor.ViewId);
        Assert.Equal(EventDetailsViewDescriptor.Rank, views.Max(d => d.Rank));   // the highest: the default / first
        Assert.False(editor.SelectTimelineEvent(new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent(0, "life.spawned", 100, 0, 0, 0, 0)));
        Assert.Null(editor.EventFocus.Event);
    }

    [Fact]
    public void CE1161_AnEmptyMapClick_HandsDetailsTheMapBackground()
    {
        var editor = RealEditor();
        var store = editor.AiEditorSelectionStore;
        store.NotifySurfaceFocused(SelectionOrigin.GraphCanvas);
        editor.OnMapSelectionChanged(new Entity(4, 1), System.Numerics.Vector3.Zero);   // a unit: the latch does not move
        Assert.Equal(SelectionOrigin.GraphCanvas, store.FocusedSurface);
        editor.OnMapSelectionChanged(Entity.Null, System.Numerics.Vector3.Zero);        // the empty map
        Assert.Equal(SelectionOrigin.MapBackground, store.FocusedSurface);
    }
}
