using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Recipes;

namespace Hrot.BTree.Editor;

/// <summary>
/// BTree implementation of <see cref="INewAssetService"/>.
/// Mints a fresh <see cref="AssetId"/>, builds a minimal valid BTree DTO
/// (or clones a recipe), and persists it as valid JSON under the Assets root.
/// </summary>
/// <remarks>
/// Design §18.3: BTree/HSM/Scenario impls <b>mint + persist</b> (write JSON).
/// The Blueprint impl (BATCH-17) is mint-only; the dialog (MTB-P6-T5) reconciles.
/// </remarks>
public sealed class BTreeNewAssetService : INewAssetService
{
    private readonly IEditableAsset _emptyRecipe;
    private readonly string _assetRootPath;

    /// <summary>
    /// Creates the service with the default assets root
    /// (<see cref="AssetRoots.AssetsFor"/>(<see cref="AssetKind.BTree"/>)).
    /// </summary>
    public BTreeNewAssetService()
        : this(assetRootPath: null) { }

    /// <summary>
    /// Creates the service with an explicit assets root path.
    /// Pass <see langword="null"/> to use the default.
    /// </summary>
    /// <param name="assetRootPath">
    /// Absolute path to the assets root directory for BTree files.
    /// When <see langword="null"/>, defaults to <see cref="AssetRoots.AssetsFor"/>(<see cref="AssetKind.BTree"/>).
    /// </param>
    public BTreeNewAssetService(string? assetRootPath)
    {
        _emptyRecipe = new BTreeEditableAssetAdapter(MakeEmptyDto(), "");
        _assetRootPath = assetRootPath ?? AssetRoots.AssetsFor(AssetKind.BTree);
    }

    /// <inheritdoc />
    public AssetKind Kind => AssetKind.BTree;

    /// <inheritdoc />
    public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must not be empty.", nameof(name));

        BehaviorTreeAssetDto dto;

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
            var json = BTreeJsonServices.Serialize(recipeDto);
            dto = BTreeJsonServices.Deserialize(json)
                  ?? throw new InvalidOperationException("Recipe round-trip deserialization returned null.");
            dto.AssetId = Guid.NewGuid();
            dto.Name = name;
        }

        // Persist: write to <assetRootPath>/<relPath>/<name>.btree.json
        var filePath = AssetSavePath.Compose(Kind, relPath, name, _assetRootPath);

        var jsonOut = BTreeJsonServices.Serialize(dto);
        var prettyJson = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(jsonOut);
        AtomicFileWriter.Write(filePath, prettyJson);

        return new BTreeEditableAssetAdapter(dto, filePath);
    }

    /// <inheritdoc />
    public IReadOnlyList<IEditableAsset> AvailableRecipes()
    {
        // Synthetic "Empty" entry.
        var recipes = new List<IEditableAsset> { _emptyRecipe };

        // Discover on-disk BTree recipes (if any exist under Recipes/BTrees).
        // TODO: wire IRecipeDiscovery when a recipe-discovery service exists.
        // For now, only the in-code "Empty" recipe is offered.

        return recipes;
    }

    private static bool IsEmptyRecipe(IEditableAsset recipe)
        => string.Equals(recipe.Name, "Empty", StringComparison.OrdinalIgnoreCase);

    private static BehaviorTreeAssetDto ExtractDto(IEditableAsset recipe)
    {
        if (recipe is BTreeEditableAssetAdapter adapter && adapter.Dto != null)
            return adapter.Dto;

        throw new ArgumentException(
            $"Recipe must be a {nameof(BTreeEditableAssetAdapter)} with a non-null DTO.",
            nameof(recipe));
    }

    /// <summary>
    /// Synthesizes a minimal valid <see cref="BehaviorTreeAssetDto"/> in code —
    /// no disk read, no file I/O.
    /// </summary>
    internal static BehaviorTreeAssetDto MakeEmptyDto()
    {
        return new BehaviorTreeAssetDto
        {
            AssetId            = Guid.NewGuid(),
            Name               = "Empty",
            TargetNamespace    = "",
            BlackboardTypeName = "",
            ContextTypeName    = "",
            Canvas             = new CanvasDto { Zoom = 1.0f },
            Nodes              = new List<BTreeNodeDto>(),
            Pills              = new List<BTreePillDto>(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions       = new SuppressionsDto(),
            Blackboard         = new BlackboardBlockDto(),
        };
    }
}

/// <summary>
/// Thin <see cref="IEditableAsset"/> adapter for BTree DTOs.
/// Carries the DTO so callers can inspect recipe content and the written file path.
/// </summary>
public sealed class BTreeEditableAssetAdapter : IEditableAsset
{
    private readonly BehaviorTreeAssetDto? _dto;
    private readonly string _sourceFilePath;

    /// <summary>
    /// The underlying DTO. May be <see langword="null"/> when the adapter is
    /// constructed for an entity that has no DTO (e.g. a "from-disk" reference).
    /// </summary>
    public BehaviorTreeAssetDto? Dto => _dto;

    public BTreeEditableAssetAdapter(BehaviorTreeAssetDto? dto, string sourceFilePath)
    {
        _dto = dto;
        _sourceFilePath = sourceFilePath;
    }

    public Guid AssetId => _dto?.AssetId ?? Guid.Empty;
    public string Name => _dto?.Name ?? string.Empty;
    public AssetKind Kind => AssetKind.BTree;
    public string SourceFilePath => _sourceFilePath;
    public bool IsDirty => false;
    public bool IsEditorOwned => true;
    public event Action? Changed { add { } remove { } }
}
