using Hrot.UI.Common.Panels;
using Hrot.UI.Common.Facades;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="PreviewPanel"/>.
/// Tests call the <c>internal</c> handler methods directly to bypass ImGui.
/// </summary>
public class PreviewPanelTests
{
    // ── EnterPreviewMode ──────────────────────────────────────────────────────

    [Fact]
    public void HandleEnterPreview_WhenNotInPreviewMode_CallsEnterPreviewMode()
    {
        var panel = new PreviewPanel();
        var ctrl = new Mock<IPreviewController>();
        ctrl.Setup(c => c.IsInPreviewMode).Returns(false);

        panel.HandleEnterPreview(ctrl.Object);

        ctrl.Verify(c => c.EnterPreviewMode(false), Times.Once);
    }

    [Fact]
    public void HandleEnterPreview_DoesNotCallExitPreviewMode()
    {
        var panel = new PreviewPanel();
        var ctrl = new Mock<IPreviewController>();

        panel.HandleEnterPreview(ctrl.Object);

        ctrl.Verify(c => c.ExitPreviewMode(), Times.Never);
    }

    // ── ExitPreviewMode ───────────────────────────────────────────────────────

    [Fact]
    public void HandleExitPreview_WhenInPreviewMode_CallsExitPreviewMode()
    {
        var panel = new PreviewPanel();
        var ctrl = new Mock<IPreviewController>();
        ctrl.Setup(c => c.IsInPreviewMode).Returns(true);

        panel.HandleExitPreview(ctrl.Object);

        ctrl.Verify(c => c.ExitPreviewMode(), Times.Once);
    }

    [Fact]
    public void HandleExitPreview_DoesNotCallEnterPreviewMode()
    {
        var panel = new PreviewPanel();
        var ctrl = new Mock<IPreviewController>();

        panel.HandleExitPreview(ctrl.Object);

        ctrl.Verify(c => c.EnterPreviewMode(false), Times.Never);
    }
}
