using Fdp.Core;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Tests.Selection;

public sealed class EditorSelectionStoreTests
{
    /// <summary>⭐ <c>internal</c> since <c>L0</c> so <c>TheSelectionIsASetTests</c> uses the SAME fake —
    /// ⛔ a second one would be two answers to "what is an editable asset here" (ruling 9).</summary>
    internal sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "Test";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = "/test.cs";
        public bool IsDirty { get; init; }
        public bool IsEditorOwned { get; init; }
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    [Fact]
    public void ActiveAsset_DefaultIsNull()
    {
        var store = new EditorSelectionStore();
        Assert.Null(store.ActiveAsset);
    }

    [Fact]
    public void ActiveAsset_Set_FiresOnSelectionChanged()
    {
        var store = new EditorSelectionStore();
        int count = 0;
        store.OnSelectionChanged += () => count++;

        store.ActiveAsset = new FakeAsset();
        Assert.Equal(1, count);
    }

    [Fact]
    public void ActiveAsset_SetSameValue_DoesNotFire()
    {
        var store = new EditorSelectionStore();
        var asset = new FakeAsset();
        store.ActiveAsset = asset;

        int count = 0;
        store.OnSelectionChanged += () => count++;

        store.ActiveAsset = asset; // same reference
        Assert.Equal(0, count);
    }

    [Fact]
    public void ActiveSubSelection_NullWhenNoActiveAsset()
    {
        var store = new EditorSelectionStore();
        Assert.Null(store.ActiveSubSelection);
    }

    [Fact]
    public void ActiveSubSelection_Set_ReturnsValue()
    {
        var store = new EditorSelectionStore();
        var asset = new FakeAsset();
        store.ActiveAsset = asset;

        var sel = new BTreeNodeSelection(Guid.NewGuid());
        store.ActiveSubSelection = sel;

        Assert.Equal(sel, store.ActiveSubSelection);
    }

    [Fact]
    public void ActiveSubSelection_Set_FiresOnSelectionChanged()
    {
        var store = new EditorSelectionStore();
        store.ActiveAsset = new FakeAsset();
        int count = 0;
        store.OnSelectionChanged += () => count++;

        store.ActiveSubSelection = new BTreeNodeSelection(Guid.NewGuid());
        Assert.Equal(1, count);
    }

    [Fact]
    public void ActiveSubSelection_SetSameValue_DoesNotFire()
    {
        var store = new EditorSelectionStore();
        store.ActiveAsset = new FakeAsset();
        var sel = new BTreeNodeSelection(Guid.NewGuid());
        store.ActiveSubSelection = sel;

        int count = 0;
        store.OnSelectionChanged += () => count++;

        store.ActiveSubSelection = sel; // same record value
        Assert.Equal(0, count);
    }

    [Fact]
    public void GetSubSelection_ReturnsPerAssetSelection()
    {
        var store = new EditorSelectionStore();
        var assetA = new FakeAsset();
        var assetB = new FakeAsset();
        store.ActiveAsset = assetA;
        var selA = new BTreeNodeSelection(Guid.NewGuid());
        store.ActiveSubSelection = selA;

        store.ActiveAsset = assetB;
        var selB = new HsmStateSelection(Guid.NewGuid());
        store.ActiveSubSelection = selB;

        Assert.Equal(selA, store.GetSubSelection(assetA.AssetId));
        Assert.Equal(selB, store.GetSubSelection(assetB.AssetId));
    }

    [Fact]
    public void SetSubSelection_NoActiveAsset_UpdatesEntry()
    {
        var store = new EditorSelectionStore();
        var assetId = Guid.NewGuid();
        var sel = new HsmStateSelection(Guid.NewGuid());
        store.SetSubSelection(assetId, sel);
        Assert.Equal(sel, store.GetSubSelection(assetId));
    }

    [Fact]
    public void SelectedEntity_DefaultIsNull()
    {
        var store = new EditorSelectionStore();
        Assert.Null(store.SelectedEntity);
    }

    [Fact]
    public void SelectedEntity_Set_FiresOnSelectionChanged()
    {
        var store = new EditorSelectionStore();
        int count = 0;
        store.OnSelectionChanged += () => count++;

        store.SelectedEntity = new Entity(1, 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public void SelectedEntity_SetSameValue_DoesNotFire()
    {
        var store = new EditorSelectionStore();
        var entity = new Entity(1, 0);
        store.SelectedEntity = entity;

        int count = 0;
        store.OnSelectionChanged += () => count++;

        store.SelectedEntity = entity;
        Assert.Equal(0, count);
    }

    [Fact]
    public void SelectedEntity_SetNull_FromValue_FiresEvent()
    {
        var store = new EditorSelectionStore();
        store.SelectedEntity = new Entity(1, 0);
        int count = 0;
        store.OnSelectionChanged += () => count++;

        store.SelectedEntity = null;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Forget_RemovesSubSelection_AndFires()
    {
        var store = new EditorSelectionStore();
        var assetId = Guid.NewGuid();
        store.SetSubSelection(assetId, new HsmStateSelection(Guid.NewGuid()));

        int count = 0;
        store.OnSelectionChanged += () => count++;

        store.Forget(assetId);
        Assert.Equal(1, count);
        Assert.Null(store.GetSubSelection(assetId));
    }

    [Fact]
    public void RegisterAndUnregister_DoesNotThrow()
    {
        var store = new EditorSelectionStore();
        var id = Guid.NewGuid();
        store.RegisterOpenAsset(id);
        store.UnregisterOpenAsset(id);
        // No assertion needed -- just no exception
    }
}
