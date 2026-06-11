using System;
using System.Collections.Generic;
using System.IO;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Hsm.Editor.Persistence;

namespace Hrot.Hsm.Editor.Catalog;

/// <summary>
/// File-based <see cref="IAssetCatalogContributor"/> for HSM assets stored as
/// <c>*.hsm.json</c> files on disk.
/// <para>
/// Design §3 D4 / PU-301: implements the JSON half of the dual-load strategy (HSM side).
/// Header-lazy Discover reads only AssetId+Name via <see cref="HsmJsonServices.ReadHeader"/>
/// (skips malformed files, never throws); lazy LoadFull deserializes the full DTO and maps
/// it to a <see cref="Hrot.Hsm.Editor.Model.HsmAsset"/> with <c>IsEditorOwned=true</c>
/// and <c>SourceFilePath</c> pointing at the <c>.hsm.json</c> file.
/// </para>
/// <para>
/// On AssetId collision with the assembly contributor, the JSON contributor wins because
/// it is added after the assembly contributor in the catalog.  No <c>.hsm.json</c> files
/// exist yet (migration is PU-401); contributor is dormant in the live editor but fully
/// exercised by synthesized JSON in tests.
/// </para>
/// </summary>
public sealed class HsmJsonAssetContributor : IAssetCatalogContributor
{
    private readonly record struct HeaderEntry(string FilePath, Guid AssetId, string Name);

    private readonly List<HeaderEntry> _headers = new();
    private readonly List<IEditableAsset> _assets = new();

    /// <inheritdoc/>
    public AssetKind Kind => AssetKind.Hsm;

    /// <inheritdoc/>
    public string? BaseFolder => AssetRoots.AssetsFor(Kind);

    /// <inheritdoc/>
    public event Action? ContributorChanged;

    /// <inheritdoc/>
    public IReadOnlyList<IEditableAsset> Enumerate() => _assets;

    /// <summary>
    /// Discovers all <c>*.hsm.json</c> files under the given paths or root directory,
    /// reading only their headers (AssetId + Name).  Malformed files are silently skipped.
    /// </summary>
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
            paths = Directory.EnumerateFiles(rootDirectory, "*.hsm.json",
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

            var header = HsmJsonServices.ReadHeader(json);
            if (header.HasValue)
                _headers.Add(new HeaderEntry(filePath, header.Value.AssetId, header.Value.Name));
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

    private Hrot.Hsm.Editor.Model.HsmAsset? LoadFull(string filePath)
    {
        string json;
        try { json = File.ReadAllText(filePath); }
        catch { return null; }

        HsmAssetDto? dto;
        try { dto = HsmJsonServices.Deserialize(json); }
        catch { return null; }
        if (dto is null) return null;

        return HsmAssetMapper.ToModel(
            dto,
            sourceFilePath: filePath,
            isEditorOwned:  true);
    }
}
