using System.Numerics;
using Fdp.Presentation.WindowManager;
using Xunit;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Pure unit tests for <see cref="DockspaceLayout"/> — no ImGui dependency,
/// headless by design. Verifies the dockspace inset math per §4.1.2.
/// </summary>
public class DockspaceLayoutTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // CentralSize — width = workWidth, height = workHeight - toolbar - statusBar
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CentralSize_SubtractsToolbarAndStatusBar()
    {
        var size = DockspaceLayout.CentralSize(1920f, 1080f, 64f, 24f);

        Assert.Equal(1920f, size.X);
        Assert.Equal(1080f - 64f - 24f, size.Y); // 992
    }

    [Fact]
    public void CentralSize_ZeroInsets_ReturnsFullWorkSize()
    {
        var size = DockspaceLayout.CentralSize(800f, 600f, 0f, 0f);

        Assert.Equal(800f, size.X);
        Assert.Equal(600f, size.Y);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CentralSize — clamped to >= 0 (never negative)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CentralSize_ClampsToZero_WhenInsetsExceedWork()
    {
        // Toolbar + status bar take more space than available
        var size = DockspaceLayout.CentralSize(1024f, 80f, 100f, 50f);

        Assert.Equal(1024f, size.X);
        Assert.Equal(0f, size.Y);
    }

    [Fact]
    public void CentralSize_ClampsToZero_WhenInsetsExactlyEqualWork()
    {
        var size = DockspaceLayout.CentralSize(1920f, 100f, 60f, 40f);

        Assert.Equal(1920f, size.X);
        Assert.Equal(0f, size.Y);
    }

    [Fact]
    public void CentralSize_ClampsToZero_WhenOnlyToolbarExceedsWork()
    {
        var size = DockspaceLayout.CentralSize(800f, 50f, 64f, 0f);

        Assert.Equal(800f, size.X);
        Assert.Equal(0f, size.Y);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CentralPos — workPos + (0, toolbarHeight)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CentralPos_OffsetsTopByToolbarHeight()
    {
        var pos = DockspaceLayout.CentralPos(new Vector2(10f, 20f), 64f);

        Assert.Equal(10f, pos.X);
        Assert.Equal(84f, pos.Y); // 20 + 64
    }

    [Fact]
    public void CentralPos_ZeroToolbarHeight_ReturnsSamePosition()
    {
        var pos = DockspaceLayout.CentralPos(new Vector2(100f, 200f), 0f);

        Assert.Equal(100f, pos.X);
        Assert.Equal(200f, pos.Y);
    }

    [Fact]
    public void CentralPos_NegativeWorkPos_StillOffsets()
    {
        // WorkPos shouldn't be negative in practice, but the helper should
        // still apply the offset correctly (pure math, not validated).
        var pos = DockspaceLayout.CentralPos(new Vector2(-5f, -10f), 30f);

        Assert.Equal(-5f, pos.X);
        Assert.Equal(20f, pos.Y); // -10 + 30
    }
}
