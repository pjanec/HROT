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
    private readonly IEditableAsset _starterRecipe;
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
        _starterRecipe = new HsmEditableAssetAdapter(MakeStarterDto(), "");
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
        var filePath = AssetSavePath.Compose(Kind, relPath, name, _assetRootPath);

        var jsonOut = HsmJsonServices.Serialize(dto);
        var prettyJson = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(jsonOut);
        AtomicFileWriter.Write(filePath, prettyJson);

        return new HsmEditableAssetAdapter(dto, filePath);
    }

    /// <inheritdoc />
    public IReadOnlyList<IEditableAsset> AvailableRecipes()
    {
        // Synthetic "Empty" and "Starter" entries.
        var recipes = new List<IEditableAsset> { _emptyRecipe, _starterRecipe };

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

    /// <summary>
    /// Synthesizes a "Starter" <see cref="HsmAssetDto"/> — a minimal
    /// valid machine with a root composite and one Simple state flagged
    /// IsInitial, so a new-from-Starter HSM opens with a root + initial
    /// child ready to build under (not a blank canvas).
    /// Mirrors <see cref="Hrot.BTree.Editor.BTreeNewAssetService.MakeStarterDto"/>.
    /// </summary>
    internal static HsmAssetDto MakeStarterDto()
    {
        var rootId  = new Guid("5e010000-0000-0000-0000-000000000001");
        var initId  = new Guid("5e020000-0000-0000-0000-000000000001");
        var regionId = new Guid("5e100000-0000-0000-0000-000000000001");

        return new HsmAssetDto
        {
            AssetId            = Guid.NewGuid(),
            Name               = "Starter",
            TargetNamespace    = "",
            BlackboardTypeName = "",
            Canvas             = new HsmCanvasDto { Zoom = 1.0f },
            States = new List<StateNodeDto>
            {
                new StateNodeDto
                {
                    StableId       = rootId,
                    Name           = "__Root",
                    ChildStableIds = new List<Guid> { initId },
                    ParentStableId = null,
                    IsInitial      = false,
                    IsParallel     = false,
                    IsFinal        = false,
                    RegionIndex    = 0,
                    X              = 0,
                    Y              = 0,
                },
                new StateNodeDto
                {
                    StableId       = initId,
                    Name           = "InitState",
                    ChildStableIds = new List<Guid>(),
                    ParentStableId = rootId,
                    IsInitial      = true,
                    IsParallel     = false,
                    IsFinal        = false,
                    RegionIndex    = 0,
                    X              = 100,
                    Y              = 100,
                },
            },
            Regions = new List<RegionNodeDto>
            {
                new RegionNodeDto
                {
                    StableId             = regionId,
                    RegionIndex          = 0,
                    Name                 = "Region0",
                    Priority             = 0,
                    InitialChildStableId = rootId,
                },
            },
            Transitions       = new List<TransitionNodeDto>(),
            GlobalTransitions = new List<GlobalTransitionNodeDto>(),
            Events            = new List<EventDefinitionDto>(),
            Suppressions      = new HsmSuppressionsDto(),
            Blackboard        = new HsmBlackboardBlockDto(),
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
