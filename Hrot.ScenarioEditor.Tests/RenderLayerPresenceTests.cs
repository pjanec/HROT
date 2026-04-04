using Hrot.ScenarioEditor;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Reflection-based tests verifying that the rendering-layer migration (PACK2-E003) was
/// complete: all render layers and adapters now live in <c>Hrot.ScenarioEditor</c>.
/// </summary>
public class RenderLayerPresenceTests
{
    [Fact]
    public void ScenarioEditor_Assembly_ContainsRenderLayers()
    {
        var asm = typeof(ScenarioEditorModule).Assembly;
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.MapOverlayRenderLayer"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.RouteRenderLayer"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.MissionRenderLayer"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.SelectionRenderSystem"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.SelectionRenderConstants"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Adapters.NedVisualizerAdapterConstants"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Adapters.StubVisualizerAdapter"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Adapters.StubVisualizerConstants"));
    }

    [Fact]
    public void ScenarioEditor_Assembly_ContainsSstVisualizerAdapter()
    {
        var asm = typeof(ScenarioEditorModule).Assembly;
        // SstVisualizerAdapter.cs defines NedVisualizerAdapter
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Adapters.NedVisualizerAdapter"));
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
