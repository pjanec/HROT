using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Recipes;

namespace Hrot.Hsm.Editor;

/// <summary>
/// HSM implementation of <see cref="INewAssetService"/>.
/// Mints a fresh <see cref="AssetId"/>, builds a minimal valid HSM DTO
/// (or clones a recipe), and persists it as valid JSON under the Assets root.
/// </summary>
/// <remarks>
/// Design §18.3: BTree/HSM/Scenario impls <b>mint + persist</b> (write JSON).
/// Mirrors <see cref="Hrot.BTree.Editor.BTreeNewAssetService"/> exactly.
/// </remarks>
public sealed class HsmNewAssetService : INewAssetService
{
    private readonly IEditableAsset _emptyRecipe;
    private readonly string _assetRootPath;

    /// <summary>
    /// Creates the service with the default assets root
    /// (<see cref="AssetRoots.AssetsFor"/>(<see cref="AssetKind.Hsm"/>)).
    /// </summary>
    public HsmNewAssetService()
        : this(assetRootPath: null) { }

    /// <summary>
    /// Creates the service with an explicit assets root path.
    /// Pass <see langword="null"/> to use the default.
    /// </summary>
    /// <param name="assetRootPath">
    /// Absolute path to the assets root directory for HSM files.
    /// When <see langword="null"/>, defaults to <see cref="AssetRoots.AssetsFor"/>(<see cref="AssetKind.Hsm"/>).
    /// </param>
    public HsmNewAssetService(string? assetRootPath)
    {
        _emptyRecipe = new HsmEditableAssetAdapter(MakeEmptyDto(), "");
        _assetRootPath = assetRootPath ?? AssetRoots.AssetsFor(AssetKind.Hsm);
    }

    /// <inheritdoc />
    public AssetKind Kind => AssetKind.Hsm;

    /// <inheritdoc />
    public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must not be empty.", nameof(name));

        HsmAssetDto dto;

        if (recipe == null || IsEmptyRecipe(recipe))
        {
            dto = MakeEmptyDto();
            dto.AssetId = Guid.NewGuid();
            dto.Name = name;
        }
        else
        {
            var recipeDto = ExtractDto(recipe);
            // Clone via serialize → deserialize round-trip.
            var json = HsmJsonServices.Serialize(recipeDto);
            dto = HsmJsonServices.Deserialize(json)
                  ?? throw new InvalidOperationException("Recipe round-trip deserialization returned null.");
            dto.AssetId = Guid.NewGuid();
            dto.Name = name;
        }

        // Persist: write to <assetRootPath>/<relPath>/<name>.hsm.json
        var fileDir = string.IsNullOrEmpty(relPath) ? _assetRootPath : Path.Combine(_assetRootPath, relPath);
        var filePath = Path.Combine(fileDir, name + ".hsm.json");

        var jsonOut = HsmJsonServices.Serialize(dto);
        var prettyJson = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(jsonOut);
        AtomicFileWriter.Write(filePath, prettyJson);

        return new HsmEditableAssetAdapter(dto, filePath);
    }

    /// <inheritdoc />
    public IReadOnlyList<IEditableAsset> AvailableRecipes()
    {
        var recipes = new List<IEditableAsset> { _emptyRecipe };

        // Discover on-disk HSM recipes (if any exist under Recipes/HSMs).
        // TODO: wire IRecipeDiscovery when a recipe-discovery service exists.
        // For now, only the in-code "Empty" recipe is offered.

        return recipes;
    }

    private static bool IsEmptyRecipe(IEditableAsset recipe)
        => string.Equals(recipe.Name, "Empty", StringComparison.OrdinalIgnoreCase);

    private static HsmAssetDto ExtractDto(IEditableAsset recipe)
    {
        if (recipe is HsmEditableAssetAdapter adapter && adapter.Dto != null)
            return adapter.Dto;

        throw new ArgumentException(
            $"Recipe must be a {nameof(HsmEditableAssetAdapter)} with a non-null DTO.",
            nameof(recipe));
    }

    /// <summary>
    /// Synthesizes a minimal valid <see cref="HsmAssetDto"/> in code —
    /// no disk read, no file I/O.
    /// </summary>
    internal static HsmAssetDto MakeEmptyDto()
    {
        return new HsmAssetDto
        {
            AssetId            = Guid.NewGuid(),
            Name               = "Empty",
            TargetNamespace    = "",
            BlackboardTypeName = "",
            Canvas             = new HsmCanvasDto { Zoom = 1.0f },
            States             = new List<StateNodeDto>(),
            Regions            = new List<RegionNodeDto>(),
            Transitions        = new List<TransitionNodeDto>(),
            GlobalTransitions  = new List<GlobalTransitionNodeDto>(),
            Events             = new List<EventDefinitionDto>(),
            Suppressions       = new HsmSuppressionsDto(),
            Blackboard         = new HsmBlackboardBlockDto(),
        };
    }
}

/// <summary>
/// Thin <see cref="IEditableAsset"/> adapter for HSM DTOs.
/// Carries the DTO so callers can inspect recipe content and the written file path.
/// </summary>
public sealed class HsmEditableAssetAdapter : IEditableAsset
{
    private readonly HsmAssetDto? _dto;
    private readonly string _sourceFilePath;

    /// <summary>
    /// The underlying DTO. May be <see langword="null"/> when the adapter is
    /// constructed for an entity that has no DTO (e.g. a "from-disk" reference).
    /// </summary>
    public HsmAssetDto? Dto => _dto;

    public HsmEditableAssetAdapter(HsmAssetDto? dto, string sourceFilePath)
    {
        _dto = dto;
        _sourceFilePath = sourceFilePath;
    }

    public Guid AssetId => _dto?.AssetId ?? Guid.Empty;
    public string Name => _dto?.Name ?? string.Empty;
    public AssetKind Kind => AssetKind.Hsm;
    public string SourceFilePath => _sourceFilePath;
    public bool IsDirty => false;
    public bool IsEditorOwned => true;
    public event Action? Changed { add { } remove { } }
}
