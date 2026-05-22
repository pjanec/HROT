using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Reload;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class QuickReloadServiceTests
{
    private sealed class StubCatalog : IAssetCatalog
    {
        public IEnumerable<AssetCatalogEntry> EnumerateAll() => [];
    }

    // SC1
    [Fact]
    public async Task QuickReloadService_TriggerAsync_LogsToOutputConsole()
    {
        var console = new MockOutputConsole();
        var service = new QuickReloadService(new StubCatalog(), new EditorState(), console);
        var asset   = new BlueprintAsset { AssetId = Guid.NewGuid() };

        await service.TriggerAsync(asset);

        Assert.True(console.InfoMessages.Count > 0);
    }

    // SC2
    [Fact]
    public async Task QuickReloadService_TriggerAsync_NonNullAsset_Required()
    {
        var console = new MockOutputConsole();
        var service = new QuickReloadService(new StubCatalog(), new EditorState(), console);

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.TriggerAsync(null!));
    }
}
