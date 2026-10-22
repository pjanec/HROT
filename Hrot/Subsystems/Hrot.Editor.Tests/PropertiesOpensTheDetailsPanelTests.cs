using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐ <c>BP-515</c> DS-7 (docs/blueprints/DESIGN_Details_Panel_View_Switching.md §8; 🔒 user, 2026-10-22: <i>"when i select
/// 'Properties...' from context on an entity, it should open the 'Details' panel (which already contains the entity component view),
/// not the entity inspector panel"</i>) — the Editor's "Properties…": the entity is selected, the Scenario Details window is shown,
/// and it is asked for the Components view. ✅ Red-proof: the handler not requesting the view ⇒ no pending request.
/// </summary>
public sealed class PropertiesOpensTheDetailsPanelTests
{
    [Fact]
    public void BP515_Properties_SelectsTheEntity_ShowsTheDetailsWindow_AndAsksForTheComponentsView()
    {
        using var world = new EntityRepository();
        var unit = world.CreateEntity();
        var windows = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        var store = new EditorSelectionStore();
        var details = new DetailsWindow(
            id:                "scenario_details",
            owningPerspective: "Scenario",
            formatter:         new VariableValueFormatter(RawValueDecoder.Instance),
            views:             new DetailsViewRegistry(),
            context:           new LiveContextSource(() => DetailsContextBuilder.Build(store, "Scenario", VariableRunState.Planning)));
        windows.RegisterWindow(details);
        details.IsOpen = false;

        EditorSubsystem.PropertiesToDetails(world, unit, details, windows);

        Assert.True(details.IsOpen, "the Details window is shown");
        Assert.Equal(Hrot.Editor.Scenario.ScenarioComponentsViewDescriptor.ViewId, details.PendingViewRequest);
        world.Bus.SwapBuffers();
        var request = Assert.Single(world.Bus.ReadManaged<SelectionChangeRequest>());
        Assert.Equal(unit, Assert.Single(request.Entities));
    }

    /// <summary>⭐ DS-7 — a host takes over the pack's default with <c>Replace</c>; a click relayed from another node still does not
    /// run it (the local-only flag is kept); replacing what was never registered throws.</summary>
    [Fact]
    public void BP515_Replace_TakesOverTheDefault_AndKeepsItLocalOnly()
    {
        var registry = new Hrot.Common.Interactions.GlobalActionRegistry();
        int which = 0;
        registry.RegisterLocal(42, (_, _) => which = 1);
        registry.Replace(42, (_, _) => which = 2);
        Assert.True(registry.TryGetHandler(42, out var handler));
        handler(null!, Entity.Null);
        Assert.Equal(2, which);
        Assert.False(registry.TryGetHandler(42, relayed: true, out _), "still local-only");
        Assert.Throws<System.InvalidOperationException>(() => registry.Replace(43, (_, _) => { }));
    }
}
