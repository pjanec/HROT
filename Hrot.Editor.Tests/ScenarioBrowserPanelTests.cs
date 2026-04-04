using Hrot.Editor;
using Hrot.Editor.UI;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests;

public class ScenarioBrowserPanelTests
{
    [Fact]
    public void HandleNewClick_CallsNewScenario()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new ScenarioBrowserPanel();
        panel.HandleNewClick(mock.Object);
        mock.Verify(l => l.NewScenario(), Times.Once);
    }

    [Fact]
    public void HandleSaveClick_CallsSaveScenario()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new ScenarioBrowserPanel();
        panel.HandleSaveClick(mock.Object);
        mock.Verify(l => l.SaveScenario(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void HandleLoadClick_CallsLoadScenario()
    {
        var mock  = new Mock<IEditorLogic>();
        var panel = new ScenarioBrowserPanel();
        panel.HandleLoadClick(mock.Object);
        mock.Verify(l => l.LoadScenario(It.IsAny<string>()), Times.Once);
    }
}
