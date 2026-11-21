using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiComposition;

/// <summary>
/// ⭐⭐ <b>The per-host inputs to active-document retargeting.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.19.
/// </summary>
public sealed record AiActiveDocumentServices
{
    public required AiDocumentManager DocumentManager { get; init; }

    /// <summary>⭐ The three per-perspective selection stores. ⚠ Each window reads its store's
    /// <c>ActiveAsset</c> every frame (pull model), so setting it here is all that is needed.</summary>
    public EditorSelectionStore? BTreeStore     { get; init; }
    public EditorSelectionStore? HsmStore       { get; init; }
    public EditorSelectionStore? BlueprintStore { get; init; }

    /// <summary>
    /// ⭐ The Blueprint outline / "My Blueprint" panel. 📐 Measured: BOTH hosts construct a
    /// <c>BlueprintMyBlueprintWindow</c> and retarget it with the same seven arguments pulled from the
    /// document's <see cref="AiCanvasContext"/>.
    ///
    /// <para>⛔⛔ <b>A PROVIDER, not a value, and that is load-bearing.</b> 🔴 Measured while extracting
    /// this: <c>EditorSubsystem</c> wires <c>ActiveChanged</c> at <c>:3575</c> and does not assign
    /// <c>_blueprintMyBlueprintWindow</c> until <c>:4449</c>. ⇒ the original code read the field LATE,
    /// inside the handler, through <c>?.</c> — and capturing it by value at bind time would have
    /// passed <see langword="null"/> forever, silently disabling the panel on the editor only.
    /// ⚠ Exactly the class of defect this whole extraction exists to prevent, so the shape has to
    /// preserve the late read rather than merely look tidier.</para>
    /// </summary>
    public Func<Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintWindow?>? BlueprintOutline { get; init; }

    /// <summary>
    /// ⚠ <b>Host-only work, run AFTER the shared retarget.</b> ⛔ The editor does a great deal more
    /// here — rebuilding the BTree/HSM picker-drawer maps and facet dispatchers, the legacy variables
    /// bridge, the graph-signature window — none of which CGF has or should have. 🔒 That is a real
    /// difference, so it is a hook rather than something this binder decides.
    /// </summary>
    public Action<AiDocument?>? AfterRetarget { get; init; }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-343</c> — the SHARED core of "the active document changed", for both hosts.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.19.
///
/// <para>🔴 <b>What this fixes, and it is the third instance of one disease.</b>
/// <c>DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md</c> §11 ② records the finding verbatim: with the
/// document factories wired, CGF's canvas matched the editor exactly — and <c>my-blueprint</c> still
/// said <i>"No blueprint open."</i> while <c>details</c> still had <c>assetId: null</c>, because those
/// surfaces do NOT read the document manager. ⇒ the fix was <i>"the editor's handler, trimmed to what
/// this host has"</i> — ⛔ i.e. another copy, for the same reason as the others: the shared home was
/// frozen.</para>
///
/// <para>⭐⭐ <b>The genuinely shared part is small and load-bearing:</b> the three selection stores,
/// and the seven-argument retarget pulled off <see cref="AiCanvasContext"/>. ⚠ <b>That second one is
/// where a copy silently degrades a panel</b> — omit <c>currentGraphId</c> and the Local Variables
/// section edits a graph the designer is not looking at (<c>BP-57</c>/<c>BP-72</c>); omit
/// <c>indicators</c> and <c>BP-223</c>'s refusal toast is discarded. 🔒 Exactly the failure mode a
/// second copy invites.</para>
/// </summary>
public static class AiActiveDocumentBinder
{
    /// <summary>
    /// ⭐ Subscribe <c>ActiveChanged</c> so the three stores and the Blueprint outline follow the
    /// active document, then run the host's own extras.
    /// </summary>
    public static void Bind(AiActiveDocumentServices services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        var manager = services.DocumentManager;

        manager.ActiveChanged += () =>
        {
            var active = manager.Active;

            // ⭐ AIE-025 — retarget the per-perspective stores. A window whose kind is not active
            //   must go NULL, or it keeps rendering the previous asset's schema.
            if (services.BTreeStore     is not null)
                services.BTreeStore.ActiveAsset     = active?.Kind == AssetKind.BTree     ? active.Asset : null;
            if (services.HsmStore       is not null)
                services.HsmStore.ActiveAsset       = active?.Kind == AssetKind.Hsm       ? active.Asset : null;
            if (services.BlueprintStore is not null)
                services.BlueprintStore.ActiveAsset = active?.Kind == AssetKind.Blueprint ? active.Asset : null;

            // ⭐⭐ AIE-047 — the Blueprint outline follows the document, with every argument the
            //    panel needs. ⛔ The arguments are pulled from the canvas context the factory built,
            //    and each one has a defect behind it (see the type's remarks).
            // ⚠ Resolved HERE, per fire — see the property's remarks: the editor assigns its panel
            //   ~900 lines after this binder is wired.
            var outline = services.BlueprintOutline?.Invoke();
            if (outline is not null)
            {
                if (active?.Kind == AssetKind.Blueprint)
                {
                    var ctx = active.ViewState as AiCanvasContext;
                    outline.Retarget(
                        editableAsset:  active.Asset,
                        blueprintAsset: ctx?.AssetRef as Hrot.Blueprints.Core.Assets.BlueprintAsset,
                        hostServices:   ctx?.View.Host,
                        // ⚠ BCP-BATCH-02-FIX Task 3 — the document's REAL command set, so "+ Variable"
                        //   hits the registered handler instead of a fresh, empty command instance.
                        commands:       ctx?.Commands ?? new NodeEditor.Core.Action.EditorCommandsImpl(),
                        view:           ctx?.View,
                        // ⚠ BP-57/BP-72 — the Local Variables section is GRAPH-scoped; without this it
                        //   sits on functionGraphs[0] after a graph switch.
                        currentGraphId: ctx?.CurrentGraphId,
                        // ⚠ BP-223 — where the locals "+" refusal on a macro graph is drawn.
                        indicators:     ctx?.Indicators);
                }
                else
                {
                    outline.Retarget(null, null, null, null);
                }
            }

            services.AfterRetarget?.Invoke(active);
        };
    }
}
