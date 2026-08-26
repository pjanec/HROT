using Hrot.Common;
using Hrot.Editor;
using Hrot.Editor.UI;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests;

public class EditorToolbarPanelTests
{
    [Fact]
    public void HandleSpawnClick_ActivatesSpawnTool()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new EditorToolbarPanel();
        panel.HandleSpawnClick(mock.Object);
        mock.Verify(l => l.ActivateTool(EditorTool.Spawn), Times.Once);
    }

    [Fact]
    public void HandleSelectClick_ActivatesSelectTool()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new EditorToolbarPanel();
        panel.HandleSelectClick(mock.Object);
        mock.Verify(l => l.ActivateTool(EditorTool.Select), Times.Once);
    }
}
