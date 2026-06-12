using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Recipes;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class AssetNameFolderModalTests
{
    // ── Fake dialog (spy) ─────────────────────────────────────────────

    /// <summary>
    /// Fake <see cref="INameFolderDialog"/> that records calls to
    /// <see cref="Confirm"/> and whose <see cref="CanConfirm"/> is
    /// directly settable by tests.
    /// </summary>
    private sealed class FakeNameFolderDialog : INameFolderDialog
    {
        public string Title { get; set; } = "Test Dialog";
        public string Name { get; set; } = "";
        public FolderPickerState FolderPicker { get; }
        public bool CanConfirmResult { get; set; }
        public ConfirmResult ConfirmResult { get; set; } =
            ConfirmResult.Success(new FakeAsset { Name = "TestAsset" });

        public int ConfirmCallCount { get; private set; }
        public Action<IEditableAsset>? LastOnCreated { get; private set; }

        public FakeNameFolderDialog()
        {
            FolderPicker = new FolderPickerState(Array.Empty<string>());
        }

        public FakeNameFolderDialog(IEnumerable<string>? knownFolderPaths)
        {
            FolderPicker = new FolderPickerState(knownFolderPaths);
        }

        public bool CanConfirm() => CanConfirmResult;

        public ConfirmResult Confirm(Action<IEditableAsset>? onCreated = null)
        {
            ConfirmCallCount++;
            LastOnCreated = onCreated;
            return ConfirmResult;
        }
    }

    // ── Fake asset ────────────────────────────────────────────────────

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

    // ── Tests ──────────────────────────────────────────────────────────

    /// <summary>
    /// After <see cref="AssetNameFolderModal.Open"/>, <see cref="AssetNameFolderModal.IsOpen"/>
    /// is true; after <see cref="AssetNameFolderModal.Close"/>, false.
    /// </summary>
    [Fact]
    public void Open_SetsIsOpen_True()
    {
        var modal = new AssetNameFolderModal();
        var dialog = new FakeNameFolderDialog { CanConfirmResult = true };

        modal.Open(dialog);
        Assert.True(modal.IsOpen);

        modal.Close();
        Assert.False(modal.IsOpen);
    }

    /// <summary>
    /// When <c>CanConfirm() == true</c>, <see cref="AssetNameFolderModal.ConfirmActive"/>
    /// invokes <c>dialog.Confirm</c> (forwarding <c>onCreated</c>), returns success, and
    /// <see cref="AssetNameFolderModal.IsOpen"/> becomes false.
    /// </summary>
    [Fact]
    public void ConfirmActive_WhenCanConfirm_CallsConfirm_AndCloses()
    {
        var modal = new AssetNameFolderModal();
        var dialog = new FakeNameFolderDialog { CanConfirmResult = true };
        Action<IEditableAsset>? onCreated = _ => { };

        modal.Open(dialog, onCreated: onCreated);

        var result = modal.ConfirmActive();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, dialog.ConfirmCallCount);
        // onCreated was forwarded to dialog.Confirm.
        Assert.Same(onCreated, dialog.LastOnCreated);
        Assert.False(modal.IsOpen, "Modal should close on successful confirm.");
    }

    /// <summary>
    /// When <c>CanConfirm() == false</c>, <see cref="AssetNameFolderModal.ConfirmActive"/>
    /// does NOT call <see cref="INameFolderDialog.Confirm"/>, returns a Fail, and
    /// <see cref="AssetNameFolderModal.IsOpen"/> stays true.
    /// </summary>
    [Fact]
    public void ConfirmActive_WhenCannotConfirm_DoesNotConfirm_StaysOpen()
    {
        var modal = new AssetNameFolderModal();
        var dialog = new FakeNameFolderDialog { CanConfirmResult = false };

        modal.Open(dialog);

        var result = modal.ConfirmActive();

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(0, dialog.ConfirmCallCount);
        Assert.True(modal.IsOpen, "Modal should stay open when confirm fails.");
    }

    /// <summary>
    /// <see cref="AssetNameFolderModal.Open"/> then <see cref="AssetNameFolderModal.Close"/>
    /// never calls <see cref="INameFolderDialog.Confirm"/>.
    /// </summary>
    [Fact]
    public void Close_DoesNotConfirm()
    {
        var modal = new AssetNameFolderModal();
        var dialog = new FakeNameFolderDialog { CanConfirmResult = true };

        modal.Open(dialog);
        modal.Close();

        Assert.Equal(0, dialog.ConfirmCallCount);
        Assert.False(modal.IsOpen);
    }
}
