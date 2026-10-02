using Hrot.Editor.AiShared.Shell;

namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// ⭐⭐⭐ <b>A subsystem-provided pane, drawn as a DETAILS VIEW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §4 (<c>L3.1</c>) · <c>DESIGN_Editor_Entity_Selection_Source.md</c> §11.
///
/// <para>⚠⚠ <b>The class comment here used to read <i>"rendered inside the RuntimeInspectorWindow
/// content area … selected at runtime by matching TargetKind to the active asset's kind."</i></b>
/// ⛔ That window is GONE (<c>CE-303</c>): §4 ruled it <i>"registry on the wrong axis (<c>R-112</c>) ⇒
/// <b>dissolves</b>: 3 panes → 3 predicated views"</i>, and the code had kept both for a while.
/// ⭐ A pane is now reached through <c>details.runtime.&lt;kind&gt;</c> and nothing else.</para>
/// </summary>
public interface IRuntimeInspectorPane
{
    /// <summary>The asset kind this pane handles.</summary>
    AssetKind TargetKind { get; }

    /// <summary>
    /// ⭐ Draw the pane's ImGui content. ⛔ Do NOT call <c>ImGui.Begin</c>/<c>End</c> — the shell did.
    ///
    /// <para>⭐⭐⭐ <b><c>CE-303</c> — the CONTEXT is a parameter, and that is the whole point of the
    /// change.</b> 🔴 The pane used to resolve its own entity from a GLOBAL
    /// (<c>EditorSelectionStore.SelectedEntity</c>), so a PINNED copy of this view rendered whatever
    /// was selected NOW rather than the entity it was pinned to. ⇒ ⭐ the context is LIVE when the view
    /// is docked and FROZEN when it is pinned, so honouring it is all pinning requires.</para>
    ///
    /// <para>⚠ <b>A pane that needs no entity may ignore it</b> — 📐 measured: BTree and HSM do, and say
    /// so at their own <c>Draw</c>. ⛔ NOT a default body: 📌 <c>U-5</c>/<c>BP-230</c> — <i>"a default
    /// body is the interface volunteering to lie on an implementer's behalf"</i>, and a pane that
    /// silently kept reading a global is exactly the lie this parameter exists to end.</para>
    /// </summary>
    void Draw(DetailsContext context);
}
