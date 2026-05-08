using Hrot.ScenarioEditor;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Reflection-based tests verifying that the surviving rendering-layer types
/// still live in <c>Hrot.ScenarioEditor</c> after the GZ059 legacy-layer cleanup.
/// </summary>
public class RenderLayerPresenceTests
{
    [Fact]
    public void ScenarioEditor_Assembly_ContainsSelectionRenderTypes()
    {
        var asm = typeof(ScenarioEditorModule).Assembly;
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.SelectionRenderSystem"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.SelectionRenderConstants"));
    }

    [Fact]
    public void IG_Assembly_DoesNotContainMovedRenderLayers()
    {
        var asm = typeof(Hrot.IG.IgApplication).Assembly;
        Assert.Null(asm.GetType("Hrot.IG.Systems.MapOverlayRenderLayer"));
        Assert.Null(asm.GetType("Hrot.IG.Systems.RouteRenderLayer"));
        Assert.Null(asm.GetType("Hrot.IG.Systems.SelectionRenderSystem"));
        Assert.Null(asm.GetType("Hrot.IG.Adapters.NedVisualizerAdapter"));
    }
}
