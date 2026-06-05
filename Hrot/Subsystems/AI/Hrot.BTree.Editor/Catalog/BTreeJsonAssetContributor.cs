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
    /// Creates a new contributor, optionally wiring a debug session for symbolication.
    /// </summary>
    public BTreeJsonAssetContributor(BTreeDebugSession? debugSession = null)
    {
        _debugSession = debugSession;
    }

    /// <inheritdoc/>
    public AssetKind Kind => AssetKind.BTree;

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

        IEnumerable<string> paths;
        if (jsonPaths != null)
        {
            paths = jsonPaths;
        }
        else if (rootDirectory != null && Directory.Exists(rootDirectory))
        {
            paths = Directory.EnumerateFiles(rootDirectory, "*.btree.json",
                SearchOption.AllDirectories);
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
