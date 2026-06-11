using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Debug;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class EditorInfrastructureTests
{
    // SC1
    [Fact]
    public void DirtyTracker_MarkDirty_ThenIsDirty()
    {
        var tracker = new DirtyTracker();
        var id = Guid.NewGuid();
        tracker.MarkDirty(id);
        Assert.True(tracker.IsDirty(id));
    }

    // SC2
    [Fact]
    public void DirtyTracker_MarkClean_AfterDirty()
    {
        var tracker = new DirtyTracker();
        var id = Guid.NewGuid();
        tracker.MarkDirty(id);
        tracker.MarkClean(id);
        Assert.False(tracker.IsDirty(id));
    }

    // SC3
    [Fact]
    public void EditorSelectionStore_SelectAsset_FiresEvent()
    {
        var store = new EditorSelectionStore();
        int fired = 0;
        store.OnSelectionChanged += () => fired++;
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid() };
        store.SelectAsset(asset);
        Assert.Equal(1, fired);
    }

    // SC4
    [Fact]
    public void EditorSelectionStore_SelectAsset_UpdatesSelectedAsset()
    {
        var store = new EditorSelectionStore();
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid() };
        store.SelectAsset(asset);
        Assert.Same(asset, store.SelectedAsset);
    }

    // SC5
    [Fact]
    public void EditorState_SetAndGet_InMemoryAsset()
    {
        var state = new EditorState();
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid() };
        state.SetInMemoryAsset(asset);
        Assert.Same(asset, state.GetInMemoryAsset(asset.AssetId));
    }

    // SC6
    [Fact]
    public void EditorState_RemoveInMemoryAsset()
    {
        var state = new EditorState();
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid() };
        state.SetInMemoryAsset(asset);
        state.RemoveInMemoryAsset(asset.AssetId);
        Assert.Null(state.GetInMemoryAsset(asset.AssetId));
    }

    // SC7
    [Fact]
    public void BlueprintPeerSource_EmptyDirectory_EnumeratesNone()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var catalog = new BlueprintPeerSource(tempDir);
            Assert.Empty(catalog.EnumerateAll());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // SC8
    [Fact]
    public void BlueprintEditorModule_OnEditorActivated_RegistersMenuEntries()
    {
        var registrar = new MockWindowRegistrar();
        var module = CreateModule(registrar);
        module.RegisterWindow(new CountingWindow("Win1"));
        module.RegisterWindow(new CountingWindow("Win2"));
        module.OnEditorActivated();
        Assert.Equal(2, registrar.MenuEntries.Count);
    }

    // SC9
    [Fact]
    public void BlueprintEditorModule_DrawAllWindows_OnlyDrawsVisible()
    {
        var module = CreateModule(new MockWindowRegistrar());
        var visible = new CountingWindow("Visible") { IsVisible = true };
        var hidden  = new CountingWindow("Hidden")  { IsVisible = false };
        module.RegisterWindow(visible);
        module.RegisterWindow(hidden);
        module.DrawAllWindows();
        Assert.Equal(1, visible.DrawCallCount);
        Assert.Equal(0, hidden.DrawCallCount);
    }

    // SC10
    [Fact]
    public void MasterSyncTimeControllerAdapter_ImplementsInterface()
    {
        Assert.True(typeof(MasterSyncTimeControllerAdapter).IsAssignableTo(typeof(IBlueprintTimeController)));
    }

    // Helper: create a BlueprintEditorModule with a null-sink output console.
    private static BlueprintEditorModule CreateModule(IWindowRegistrar registrar)
    {
        return new BlueprintEditorModule(
            registrar,
            new DirtyTracker(),
            new EditorSelectionStore(),
            new EditorState(),
            new NullOutputConsole());
    }

    private sealed class NullOutputConsole : IOutputConsole
    {
        public void LogInfo(string message)    { }
        public void LogWarning(string message) { }
        public void LogError(string message)   { }
        public void LogDebug(string message)   { }
        public void LogDiagnostic(Microsoft.CodeAnalysis.Diagnostic diagnostic) { }
    }
}
