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

    // ── CE-460 (E4): product-first tree ────────────────────────────────

    /// <summary>A service whose recipes answer <see cref="INewAssetService.ProductOf"/> by name.</summary>
    private sealed class FakeProductService : INewAssetService
    {
        private readonly IReadOnlyList<IEditableAsset> _recipes;
        private readonly IReadOnlyDictionary<string, AuthoringProduct> _products;

        public FakeProductService(AssetKind kind, IReadOnlyList<IEditableAsset> recipes,
                                  IReadOnlyDictionary<string, AuthoringProduct> products)
        {
            Kind = kind; _recipes = recipes; _products = products;
        }

        public AssetKind Kind { get; }
        public IReadOnlyList<IEditableAsset> AvailableRecipes() => _recipes;
        public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
            => throw new NotSupportedException();
        public AuthoringProduct? ProductOf(IEditableAsset recipe)
            => _products.TryGetValue(recipe.Name, out var p) ? p : null;
        // ⚠ Mirrors BlueprintNewAssetService: "Behavior" is a blank template like "Empty".
        public bool IsBlankTemplate(IEditableAsset recipe) => recipe.Name is "Empty" or "Behavior";
    }

    private static Dictionary<AssetKind, INewAssetService> ProductServices()
    {
        static FakeEditableAsset A(string n, AssetKind k) => new() { Name = n, Kind = k };
        return new Dictionary<AssetKind, INewAssetService>
        {
            // Blueprint: Empty (instance, no product), Behavior blank, one condition recipe from disk.
            [AssetKind.Blueprint] = new FakeProductService(AssetKind.Blueprint,
                new IEditableAsset[] { A("Empty", AssetKind.Blueprint), A("Behavior", AssetKind.Blueprint),
                                       A("GateConditionDemo", AssetKind.Blueprint) },
                new Dictionary<string, AuthoringProduct>
                {
                    ["Behavior"] = AuthoringProduct.Behavior,
                    ["GateConditionDemo"] = AuthoringProduct.Condition,
                }),
            // BTree: two recipes, both behaviours.
            [AssetKind.BTree] = new FakeProductService(AssetKind.BTree,
                new IEditableAsset[] { A("Empty", AssetKind.BTree), A("Starter", AssetKind.BTree) },
                new Dictionary<string, AuthoringProduct>
                {
                    ["Empty"] = AuthoringProduct.Behavior, ["Starter"] = AuthoringProduct.Behavior,
                }),
            // Scenario: says nothing ⇒ never under a product.
            [AssetKind.Scenario] = new FakeNewAssetService(AssetKind.Scenario,
                new IEditableAsset[] { A("Empty", AssetKind.Scenario) }),
        };
    }

    /// <summary>
    /// ⭐⭐ <c>CE-460</c> — under a product ROOT the tree lists only that product's recipes, the path starts
    /// at the technology, a technology with ONE recipe collapses to a leaf named after it, and the C# row
    /// is appended. 🔴 Red-proof: drop the ProductOf filter in Query ⇒ the Scenario and Empty-blueprint
    /// recipes appear and the count assertion fails.
    /// </summary>
    [Fact]
    public void CE460_A_product_root_lists_that_product_by_technology()
    {
        var source  = new RecipePickerSource(ProductServices(), product: AuthoringProduct.Behavior);
        var entries = source.BuildEntries("", null);

        Assert.Equal("New Behavior", source.Title);

        // BTree keeps its folder (two recipes); Blueprint collapses to ONE leaf named "Blueprint".
        var recipes = entries.Where(e => e.Tag is RecipeChoice).ToList();
        Assert.Equal(3, recipes.Count);
        Assert.Equal(2, recipes.Count(e => e.Category == "BTree"));
        var bp = Assert.Single(recipes, e => ((RecipeChoice)e.Tag!).Kind == AssetKind.Blueprint);
        Assert.Null(bp.Category);
        Assert.Equal("Blueprint", bp.Name);
        Assert.Equal("Behavior", ((RecipeChoice)bp.Tag!).Recipe.Name);

        // ⭐ D5 — the C# row, shown, not creatable.
        var cs = Assert.Single(entries, e => e.Tag is NotCreatableChoice);
        Assert.Equal(RecipePickerSource.CSharpTechnology, ((NotCreatableChoice)cs.Tag!).Technology);
        Assert.Contains("CE-459", cs.Description);
    }

    /// <summary>
    /// ⭐ A collapsed CONTENT recipe keeps its own name beside the technology, so two conditions from disk
    /// would not both read "Blueprint".
    /// </summary>
    [Fact]
    public void CE460_A_collapsed_content_recipe_keeps_its_name()
    {
        var entries = new RecipePickerSource(ProductServices(), product: AuthoringProduct.Condition)
            .BuildEntries("", null);

        var leaf = Assert.Single(entries, e => e.Tag is RecipeChoice);
        Assert.Equal("Blueprint: GateConditionDemo", leaf.Name);
        Assert.Null(leaf.Category);
    }

    /// <summary>
    /// ⭐⭐ New Asset is UNCHANGED (handoff acceptance ③): no product ⇒ every recipe of every kind, under
    /// its kind, and no C# row.
    /// </summary>
    [Fact]
    public void CE460_Without_a_product_the_tree_is_new_assets_unchanged()
    {
        var entries = new RecipePickerSource(ProductServices()).BuildEntries("", null);

        Assert.Equal(6, entries.Count);
        Assert.All(entries, e => Assert.IsType<RecipeChoice>(e.Tag));
        Assert.All(entries, e => Assert.Equal(((RecipeChoice)e.Tag!).Kind.ToString(), e.Category));
    }
}
