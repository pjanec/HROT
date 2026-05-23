using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

public class RuntimeInspectorWindowTests
{
    private static RuntimeInspectorWindow CreateWindow() =>
        new RuntimeInspectorWindow(new EditorSelectionStore(), new DebugSessionRegistry());

    private sealed class StubPane : IRuntimeInspectorPane
    {
        public AssetKind TargetKind => AssetKind.BTree;
        public void Draw() { }
    }

    [Fact]
    public void Constructor_SetsId()
    {
        var window = CreateWindow();
        Assert.Equal("ai_runtime_inspector", window.Id);
    }

    [Fact]
    public void Constructor_SetsTitle()
    {
        var window = CreateWindow();
        Assert.Equal("Runtime Inspector", window.Title);
    }

    [Fact]
    public void Constructor_SetsScopePerspectiveBound()
    {
        var window = CreateWindow();
        Assert.Equal(WindowScope.PerspectiveBound, window.Scope);
    }

    [Fact]
    public void RegisterPane_IncreasesRegisteredPaneCount()
    {
        var window = CreateWindow();
        Assert.Equal(0, window.RegisteredPaneCount);
        window.RegisterPane(new StubPane());
        Assert.Equal(1, window.RegisteredPaneCount);
    }

    [Fact]
    public void RegisterPane_MultipleCanBeRegistered()
    {
        var window = CreateWindow();
        window.RegisterPane(new StubPane());
        window.RegisterPane(new StubPane());
        window.RegisterPane(new StubPane());
        Assert.Equal(3, window.RegisteredPaneCount);
    }
}
