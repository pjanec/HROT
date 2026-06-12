using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Recipes;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Recipes;

// ─────────────────────────────────────────────────────────────────────────────
// BATCH-39 — INameFolderDialog implementation tests
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NameFolderDialogTests
{
    // ── Fakes ───────────────────────────────────────────────────────────

    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public AssetKind Kind { get; init; }
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    private sealed class FakeNewAssetService : INewAssetService
    {
        public AssetKind Kind { get; }

        public FakeNewAssetService(AssetKind kind)
        {
            Kind = kind;
        }

        public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
            => new FakeAsset { AssetId = Guid.NewGuid(), Name = name, Kind = Kind };

        public IReadOnlyList<IEditableAsset> AvailableRecipes()
            => new List<IEditableAsset> { new FakeAsset { Name = "Empty", Kind = Kind } };
    }

    // ── NewAssetDialog_ImplementsINameFolderDialog ──────────────────────

    [Fact]
    public void NewAssetDialog_ImplementsINameFolderDialog()
    {
        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Blueprint] = new FakeNewAssetService(AssetKind.Blueprint),
        };

        var dialog = new NewAssetDialog(services)
        {
            Kind = AssetKind.Blueprint,
        };

        // Assignable to INameFolderDialog.
        INameFolderDialog iface = dialog;
        Assert.NotNull(iface);

        // Title equals $"New {Kind}".
        Assert.Equal("New Blueprint", iface.Title);

        // Name round-trips via the interface.
        iface.Name = "TestName";
        Assert.Equal("TestName", dialog.Name);
        Assert.Equal("TestName", iface.Name);

        // FolderPicker is the same instance.
        Assert.Same(dialog.FolderPicker, iface.FolderPicker);
    }

    // ── SaveAsDialog_ImplementsINameFolderDialog ────────────────────────

    [Fact]
    public void SaveAsDialog_ImplementsINameFolderDialog()
    {
        var sourceAsset = new FakeAsset
        {
            Name = "SourceAsset",
            Kind = AssetKind.Blueprint,
        };

        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Blueprint] = new FakeNewAssetService(AssetKind.Blueprint),
        };

        var dialog = new SaveAsDialog(sourceAsset, services);

        // Assignable to INameFolderDialog.
        INameFolderDialog iface = dialog;
        Assert.NotNull(iface);

        // Title == "Save As".
        Assert.Equal("Save As", iface.Title);

        // Name exposed via interface.
        Assert.Equal("SourceAsset", iface.Name);

        // FolderPicker exposed.
        Assert.Same(dialog.FolderPicker, iface.FolderPicker);
    }
}
