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

    [Fact]
    public void CE1161_TheProductionScenarioCatalogue_OffersTheScenarioAndZonesViews()
    {
        var ids = RealEditor().ScenarioWorkspace!.DetailsViews.All.Select(d => d.Id).ToArray();
        Assert.Contains(ScenarioEnvironmentViewDescriptor.ViewId, ids);
        Assert.Contains(ZonesDetailsViewDescriptor.ViewId, ids);
    }

    [Fact]
    public void CE1161_TheScenarioView_AppliesOnTheEmptyMap_Only()
    {
        var d = ScenarioEnvironmentViewDescriptor.For(() => null, () => null, (_, _) => { });
        Assert.True(d.AppliesTo(Context(SelectionOrigin.MapBackground, 0)));
        Assert.False(d.AppliesTo(Context(SelectionOrigin.MapBackground, 1)));   // a unit picked: its own views win
        Assert.False(d.AppliesTo(Context(SelectionOrigin.GraphCanvas, 0)));
        Assert.True(ScenarioEnvironmentViewDescriptor.Rank < ZonesDetailsViewDescriptor.Rank);
    }

    [Fact]
    public void CE1161_TheTypedStartTime_IsWrittenAsTyped_ABadOneIsRefused()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<ScenarioClock>();
        var env = world.CreateEntity();
        world.AddComponent(env, ScenarioClock.Default);
        var view = new ScenarioEnvironmentView(() => world, () => null, (_, _) => { });

        view.StartText = "2025-12-24 18:45";
        Assert.True(view.Apply(world));
        Assert.Equal(new DateTime(2025, 12, 24, 18, 45, 0, DateTimeKind.Utc), world.GetComponentRO<ScenarioClock>(env).StartUtc);

        view.StartText = "tomorrow";
        Assert.False(view.Apply(world));
        Assert.Equal(new DateTime(2025, 12, 24, 18, 45, 0, DateTimeKind.Utc), world.GetComponentRO<ScenarioClock>(env).StartUtc);
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
