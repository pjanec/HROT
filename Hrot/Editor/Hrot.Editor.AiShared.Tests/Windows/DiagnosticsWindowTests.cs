using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

public class DiagnosticsWindowTests
{
    [Fact]
    public void Constructor_SetsId()
    {
        var window = new DiagnosticsWindow(
            new AssetCatalog(),
            Array.Empty<IAssetValidator>());
        Assert.Equal("ai_diagnostics", window.Id);
    }

    [Fact]
    public void Constructor_SetsTitle()
    {
        var window = new DiagnosticsWindow(
            new AssetCatalog(),
            Array.Empty<IAssetValidator>());
        Assert.Equal("Diagnostics", window.Title);
    }

    [Fact]
    public void Constructor_AcceptsEmptyValidators()
    {
        var ex = Record.Exception(() => new DiagnosticsWindow(
            new AssetCatalog(),
            Array.Empty<IAssetValidator>()));
        Assert.Null(ex);
    }
}
