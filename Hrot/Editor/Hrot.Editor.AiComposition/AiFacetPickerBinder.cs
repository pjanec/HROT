using Fdp.Toolkit.Behavior;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Inspector;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Inspector;
using StructEdit.Core;

namespace Hrot.Editor.AiComposition;

/// <summary>
/// ⭐⭐ <b>What rebuilding the BTree/HSM facet pickers needs.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.21.
/// </summary>
public sealed record AiFacetPickerServices
{
    /// <summary>The per-perspective registrars whose <c>NodeProperties</c> source holds the pickers.</summary>
    public PerspectiveWorkspaceRegistrar? BTreeRegistrar { get; init; }
    public PerspectiveWorkspaceRegistrar? HsmRegistrar   { get; init; }

    /// <summary>⚠ Without this the pickers cannot be attached at all — the drawers hang off it.</summary>
    public IComponentEditService? FacetEditService { get; init; }

    /// <summary>⚠ BTree drawers only: <c>BuildDrawers</c> requires a non-null registry.</summary>
    public BehaviorRegistry? BehaviorRegistry { get; init; }

    public IActionSchemaExporter? ActionSchema { get; init; }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-347</c> — the attribute-dispatched dropdowns follow the active document, on BOTH
/// hosts.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.21.
///
/// <para>🔒 <b>Raised by the user:</b> <i>"why hosts differ in … picker drawer … I would expect these
/// 3 to be same in both cgf and editor."</i> 📐 Measured: <c>CgfSubsystem</c> constructs
/// <c>_btreeRegistrar</c>/<c>_hsmRegistrar</c> (<c>:2065-2066</c>) — <b>the very objects these pickers
/// hang off</b> — and made <b>0</b> <c>SetFacetEditService</c>/<c>SetFacetDispatcher</c> calls against
/// the editor's <b>13</b>. ⛔ A search of the CGF slice designs found <b>no record</b> that CGF should
/// lack them ⇒ a GAP, not a design decision. ⭐ And every prerequisite was already on the host:
/// <c>_behaviorRegistry</c>, <c>_facetEditService</c> and the schema exporter.</para>
///
/// <para>⛔⛔ <b>Why this lives HERE and was not simply copied into CGF.</b> The obvious fix — paste the
/// editor's three arms into <c>CgfSubsystem</c> — would have created a FIFTH duplicate in the session
/// that removed four. 🔒 One implementation, both callers; the editor's inline arms are deleted in
/// the same commit.</para>
///
/// <para>⭐ <b>The <c>FqnContext</c> is shared between dispatcher and drawer DELIBERATELY</b>
/// (<c>BB1D</c>): the blackboard-field picker filters by the current action's DTO type, and it must see
/// the FQN the dispatcher wrote <b>in the same frame</b>. ⛔ Two contexts would render a frame stale.</para>
/// </summary>
public static class AiFacetPickerBinder
{
    /// <summary>
    /// ⭐ Rebuild the picker maps for the newly active document. ⚠ Cheap by design — the maps are 1–2
    /// entries built from an asset already in memory, no I/O — so it runs on every activation.
    ///
    /// <para>⚠ <c>SetFacetEditService</c> also drops the cached StructEdit session, so the next render
    /// opens a fresh one against the correct facet type. ⭐ Harmless when the asset type did not change.</para>
    /// </summary>
    public static void Rebuild(AiDocument? active, AiFacetPickerServices services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        var editService = services.FacetEditService;

        if (active?.Kind == AssetKind.BTree
            && active.Asset is Hrot.BTree.Editor.Model.BehaviorTreeAsset btreeAsset
            && services.BehaviorRegistry is not null)
        {
            // ⭐ BB1D — ONE context, written by the dispatcher and read by the drawer.
            var ctx     = new BTreeFacetFqnContext();
            var drawers = BTreePickerDrawerFactory.BuildDrawers(
                btreeAsset, services.BehaviorRegistry, services.ActionSchema, ctx);

            services.BTreeRegistrar?.NodeProperties.SetFacetEditService(editService, drawers);
            services.BTreeRegistrar?.NodeProperties.SetFacetDispatcher(
                BTreeSelectionBridgeHelper.BuildFacetDispatcher(btreeAsset, ctx));
        }
        else if (active?.Kind == AssetKind.Hsm
            && active.Asset is Hrot.Hsm.Editor.Model.HsmAsset hsmAsset)
        {
            var ctx     = new HsmFacetFqnContext();
            var drawers = HsmPickerDrawerFactory.BuildDrawers(hsmAsset, services.ActionSchema, ctx);

            services.HsmRegistrar?.NodeProperties.SetFacetEditService(editService, drawers);
            services.HsmRegistrar?.NodeProperties.SetFacetDispatcher(
                HsmSelectionBridgeHelper.BuildFacetDispatcher(hsmAsset, ctx));
        }
        else
        {
            // ⚠ Switching to Blueprint, or clearing: reset the pickers to their plain-text fallback.
            // ⛔ The edit service itself REMAINS, so the inspector still renders struct fields.
            services.BTreeRegistrar?.NodeProperties.SetFacetEditService(editService, null);
            services.HsmRegistrar?.NodeProperties.SetFacetEditService(editService, null);
            services.BTreeRegistrar?.NodeProperties.SetFacetDispatcher(null);
            services.HsmRegistrar?.NodeProperties.SetFacetDispatcher(null);
        }
    }
}
