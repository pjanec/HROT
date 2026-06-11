using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Browser;

namespace Hrot.Editor.AiShared.Recipes;

/// <summary>
/// Pure-logic model for the New Asset dialog (§18.2), separated from ImGui draw calls.
/// Injects a per-kind <see cref="INewAssetService"/> registry and reuses
/// <see cref="AssetBaseNameCollisionGuard"/> for base-name collision checking.
/// </summary>
/// <remarks>
/// <para>
/// <b>DEC-12 reconciliation:</b> <see cref="Confirm"/> calls
/// <see cref="INewAssetService.CreateNew"/> for all kinds, then performs an additional
/// file save only for <see cref="AssetKind.Blueprint"/> (which is mint-only). BTree,
/// HSM, and Scenario services already persist in their <c>CreateNew</c> — the dialog
/// does <b>not</b> double-write for those kinds.
/// </para>
/// <para>
/// <b>Testable seams:</b> <see cref="CanConfirm"/> is pure; <see cref="Confirm"/>
/// accepts optional overrides for the file-system list-directory delegate and the
/// Blueprint save action so that headless tests can run without real filesystem
/// access or Blueprint serialization.
/// </para>
/// </remarks>
public sealed class NewAssetDialog
{
    private readonly IReadOnlyDictionary<AssetKind, INewAssetService> _services;
    private readonly Func<string, IEnumerable<string>> _listFilesInDir;
    private readonly Action<IEditableAsset, string>? _saveMintOnlyAsset;
    private readonly string? _assetRootOverride;

    /// <summary>
    /// The kind of asset to create. Changing this should reset <see cref="Recipe"/>
    /// because recipe lists are per-kind (caller responsibility).
    /// </summary>
    public AssetKind Kind { get; set; }

    /// <summary>
    /// The selected recipe (including the in-code "Empty" entry from
    /// <see cref="INewAssetService.AvailableRecipes"/>). May be <see langword="null"/>
    /// before selection — <see cref="CanConfirm"/> requires a non-null value.
    /// </summary>
    public IEditableAsset? Recipe { get; set; }

    /// <summary>
    /// The display name for the new asset (without extension). Must not be empty.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The folder picker state tracking the selected target subfolder (relative to
    /// the kind's Assets root). Set <see cref="FolderPickerState.SelectedRelPath"/>
    /// before confirming. Never <see langword="null"/>.
    /// </summary>
    public FolderPickerState FolderPicker { get; }

    /// <summary>
    /// Creates the dialog model.
    /// </summary>
    /// <param name="services">
    /// Registry of per-kind <see cref="INewAssetService"/> implementations.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="knownFolderPaths">
    /// Initial known folder paths for the <see cref="FolderPickerState"/>.
    /// May be empty; <see langword="null"/> is treated as empty.
    /// </param>
    /// <param name="listFilesInDir">
    /// Delegate that returns full file paths for a given directory.
    /// In production, pass <c>Directory.EnumerateFiles</c>; inject a test double
    /// in headless tests. Defaults to <see cref="Directory.EnumerateFiles"/>.
    /// </param>
    /// <param name="saveMintOnlyAsset">
    /// Save delegate for mint-only kinds (Blueprint). Receives the new in-memory
    /// asset and the target absolute file path. In production, wires to the
    /// Blueprint JSON save; in tests, a recording fake.
    /// When <see langword="null"/> and <see cref="Kind"/> is
    /// <see cref="AssetKind.Blueprint"/>, the save step is skipped (caller must
    /// handle persistence separately or accept a mint-only result).
    /// </param>
    /// <param name="assetRootOverride">
    /// Optional absolute path to use in place of <see cref="AssetRoots.AssetsFor"/>
    /// when composing save paths (for headless tests with temp roots).
    /// Pass <see langword="null"/> to use the production default.
    /// </param>
    public NewAssetDialog(
        IReadOnlyDictionary<AssetKind, INewAssetService> services,
        IEnumerable<string>?                              knownFolderPaths = null,
        Func<string, IEnumerable<string>>?                listFilesInDir  = null,
        Action<IEditableAsset, string>?                   saveMintOnlyAsset = null,
        string?                                           assetRootOverride = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _listFilesInDir = listFilesInDir ?? Directory.EnumerateFiles;
        _saveMintOnlyAsset = saveMintOnlyAsset;
        _assetRootOverride = assetRootOverride;
        FolderPicker = new FolderPickerState(knownFolderPaths ?? Array.Empty<string>());
    }

    // ── Testable seams ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the dialog can be confirmed:
    /// a non-empty <see cref="Name"/>, a selected <see cref="Recipe"/>, and
    /// a registered <see cref="INewAssetService"/> for the current
    /// <see cref="Kind"/>.
    /// </summary>
    public bool CanConfirm()
        => !string.IsNullOrWhiteSpace(Name)
        && Recipe != null
        && _services.ContainsKey(Kind);

    /// <summary>
    /// Validates, collision-checks, creates the new asset, saves (if needed per
    /// DEC-12), and invokes <paramref name="onCreated"/> with the result.
    /// </summary>
    /// <param name="onCreated">
    /// Callback that receives the newly minted asset on success.
    /// May be <see langword="null"/> — the result is still returned.
    /// </param>
    /// <returns>A <see cref="ConfirmResult"/> indicating success or failure.</returns>
    public ConfirmResult Confirm(Action<IEditableAsset>? onCreated = null)
    {
        // 1. Validate.
        if (!CanConfirm())
            return ConfirmResult.Fail("Name, Recipe, and Kind must be set.");

        if (!_services.TryGetValue(Kind, out var service))
            return ConfirmResult.Fail($"No service registered for {Kind}.");

        // 2. Collision-check for file-based kinds.
        string? computedSavePath = null;
        if (Kind is AssetKind.Blueprint or AssetKind.BTree or AssetKind.Hsm)
        {
            computedSavePath = AssetSavePath.Compose(
                Kind, FolderPicker.SelectedRelPath, Name, _assetRootOverride);

            var dir = Path.GetDirectoryName(computedSavePath);
            if (dir != null)
            {
                // D5 collision guard (CS ↔ JSON base-name collision).
                var collision = AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
                    computedSavePath, _listFilesInDir);
                if (collision != null)
                    return ConfirmResult.Fail(collision);

                // Direct file-name collision (same exact path).
                if (File.Exists(computedSavePath))
                    return ConfirmResult.Fail(
                        $"An asset named '{Name}' already exists at '{computedSavePath}'.");
            }
        }

        // 3. CreateNew via service (mints fresh AssetId; BTree/HSM/Scenario
        //    persist within).
        IEditableAsset newAsset;
        try
        {
            newAsset = service.CreateNew(Recipe, Name, FolderPicker.SelectedRelPath);
        }
        catch (Exception ex)
        {
            return ConfirmResult.Fail($"Failed to create asset: {ex.Message}");
        }

        // 4. DEC-12: Blueprint is mint-only — dialog performs the subfolder-aware
        //    save from T7. BTree/HSM/Scenario already persisted in CreateNew.
        if (Kind == AssetKind.Blueprint && computedSavePath != null)
        {
            if (_saveMintOnlyAsset != null)
            {
                var dir = Path.GetDirectoryName(computedSavePath);
                if (dir != null)
                    Directory.CreateDirectory(dir);
                _saveMintOnlyAsset(newAsset, computedSavePath);
            }
        }

        // 5. Callback.
        onCreated?.Invoke(newAsset);

        return ConfirmResult.Success(newAsset);
    }

    /// <summary>
    /// Returns the available recipes for <paramref name="kind"/> from the
    /// registered service, or an empty list when no service is registered
    /// for that kind.
    /// </summary>
    public IReadOnlyList<IEditableAsset> RecipesForKind(AssetKind kind)
    {
        if (_services.TryGetValue(kind, out var svc))
            return svc.AvailableRecipes();
        return Array.Empty<IEditableAsset>();
    }
}

/// <summary>
/// Discriminated result of <see cref="NewAssetDialog.Confirm"/>.
/// </summary>
public sealed class ConfirmResult
{
    /// <summary>
    /// <see langword="true"/> when the asset was created and persisted successfully.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// The newly minted asset, or <see langword="null"/> on failure.
    /// </summary>
    public IEditableAsset? Asset { get; }

    /// <summary>
    /// The human-readable error message, or <see langword="null"/> on success.
    /// </summary>
    public string? Error { get; }

    private ConfirmResult(bool isSuccess, IEditableAsset? asset, string? error)
    {
        IsSuccess = isSuccess;
        Asset = asset;
        Error = error;
    }

    /// <summary>Creates a success result carrying the new asset.</summary>
    public static ConfirmResult Success(IEditableAsset asset)
        => new(true, asset ?? throw new ArgumentNullException(nameof(asset)), null);

    /// <summary>Creates a failure result carrying an error message.</summary>
    public static ConfirmResult Fail(string error)
        => new(false, null, error ?? throw new ArgumentNullException(nameof(error)));
}
