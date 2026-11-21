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

    /// <summary>
    /// ⭐ The root <see cref="Discover"/> was last given, so <see cref="BaseFolder"/> can name the
    /// folder this contributor actually scans instead of re-deriving a different one (F2).
    /// <see langword="null"/> until a <c>rootDirectory</c> is supplied.
    /// </summary>
    private string? _scannedRoot;

    /// <inheritdoc/>
    public AssetKind Kind => AssetKind.Hsm;

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
    /// Discovers all <c>*.hsm.json</c> files under the given paths or root directory,
    /// reading only their headers (AssetId + Name).  Malformed files are silently skipped.
    /// </summary>
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
            // Case-insensitive match: *.hsm.json files may have drifted extension casing
            // when authored on Windows; PlatformDefault would silently miss them on Linux.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MatchCasing           = MatchCasing.CaseInsensitive,
            };
            paths = Directory.EnumerateFiles(rootDirectory, "*.hsm.json", options);
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
