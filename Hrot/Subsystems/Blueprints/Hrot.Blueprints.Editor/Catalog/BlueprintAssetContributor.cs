using System.Text.Json;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Identity;
using Hrot.Editor.AiShared.References;

namespace Hrot.Blueprints.Editor.Catalog;

/// <summary>
/// Implements <see cref="IAssetCatalogContributor"/> for <see cref="AssetKind.Blueprint"/>.
/// Enumerates <c>*.bp.json</c> files under a root directory, extracting only the header
/// fields (<c>AssetId</c>, <c>Name</c>) without deserializing the full asset (lazy/header-only).
/// Fires <see cref="ContributorChanged"/> on every <see cref="Refresh"/> call.
/// </summary>
public sealed class BlueprintAssetContributor : IAssetCatalogContributor
{
    private readonly string _rootDirectory;
    private List<IEditableAsset> _assets = new();

    /// <param name="rootDirectory">Root directory to scan for <c>*.bp.json</c> files.</param>
    public BlueprintAssetContributor(string rootDirectory)
    {
        _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
    }

    /// <inheritdoc/>
    public AssetKind Kind => AssetKind.Blueprint;

    /// <inheritdoc/>
    public string? BaseFolder => AssetRoots.AssetsFor(Kind);

    /// <inheritdoc/>
    public event Action? ContributorChanged;

    /// <inheritdoc/>
    public IReadOnlyList<IEditableAsset> Enumerate() => _assets;

    /// <summary>
    /// Rescans the root directory for <c>*.bp.json</c> files and fires
    /// <see cref="ContributorChanged"/>. Call this on editor init and on each hot reload.
    /// </summary>
    public void Refresh()
    {
        var found = new List<IEditableAsset>();

        if (Directory.Exists(_rootDirectory))
        {
            // Case-insensitive match: *.bp.json files may have drifted extension casing
            // when authored on Windows; PlatformDefault would silently miss them on Linux.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MatchCasing           = MatchCasing.CaseInsensitive,
            };

            foreach (var filePath in Directory.EnumerateFiles(
                _rootDirectory, "*.bp.json", options))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Read AssetId — skip file if absent or invalid.
                    if (!root.TryGetProperty("AssetId", out var idEl) ||
                        !idEl.TryGetGuid(out var assetId))
                        continue;

                    // Read Name — fall back to filename stem.
                    var name = string.Empty;
                    if (root.TryGetProperty("Name", out var nameEl) &&
                        nameEl.ValueKind == JsonValueKind.String)
                        name = nameEl.GetString() ?? string.Empty;

                    if (string.IsNullOrEmpty(name))
                        name = Path.GetFileNameWithoutExtension(
                            Path.GetFileNameWithoutExtension(filePath)); // strip both ".bp" and ".json"

                    // Punch-list #9: header-only Dispatch + Primitive.Intent → per-intent picker icon.
                    string? dispatch = root.TryGetProperty("Dispatch", out var dEl) && dEl.ValueKind == JsonValueKind.String
                        ? dEl.GetString() : null;
                    string? intent = root.TryGetProperty("Primitive", out var pEl)
                        && pEl.ValueKind == JsonValueKind.Object
                        && pEl.TryGetProperty("Intent", out var iEl) && iEl.ValueKind == JsonValueKind.String
                        ? iEl.GetString() : null;
                    var iconKey = BlueprintIconKeys.ForHeader(dispatch, intent);

                    // BP-85: the same header Dispatch also answers "is this an Instance blueprint?"
                    // in the canvas breadcrumb — previously nothing on screen did.
                    found.Add(new BlueprintFileAsset(assetId, name, filePath, iconKey, dispatch));
                }
                catch
                {
                    // Skip unreadable or malformed files — never throw.
                    continue;
                }
            }
        }

        _assets = found;
        ContributorChanged?.Invoke();
    }
}

/// <summary>
/// Lightweight <see cref="IEditableAsset"/> that represents a Blueprint file asset.
/// Only the header is read on construction; the full <see cref="Hrot.Blueprints.Core.Assets.BlueprintAsset"/>
/// is loaded on demand by the editor host when the document is opened.
/// </summary>
internal sealed class BlueprintFileAsset
    : IEditableAsset, IComposedBlueprintIdentity, IAssetIconKeyProvider, IAssetSubtitleProvider
{
    private bool _isDirty;

    public BlueprintFileAsset(
        Guid assetId, string name, string sourceFilePath, string? iconKey = null, string? dispatch = null)
    {
        AssetId = assetId;
        Name = name;
        SourceFilePath = sourceFilePath;
        IconKey = iconKey;
        Subtitle = string.IsNullOrWhiteSpace(dispatch) ? null : dispatch;

        // Phase C (AIE-053): precompute the generated AiPrimitive class name HERE, on the
        // blueprint-editor side which legitimately references the compiler. The shared
        // ComposedBlueprintResolver then matches composed BTree node FQNs against this string
        // WITHOUT any hash recomputation — keeping the foundational shared layer Roslyn-free.
        // Mirrors AiPrimitiveEmitter's class name: "{SanitizedName}_{BlueprintId:X8}_Bp".
        GeneratedClassName = $"{Sanitizer.SanitizeName(name)}_{BlueprintIdHash.Compute(assetId):X8}_Bp";
    }

    public Guid AssetId { get; }
    public string Name { get; }
    public AssetKind Kind => AssetKind.Blueprint;
    public string SourceFilePath { get; }

    /// <inheritdoc/>
    public string GeneratedClassName { get; }

    /// <inheritdoc/>
    public string? IconKey { get; }

    /// <inheritdoc/>
    /// <remarks>BP-85: the asset's dispatch ("Instance" / "Library" / "AiPrimitive").</remarks>
    public string? Subtitle { get; }

    public bool IsDirty => _isDirty;
    public bool IsEditorOwned => false;

    /// <summary>Marks this asset dirty (called after an in-memory edit).</summary>
    public void MarkDirty() { _isDirty = true; Changed?.Invoke(); }

    /// <summary>Clears the dirty flag (called after a successful save).</summary>
    public void MarkClean() { _isDirty = false; Changed?.Invoke(); }

    public event Action? Changed;
}
