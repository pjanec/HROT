using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

public class AssetBrowserWindowTests
{
    private static AssetBrowserWindow CreateWindow() =>
        new AssetBrowserWindow(new EditorSelectionStore(), new AssetCatalog());

    [Fact]
    public void Constructor_SetsId()
    {
        var window = CreateWindow();
        Assert.Equal("ai_asset_browser", window.Id);
    }

    [Fact]
    public void Constructor_SetsTitle()
    {
        var window = CreateWindow();
        Assert.Equal("Asset Browser", window.Title);
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
