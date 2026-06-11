using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Recipes;

namespace Hrot.Editor;

/// <summary>
/// Scenario implementation of <see cref="INewAssetService"/>.
/// Routes scenario creation to <see cref="IScenarioCreationSession"/>
/// (<see cref="IEditorLogic"/> in production). No file I/O — scenarios
/// are saved by the editor backend.
/// </summary>
/// <remarks>
/// Design §18.3 / §19:
/// <b>Empty</b> → <c>NewScenario()</c> then <c>SaveScenarioAs(relPath/name)</c>.
/// <b>FromSeed</b> → load the seed scenario then <c>SaveScenarioAs</c> under the new name.
/// Uses <see cref="IScenarioCreationSession"/> as a narrow testable seam.
/// </remarks>
public sealed class ScenarioNewAssetService : INewAssetService
{
    private readonly IScenarioCreationSession _session;
    private readonly IEditableAsset _emptyRecipe;
    private readonly List<IEditableAsset> _seedRecipes;

    public ScenarioNewAssetService(IScenarioCreationSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _emptyRecipe = new ScenarioEditableAssetAdapter(
            Guid.Empty, "Empty", string.Empty, isRecipe: true);
        _seedRecipes = new List<IEditableAsset>();
    }

    /// <summary>
    /// Production constructor that discovers seed scenarios from the Recipes/Scenarios
    /// directory and wraps them as recipe entries.
    /// </summary>
    public ScenarioNewAssetService(
        IScenarioCreationSession session,
        IEnumerable<string> seedScenarioNames)
        : this(session)
    {
        foreach (var name in seedScenarioNames)
        {
            _seedRecipes.Add(new ScenarioEditableAssetAdapter(
                Guid.Empty, name, string.Empty, isRecipe: true));
        }
    }

    /// <inheritdoc />
    public AssetKind Kind => AssetKind.Scenario;

    /// <inheritdoc />
    public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must not be empty.", nameof(name));

        // Build the full scenario name: <relPath>/<name> (or just <name> if relPath is empty).
        var fullName = string.IsNullOrEmpty(relPath) ? name : relPath + "/" + name;

        if (recipe == null || IsEmptyRecipe(recipe))
        {
            // Empty path: create a new empty world, then save under the new name.
            _session.NewScenario();
            _session.SaveScenarioAs(fullName);
        }
        else
        {
            // FromSeed: load the seed scenario by its name, then save as new name.
            // The recipe's Name identifies which seed scenario to load.
            _session.LoadScenarioByName(recipe.Name);
            _session.SaveScenarioAs(fullName);
        }

        // Scenario assets don't have a file path in the Assets tree — they live
        // in the scenarios root (managed by IEditorLogic/NAS).
        return new ScenarioEditableAssetAdapter(
            Guid.NewGuid(), name, string.Empty, isRecipe: false);
    }

    /// <inheritdoc />
    public IReadOnlyList<IEditableAsset> AvailableRecipes()
    {
        var all = new List<IEditableAsset> { _emptyRecipe };
        all.AddRange(_seedRecipes);
        return all;
    }

    private static bool IsEmptyRecipe(IEditableAsset recipe)
        => string.Equals(recipe.Name, "Empty", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Thin <see cref="IEditableAsset"/> adapter for scenario assets and recipes.
/// Scenarios have no DTO — they are saved/loaded by the editor backend.
/// </summary>
public sealed class ScenarioEditableAssetAdapter : IEditableAsset
{
    private readonly Guid _assetId;
    private readonly string _name;
    private readonly string _sourceFilePath;
    private readonly bool _isRecipe;

    public ScenarioEditableAssetAdapter(
        Guid assetId, string name, string sourceFilePath, bool isRecipe)
    {
        _assetId = assetId;
        _name = name;
        _sourceFilePath = sourceFilePath;
        _isRecipe = isRecipe;
    }

    public Guid AssetId => _assetId;
    public string Name => _name;
    public AssetKind Kind => AssetKind.Scenario;
    public string SourceFilePath => _sourceFilePath;
    public bool IsDirty => false;
    public bool IsEditorOwned => !_isRecipe; // recipes are not editor-owned documents
    public event Action? Changed { add { } remove { } }
}
