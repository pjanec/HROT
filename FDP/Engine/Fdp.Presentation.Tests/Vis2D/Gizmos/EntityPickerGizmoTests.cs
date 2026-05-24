using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Gizmos;
using Moq;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos;

/// <summary>
/// Unit tests for <see cref="EntityPickerGizmo"/> crosshair rendering color logic.
/// </summary>
public class EntityPickerGizmoTests
{
    private static EntityPickerGizmo CreateGizmo(bool hoveredValid = false)
    {
        var filter = new Mock<IEntityFilter>();
        filter.Setup(f => f.IsMatch(It.IsAny<Entity>())).Returns(hoveredValid);

        var gizmo = new EntityPickerGizmo(
            hitTest:     _ => Entity.Null,
            filter:      filter.Object,
            onPicked:    _ => { },
            onCancelled: () => { },
            onRemove:    () => { });

        return gizmo;
    }

    [Fact]
    public void UpdateAndDraw_NoHoveredEntity_DrawsAmberCrosshair()
    {
        // hoveredValid = false -> no valid pick target under cursor.
        var gizmo = CreateGizmo(hoveredValid: false);
        var draw  = new Mock<IDebugDrawBuilder>();

        gizmo.UpdateAndDraw(new Moq.Mock<ISimulationView>().Object, 0f, draw.Object);

        Assert.NotNull(gizmo.TestHook_LastDrawColor);
        var color = gizmo.TestHook_LastDrawColor!.Value;
        Assert.Equal(255, color.R);
        Assert.Equal(161, color.G);
        Assert.Equal(0,   color.B);
        Assert.Equal(255, color.A);
    }

    [Fact]
    public void UpdateAndDraw_HoveredEntityValid_DrawsRedCrosshair()
    {
        // Use TestHook_ForceHoveredValid to simulate a valid entity under the cursor.
        var gizmo = CreateGizmo(hoveredValid: false);
        gizmo.TestHook_ForceHoveredValid = true;

        var draw = new Mock<IDebugDrawBuilder>();
        gizmo.UpdateAndDraw(new Moq.Mock<ISimulationView>().Object, 0f, draw.Object);

        Assert.NotNull(gizmo.TestHook_LastDrawColor);
        var color = gizmo.TestHook_LastDrawColor!.Value;
        Assert.Equal(255, color.R);
        Assert.Equal(0,   color.G);
        Assert.Equal(0,   color.B);
        Assert.Equal(255, color.A);
    }
}
