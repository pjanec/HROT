using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Inspector;
using Hrot.Blueprints.Editor.Reload;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class EditorWindowTests
{
    private static QuickReloadService MakeQuickReload() =>
        new QuickReloadService(
            new StubCatalog(),
            new EditorState(),
            new MockOutputConsole(),
            new BlueprintCompiler(),
            new AiHotReloadCoordinator(
                new BehaviorRegistry(), new BlueprintRegistry(),
                new AiHotReloadCoordinatorOptions()));

    private sealed class StubCatalog : IAssetCatalog
    {
        public IEnumerable<AssetCatalogEntry> EnumerateAll() => [];
    }

    // GraphEditorWindow

    [Fact]
    public void GraphEditorWindow_Constructor_SetsTitle()
    {
        var w = new GraphEditorWindow(
            new EditorSelectionStore(),
            new DirtyTracker(),
            new EditorState(),
            MakeQuickReload(),
            new FullRebuildService(new MockOutputConsole()));

        Assert.Equal("Graph Editor", w.Title);
    }

    [Fact]
    public void GraphEditorWindow_SelectionChanged_OpensAsset()
    {
        var store = new EditorSelectionStore();
        var w = new GraphEditorWindow(
            store,
            new DirtyTracker(),
            new EditorState(),
            MakeQuickReload(),
            new FullRebuildService(new MockOutputConsole()));

        var asset = new Hrot.Blueprints.Core.Assets.BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "TestBlueprint",
        };
        store.SelectAsset(asset);

        Assert.Equal(asset, w.CurrentAsset);
    }

    [Fact]
    public void GraphEditorWindow_Constructor_ThrowsOnNullParams()
    {
        var store   = new EditorSelectionStore();
        var tracker = new DirtyTracker();
        var state   = new EditorState();
        var qrs     = MakeQuickReload();
        var frs     = new FullRebuildService(new MockOutputConsole());

        Assert.Throws<ArgumentNullException>(() =>
            new GraphEditorWindow(null!, tracker, state, qrs, frs));
        Assert.Throws<ArgumentNullException>(() =>
            new GraphEditorWindow(store, null!, state, qrs, frs));
        Assert.Throws<ArgumentNullException>(() =>
            new GraphEditorWindow(store, tracker, null!, qrs, frs));
        Assert.Throws<ArgumentNullException>(() =>
            new GraphEditorWindow(store, tracker, state, null!, frs));
        Assert.Throws<ArgumentNullException>(() =>
            new GraphEditorWindow(store, tracker, state, qrs, null!));
    }

    // InspectorWindow

    [Fact]
    public void InspectorWindow_Constructor_SetsTitle()
    {
        var w = new InspectorWindow(
            new EditorSelectionStore(), new DirtyTracker(), new DrawerRegistry());
        Assert.Equal("Inspector", w.Title);
    }

    // PreferencesWindow

    [Fact]
    public void PreferencesWindow_Constructor_SetsTitle()
    {
        var w = new PreferencesWindow(BlueprintEditorPreferences.Defaults, "/tmp/prefs.json");
        Assert.Equal("Blueprint Preferences", w.Title);
    }

    [Fact]
    public void PreferencesWindow_Constructor_ThrowsOnNullParams()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PreferencesWindow(null!, "/tmp/prefs.json"));
        Assert.Throws<ArgumentNullException>(() =>
            new PreferencesWindow(BlueprintEditorPreferences.Defaults, null!));
    }
}
