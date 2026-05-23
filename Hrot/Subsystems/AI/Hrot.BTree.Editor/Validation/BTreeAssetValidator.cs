using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Validation;

namespace Hrot.BTree.Editor.Validation;

/// <summary>
/// Adapts BTreeValidator to the shared IAssetValidator interface so BTree diagnostics
/// can be shown in the cross-asset DiagnosticsWindow.
/// </summary>
public sealed class BTreeAssetValidator : IAssetValidator
{
    private readonly BTreeValidator _inner;

    public BTreeAssetValidator(BTreeValidator inner)
    {
        _inner = inner;
    }

    public AssetKind SupportedKind => AssetKind.BTree;

    public IReadOnlyList<AssetDiagnostic> Validate(IEditableAsset asset)
    {
        if (asset is not BehaviorTreeAsset btAsset)
            return Array.Empty<AssetDiagnostic>();

        var raw = _inner.Validate(btAsset);
        var result = new List<AssetDiagnostic>(raw.Count);
        foreach (var d in raw)
        {
            result.Add(new AssetDiagnostic(
                AssetId: asset.AssetId,
                AssetName: asset.Name,
                Severity: MapSeverity(d.Severity),
                Code: d.Code.ToString(),
                Message: d.Message));
        }
        return result;
    }

    private static AssetDiagnosticSeverity MapSeverity(BTreeDiagnosticSeverity s) => s switch
    {
        BTreeDiagnosticSeverity.Info    => AssetDiagnosticSeverity.Info,
        BTreeDiagnosticSeverity.Warning => AssetDiagnosticSeverity.Warning,
        BTreeDiagnosticSeverity.Error   => AssetDiagnosticSeverity.Error,
        _                               => AssetDiagnosticSeverity.Error,
    };
}
