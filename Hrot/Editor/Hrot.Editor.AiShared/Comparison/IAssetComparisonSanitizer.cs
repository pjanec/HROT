namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Implemented by each asset-kind's sanitizer. Reads the canonical asset file(s),
/// strips presentation noise, and returns LLM-ready text plus structured metadata.
/// </summary>
public interface IAssetComparisonSanitizer
{
    /// <summary>The asset kind this sanitizer handles.</summary>
    AssetKind TargetKind { get; }

    /// <summary>
    /// Sanitizes the asset at the given path. Must never throw; returns a warning
    /// instead on recoverable errors.
    /// </summary>
    SanitizationResult Sanitize(AssetExportRequest request);
}

/// <summary>
/// Input to a sanitizer: paths to the asset files and the expected kind.
/// </summary>
public sealed record AssetExportRequest(
    /// <summary>Absolute path to the canonical main asset file (e.g., OrcGuard_BT.cs).</summary>
    string AssetMainFilePath,
    /// <summary>Directory containing companion files (e.g., Blackboard.cs). May be null.</summary>
    string? CompanionDirectoryPath,
    /// <summary>Expected asset kind; used to validate the sanitizer is correct for this file.</summary>
    AssetKind ExpectedKind);

/// <summary>
/// Output of a sanitizer: the LLM-ready sanitized text, structured metadata, and any warnings.
/// </summary>
public sealed record SanitizationResult(
    /// <summary>Deterministic LLM-ready text output. Byte-identical for the same input.</summary>
    string SanitizedText,
    /// <summary>Structured metadata extracted from the asset, used for the per-version export header.</summary>
    AssetMetadataBlock Metadata,
    /// <summary>Non-fatal warnings accumulated during sanitization.</summary>
    IReadOnlyList<SanitizationWarning> Warnings);

/// <summary>
/// Structured metadata extracted from an asset file; used to populate the per-version header
/// in the comparison export text.
/// </summary>
public sealed record AssetMetadataBlock(
    /// <summary>Human-readable name of the asset (e.g., "OrcGuard_BT").</summary>
    string AssetName,
    /// <summary>Kind of the asset.</summary>
    AssetKind Kind,
    /// <summary>Stable asset identifier parsed from the asset file.</summary>
    Guid AssetId,
    /// <summary>Absolute path of the file that was sanitized.</summary>
    string SourceFilePath,
    /// <summary>Companion files included in the export (e.g., Blackboard.cs). Empty when none.</summary>
    IReadOnlyList<string> CompanionFiles,
    /// <summary>UTC last-write timestamp of the main file, or null if unavailable.</summary>
    DateTime? LastModifiedTimestamp,
    /// <summary>
    /// Set when the sanitizer migrated the document to the current schema version before processing.
    /// Null for same-version comparisons. Used to surface a notice in the comparison UI.
    /// </summary>
    string? MigrationNotice = null);

/// <summary>
/// A non-fatal warning raised during sanitization (e.g., missing layout method, malformed content).
/// </summary>
public sealed record SanitizationWarning(string Message);
