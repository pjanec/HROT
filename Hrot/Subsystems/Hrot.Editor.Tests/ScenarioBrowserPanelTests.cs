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
    public void HandleSaveClick_WithLoadedScenario_CallsSaveCurrentScenario()
    {
        var mock = new Mock<IEditorLogic>();
        mock.Setup(l => l.LoadedScenarioName).Returns("myScenario");
        var panel = new ScenarioBrowserPanel();
        panel.HandleSaveClick(mock.Object);
        mock.Verify(l => l.SaveCurrentScenario(), Times.Once);
    }

    [Fact]
    public void HandleSaveClick_WithNoLoadedScenario_DoesNotCallSaveMethods()
    {
        var mock = new Mock<IEditorLogic>();
        mock.Setup(l => l.LoadedScenarioName).Returns((string?)null);
        var panel = new ScenarioBrowserPanel();
        // When no scenario is loaded, HandleSaveClick opens the Save As modal (no direct save call)
        panel.HandleSaveClick(mock.Object);
        mock.Verify(l => l.SaveCurrentScenario(), Times.Never);
        mock.Verify(l => l.SaveScenarioAs(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void HandleLoadClick_DoesNotThrow()
    {
        var panel = new ScenarioBrowserPanel();
        // HandleLoadClick() just sets a flag — exercised here for coverage
        panel.HandleLoadClick();
    }
}
