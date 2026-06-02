using System.Linq;
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

/// <summary>
/// Classifies a dangling reference by how severely deleting the referenced asset
/// impacts the editor / runtime.
/// </summary>
public enum ReferenceCriticality
{
    /// <summary>
    /// The reference is name- or value-based; the runtime can tolerate a missing
    /// target gracefully (e.g. a failing BTree subtree node, an unresolved event).
    /// The inspector will flag it, but no compilation is broken.
    /// </summary>
    AutoResolvable,

    /// <summary>
    /// The reference is type-based or required for compilation; deleting the target
    /// asset will break the C# build (e.g. a typed <c>AssetReference</c> exported
    /// type, a blackboard field type, or an action/condition/guard FQN that requires
    /// the declaring type to exist).
    /// </summary>
    Critical,
}

/// <summary>
/// A dangling reference enriched with its <see cref="Criticality"/> classification.
/// </summary>
/// <param name="Reference">The underlying reference information.</param>
/// <param name="Criticality">Whether this reference is Critical or AutoResolvable.</param>
public sealed record ClassifiedDanglingReference(
    AssetReferenceInfo Reference,
    ReferenceCriticality Criticality);

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

/// <summary>
/// Preview of a delete operation. Backward-compatible: <see cref="DanglingReferences"/>
/// contains all dangling refs (unchanged); <see cref="ClassifiedReferences"/> adds
/// per-ref criticality classification (AIE-053).
/// </summary>
public sealed record DeletePreview(
    Guid AssetId,
    IReadOnlyList<AssetReferenceInfo> DanglingReferences,
    IReadOnlyList<RefactorIssue> Issues)
{
    /// <summary>
    /// Classified view of <see cref="DanglingReferences"/>.
    /// Populated by <see cref="RefactorService.PreviewDelete"/>; null on records
    /// constructed by stubs/tests that use the positional constructor.
    /// </summary>
    public IReadOnlyList<ClassifiedDanglingReference> ClassifiedReferences { get; init; }
        = System.Array.Empty<ClassifiedDanglingReference>();

    /// <summary>
    /// Convenience view: only the references classified as
    /// <see cref="ReferenceCriticality.Critical"/>.
    /// </summary>
    public IReadOnlyList<AssetReferenceInfo> CriticalReferences =>
        ClassifiedReferences
            .Where(c => c.Criticality == ReferenceCriticality.Critical)
            .Select(c => c.Reference)
            .ToList();
}

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
