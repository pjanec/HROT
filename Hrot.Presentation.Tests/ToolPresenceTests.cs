using Hrot.IG;
using Hrot.ScenarioEditor;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Reflection-based tests verifying that the tool-migration (PACK2-E002) was complete:
/// all 10 interaction tools now live in <c>Hrot.ScenarioEditor</c> and are absent from
/// <c>Hrot.IG</c>.
/// </summary>
public class ToolPresenceTests
{
    [Fact]
    public void ScenarioEditor_Assembly_ContainsAllToolTypes()
    {
        var asm = typeof(ScenarioEditorModule).Assembly;
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.CreationTool"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.EditTool"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.RouteEditTool"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.MeasureTool"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.StandardInteractionTool"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.CreationToolConstants"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.EditToolConstants"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.RouteEditToolConstants"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.MeasureToolConstants"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.StandardInteractionToolConstants"));
    }

    [Fact]
    public void IG_Assembly_DoesNotContainToolTypes()
    {
        var asm = typeof(IgApplication).Assembly;
        Assert.Null(asm.GetType("Hrot.IG.Tools.CreationTool"));
        Assert.Null(asm.GetType("Hrot.IG.Tools.EditTool"));
        Assert.Null(asm.GetType("Hrot.IG.Tools.RouteEditTool"));
        Assert.Null(asm.GetType("Hrot.IG.Tools.MeasureTool"));
        Assert.Null(asm.GetType("Hrot.IG.Tools.StandardInteractionTool"));
    }
}
