namespace Hrot.Editor.AiShared.Validation;

/// <summary>Severity of an aggregated diagnostic across all asset types.</summary>
public enum AssetDiagnosticSeverity { Info, Warning, Error }

/// <summary>A single diagnostic entry from any asset type's validator.</summary>
public sealed record AssetDiagnostic(
    Guid AssetId,
    string AssetName,
    AssetDiagnosticSeverity Severity,
    string Code,
    string Message);

/// <summary>
/// Contract for per-asset-kind validators that produce AssetDiagnostics
/// suitable for cross-asset aggregation in the DiagnosticsWindow.
/// </summary>
public interface IAssetValidator
{
    /// <summary>Asset kind this validator handles.</summary>
    AssetKind SupportedKind { get; }

    /// <summary>
    /// Validates the given asset and returns a flat list of diagnostics.
    /// Must handle only assets whose Kind == SupportedKind.
    /// </summary>
    IReadOnlyList<AssetDiagnostic> Validate(IEditableAsset asset);
}
