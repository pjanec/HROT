using Hrot.Editor.AiShared.References;

namespace Hrot.Editor.AiShared.Refactor;

public interface IRefactorService
{
    // ---- Read-only queries ----
    IReadOnlyList<AssetReferenceInfo> FindReferences(string targetKey);
    IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid hostAssetId);

    // ---- Mutation: previewed, then applied ----
    RefactorPreview PreviewRename(string fromKey, string toKey, RefactorOptions options);
    RefactorResult ApplyRename(RefactorPreview preview);

    DeletePreview PreviewDelete(Guid assetId, DeleteOptions options);
    RefactorResult ApplyDelete(DeletePreview preview);

    // ---- Async variants ----
    Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default);
    Task<RefactorResult> ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default);
}

public sealed record RefactorOptions(
    bool IncludeBlueprint = true,
    bool IncludeBTree = true,
    bool IncludeHsm = true,
    bool DryRunOnly = false);

public sealed record DeleteOptions(
    bool AllowDanglingReferences = false);

public sealed record RefactorPreview(
    string FromKey,
    string ToKey,
    IReadOnlyList<RefactorFileEdit> Edits,
    IReadOnlyList<RefactorIssue> Issues);

public sealed record DeletePreview(
    Guid AssetId,
    IReadOnlyList<AssetReferenceInfo> DanglingReferences,
    IReadOnlyList<RefactorIssue> Issues);

public sealed record RefactorFileEdit(
    string FilePath,
    Guid HostAssetId,
    IReadOnlyList<RefactorLineEdit> LineEdits);

public sealed record RefactorLineEdit(
    int LineNumber,
    string OriginalText,
    string ReplacementText,
    string ContextDescription);

public sealed record RefactorIssue(
    RefactorIssueSeverity Severity,
    string Description,
    Guid? RelatedAssetId);

public enum RefactorIssueSeverity { Info, Warning, Error }

public sealed record RefactorResult(
    bool Success,
    IReadOnlyList<string> WrittenFiles,
    string? FailureReason);

// Thin wrapper over AssetReference that includes the resolved SourceFilePath.
public sealed record AssetReferenceInfo(
    Guid HostAssetId,
    AssetKind HostKind,
    Guid HostElementId,
    string HostDisplayPath,
    string TargetKey,
    SubElementKind TargetKind,
    string SourceFilePath);
