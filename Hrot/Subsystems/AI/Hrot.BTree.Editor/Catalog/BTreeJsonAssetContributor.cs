using System;
using System.Collections.Generic;
using System.IO;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.BTree.Editor.Catalog;

/// <summary>
/// File-based <see cref="IAssetCatalogContributor"/> for BTree assets stored as
/// <c>*.btree.json</c> files on disk.
/// <para>
/// Design §3 D4 / PU-301: implements the JSON half of the dual-load strategy.
/// Header-lazy Discover reads only AssetId+Name via <see cref="BTreeJsonServices.ReadHeader"/>
/// (skips malformed files, never throws); lazy <see cref="LoadFull"/> deserializes the
/// full DTO and maps it to a <see cref="Hrot.BTree.Editor.Model.BehaviorTreeAsset"/>
/// with <c>IsEditorOwned=true</c> and <c>SourceFilePath</c> pointing at the
/// <c>.btree.json</c> file.
/// </para>
/// <para>
/// On AssetId collision with the assembly contributor, the JSON contributor wins:
/// wiring in <see cref="Hrot.Editor.AiShared.Catalog.AssetCatalog"/> ensures
/// JSON-loaded assets supersede assembly-projected ones because the JSON contributor
/// is added last and its entries overwrite by AssetId.
/// </para>
/// <para>
/// No <c>.btree.json</c> files exist under <c>Hrot.AI.Behaviors</c> yet (migration
/// is PU-401).  The contributor is dormant in the live editor (discovers zero files)
/// but fully exercised by tests using synthesized JSON.
/// </para>
/// </summary>
public sealed class BTreeJsonAssetContributor : IAssetCatalogContributor
{
    // Header info cached from Discover; full assets loaded lazily on first Enumerate.
    private readonly record struct HeaderEntry(string FilePath, Guid AssetId, string Name);

    private readonly List<HeaderEntry> _headers = new();
    private readonly List<IEditableAsset> _assets = new();
    private readonly BTreeDebugSession? _debugSession;

    /// <summary>
    /// ⭐ The root <see cref="Discover"/> was last given, so <see cref="BaseFolder"/> can name the
    /// folder this contributor actually scans instead of re-deriving a different one (F2).
    /// <see langword="null"/> until a <c>rootDirectory</c> is supplied.
    /// </summary>
    private string? _scannedRoot;

    /// <summary>
    /// Creates a new contributor, optionally wiring a debug session for symbolication.
    /// </summary>
    public BTreeJsonAssetContributor(BTreeDebugSession? debugSession = null)
    {
        _debugSession = debugSession;
    }

    /// <inheritdoc/>
    public AssetKind Kind => AssetKind.BTree;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ <b>The root this contributor was last told to scan</b> (<see cref="Discover"/>'s
    /// <c>rootDirectory</c>) — ⛔ <b>not</b> <c>AssetRoots.AssetsFor(Kind)</c>.
    /// 📄 See <c>BlueprintAssetContributor.BaseFolder</c> for the measurement (review round 4, F2):
    /// <c>AssetsFor</c> omits the source walk-up that <c>ResolveAssetsRoot</c> applies, so the two
    /// disagree on any host with a source tree and no configured root.
    ///
    /// <para>⚠ <b>The fallback is deliberate and it is honest.</b> This contributor can also be driven
    /// by an explicit <c>jsonPaths</c> list with no root at all (the test arm), and a set of loose
    /// paths has no single base folder. ⇒ until a <c>rootDirectory</c> is supplied, the property keeps
    /// answering <c>AssetsFor(Kind)</c>, exactly as before.</para>
    /// </remarks>
    public string? BaseFolder => _scannedRoot ?? AssetRoots.AssetsFor(Kind);

    /// <inheritdoc/>
    public event Action? ContributorChanged;

    /// <inheritdoc/>
    public IReadOnlyList<IEditableAsset> Enumerate() => _assets;

    /// <summary>
    /// Discovers all <c>*.btree.json</c> files under <paramref name="rootDirectory"/>,
    /// reading only their headers (AssetId + Name).  Malformed files are silently skipped.
    /// After discovery <see cref="LoadAll"/> must be called (or the assets refreshed
    /// via <see cref="Refresh"/>) to populate the asset list exposed by
    /// <see cref="Enumerate"/>.
    /// </summary>
    /// <param name="jsonPaths">
    ///   Explicit list of <c>*.btree.json</c> file paths to discover.
    ///   When null, falls back to <paramref name="rootDirectory"/> enumeration.
    /// </param>
    /// <param name="rootDirectory">
    ///   Root folder to scan when <paramref name="jsonPaths"/> is null.
    /// </param>
    public void Discover(IEnumerable<string>? jsonPaths = null, string? rootDirectory = null)
    {
        _headers.Clear();

        // ⭐ F2: remember the root we were NAMED, not the one AssetsFor would re-derive. Captured even
        //    when the directory does not exist yet — the caller's intent is still "this is my root",
        //    and answering a DIFFERENT existing folder is what the defect was.
        if (rootDirectory != null)
            _scannedRoot = rootDirectory;

        IEnumerable<string> paths;
        if (jsonPaths != null)
        {
            paths = jsonPaths;
        }
        else if (rootDirectory != null && Directory.Exists(rootDirectory))
        {
            // Case-insensitive match: *.btree.json files may have drifted extension casing
            // when authored on Windows; PlatformDefault would silently miss them on Linux.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MatchCasing           = MatchCasing.CaseInsensitive,
            };
            paths = Directory.EnumerateFiles(rootDirectory, "*.btree.json", options);
        }
        else
        {
            paths = Array.Empty<string>();
        }

        foreach (var filePath in paths)
        {
            string? json = null;
            try { json = File.ReadAllText(filePath); }
            catch { continue; }

            var header = BTreeJsonServices.ReadHeader(json);
            if (header.HasValue)
                _headers.Add(new HeaderEntry(filePath, header.Value.AssetId, header.Value.Name));
            // malformed files are silently skipped (no throw)
        }
    }

    /// <summary>
    /// Full refresh: re-discover from the given paths/root and reload all assets.
    /// Fires <see cref="ContributorChanged"/> when done.
    /// </summary>
    public void Refresh(IEnumerable<string>? jsonPaths = null, string? rootDirectory = null)
    {
        Discover(jsonPaths, rootDirectory);
        LoadAll();
        ContributorChanged?.Invoke();
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Loads (or re-loads) all previously discovered headers into the asset list.
    /// Skips files that cannot be deserialized.
    /// </summary>
    private void LoadAll()
    {
        _assets.Clear();
        foreach (var h in _headers)
        {
            var asset = LoadFull(h.FilePath);
            if (asset != null)
                _assets.Add(asset);
        }
    }

    /// <summary>
    /// Lazily loads the full asset for a single file path.
    /// Returns null when the file cannot be read or deserialized.
    /// </summary>
    private Hrot.BTree.Editor.Model.BehaviorTreeAsset? LoadFull(string filePath)
    {
        string json;
        try { json = File.ReadAllText(filePath); }
        catch { return null; }

        BehaviorTreeAssetDto? dto;
        try { dto = BTreeJsonServices.Deserialize(json); }
        catch { return null; }
        if (dto is null) return null;

        var asset = BehaviorTreeAssetMapper.ToModel(
            dto,
            sourceFilePath: filePath,
            isEditorOwned:  true);

        // Store the debug session on the asset so StitchKernelIndices (PU-302) can
        // re-wire symbolication without external injection at stitch time.
        asset.SetDebugSession(_debugSession);

        return asset;
    }
}
