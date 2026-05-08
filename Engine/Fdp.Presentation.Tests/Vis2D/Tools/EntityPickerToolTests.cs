using System.Numerics;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Tests.Input;
using Fdp.Toolkit.Vis2D.Tools;
using Moq;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="EntityPickerTool"/> crosshair rendering (BUG2-T002).
/// Uses <see cref="EntityPickerTool.TestHook_SkipRaylibCalls"/> so Raylib is never invoked.
/// </summary>
public class EntityPickerToolTests
{
    private static EntityPickerTool CreateTool(bool hoveredValid = false)
    {
        var factory = new Mock<IEntityFilterFactory>();

        IEntityFilter? capturedFilter = null;
        factory.Setup(f => f.CreateFilter(It.IsAny<string[]>()))
               .Returns<string[]>(_ =>
               {
                   var filter = new Mock<IEntityFilter>();
                   filter.Setup(f2 => f2.IsMatch(It.IsAny<Fdp.Core.Entity>())).Returns(hoveredValid);
                   capturedFilter = filter.Object;
                   return capturedFilter;
               });

        var tool = new EntityPickerTool(factory.Object, null);
        tool.TestHook_SkipRaylibCalls = true;

        // Attach to a canvas so PickTopmostEntity path works.
        var input  = new MockInputProvider();
        var canvas = new MapCanvas(input);
        tool.OnEnter(canvas);

        // Move cursor to a non-zero position.
        tool.HandleHover(new Vector2(50f, 50f));

        return tool;
    }

    [Fact]
    public void Draw_NoHoveredEntity_DrawsAmberCrosshair()
    {
        // hoveredValid = false → no valid pick target under cursor.
        var tool = CreateTool(hoveredValid: false);

        tool.Draw(new RenderContext { Zoom = 1f });

        Assert.NotNull(tool.TestHook_LastUsedColor);
        var color = tool.TestHook_LastUsedColor!.Value;
        Assert.Equal(255, color.R);
        Assert.Equal(161, color.G);
        Assert.Equal(0,   color.B);
        Assert.Equal(255, color.A);
    }

    [Fact]
    public void Draw_HoveredEntity_DrawsRedCrosshair()
    {
        // Use TestHook_ForceHoveredValid to simulate a valid entity under the cursor.
        var factory = new Mock<IEntityFilterFactory>();
        factory.Setup(f => f.CreateFilter(It.IsAny<string[]>()))
               .Returns(new Mock<IEntityFilter>().Object);

        var tool = new EntityPickerTool(factory.Object, null);
        tool.TestHook_SkipRaylibCalls = true;
        tool.TestHook_ForceHoveredValid = true;

        tool.Draw(new RenderContext { Zoom = 1f });

        Assert.NotNull(tool.TestHook_LastUsedColor);
        var color = tool.TestHook_LastUsedColor!.Value;
        Assert.Equal(255, color.R);
        Assert.Equal(0,   color.G);
        Assert.Equal(0,   color.B);
        Assert.Equal(255, color.A);
    }
}
