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

    // ── CE-460 (E4): product-first entries, over the REAL per-kind services ─────────────────────────

    /// <summary>
    /// ⭐⭐ <c>CE-460</c> — File / New Behavior… opens the SAME launcher rooted at Behavior, over the
    /// PRODUCTION services: BTree and HSM each keep a folder (Empty + Starter), the blueprint's one
    /// Behavior template collapses to a leaf named "Blueprint", and picking it routes through the SAME
    /// dialog as New Asset. ⛔ An Instance or Library blueprint must NOT be offered as a behaviour.
    /// 🔴 Red-proof: return <c>null</c> from <c>BlueprintNewAssetService.ProductOf</c> for Behavior ⇒ no
    /// "Blueprint" leaf.
    /// </summary>
    [Fact]
    public void CE460_New_behavior_lists_every_technology_and_routes_through_the_one_dialog()
    {
        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Blueprint] = new Hrot.Blueprints.Editor.BlueprintNewAssetService(),
            [AssetKind.BTree]     = new Hrot.BTree.Editor.BTreeNewAssetService(),
            [AssetKind.Hsm]       = new Hrot.Hsm.Editor.HsmNewAssetService(),
        };

        var picker = new FakeOpenPicker();
        (AssetKind Kind, IEditableAsset Recipe)? dialog = null;
        var launcher = new NewAssetLauncher(picker.OpenPicker, services, (k, r) => dialog = (k, r));

        launcher.Open(AuthoringProduct.Behavior);

        Assert.Equal("New Behavior", picker.CapturedRequest!.Title);
        Assert.Equal("assets.new.behavior", picker.CapturedRequest.ContextKey);
        // ⭐ D2 — BTree's blank template is the pre-selected default.
        Assert.Equal("BTree:Empty", picker.CapturedRequest.InitialSelectionId);
        var entries = picker.CapturedRequest.ItemsProvider().ToList();

        Assert.Equal(2, entries.Count(e => e.Category == "BTree"));
        Assert.Equal(2, entries.Count(e => e.Category == "Hsm"));
        var bp = Assert.Single(entries, e => e.Tag is RecipeChoice { Kind: AssetKind.Blueprint });
        Assert.Equal("Blueprint", bp.Name);
        Assert.Equal("Behavior", ((RecipeChoice)bp.Tag!).Recipe.Name);
        Assert.DoesNotContain(entries, e => e.Tag is RecipeChoice rc
            && rc.Kind == AssetKind.Blueprint && rc.Recipe.Name is "Empty" or "Function Library");

        picker.InvokeHandler(ConfirmResult(bp));
        Assert.Equal(AssetKind.Blueprint, dialog!.Value.Kind);
        Assert.Same(((RecipeChoice)bp.Tag!).Recipe, dialog.Value.Recipe);
    }

    /// <summary>
    /// ⭐ D5 — the C# row is shown and picking it creates NOTHING.
    /// </summary>
    [Fact]
    public void CE460_Picking_the_csharp_row_creates_nothing()
    {
        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Blueprint] = new Hrot.Blueprints.Editor.BlueprintNewAssetService(),
        };
        var picker = new FakeOpenPicker();
        bool dialogCalled = false;
        var launcher = new NewAssetLauncher(picker.OpenPicker, services, (_, _) => dialogCalled = true);

        launcher.Open(AuthoringProduct.Condition);

        // ⭐ D2 — Blueprint's Condition template is the pre-selected default.
        Assert.Equal("Blueprint:Condition", picker.CapturedRequest!.InitialSelectionId);

        var cs = Assert.Single(picker.CapturedRequest!.ItemsProvider(), e => e.Tag is NotCreatableChoice);
        Assert.False(cs.IsEnabled);   // the generic picker never confirms it…
        picker.InvokeHandler(ConfirmResult(cs));   // …and if a host forced it through, nothing is created

        Assert.False(dialogCalled);
    }
}
