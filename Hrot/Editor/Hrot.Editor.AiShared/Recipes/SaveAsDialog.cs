using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Browser;

namespace Hrot.Editor.AiShared.Recipes;

/// <summary>
/// Pure-logic model for the Save-As dialog (§18.2, §18.5), separated from ImGui
/// draw calls. Takes the <b>current document's asset</b> as the source/recipe,
/// mints a <b>fresh <see cref="IEditableAsset.AssetId"/></b> on every confirm
/// (duplicate semantics — §18.5), and writes the new asset under the picked
/// subfolder.
/// </summary>
/// <remarks>
/// <para>
/// <b>Key differences from <see cref="NewAssetDialog"/>:</b>
/// <list type="bullet">
///   <item>No <c>Kind</c> selector — the kind is fixed to the source asset's
///       <see cref="IEditableAsset.Kind"/>.</item>
///   <item>No <c>Recipe</c> selector — the source asset IS the recipe passed to
///       <see cref="INewAssetService.CreateNew"/>.</item>
///   <item><b>Scenario Save-As</b> routes directly to a
///       <c>saveScenarioAs</c> delegate (→ <c>IEditorLogic.SaveScenarioAs</c>)
///       rather than through <see cref="INewAssetService.CreateNew"/>, because
///       the scenario is already loaded and does not need reloading.</item>
/// </list>
/// </para>
/// <para>
/// <b>DEC-12 reconciliation:</b> same per-kind save policy as
/// <see cref="NewAssetDialog"/> — Blueprint is mint-only (dialog performs the
/// file save); BTree/HSM persist in <c>CreateNew</c>; Scenario is a dedicated
/// path.
/// </para>
/// <para>
/// <b>Testable seams:</b> <see cref="CanConfirm"/> is pure; <see cref="Confirm"/>
/// accepts optional overrides for the file-system list-directory delegate, the
/// Blueprint save action, and the Scenario save action so that headless tests
/// can run without real filesystem access or editor backends.
/// </para>
/// </remarks>
public sealed class SaveAsDialog
{
    private readonly IEditableAsset _sourceAsset;
    private readonly IReadOnlyDictionary<AssetKind, INewAssetService> _services;
    private readonly Func<string, IEnumerable<string>> _listFilesInDir;
    private readonly Action<IEditableAsset, string>? _saveMintOnlyAsset;
    private readonly Action<string>? _saveScenarioAs;
    private readonly string? _assetRootOverride;

    /// <summary>
    /// The kind of the source asset (fixed — not settable).
    /// </summary>
    public AssetKind Kind => _sourceAsset.Kind;

    /// <summary>
    /// The source asset being saved under a new identity.
    /// </summary>
    public IEditableAsset SourceAsset => _sourceAsset;

    /// <summary>
    /// The display name for the new asset (without extension).
    /// Defaults to the source asset's <see cref="IEditableAsset.Name"/>.
    /// Must not be empty for <see cref="CanConfirm"/> to return <see langword="true"/>.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The folder picker state tracking the selected target subfolder (relative to
    /// the kind's Assets root). Set <see cref="FolderPickerState.SelectedRelPath"/>
    /// before confirming. Never <see langword="null"/>.
    /// </summary>
    public FolderPickerState FolderPicker { get; }

    /// <summary>
    /// Creates the Save-As dialog model.
    /// </summary>
    /// <param name="sourceAsset">
    /// The current document's asset (never <see langword="null"/>). Its content
    /// is the source for the save-as clone.
    /// </param>
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
    /// <see cref="AssetKind.Blueprint"/>, the save step is skipped.
    /// </param>
    /// <param name="saveScenarioAs">
    /// Save delegate for Scenario Save-As. Receives the full scenario name
    /// (<c>relPath/name</c>). In production, delegates to
    /// <c>IEditorLogic.SaveScenarioAs</c>. When <see langword="null"/> and
    /// <see cref="Kind"/> is <see cref="AssetKind.Scenario"/>,
    /// <see cref="Confirm"/> returns a failure.
    /// </param>
    /// <param name="assetRootOverride">
    /// Optional absolute path to use in place of <see cref="AssetRoots.AssetsFor"/>
    /// when composing save paths (for headless tests with temp roots).
    /// Pass <see langword="null"/> to use the production default.
    /// </param>
    public SaveAsDialog(
        IEditableAsset                                 sourceAsset,
        IReadOnlyDictionary<AssetKind, INewAssetService> services,
        IEnumerable<string>?                            knownFolderPaths = null,
        Func<string, IEnumerable<string>>?              listFilesInDir   = null,
        Action<IEditableAsset, string>?                 saveMintOnlyAsset = null,
        Action<string>?                                 saveScenarioAs    = null,
        string?                                         assetRootOverride = null)
    {
        _sourceAsset = sourceAsset ?? throw new ArgumentNullException(nameof(sourceAsset));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _listFilesInDir = listFilesInDir ?? Directory.EnumerateFiles;
        _saveMintOnlyAsset = saveMintOnlyAsset;
        _saveScenarioAs = saveScenarioAs;
        _assetRootOverride = assetRootOverride;
        FolderPicker = new FolderPickerState(knownFolderPaths ?? Array.Empty<string>());

        // Seed the name from the source asset.
        Name = sourceAsset.Name;
    }

    // ── Testable seams ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the dialog can be confirmed:
    /// a non-empty <see cref="Name"/> and a registered
    /// <see cref="INewAssetService"/> for the <see cref="Kind"/>.
    /// </summary>
    public bool CanConfirm()
        => !string.IsNullOrWhiteSpace(Name)
        && _services.ContainsKey(Kind);

    /// <summary>
    /// Validates, collision-checks, creates the new asset with a
    /// <b>fresh <see cref="IEditableAsset.AssetId"/></b> (duplicate semantics
    /// per §18.5), saves (if needed per DEC-12), and invokes
    /// <paramref name="onCreated"/> with the result.
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
            return ConfirmResult.Fail(
                $"Name must be set and a service must be registered for {Kind}.");

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

        IEditableAsset newAsset;

        // 3. Scenario Save-As: route directly to saveScenarioAs delegate.
        //    (The scenario is already loaded; we just save under a new name.)
        if (Kind == AssetKind.Scenario)
        {
            if (_saveScenarioAs == null)
                return ConfirmResult.Fail(
                    "No scenario save delegate available for Save-As.");

            var fullScenarioName = string.IsNullOrEmpty(FolderPicker.SelectedRelPath)
                ? Name
                : FolderPicker.SelectedRelPath + "/" + Name;

            try
            {
                _saveScenarioAs(fullScenarioName);
            }
            catch (Exception ex)
            {
                return ConfirmResult.Fail($"Failed to save scenario: {ex.Message}");
            }

            // Mint a fresh handle — Scenario assets have no file path in
            // the Assets tree; identity is minted here.
            newAsset = new SaveAsAssetResult(Guid.NewGuid(), Name, AssetKind.Scenario);
        }
        else
        {
            // 4. File-based kinds: reuse INewAssetService.CreateNew with the
            //    source asset as the recipe — mints a fresh AssetId (§18.5).
            try
            {
                newAsset = service.CreateNew(_sourceAsset, Name, FolderPicker.SelectedRelPath);
            }
            catch (Exception ex)
            {
                return ConfirmResult.Fail($"Failed to create asset: {ex.Message}");
            }

            // 5. DEC-12: Blueprint is mint-only — dialog performs the
            //    subfolder-aware save. BTree/HSM already persisted in CreateNew.
            if (Kind == AssetKind.Blueprint && computedSavePath != null)
            {
                if (_saveMintOnlyAsset != null)
                {
                    var dirPath = Path.GetDirectoryName(computedSavePath);
                    if (dirPath != null)
                        Directory.CreateDirectory(dirPath);
                    _saveMintOnlyAsset(newAsset, computedSavePath);
                }
            }
        }

        // 6. Callback.
        onCreated?.Invoke(newAsset);

        return ConfirmResult.Success(newAsset);
    }

    // ── Internal adapter for Scenario Save-As result ────────────────────────

    /// <summary>
    /// Lightweight <see cref="IEditableAsset"/> adapter for Save-As results
    /// when no full asset DTO is needed (e.g. Scenario Save-As).
    /// </summary>
    internal sealed class SaveAsAssetResult : IEditableAsset
    {
        public SaveAsAssetResult(Guid assetId, string name, AssetKind kind)
        {
            AssetId = assetId;
            Name = name;
            Kind = kind;
        }

        public Guid AssetId { get; }
        public string Name { get; }
        public AssetKind Kind { get; }
        public string SourceFilePath => "";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }
}
