using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Icons;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Browser;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>AssetBrowserDockedWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⭐ <see cref="AssetBrowserPanel"/> has no id of its own (the queue's "plain <c>*Panel</c>"
/// gotcha) — this window supplies its own <c>Id</c>/<c>Kind</c> and does the actual
/// <c>PanelSnapshot.Register</c> call; the panel only builds the pure projection.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class AssetBrowserDockedWindowDumpsItsStateTests : IDisposable
{
    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "TestAsset";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _assets;
        public FakeCatalog(params IEditableAsset[] assets) => _assets = new List<IEditableAsset>(assets);

        public IReadOnlyList<IEditableAsset> All => _assets.AsReadOnly();
        public IEditableAsset? FindByAssetId(Guid assetId) => _assets.FirstOrDefault(a => a.AssetId == assetId);
        public IEditableAsset? FindByName(string name) => _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
#pragma warning disable 67
        public event Action<AssetKind>? Changed;
#pragma warning restore 67
    }

    private sealed class FakeIconProvider : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle)
        {
            handle = new IconHandle(1, 16, 16);
            return true;
        }
    }

    private static AssetBrowserDockedWindow CreateWindow(IAssetCatalog? catalog = null, string? id = null) =>
        new(
            catalog ?? new FakeCatalog(),
            new FakeIconProvider(),
            new AssetBrowserPanelOptions { Kinds = AssetKindFilter.All, ShowAllTab = true },
            _ => { },
            id: id);

    public AssetBrowserDockedWindowDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "asset_browser_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = CreateWindow(id: id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesARealField()
    {
        const string id = "asset_browser_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var asset  = new FakeAsset { Name = "Foo", Kind = AssetKind.Blueprint };
        var window = CreateWindow(new FakeCatalog(asset), id: id);

        window.SimulateBuildAndPublish();

        var stored = PanelSnapshot.TryGet(id);
        Assert.NotNull(stored);
        Assert.Equal(id, stored!.PanelId);
        Assert.Equal(AssetBrowserDockedWindow.Kind, stored.PanelKind);

        var dump = stored.Dump();
        Assert.Equal(1, dump["visibleAssetCount"]!.GetValue<int>());
        Assert.Equal("All", dump["activeTab"]!.GetValue<string>());
        Assert.Null(dump["selectedAssetName"]);
        Assert.Equal("", dump["filter"]!.GetValue<string>());
    }

    [Fact]
    public void FilterIsReflectedInTheDump()
    {
        const string id = "asset_browser_rail3";
        PanelSnapshot.CaptureEnabled = true;
        var window = CreateWindow(id: id);
        window.Panel.Filter = "abc";

        window.SimulateBuildAndPublish();

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.Equal("abc", dump["filter"]!.GetValue<string>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        const string id = "asset_browser_rail4";
        var asset  = new FakeAsset { Name = "Foo" };
        var window = CreateWindow(new FakeCatalog(asset), id: id);   // CaptureEnabled stays false

        var vm = window.SimulateBuildAndPublish();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.Equal(1, vm.VisibleAssetCount);
    }
}
