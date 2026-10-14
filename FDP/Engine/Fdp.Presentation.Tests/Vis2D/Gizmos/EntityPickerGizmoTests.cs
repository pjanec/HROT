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

    // ⭐ CE-1065 — the crosshair is HALF the size it was (user, 2026-10-14: "make … the entity picker gizmo 2 times smaller"):
    //   arms reach 6 m from the cursor (were 12), the centre circle is 2 m (was 4); the line width stays in pixels.
    [Fact]
    public void CE1065_TheCrosshair_IsHalfTheOldSize()
    {
        var gizmo = CreateGizmo();
        var buffer = new DebugPrimitiveBuffer();
        gizmo.UpdateAndDraw(new Moq.Mock<ISimulationView>().Object, 0f, buffer);
        float reach = 0f, circle = -1f;
        foreach (var p in buffer.GetFrame())
        {
            if (p.Shape == GizmoMap.Contracts.Primitives.DebugPrimitiveShape.Sphere) circle = p.SphereRadius;
            if (p.Shape == GizmoMap.Contracts.Primitives.DebugPrimitiveShape.Line)
                reach = MathF.Max(reach, MathF.Max(MathF.Abs(p.LineStart.X), MathF.Max(MathF.Abs(p.LineEnd.X), MathF.Max(MathF.Abs(p.LineStart.Y), MathF.Abs(p.LineEnd.Y)))));
        }
        Assert.Equal(6f, reach, 3);
        Assert.Equal(2f, circle);
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
