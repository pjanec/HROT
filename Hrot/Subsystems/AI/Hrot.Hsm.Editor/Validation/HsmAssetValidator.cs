using System;
using System.Collections.Generic;
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

    /// <remarks>
    /// ⭐⭐⭐ <b><c>E4</c> — the resolvers are THREADED, not left at their defaults.</b>
    ///
    /// <para>
    /// 📄 <c>DEBT-AIB-028</c>(b)/(c): <i>"<c>_isStatefulSubtree</c> defaults to <c>_ =&gt; false</c> and
    /// production never supplies a real resolver … the production <c>HsmAssetValidator</c> entry point
    /// isn't threaded to pass the resolver."</i> ⇒ rules 8/8b were <b>dormant in production</b> — they
    /// existed, were tested, and could never fire against a real asset. ⛔ That is trap #5 in its
    /// purest form: a rule that is present, green, and inert.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The resolver is built by the COMPOSITION ROOT, not here.</b> It has to answer for both
    /// <c>BehaviorTreeAsset</c> and <c>HsmAsset</c>, and this assembly can see only one of them. ⇒ the
    /// argument, not the lookup.
    /// </para>
    ///
    /// <para>
    /// 📌 <b>Rules 8/8b may still not fire on real assets, and that is expected — but Batch 92
    /// (<c>92e</c>) SPLIT the two halves this note used to run together.</b>
    /// </para>
    ///
    /// <para>
    /// ✅ <b>Persistence: FIXED.</b> <c>StateNode.SubtreeAssetId</c> round-trips —
    /// <c>HsmAssetDto.cs:73</c>, <c>DEBT-AIB-028(a)</c> resolved in Batch 75. ⛔ The old wording
    /// <i>"is not persisted"</i> was rotted.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>"Nothing sets it": still TRUE.</b> There is no authoring gesture on HSM that assigns a
    /// sub-tree to a state, so no real asset carries a non-empty value. ⇒ ⭐ that — not persistence —
    /// is what <c>E5</c> still needs. ⭐ This item makes the wiring honest; <c>E5</c> makes it
    /// reachable.
    /// </para>
    /// </remarks>
    public HsmAssetValidator(
        IActionSchemaExporter? schema = null,
        Func<Guid, bool>? isStatefulSubtree = null,
        Func<Guid, IReadOnlyCollection<int>>? sharedScopeKeys = null)
    {
        _inner = new HsmValidator(schema, isStatefulSubtree, sharedScopeKeys);
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
