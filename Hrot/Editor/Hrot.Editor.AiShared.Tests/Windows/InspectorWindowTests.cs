using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

public class InspectorWindowTests
{
    private static InspectorWindow CreateWindow() =>
        new InspectorWindow(new EditorSelectionStore());

    [Fact]
    public void Constructor_SetsId()
    {
        var window = CreateWindow();
        Assert.Equal("ai_inspector", window.Id);
    }

    [Fact]
    public void Constructor_SetsTitle()
    {
        var window = CreateWindow();
        Assert.Equal("Inspector", window.Title);
    }

    [Fact]
    public void Constructor_SetsOwningPerspective()
    {
        var window = CreateWindow();
        Assert.Equal("Authoring", window.OwningPerspective);
    }

    [Fact]
    public void Constructor_SetsScopePerspectiveBound()
    {
        var window = CreateWindow();
        Assert.Equal(WindowScope.PerspectiveBound, window.Scope);
    }
}
