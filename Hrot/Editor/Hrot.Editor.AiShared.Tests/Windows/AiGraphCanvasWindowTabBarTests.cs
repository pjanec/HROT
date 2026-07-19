using Fdp.Presentation.Fonts;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// Tests for the MULTI-TAB bar added to <see cref="AiGraphCanvasWindow"/>: per-kind tab
/// projection (<see cref="AiGraphCanvasWindow.TabDocuments"/>), the tab label/glyph mapping
/// (<see cref="AiGraphCanvasWindow.GetTabLabel"/> / <see cref="AiGraphCanvasWindow.GetTabGlyph"/>),
/// the click/close test seams, and the Alt+Left back-navigation history.
/// <para>
/// All tests are headless — the ImGui-only rendering path (<c>DrawTabBar</c> itself, the
/// <c>BeginTabBar</c>/<c>BeginTabItem</c> calls and the ImGui-selection sync) is not exercised
/// here (it requires a live ImGui context, consistent with the rest of this file); these tests
/// cover the pure projection/activation/history logic that backs it.
/// </para>
/// </summary>
public sealed class AiGraphCanvasWindowTabBarTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AiDocumentManager MakeDocManager() =>
        new(perspectiveSwitchCallback: _ => { });

    private sealed class RecordingRenderSeam : ICanvasRenderSeam
    {
        public void Render(NodeEditor.Core.View.GraphView view) { }
    }

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(AssetKind kind, string name = "Test")
        {
            Kind    = kind;
            Name    = name;
            AssetId = Guid.NewGuid();
        }
        public Guid      AssetId       { get; }
        public string    Name          { get; }
        public AssetKind Kind          { get; }
        public string    SourceFilePath => "/fake.cs";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    /// <summary>A fake asset that also supplies an <see cref="IAssetIconKeyProvider.IconKey"/> override.</summary>
    private sealed class FakeIconAsset : IEditableAsset, IAssetIconKeyProvider
    {
        public FakeIconAsset(AssetKind kind, string? iconKey, string name = "Test")
        {
            Kind    = kind;
            Name    = name;
            AssetId = Guid.NewGuid();
            IconKey = iconKey;
        }
        public Guid      AssetId       { get; }
        public string    Name          { get; }
        public AssetKind Kind          { get; }
        public string    SourceFilePath => "/fake.cs";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
        public string?   IconKey        { get; }
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    // ── TabDocuments: per-kind projection ─────────────────────────────────────

    [Fact]
    public void TabDocuments_FiltersToOwnKind_InOpenOrder()
    {
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        var t1 = dm.Open(new FakeAsset(AssetKind.BTree, "T1"));
        var h1 = dm.Open(new FakeAsset(AssetKind.Hsm, "H1"));
        var t2 = dm.Open(new FakeAsset(AssetKind.BTree, "T2"));

        var tabs = win.TabDocuments;

        Assert.Equal(2, tabs.Count);
        Assert.Same(t1, tabs[0]);
        Assert.Same(t2, tabs[1]);
        Assert.DoesNotContain(h1, tabs);
    }

    [Fact]
    public void TabDocuments_Empty_WhenNoDocumentOfThisKindIsOpen()
    {
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        dm.Open(new FakeAsset(AssetKind.Hsm, "H1"));

        Assert.Empty(win.TabDocuments);
    }

    // ── Tab label / glyph mapping ──────────────────────────────────────────────

    [Fact]
    public void GetTabLabel_ContainsGlyph_Name_AndStableAssetIdSuffix()
    {
        var asset = new FakeAsset(AssetKind.BTree, "MyTree");
        var doc   = new AiDocument(asset, AssetKind.BTree);

        var label = AiGraphCanvasWindow.GetTabLabel(doc);

        Assert.StartsWith(IconsFontAwesome6.Sitemap + " MyTree", label);
        Assert.EndsWith("###" + asset.AssetId, label);
    }

    [Theory]
    [InlineData(AssetKind.BTree)]
    [InlineData(AssetKind.Hsm)]
    [InlineData(AssetKind.Blueprint)]
    [InlineData(AssetKind.Blackboard)]
    [InlineData(AssetKind.Utility)]
    [InlineData(AssetKind.Scenario)]
    public void GetTabGlyph_DefaultsPerKind_AreNonEmpty(AssetKind kind)
    {
        var doc = new AiDocument(new FakeAsset(kind), kind);
        var glyph = AiGraphCanvasWindow.GetTabGlyph(doc);
        Assert.False(string.IsNullOrEmpty(glyph));
    }

    [Fact]
    public void GetTabGlyph_BTree_IsSitemap()
    {
        var doc = new AiDocument(new FakeAsset(AssetKind.BTree), AssetKind.BTree);
        Assert.Equal(IconsFontAwesome6.Sitemap, AiGraphCanvasWindow.GetTabGlyph(doc));
    }

    [Fact]
    public void GetTabGlyph_Hsm_IsCircleNodes()
    {
        var doc = new AiDocument(new FakeAsset(AssetKind.Hsm), AssetKind.Hsm);
        Assert.Equal(IconsFontAwesome6.CircleNodes, AiGraphCanvasWindow.GetTabGlyph(doc));
    }

    [Fact]
    public void GetTabGlyph_Blueprint_WithoutIconKey_DefaultsToBolt()
    {
        var doc = new AiDocument(new FakeAsset(AssetKind.Blueprint), AssetKind.Blueprint);
        Assert.Equal(IconsFontAwesome6.Bolt, AiGraphCanvasWindow.GetTabGlyph(doc));
    }

    [Fact]
    public void GetTabGlyph_BlueprintAction_UsesIconKeyOverride()
    {
        var asset = new FakeIconAsset(AssetKind.Blueprint, AssetKindIcons.BlueprintActionIconKey);
        var doc   = new AiDocument(asset, AssetKind.Blueprint);
        Assert.Equal(IconsFontAwesome6.Bolt, AiGraphCanvasWindow.GetTabGlyph(doc));
    }

    [Fact]
    public void GetTabGlyph_BlueprintCondition_UsesIconKeyOverride()
    {
        var asset = new FakeIconAsset(AssetKind.Blueprint, AssetKindIcons.BlueprintConditionIconKey);
        var doc   = new AiDocument(asset, AssetKind.Blueprint);
        Assert.Equal(IconsFontAwesome6.CircleQuestion, AiGraphCanvasWindow.GetTabGlyph(doc));
    }

    [Fact]
    public void GetTabGlyph_BlueprintFunction_UsesIconKeyOverride()
    {
        var asset = new FakeIconAsset(AssetKind.Blueprint, AssetKindIcons.BlueprintFunctionIconKey);
        var doc   = new AiDocument(asset, AssetKind.Blueprint);
        Assert.Equal(IconsFontAwesome6.Gear, AiGraphCanvasWindow.GetTabGlyph(doc));
    }

    // ── Tab click / close test seams ───────────────────────────────────────────

    [Fact]
    public void SimulateTabClick_ActivatesTheClickedDocument()
    {
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        var a = dm.Open(new FakeAsset(AssetKind.BTree, "A"));
        var b = dm.Open(new FakeAsset(AssetKind.BTree, "B")); // b becomes active

        Assert.Same(b, dm.Active);

        win.SimulateTabClick(a);

        Assert.Same(a, dm.Active);
    }

    [Fact]
    public void SimulateTabClose_RemovesDocumentFromOpenDocuments()
    {
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        var a = dm.Open(new FakeAsset(AssetKind.BTree, "A"));

        win.SimulateTabClose(a);

        Assert.DoesNotContain(a, dm.OpenDocuments);
    }

    // ── Alt+Left back-navigation history ──────────────────────────────────────

    [Fact]
    public void BackNavigation_PopsPreviouslyActiveDocumentsInLifoOrder()
    {
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        var doc1 = dm.Open(new FakeAsset(AssetKind.BTree, "One"));
        win.SimulateDrawClientArea(); // tracks doc1 (nothing to push yet)

        var doc2 = dm.Open(new FakeAsset(AssetKind.BTree, "Two"));
        win.SimulateDrawClientArea(); // pushes doc1

        var doc3 = dm.Open(new FakeAsset(AssetKind.BTree, "Three"));
        win.SimulateDrawClientArea(); // pushes doc2

        Assert.Same(doc3, dm.Active);

        win.SimulateBackNavigation();
        Assert.Same(doc2, dm.Active);
        win.SimulateDrawClientArea(); // must NOT re-push doc3 (guarded by _suppressHistoryPush)

        win.SimulateBackNavigation();
        Assert.Same(doc1, dm.Active);
    }

    [Fact]
    public void BackNavigation_SkipsStaleClosedEntries()
    {
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        var doc1 = dm.Open(new FakeAsset(AssetKind.BTree, "One"));
        win.SimulateDrawClientArea();

        var doc2 = dm.Open(new FakeAsset(AssetKind.BTree, "Two"));
        win.SimulateDrawClientArea(); // pushes doc1

        dm.Close(doc1); // doc1 closed while sitting in history

        // The only history entry (doc1) is stale — back-navigation must no-op, not throw.
        win.SimulateBackNavigation();

        Assert.Same(doc2, dm.Active);
    }

    [Fact]
    public void BackNavigation_NoOp_WhenHistoryIsEmpty()
    {
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        var doc1 = dm.Open(new FakeAsset(AssetKind.BTree, "One"));
        win.SimulateDrawClientArea();

        win.SimulateBackNavigation(); // no history yet

        Assert.Same(doc1, dm.Active);
    }
}
