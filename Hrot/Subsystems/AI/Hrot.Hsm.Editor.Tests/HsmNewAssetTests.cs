using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Editor.AiShared;
using Hrot.Hsm.Editor;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmNewAssetTests : IDisposable
{
    private readonly string _tempRoot;

    public HsmNewAssetTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "HsmNewAssetTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Create_WritesValidJson_FreshId()
    {
        var svc = new HsmNewAssetService(_tempRoot);
        var result = svc.CreateNew(null, "MyHsm", "combat");

        // Assert the file exists at the expected relative path.
        var expectedPath = Path.Combine(_tempRoot, "combat", "MyHsm.hsm.json");
        Assert.True(File.Exists(expectedPath), $"Expected file at '{expectedPath}'");

        // Read back and round-trip.
        var json = File.ReadAllText(expectedPath);
        var deserialized = HsmJsonServices.Deserialize(json);
        Assert.NotNull(deserialized);

        // Fresh AssetId: not a zero guid and matches the returned adapter.
        Assert.NotEqual(Guid.Empty, deserialized.AssetId);
        Assert.Equal(result.AssetId, deserialized.AssetId);
        Assert.Equal("MyHsm", deserialized.Name);

        // Returned adapter has correct kind and file path.
        Assert.Equal(AssetKind.Hsm, result.Kind);
        Assert.Equal(expectedPath, result.SourceFilePath);
    }

    [Fact]
    public void Create_Empty_DtoIsMinimalButValid()
    {
        var svc = new HsmNewAssetService(_tempRoot);
        var result = svc.CreateNew(null, "EmptyHsm", "");

        var expectedPath = Path.Combine(_tempRoot, "EmptyHsm.hsm.json");
        Assert.True(File.Exists(expectedPath));

        var json = File.ReadAllText(expectedPath);
        var dto = HsmJsonServices.Deserialize(json);
        Assert.NotNull(dto);
        Assert.Equal("EmptyHsm", dto.Name);
        Assert.NotEqual(Guid.Empty, dto.AssetId);

        // Round-trip: serialize → deserialize produces equivalent DTO.
        var roundTripped = HsmJsonServices.Deserialize(HsmJsonServices.Serialize(dto));
        Assert.NotNull(roundTripped);
        Assert.Equal(dto.AssetId, roundTripped.AssetId);
        Assert.Equal(dto.Name, roundTripped.Name);
    }

    [Fact]
    public void Create_FromRecipe_ClonesContent_NewIdentity()
    {
        // Create a recipe DTO with some content.
        var recipeDto = HsmNewAssetService.MakeEmptyDto();
        recipeDto.AssetId = Guid.NewGuid();
        recipeDto.Name = "MyRecipe";
        recipeDto.TargetNamespace = "MyNS";
        recipeDto.States.Add(new StateNodeDto
        {
            StableId = Guid.NewGuid(),
            Name = "Idle",
            IsInitial = true,
        });

        // Build a service, then use the recipe adapter to clone.
        var svc = new HsmNewAssetService(_tempRoot);
        var recipeAdapter = new HsmEditableAssetAdapter(recipeDto, "");

        var result = svc.CreateNew(recipeAdapter, "CloneName", "sub");
        var expectedPath = Path.Combine(_tempRoot, "sub", "CloneName.hsm.json");
        Assert.True(File.Exists(expectedPath));

        var cloneDto = HsmJsonServices.Deserialize(File.ReadAllText(expectedPath));
        Assert.NotNull(cloneDto);

        // New identity.
        Assert.NotEqual(recipeDto.AssetId, cloneDto.AssetId);
        Assert.NotEqual(Guid.Empty, cloneDto.AssetId);
        Assert.Equal("CloneName", cloneDto.Name);

        // Content cloned.
        Assert.Equal(recipeDto.TargetNamespace, cloneDto.TargetNamespace);
        Assert.Single(cloneDto.States);
        Assert.Equal("Idle", cloneDto.States[0].Name);

        // Source file path points to the written location.
        Assert.Equal(expectedPath, result.SourceFilePath);
    }

    [Fact]
    public void AvailableRecipes_IncludesEmptyEntry()
    {
        var svc = new HsmNewAssetService(_tempRoot);
        var recipes = svc.AvailableRecipes();

        Assert.NotEmpty(recipes);
        var empty = recipes.FirstOrDefault(r => r.Name == "Empty");
        Assert.NotNull(empty);
        Assert.Equal(AssetKind.Hsm, empty.Kind);
    }

    [Fact]
    public void CreateNew_NullName_Throws()
    {
        var svc = new HsmNewAssetService(_tempRoot);
        Assert.Throws<ArgumentException>(() => svc.CreateNew(null, "", ""));
        Assert.Throws<ArgumentException>(() => svc.CreateNew(null, "  ", ""));
    }

    [Fact]
    public void Kind_IsHsm()
    {
        var svc = new HsmNewAssetService(_tempRoot);
        Assert.Equal(AssetKind.Hsm, svc.Kind);
    }
}
