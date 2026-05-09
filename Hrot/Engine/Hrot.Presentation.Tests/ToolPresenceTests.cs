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
        // Deleted in Phase 6 (BATCH-30) -- MeasureTool replaced by MeasureGizmo.
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.MeasureTool"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.MeasureToolConstants"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Gizmos.MeasureGizmo"));

        // Deleted in Phase 2 -- must no longer exist in ScenarioEditor assembly.
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.EditTool"));
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.RouteEditTool"));
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.EditToolConstants"));
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.RouteEditToolConstants"));

        // Phase 3 erasures (BATCH-26)
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.CreationTool"));
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.CreationToolConstants"));

        // Phase 3 additions (BATCH-26)
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Gizmos.EntityPlacementGizmo"));
        // Deleted in Phase 3 (BATCH-29) -- PlacementCanvasBridge replaced by GlobalGizmoManager.
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Gizmos.PlacementCanvasBridge"));

        // Phase 5 erasures (BATCH-28)
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.StandardInteractionTool"));
        Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.StandardInteractionToolConstants"));

        // Phase 5 additions (BATCH-28)
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Systems.SelectionInteractionSystem"));
        Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Gizmos.EntityDragGizmo"));
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
