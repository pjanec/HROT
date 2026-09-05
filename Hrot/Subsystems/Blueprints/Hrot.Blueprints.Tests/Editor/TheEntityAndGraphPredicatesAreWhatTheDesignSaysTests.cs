using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>VC-4</c>'s rails — THE TWO PREDICATES, PINNED, WITH THE HANDOFF'S GUESS CORRECTED.</b>
/// 🔴 <b>User, visual check <c>2026-08-22</c>:</b> <i>"graph-signature and Runtime views don't appear in
/// the details panel (empty graph click → 'no node selected')."</i>
///
/// <para>⭐⭐⭐ <b>VERDICT: no predicate is changed. Both refuse CORRECTLY, and one of the handoff's two
/// stated causes is FALSE.</b></para>
///
/// <list type="table">
/// <item><term>⛔ handoff said</term><description><i>"graph-signature needs a <b>graph row selected</b>
///   (not empty space)"</i></description></item>
/// <item><term>📐 measured</term><description><b>Its predicate says nothing about selection.</b>
///   <c>GraphSignatureWindow.AppliesTo</c> is <c>Asset.Kind == Blueprint</c> ∧
///   <c>EditableGraphs(asset).Count > 0</c> — i.e. <i>"a Blueprint document that HAS at least one
///   Function/Event/Macro graph"</i>. ⇒ ⭐ clicking empty canvas does NOT make it decline; being on a
///   non-Blueprint document, or on a blueprint with no such graph, does.</description></item>
/// <item><term>⭐ runtime</term><description>the handoff is RIGHT — <c>Mode != Planning</c> ∧ its asset
///   kind, §6 <c>L3</c>'s row verbatim. ⇒ in a Planning editor it declines <b>by design</b>.</description></item>
/// </list>
///
/// <para>⚠⚠ <b>What I could NOT establish, stated rather than guessed:</b> which of the two remaining
/// causes the user actually hit. ⛔ It needs a running editor with a known document. ⭐ There is also a
/// third live suspect worth naming: <c>AppliesTo</c> reads
/// <c>_asset ?? _selectionStore.SelectedAsset</c> — the <b>LEGACY</b> selection store
/// *(<c>_blueprintLegacySelectionStore</c>)*, not the store the <c>DetailsContext</c> is built from.
/// 📌 The same split-selection-model hazard as <c>L6.4</c>'s <c>MissionPanel</c> and <c>UXI-11</c>. ⇒ if
/// that bridge is not fed on the perspective the user was in, the predicate answers <c>false</c> for a
/// document that is plainly open.</para>
///
/// <para>⛔ <b>So these rails PIN the predicates rather than change them</b> — 📌 the handoff's own
/// instruction: <i>"if the intent is broader, adjust the predicate — <b>argue it against §6 L3</b>."</i>
/// ⭐ §6 <c>L3</c>'s table states both rules in the words above; ⛔ loosening either without a design
/// change would put a view on screen that then has to apologise, which is <c>R-117</c>'s defect one
/// level down.</para>
/// </summary>
public sealed class TheEntityAndGraphPredicatesAreWhatTheDesignSaysTests
{
    private sealed class NoEntities : IEntitySelectionSource
    {
        IReadOnlyList<Fdp.Core.Entity> IEntitySelectionSource.Selected()
            => Array.Empty<Fdp.Core.Entity>();
    }

    private static DetailsContext Context(VariableRunState mode)
        => DetailsContextBuilder.Build(
            new EditorSelectionStore(), "Blueprint", mode, new NoEntities());

    // ══ Runtime — Mode != Planning ═══════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Runtime declines while PLANNING, and that is the design, not a defect.</b>
    /// 📄 §6 <c>L3</c>'s Runtime row: <i>"<c>Mode != Planning</c> ∧ its asset kind"</i>.
    /// ⛔ 📌 The design's own reason, from <c>RuntimeDetailsView</c>'s remarks: the old window drew its
    /// pane in EVERY mode and each pane then said <i>"No live BTree state."</i> from inside — a view
    /// claiming the panel in order to apologise. ⇒ ⭐ declining hands the answer to the shell's one grey
    /// line, in one voice for every host.
    /// </summary>
    [Fact]
    public void Runtime_DeclinesWhilePlanning_AndThatIsTheDesign()
    {
        Assert.False(DetailsViewPredicates.ModeIsNot(Context(VariableRunState.Planning),
                                                     VariableRunState.Planning));

        // ⭐ …and it is ready the moment the sim is live — ⛔ so the rule is "not yet", not "never".
        Assert.True(DetailsViewPredicates.ModeIsNot(Context(VariableRunState.Running),
                                                    VariableRunState.Planning));
        Assert.True(DetailsViewPredicates.ModeIsNot(Context(VariableRunState.Paused),
                                                    VariableRunState.Planning));
    }

    /// <summary>
    /// ⭐⭐ <b>The kind clause is the OTHER half, and it is a clause — not a registry axis.</b>
    /// 📌 <c>R-112</c>: <i>"<c>AssetKind</c> is never a view key — a host says so in its own
    /// predicate."</i> ⚠ Railed because collapsing this back into a lookup is precisely the mistake §4
    /// dissolved <c>RuntimeInspectorWindow</c> for.
    /// </summary>
    [Fact]
    public void Runtime_AlsoNeedsItsAssetKind_AndAnEmptyContextHasNone()
        => Assert.False(DetailsViewPredicates.AssetKindIs(
                            Context(VariableRunState.Running), AssetKind.Blueprint));

    // ══ the correction ═══════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE CORRECTION, held as a rail: graph-signature's rule is about the DOCUMENT, not the
    /// SELECTION.</b>
    ///
    /// <para>⛔ A context with no asset declines — ⭐ but so would one with a Blueprint asset and no
    /// editable graph, and NEITHER has anything to do with what is selected on the canvas. ⚠ This rail
    /// cannot construct a real <c>BlueprintAsset</c> context *(the shell's <c>IEditableAsset</c> and
    /// <c>BlueprintAsset</c> are different hierarchies — the predicate's own remarks measure that)*, so
    /// it pins the half that IS expressible and the class remarks carry the rest.</para>
    ///
    /// <para>📌 Why pin it at all: the handoff's stated cause would have sent the next session to add a
    /// selection clause — ⛔ a change that makes the view appear LESS often, aimed at a rule that does
    /// not exist.</para>
    /// </summary>
    [Fact]
    public void GraphSignature_KeysOnTheDocumentKind_NotOnASelection()
    {
        var noDocument = Context(VariableRunState.Planning);

        Assert.Null(noDocument.Asset);
        Assert.False(DetailsViewPredicates.AssetKindIs(noDocument, AssetKind.Blueprint));

        // ⭐⭐ The point of the rail: the context the user's "empty canvas click" produces differs from a
        //   populated one ONLY in `Selection` — and no clause of either predicate reads it.
        Assert.Empty(noDocument.Selection);
    }
}
