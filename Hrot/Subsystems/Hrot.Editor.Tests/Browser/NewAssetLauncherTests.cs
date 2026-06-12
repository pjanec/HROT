using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Recipes;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.Tests.Browser;

/// <summary>
/// Tests for <see cref="NewAssetLauncher"/> (MTB2-T7).
/// </summary>
public sealed class NewAssetLauncherTests
{
    // ── Stub IEditableAsset (recipe) ──────────────────────────────────────

    private sealed class StubRecipe : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public AssetKind Kind { get; init; }
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty { get; init; }
        public bool IsEditorOwned { get; init; }
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    // ── Fake INewAssetService ────────────────────────────────────────────

    private sealed class FakeNewAssetService : INewAssetService
    {
        private readonly AssetKind _kind;
        private readonly IReadOnlyList<IEditableAsset> _recipes;

        public FakeNewAssetService(AssetKind kind, params IEditableAsset[] recipes)
        {
            _kind = kind;
            _recipes = recipes.ToList().AsReadOnly();
        }

        public AssetKind Kind => _kind;

        public IReadOnlyList<IEditableAsset> AvailableRecipes() => _recipes;

        public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
            => throw new NotSupportedException("Fake does not create assets.");
    }

    // ── Fake openPicker helper ───────────────────────────────────────────

    /// <summary>
    /// Captures the <see cref="PickerRequest"/> and exposes a method to invoke
    /// the result handler with a crafted <see cref="PickerResult"/>.
    /// </summary>
    private sealed class FakeOpenPicker
    {
        public PickerRequest? CapturedRequest { get; private set; }
        private Action<PickerResult>? _handler;

        public void OpenPicker(PickerRequest request, Action<PickerResult> onChosen)
        {
            CapturedRequest = request;
            _handler = onChosen;
        }

        public void InvokeHandler(PickerResult result)
        {
            _handler?.Invoke(result);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static PickerResult ConfirmResult(PickerEntry entry)
        => new(new[] { entry });

    private static PickerResult CancelResult()
        => new(Array.Empty<PickerEntry>());

    // ── Tests ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opening the launcher builds a Tree-layout PickerRequest from the recipe source.
    /// Asserts Layout, SelectionMode, and that ItemsProvider() yields recipe entries
    /// (including "Empty") whose Tag is a RecipeChoice.
    /// </summary>
    [Fact]
    public void Open_BuildsTreeRequest_FromRecipeSource()
    {
        var emptyRecipe = new StubRecipe { Kind = AssetKind.Blueprint, Name = "Empty" };
        var otherRecipe = new StubRecipe { Kind = AssetKind.Blueprint, Name = "MyRecipe" };

        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Blueprint] = new FakeNewAssetService(AssetKind.Blueprint, emptyRecipe, otherRecipe),
        };

        var fakePicker = new FakeOpenPicker();
        Action<AssetKind, IEditableAsset> showDialog = (_, _) => { };

        var launcher = new NewAssetLauncher(
            openPicker: fakePicker.OpenPicker,
            services: services,
            showNewAssetDialog: showDialog);

        launcher.Open();

        Assert.NotNull(fakePicker.CapturedRequest);
        Assert.Equal(PickerLayout.Tree, fakePicker.CapturedRequest!.Layout);
        Assert.Equal(PickerSelectionMode.Single, fakePicker.CapturedRequest.SelectionMode);
        Assert.Equal("New Asset", fakePicker.CapturedRequest.Title);

        // ItemsProvider should yield recipe entries with RecipeChoice tags.
        var entries = fakePicker.CapturedRequest.ItemsProvider().ToList();
        Assert.Equal(2, entries.Count);

        // An "Empty" entry must be present.
        Assert.Contains(entries, e => e.Name == "Empty");
        // A non-Empty recipe entry must also be present.
        Assert.Contains(entries, e => e.Name == "MyRecipe");

        // Every entry's Tag must be a RecipeChoice with the correct Kind.
        Assert.All(entries, e =>
        {
            var rc = Assert.IsType<RecipeChoice>(e.Tag);
            Assert.Equal(AssetKind.Blueprint, rc.Kind);
        });
    }

    /// <summary>
    /// Confirming a pick invokes showNewAssetDialog with the picked (kind, recipe).
    /// </summary>
    [Fact]
    public void Open_Pick_InvokesNewAssetDialog_WithKindAndRecipe()
    {
        var recipe = new StubRecipe { Kind = AssetKind.BTree, Name = "Conditional" };

        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.BTree] = new FakeNewAssetService(AssetKind.BTree, recipe),
        };

        var fakePicker = new FakeOpenPicker();
        (AssetKind, IEditableAsset)? dialogCall = null;
        Action<AssetKind, IEditableAsset> showDialog = (k, r) => dialogCall = (k, r);

        var launcher = new NewAssetLauncher(
            openPicker: fakePicker.OpenPicker,
            services: services,
            showNewAssetDialog: showDialog);

        launcher.Open();

        // Simulate user picking the first entry.
        var entries = fakePicker.CapturedRequest!.ItemsProvider();
        var firstEntry = entries.First();
        fakePicker.InvokeHandler(ConfirmResult(firstEntry));

        Assert.NotNull(dialogCall);
        Assert.Equal(AssetKind.BTree, dialogCall!.Value.Item1);
        Assert.Same(recipe, dialogCall!.Value.Item2);
    }

    /// <summary>
    /// Cancelling the picker does NOT invoke showNewAssetDialog.
    /// </summary>
    [Fact]
    public void Open_Cancel_DoesNothing()
    {
        var recipe = new StubRecipe { Kind = AssetKind.Hsm, Name = "SimpleState" };

        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Hsm] = new FakeNewAssetService(AssetKind.Hsm, recipe),
        };

        var fakePicker = new FakeOpenPicker();
        bool dialogCalled = false;
        Action<AssetKind, IEditableAsset> showDialog = (_, _) => dialogCalled = true;

        var launcher = new NewAssetLauncher(
            openPicker: fakePicker.OpenPicker,
            services: services,
            showNewAssetDialog: showDialog);

        launcher.Open();

        // Simulate user cancelling.
        fakePicker.InvokeHandler(CancelResult());

        Assert.False(dialogCalled, "showNewAssetDialog should not be called on cancel.");
    }
}
