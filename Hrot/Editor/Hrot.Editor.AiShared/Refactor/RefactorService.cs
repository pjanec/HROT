using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;

namespace Hrot.Editor.AiShared.Refactor;

public sealed class RefactorService : IRefactorService
{
    private readonly IReferenceCatalog _referenceCatalog;
    private readonly IAssetCatalog _assetCatalog;
    private readonly AtomicMultiFileWriter _writer;

    public RefactorService(
        IReferenceCatalog referenceCatalog,
        IAssetCatalog assetCatalog,
        AtomicMultiFileWriter writer)
    {
        _referenceCatalog = referenceCatalog;
        _assetCatalog = assetCatalog;
        _writer = writer;
    }

    public IReadOnlyList<AssetReferenceInfo> FindReferences(string targetKey)
    {
        var refs = _referenceCatalog.FindReferences(targetKey);
        return refs.Select(ToInfo).ToList();
    }

    public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid hostAssetId)
    {
        var refs = _referenceCatalog.AllReferencesIn(hostAssetId);
        return refs.Select(ToInfo).ToList();
    }

    public RefactorPreview PreviewRename(string fromKey, string toKey, RefactorOptions options)
    {
        var references = _referenceCatalog.FindReferences(fromKey);
        var filtered = references.Where(r => IsIncluded(r.HostKind, options)).ToList();

        var issues = new List<RefactorIssue>();
        var fileEdits = new List<RefactorFileEdit>();

        // Check for key collision in the catalog.
        if (_referenceCatalog.FindElement(toKey) != null)
        {
            issues.Add(new RefactorIssue(RefactorIssueSeverity.Warning,
                $"Key '{toKey}' already exists in the catalog.", null));
        }

        // Group by HostAssetId so each file is read once.
        var groups = filtered.GroupBy(r => r.HostAssetId).ToList();

        foreach (var group in groups)
        {
            var hostAssetId = group.Key;
            var asset = _assetCatalog.FindByAssetId(hostAssetId);
            var sourcePath = asset?.SourceFilePath ?? string.Empty;

            if (string.IsNullOrEmpty(sourcePath))
            {
                issues.Add(new RefactorIssue(RefactorIssueSeverity.Warning,
                    $"Asset {hostAssetId} has no source file path.", hostAssetId));
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                issues.Add(new RefactorIssue(RefactorIssueSeverity.Warning,
                    $"Source file '{sourcePath}' does not exist.", hostAssetId));
                continue;
            }

            var lines = File.ReadAllLines(sourcePath);
            var lineEdits = new List<RefactorLineEdit>();
            var hostKind = asset!.Kind;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains(fromKey))
                {
                    var lineNum = i + 1;
                    var contextDesc = $"Reference in {hostKind} asset at line {lineNum}";
                    lineEdits.Add(new RefactorLineEdit(
                        lineNum,
                        line,
                        line.Replace(fromKey, toKey),
                        contextDesc));
                }
            }

            if (lineEdits.Count > 0)
            {
                fileEdits.Add(new RefactorFileEdit(sourcePath, hostAssetId, lineEdits));
            }
        }

        return new RefactorPreview(fromKey, toKey, fileEdits, issues);
    }

    public RefactorResult ApplyRename(RefactorPreview preview)
    {
        if (preview.Issues.Any(i => i.Severity == RefactorIssueSeverity.Error))
        {
            return new RefactorResult(false, Array.Empty<string>(),
                "Preview contains Error-level issues; aborting.");
        }

        var filePathToContent = new Dictionary<string, string>();

        try
        {
            foreach (var fileEdit in preview.Edits)
            {
                var lines = File.ReadAllLines(fileEdit.FilePath).ToArray();
                foreach (var lineEdit in fileEdit.LineEdits)
                {
                    lines[lineEdit.LineNumber - 1] = lineEdit.ReplacementText;
                }
                filePathToContent[fileEdit.FilePath] = string.Join(Environment.NewLine, lines);
            }
        }
        catch (Exception ex)
        {
            return new RefactorResult(false, Array.Empty<string>(), ex.Message);
        }

        var result = _writer.Write(filePathToContent);
        if (!result.Success)
        {
            return new RefactorResult(false, result.SuccessfullyWritten,
                result.FailureReason ?? "Unknown write failure.");
        }

        return new RefactorResult(true, result.SuccessfullyWritten, null);
    }

    public DeletePreview PreviewDelete(Guid assetId, DeleteOptions options)
    {
        var danglingRefs = new List<AssetReferenceInfo>();
        var issues = new List<RefactorIssue>();

        // Find all sub-elements whose source is the asset being deleted.
        var elements = _referenceCatalog.AllElements
            .Where(e => e.SourceAssetId == assetId)
            .ToList();

        foreach (var element in elements)
        {
            var refs = _referenceCatalog.FindReferences(element.Key);
            danglingRefs.AddRange(refs.Select(ToInfo));
        }

        if (danglingRefs.Count > 0 && !options.AllowDanglingReferences)
        {
            issues.Add(new RefactorIssue(RefactorIssueSeverity.Warning,
                $"Asset {assetId} has {danglingRefs.Count} dangling reference(s).", assetId));
        }

        return new DeletePreview(assetId, danglingRefs, issues);
    }

    public RefactorResult ApplyDelete(DeletePreview preview)
    {
        if (preview.Issues.Any(i => i.Severity == RefactorIssueSeverity.Error))
        {
            return new RefactorResult(false, Array.Empty<string>(),
                "Preview contains Error-level issues; aborting.");
        }

        var asset = _assetCatalog.FindByAssetId(preview.AssetId);
        if (asset == null)
        {
            return new RefactorResult(false, Array.Empty<string>(),
                $"Asset {preview.AssetId} not found in catalog.");
        }

        var path = asset.SourceFilePath;
        var written = new List<string>();
        if (File.Exists(path))
        {
            File.Delete(path);
            written.Add(path);
        }

        return new RefactorResult(true, written, null);
    }

    public Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default)
        => Task.Run(() => PreviewRename(fromKey, toKey, options), ct);

    public Task<RefactorResult> ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default)
        => Task.Run(() => ApplyRename(preview), ct);

    private AssetReferenceInfo ToInfo(AssetReference r)
    {
        var sourcePath = _assetCatalog.FindByAssetId(r.HostAssetId)?.SourceFilePath ?? string.Empty;
        return new AssetReferenceInfo(
            r.HostAssetId, r.HostKind, r.HostElementId,
            r.HostDisplayPath, r.TargetKey, r.TargetKind, sourcePath);
    }

    private static bool IsIncluded(AssetKind kind, RefactorOptions options) => kind switch
    {
        AssetKind.Blueprint => options.IncludeBlueprint,
        AssetKind.BTree => options.IncludeBTree,
        AssetKind.Hsm => options.IncludeHsm,
        _ => true,
    };
}
