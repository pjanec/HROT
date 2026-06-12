using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Recipes;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class RecipePickerSourceTests
{
    // ── Fake IEditableAsset for tests ─────────────────────────────────

    private sealed class FakeEditableAsset : IEditableAsset
    {
        public Guid AssetId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public AssetKind Kind { get; set; }
        public string SourceFilePath { get; set; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    // ── Fake INewAssetService for tests ────────────────────────────────

    private sealed class FakeNewAssetService : INewAssetService
    {
        public AssetKind Kind { get; }

        private readonly IReadOnlyList<IEditableAsset> _recipes;

        public FakeNewAssetService(AssetKind kind, IReadOnlyList<IEditableAsset> recipes)
        {
            Kind = kind;
            _recipes = recipes;
        }

        public IReadOnlyList<IEditableAsset> AvailableRecipes() => _recipes;

        public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
            => throw new NotSupportedException("Not needed for RecipePickerSource tests.");
    }

    // ── MTB2-T6-01: Empty entries per kind ─────────────────────────────

    [Fact]
    public void Entries_IncludeEmptyPerKind()
    {
        var emptyBp = new FakeEditableAsset { Name = "Empty", Kind = AssetKind.Blueprint };
        var recipeBp = new FakeEditableAsset { Name = "MyBlueprint", Kind = AssetKind.Blueprint };
        var emptyHsm = new FakeEditableAsset { Name = "Empty", Kind = AssetKind.Hsm };

        var svcA = new FakeNewAssetService(AssetKind.Blueprint, new IEditableAsset[] { emptyBp, recipeBp });
        var svcB = new FakeNewAssetService(AssetKind.Hsm, new IEditableAsset[] { emptyHsm });

        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Blueprint] = svcA,
            [AssetKind.Hsm] = svcB
        };

        var source = new RecipePickerSource(services);
        var entries = source.BuildEntries("", null);

        // There should be two "Empty"-named entries — one per kind.
        var emptyEntries = entries.Where(e => e.Name == "Empty").ToList();
        Assert.Equal(2, emptyEntries.Count);

        // Verify each has the correct kind in its RecipeChoice tag.
        var tags = emptyEntries.Select(e => (RecipeChoice)e.Tag!).ToList();
        Assert.Contains(tags, t => t.Kind == AssetKind.Blueprint);
        Assert.Contains(tags, t => t.Kind == AssetKind.Hsm);
    }

    // ── MTB2-T6-02: Category, IconKey, Tag ─────────────────────────────

    [Fact]
    public void Entries_HaveKindCategory_PerKindIcon_AndRecipeTag()
    {
        var recipe = new FakeEditableAsset { Name = "CombatAI", Kind = AssetKind.Blueprint };
        var svc = new FakeNewAssetService(AssetKind.Blueprint, new IEditableAsset[] { recipe });
        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Blueprint] = svc
        };

        // Without recipeCategory → Category is just the kind name.
        var source = new RecipePickerSource(services);
        var entry = source.ToEntry(new RecipeChoice(AssetKind.Blueprint, recipe));

        Assert.Equal("Blueprint", entry.Category);
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Blueprint), entry.IconKey);
        Assert.Equal("asset/blueprint", entry.IconKey);

        var tag = Assert.IsType<RecipeChoice>(entry.Tag);
        Assert.Equal(AssetKind.Blueprint, tag.Kind);
        Assert.Same(recipe, tag.Recipe);

        // With recipeCategory → Category = "Kind/Sub".
        var sourceWithCategory = new RecipePickerSource(
            services,
            recipeCategory: _ => "AI");

        var entryWithCategory = sourceWithCategory.ToEntry(
            new RecipeChoice(AssetKind.Blueprint, recipe));

        Assert.Equal("Blueprint/AI", entryWithCategory.Category);
    }

    // ── MTB2-T6-03: Stable item key ────────────────────────────────────

    [Fact]
    public void GetItemKey_StableAcrossQueries()
    {
        var empty = new FakeEditableAsset { Name = "Empty", Kind = AssetKind.Blueprint };
        var svc = new FakeNewAssetService(AssetKind.Blueprint, new IEditableAsset[] { empty });
        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Blueprint] = svc
        };

        var source = new RecipePickerSource(services);

        var results1 = source.Query("", null);
        var key1 = source.GetItemKey(results1[0]);

        var results2 = source.Query("", null);
        var key2 = source.GetItemKey(results2[0]);

        Assert.Equal(key1, key2);
        Assert.Equal("Blueprint:Empty", key1);
    }

    // ── MTB2-T6-04: Recipe description ─────────────────────────────────

    [Fact]
    public void Description_FromRecipeMetadata_WhenPresent()
    {
        var theRecipe = new FakeEditableAsset { Name = "CloneBot", Kind = AssetKind.Blueprint };
        var otherRecipe = new FakeEditableAsset { Name = "Empty", Kind = AssetKind.Blueprint };
        var svc = new FakeNewAssetService(AssetKind.Blueprint,
            new IEditableAsset[] { theRecipe, otherRecipe });
        var services = new Dictionary<AssetKind, INewAssetService>
        {
            [AssetKind.Blueprint] = svc
        };

        var source = new RecipePickerSource(
            services,
            describe: a => a == theRecipe ? "Clone of X" : null);

        var entry1 = source.ToEntry(new RecipeChoice(AssetKind.Blueprint, theRecipe));
        Assert.Equal("Clone of X", entry1.Description);

        var entry2 = source.ToEntry(new RecipeChoice(AssetKind.Blueprint, otherRecipe));
        Assert.Null(entry2.Description);
    }
}
