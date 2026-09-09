using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 follow-up — <see cref="HsmEventsWindow"/> as a Details panel VIEW.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>Hrot.Editor.AiShared/Shell/VariablesDetailsView.cs</c> — the mirrored precedent: an
/// ASSET-scoped view (not selection-scoped), exactly the shape <c>NodePropertiesDetailsView</c>'s own
/// remarks distinguish as <i>"Track C's table is ASSET-scoped"</i>.
///
/// <para>⭐⭐ <b>WHY ASSET-SCOPED, NOT SELECTION-SCOPED.</b> <c>HsmEventsWindow</c> lists every event
/// declaration on the CURRENTLY OPEN <see cref="HsmAsset"/> — it does not read
/// <see cref="DetailsContext.Selection"/> at all, the same shape as
/// <see cref="VariablesDetailsView"/>'s section. This is a measured, established category
/// (asset-scoped) rather than a forced fit onto the selection-scoped shape most of this family uses.
/// </para>
///
/// <para>⚠⚠ <b>The wrapped <see cref="HsmEventsWindow"/> is REBUILT when the asset changes</b> —
/// mirroring <c>GraphSignatureWindow</c>'s own <c>_asset</c> cache-and-refresh idiom. Its only
/// per-instance state is the rename modal (<c>_pendingRenameEvent</c>/<c>_renameBuf</c>), which is
/// legitimately invalidated by an asset switch anyway — the same reasoning
/// <c>NodePropertiesDetailsView</c> uses to drop its edit sessions when the facet type changes.</para>
/// </summary>
public sealed class HsmEventsDetailsView : IDetailsViewInstance
{
    private readonly IRefactorService  _refactorService;
    private readonly FindResultsWindow _findResults;

    private HsmAsset?         _cachedAsset;
    private HsmEventsWindow?  _inner;

    public HsmEventsDetailsView(IRefactorService refactorService, FindResultsWindow findResults)
    {
        _refactorService = refactorService ?? throw new ArgumentNullException(nameof(refactorService));
        _findResults      = findResults      ?? throw new ArgumentNullException(nameof(findResults));
    }

    /// <summary>⭐ Rebuilds the wrapped window only when the asset actually changed — the same
    /// short-circuit <c>GraphSignatureWindow</c> uses at its own <c>_asset</c> cache.</summary>
    private HsmEventsWindow EnsureInnerFor(HsmAsset asset)
    {
        if (_inner == null || !ReferenceEquals(_cachedAsset, asset))
        {
            _cachedAsset = asset;
            _inner       = new HsmEventsWindow(asset, _refactorService, _findResults);
        }
        return _inner;
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. No ImGui here.</summary>
    private HsmEventsWindowViewModel BuildAndPublish(HsmAsset asset, string idScope)
    {
        var inner   = EnsureInnerFor(asset);
        var panelId = $"{idScope}/{HsmEventsDetailsViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);

        var vm = inner.BuildViewModel(panelId, HsmEventsDetailsViewDescriptor.ViewId);

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal HsmEventsWindowViewModel SimulateDraw(HsmAsset asset, string idScope) => BuildAndPublish(asset, idScope);

    /// <inheritdoc/>
    public void Draw(DetailsContext context, string idScope)
    {
        ArgumentNullException.ThrowIfNull(context);
        // ⚠ Defensive — AppliesTo already guards this; a view must never draw a blank claim (R-117).
        if (context.Asset is not HsmAsset asset) return;

        var inner = EnsureInnerFor(asset);
        BuildAndPublish(asset, idScope);
        inner.Render();
    }

    /// <summary>⛔ Deliberately empty — <see cref="_inner"/> owns no unmanaged/disposable resources of
    /// its own (it borrows <see cref="_refactorService"/>/<see cref="_findResults"/>, both perspective-
    /// owned, mirroring <see cref="VariablesDetailsView.Dispose"/>'s "the section is BORROWED"
    /// reasoning).</summary>
    public void Dispose() { }
}

/// <summary>
/// ⭐⭐ <b>The descriptor for <see cref="HsmEventsDetailsView"/>.</b> ⭐ Its own type so a host
/// registers it with one line and the predicate lives beside the view it guards (<c>R-116</c>).
/// </summary>
public static class HsmEventsDetailsViewDescriptor
{
    /// <summary>⭐ The stable id — the layout key and the "remember my pick" key.</summary>
    public const string ViewId = "details.hsmevents";

    /// <summary>⭐ Rank <b>8</b> — below <c>Variables</c> (10, the established asset-scoped default) so
    /// Variables still wins the "nothing else claims" slot; above nothing in particular otherwise.
    /// ⚠ 📌 <c>R-98</c> — rank only decides the DEFAULT; the designer's toolbar pick wins.</summary>
    public const int Rank = 8;

    /// <summary>
    /// ⭐⭐ Build the descriptor for the perspective's shared <paramref name="refactorService"/> and
    /// <paramref name="findResults"/> window (mirrors <c>VariablesDetailsViewDescriptor.For</c>'s
    /// "wrap what the perspective already owns" shape).
    /// </summary>
    public static DetailsViewDescriptor For(IRefactorService refactorService, FindResultsWindow findResults)
    {
        ArgumentNullException.ThrowIfNull(refactorService);
        ArgumentNullException.ThrowIfNull(findResults);

        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Events",
            Rank:      Rank,
            // ⭐⭐ R-117 — do not claim an empty panel: only offer the view when the open asset is an
            //   HsmAsset that actually declares events.
            AppliesTo: Applies,
            // ⭐ A FRESH instance per window (R-120) — unlike VariablesDetailsView this view has no
            //   shared/borrowed section forcing it to return one shared object; its own state
            //   (_cachedAsset/_inner, the rename modal) is legitimately per-window.
            Create:    () => new HsmEventsDetailsView(refactorService, findResults));
    }

    /// <summary>⭐ Extracted so a rail can assert the predicate directly.</summary>
    public static bool Applies(DetailsContext context)
        => context.Asset is HsmAsset asset && asset.AllEvents.Count > 0;
}
