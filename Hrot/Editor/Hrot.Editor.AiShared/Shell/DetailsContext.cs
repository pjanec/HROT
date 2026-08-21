using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L0.3</c> — WHAT THE DETAILS PANEL IS LOOKING AT, this frame.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2's <c>classDiagram</c> — the six fields are that
/// diagram's, unchanged; §1 places this type in <c>Hrot.Editor.AiShared/Shell/</c>.
///
/// <para>⭐⭐ <b>A view never reads the store.</b> 📌 §2: <i>"only the workspace builds a context"</i>
/// ⇒ ⛔ no window reaches into <see cref="EditorSelectionStore"/> itself, so there is exactly one
/// place where <i>"what is selected"</i> is decided per frame.</para>
///
/// <para>⭐⭐ <b><see cref="Focus"/> and <see cref="Selection"/> are INDEPENDENT</b> *(<c>R-115</c>:
/// <i>"context = focus + selection"</i>)* — the latch says WHO the panel should obey, the set says
/// WHAT they picked. ⛔ Collapsing them is what made a pan look like a selection change.</para>
///
/// <para>⚠ <b>A <c>record</c>, and that is load-bearing</b> — §2b's pan sequence needs
/// <i>"the same context as the frame before"</i> to be answerable. ⭐ Value equality gives that for
/// free, ⛔ provided the LIST INSTANCES are stable, which is what
/// <see cref="EditorSelectionStore.SetSubSelections"/> guarantees.</para>
/// </summary>
/// <param name="Focus">⭐ The last CONTRIBUTING surface to hold focus — a latch, not a live read.</param>
/// <param name="Selection">
///   ⭐ The FULL sub-selection. ⛔ Never null; empty means nothing is selected — 📌 <c>R-118</c>, and
///   the reason the old <c>null</c> had to go: it also meant <i>more than one</i> and <i>unresolvable</i>.
/// </param>
/// <param name="Entities">⭐ The selected entities. ⛔ Never null.</param>
/// <param name="Asset">⚠ The active asset, or <see langword="null"/> when no document is open.</param>
/// <param name="Perspective">⭐ Which workspace asked — <c>Blueprint</c> · <c>BTree</c> · <c>HSM</c> · …</param>
/// <param name="Mode">⭐ The run state — 📌 <c>R-111</c>: <i>"the mode joins the context; one view, many modes."</i></param>
public sealed record DetailsContext(
    SelectionOrigin                   Focus,
    IReadOnlyList<IAssetSubSelection> Selection,
    IReadOnlyList<Entity>             Entities,
    IEditableAsset?                   Asset,
    string                            Perspective,
    VariableRunState                  Mode)
{
    /// <summary>⭐ The empty lists, hoisted — see <see cref="Empty"/> for why the INSTANCE matters.</summary>
    private static readonly IReadOnlyList<IAssetSubSelection> NoSelection = Array.Empty<IAssetSubSelection>();
    private static readonly IReadOnlyList<Entity>             NoEntities  = Array.Empty<Entity>();

    /// <summary>
    /// ⭐ Nothing selected, nothing open. ⚠ Used where a context is structurally required before a
    /// workspace exists — ⛔ not as a fallback for "I could not build one", which would hide a defect.
    /// </summary>
    public static DetailsContext Empty(string perspective) =>
        new(SelectionOrigin.Unknown, NoSelection, NoEntities, null, perspective, VariableRunState.Planning);
}

/// <summary>
/// ⭐⭐⭐ <b><c>L0.3</c> — THE BUILDER. One place assembles the context, from the five live sources.</b>
/// 📄 §6 <c>L0.3</c>: <i>"<c>DetailsContext</c> + the builder — all five sources present."</i>
///
/// <para>⭐⭐ <b>Why this is a free function and not <c>PerspectiveWorkspace.BuildContext</c> yet.</b>
/// 📐 §2's classDiagram does put <c>BuildContext()</c> on <c>PerspectiveWorkspace</c> — ⚠ but that type
/// is <c>L1.1</c>, and §6's dependency graph runs <c>L0.3 → L1.1</c>. ⇒ ⭐ the ASSEMBLY LOGIC lands
/// here now and <c>L1.1</c> hosts it on the workspace, so <c>L1</c> moves a call rather than writing
/// this twice *(ruling 9)*. ⛔ Stated because it is a deviation from where the diagram draws it.</para>
///
/// <para>⚠⚠ <b>The entity source is <c>L0.4</c>'s, and it is NOT wired yet.</b> 📌 §6 <c>L0.4</c>:
/// <i>"entity selection: DELETE two copies, read <c>SelectionState</c> from the World"</i>
/// *(<c>R-122</c>)*. ⭐ Until that lands this reads the store's single <c>SelectedEntity</c> — the
/// source every existing panel already reads — so the field is HONEST rather than empty, and
/// <c>L0.4</c> re-points one method. ⛔ It is not a silent default: the parameter is explicit and this
/// paragraph is the record of what it will become.</para>
/// </summary>
public static class DetailsContextBuilder
{
    /// <summary>
    /// ⭐ Assemble this frame's context. ⚠ Allocation-free when nothing changed: the selection list is
    /// the store's OWN instance *(not a copy)*, which is what makes §2b's <i>"a pan yields the same
    /// context"</i> hold — ⛔ copying here would defeat the store's stability guarantee.
    /// </summary>
    public static DetailsContext Build(
        EditorSelectionStore store,
        string               perspective,
        VariableRunState     mode,
        IReadOnlyList<Entity>? entities = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(perspective);

        return new DetailsContext(
            Focus:       store.FocusedSurface,
            Selection:   store.ActiveSubSelections,        // ⭐ the store's instance, not a copy
            Entities:    entities ?? EntitiesOf(store),
            Asset:       store.ActiveAsset,
            Perspective: perspective,
            Mode:        mode);
    }

    /// <summary>
    /// ⚠ <b><c>L0.4</c> REPLACES THIS ENTIRE METHOD</b> with a World query over <c>SelectionState</c>
    /// *(<c>R-122</c>)*. ⭐ Isolated deliberately, so that task is a one-method change.
    ///
    /// <para>⛔ <b>The cached single is not an optimisation, it is the same-instance rule</b>
    /// *(§6 <c>L0.4</c>: "return the same list instance when unchanged, or every view rebuilds per
    /// frame")* — ⚠ a fresh <c>new[]{ e }</c> every frame would make every context unequal to the last
    /// and defeat the pan guarantee through the ENTITY field instead of the selection one.</para>
    /// </summary>
    private static IReadOnlyList<Entity> EntitiesOf(EditorSelectionStore store)
    {
        var selected = store.SelectedEntity;
        if (selected is null) return Array.Empty<Entity>();

        var cached = _lastEntities;
        if (cached is { Length: 1 } && cached[0].Equals(selected.Value)) return cached;

        return _lastEntities = new[] { selected.Value };
    }

    [ThreadStatic] private static Entity[]? _lastEntities;
}
