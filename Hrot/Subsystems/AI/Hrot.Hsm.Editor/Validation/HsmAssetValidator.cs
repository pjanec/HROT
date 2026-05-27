using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Validation;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Validation;

/// <summary>
/// Adapts HsmValidator to the shared IAssetValidator interface so HSM diagnostics
/// can be shown in the cross-asset DiagnosticsWindow.
/// </summary>
public sealed class HsmAssetValidator : IAssetValidator
{
    private readonly HsmValidator _inner;

    public HsmAssetValidator(IActionSchemaExporter? schema = null)
    {
        _inner = new HsmValidator(schema);
    }

    public AssetKind SupportedKind => AssetKind.Hsm;

    public IReadOnlyList<AssetDiagnostic> Validate(IEditableAsset asset)
    {
        if (asset is not HsmAsset hsmAsset)
            return Array.Empty<AssetDiagnostic>();

        var blackboard = hsmAsset as IBlackboardManagedAsset;  // null if not wired yet
        var raw = _inner.Validate(hsmAsset, blackboard);
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

    private static AssetDiagnosticSeverity MapSeverity(HsmDiagnosticSeverity s) => s switch
    {
        HsmDiagnosticSeverity.Info    => AssetDiagnosticSeverity.Info,
        HsmDiagnosticSeverity.Warning => AssetDiagnosticSeverity.Warning,
        HsmDiagnosticSeverity.Error   => AssetDiagnosticSeverity.Error,
        _                             => AssetDiagnosticSeverity.Error,
    };
}
