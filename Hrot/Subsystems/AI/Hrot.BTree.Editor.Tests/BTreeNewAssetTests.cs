using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeNewAssetTests : IDisposable
{
    private readonly string _tempRoot;

    public BTreeNewAssetTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BTreeNewAssetTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Create_WritesValidJson_UnderAssetsRoot_FreshId()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        var result = svc.CreateNew(null, "MyBTree", "combat");

        // Assert the file exists at the expected relative path.
        var expectedPath = Path.Combine(_tempRoot, "combat", "MyBTree.btree.json");
        Assert.True(File.Exists(expectedPath), $"Expected file at '{expectedPath}'");

        // Read back and round-trip.
        var json = File.ReadAllText(expectedPath);
        var deserialized = BTreeJsonServices.Deserialize(json);
        Assert.NotNull(deserialized);

        // Fresh AssetId: not a zero guid and matches the returned adapter.
        Assert.NotEqual(Guid.Empty, deserialized.AssetId);
        Assert.Equal(result.AssetId, deserialized.AssetId);
        Assert.Equal("MyBTree", deserialized.Name);

        // Returned adapter has correct kind and file path.
        Assert.Equal(AssetKind.BTree, result.Kind);
        Assert.Equal(expectedPath, result.SourceFilePath);
    }

    [Fact]
    public void Create_Empty_DtoIsMinimalButValid()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        var result = svc.CreateNew(null, "EmptyTree", "");

        var expectedPath = Path.Combine(_tempRoot, "EmptyTree.btree.json");
        Assert.True(File.Exists(expectedPath));

        var json = File.ReadAllText(expectedPath);
        var dto = BTreeJsonServices.Deserialize(json);
        Assert.NotNull(dto);
        Assert.Equal("EmptyTree", dto.Name);
        Assert.NotEqual(Guid.Empty, dto.AssetId);

        // Round-trip: serialize → deserialize produces equivalent DTO.
        var roundTripped = BTreeJsonServices.Deserialize(BTreeJsonServices.Serialize(dto));
        Assert.NotNull(roundTripped);
        Assert.Equal(dto.AssetId, roundTripped.AssetId);
        Assert.Equal(dto.Name, roundTripped.Name);
    }

    [Fact]
    public void Create_EmptySentinel_ProducesValidJson()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        var emptyRecipe = svc.AvailableRecipes().First(r => r.Name == "Empty");
        Assert.NotNull(emptyRecipe);

        var result = svc.CreateNew(emptyRecipe, "FromEmpty", "");
        var expectedPath = Path.Combine(_tempRoot, "FromEmpty.btree.json");
        Assert.True(File.Exists(expectedPath));

        var dto = BTreeJsonServices.Deserialize(File.ReadAllText(expectedPath));
        Assert.NotNull(dto);
        Assert.Equal("FromEmpty", dto.Name);
    }

    [Fact]
    public void Create_FromRecipe_ClonesContent_NewIdentity()
    {
        // Create a recipe DTO with exactly one node of known content. MakeEmptyDto now seeds a Root
        // node (new trees always start with a root), so clear it first to build a self-contained
        // single-node recipe — this test asserts clone-copies-content, not MakeEmptyDto's seeding.
        var recipeDto = BTreeNewAssetService.MakeEmptyDto();
        recipeDto.Nodes.Clear();
        recipeDto.AssetId = Guid.NewGuid();
        recipeDto.Name = "MyRecipe";
        recipeDto.TargetNamespace = "MyNS";
        recipeDto.Nodes.Add(new BTreeRootNodeDto
        {
            VisualId = Guid.NewGuid(),
            DisplayLabel = "Root",
        });

        // Build a service, then use the recipe adapter to clone.
        var svc = new BTreeNewAssetService(_tempRoot);
        var recipeAdapter = new BTreeEditableAssetAdapter(recipeDto, "");

        var result = svc.CreateNew(recipeAdapter, "CloneName", "sub");
        var expectedPath = Path.Combine(_tempRoot, "sub", "CloneName.btree.json");
        Assert.True(File.Exists(expectedPath));

        var cloneDto = BTreeJsonServices.Deserialize(File.ReadAllText(expectedPath));
        Assert.NotNull(cloneDto);

        // New identity.
        Assert.NotEqual(recipeDto.AssetId, cloneDto.AssetId);
        Assert.NotEqual(Guid.Empty, cloneDto.AssetId);
        Assert.Equal("CloneName", cloneDto.Name);

        // Content cloned.
        Assert.Equal(recipeDto.TargetNamespace, cloneDto.TargetNamespace);
        Assert.Single(cloneDto.Nodes);
        Assert.Equal("Root", cloneDto.Nodes[0].DisplayLabel);

        // Source file path points to the written location.
        Assert.Equal(expectedPath, result.SourceFilePath);
    }

    [Fact]
    public void AvailableRecipes_IncludesEmptyEntry()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        var recipes = svc.AvailableRecipes();

        Assert.NotEmpty(recipes);
        var empty = recipes.FirstOrDefault(r => r.Name == "Empty");
        Assert.NotNull(empty);
        Assert.Equal(AssetKind.BTree, empty.Kind);
    }

    [Fact]
    public void CreateNew_NullName_Throws()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        Assert.Throws<ArgumentException>(() => svc.CreateNew(null, "", ""));
        Assert.Throws<ArgumentException>(() => svc.CreateNew(null, "  ", ""));
    }

    [Fact]
    public void Kind_IsBTree()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        Assert.Equal(AssetKind.BTree, svc.Kind);
    }

    /// <summary>
    /// ⭐ CE-1443 B2 — <i>New BTree</i> lists the on-disk recipes of <c>recipes/behaviors/BTrees</c> (the ONE shared lister, <c>RecipeFiles</c>), after Empty and Starter —
    /// so what <i>Save BTree as Recipe…</i> writes is offered. ⛔ It was a TODO (only the in-code recipes). 🔴 Mutation: the <c>RecipeFiles.Read</c> line removed ⇒ red.
    /// </summary>
    [Fact]
    public void CE1443_B2_NewBTree_ListsTheRecipesOnDisk()
    {
        var saved = AssetRoots.ConfiguredRoot;
        AssetRoots.Configure(_tempRoot);
        try
        {
            var svc = new BTreeNewAssetService(Path.Combine(_tempRoot, "work"));
            var made = svc.CreateNew(null, "MyRecipe", "");
            var recipes = AssetRoots.RecipesFor(AssetKind.BTree);
            Directory.CreateDirectory(recipes);
            File.Copy(made.SourceFilePath, Path.Combine(recipes, "MyRecipe.btree.json"));

            var listed = svc.AvailableRecipes();
            Assert.Contains(listed, r => r.Name == "MyRecipe" && r.SourceFilePath.EndsWith("MyRecipe.btree.json", StringComparison.Ordinal));
            Assert.True(listed.Count >= 3, "Empty and Starter stay first");
        }
        finally { AssetRoots.Configure(saved); }
    }
}
