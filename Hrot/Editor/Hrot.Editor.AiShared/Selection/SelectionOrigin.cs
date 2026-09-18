namespace Hrot.Editor.AiShared.Selection;

/// <summary>
/// ⭐⭐⭐ <b>WHICH SURFACE a selection came from — the thing the selection cache never recorded.</b>
///
/// <para>📌 <b>User ruling, <c>2026-08-18</c>:</b> <i>"it's not the selection what changes but actually
/// the focus to different part of the UI (from MyBlueprint to graph canvas)… the editor selection
/// cache should contain what the selected UI item comes from (what panel etc.). Otherwise we would
/// need to report and handle the click to every possible UI component."</i></para>
///
/// <para>🔴🔴 <b>The defect this closes (<c>B8</c>).</b> <c>BlueprintDetailsWindow</c> decided which arm
/// owns the panel by comparing <c>ActiveSubSelection</c> to a snapshot — a <b>value equality</b> test
/// standing in for a <b>time</b> claim *("a node selection that arrived AFTER the variable list wins it
/// back")*. ⛔ Re-clicking the SAME node is <c>Equals</c> to the snapshot, so it could never win the
/// panel back — ⚠ and that is the gesture a designer actually performs.</para>
///
/// <para>⛔⛔ <b>Why "detect the re-click" was NOT the fix.</b> 📐 Measured through all four layers: the
/// click never becomes a signal. <c>CanvasInput</c> guards its assignment with
/// <c>!Selection.Contains(node)</c> — <b>clicking an already-selected node is a deliberate no-op</b>,
/// so that dragging a multi-selection does not collapse it. <c>SelectionState</c> is a plain set with
/// no version and no event; the per-frame bridge assigns unconditionally; and the store short-circuits
/// on <c>Equals</c>. ⇒ ⭐⭐ <b>the question was never "did the node change?" but "which surface is the
/// designer working in?"</b>, and FOCUS answers that where a click cannot.</para>
///
/// <para>⭐ <b>Shared across Blueprint, BTree and HSM</b> *(<c>Q32</c> ruling 6 — one Details panel for
/// every asset type)*, which is why it lives in <c>AiShared</c> beside the store rather than in any
/// one host.</para>
/// </summary>
public enum SelectionOrigin
{
    /// <summary>
    /// ⛔ Nobody has claimed the panel. ⭐ The default so a store that was never told cannot silently
    /// impersonate a surface — the <c>Unresolved = 0</c> shape <c>VariableKind</c> already uses.
    /// </summary>
    Unknown = 0,

    /// <summary>⭐ The graph canvas — a node, link or comment the designer picked on the graph.</summary>
    GraphCanvas,

    /// <summary>⭐ The My Blueprint / outline panel — a variable, parameter or graph row.</summary>
    VariableOutline,

    /// <summary>
    /// ⭐⭐⭐ <c>U5</c> / <c>E3</c> — <b>the MAP BACKGROUND itself is the focused surface</b>: the operator
    /// clicked the map, not an entity on it.
    ///
    /// <para>⭐ This is the whole of <c>U5</c>'s answer, and it needed no new seam. The design recorded
    /// the gap as <i>"the details shell has no 'map background selected' CONTEXT —
    /// <c>PopulateEmptyMapMenu</c> proves empty space is a click target for a MENU; selection-as-context
    /// is new"</i>, and predicted the fix would be <i>"a small addition to an existing interface, not new
    /// infrastructure"</i>. 📐 Measured: the machinery is already end-to-end —
    /// <c>EditorSelectionStore.NotifySurfaceFocused</c> is how a surface claims focus (a LEVEL, called
    /// every frame it holds it), <c>DetailsContext.Focus</c> carries it, and
    /// <c>DetailsViewPredicates.FocusIs</c> is how a view's predicate reads it. ⇒ the map is just
    /// another contributing surface, and the zones view's predicate is one <c>FocusIs</c> call.</para>
    ///
    /// <para>⚠ <b>Why it is the BACKGROUND and not "the map".</b> Clicking an ENTITY on the map already
    /// produces an entity selection, which the entity-shaped views route on. ⛔ Naming this
    /// <c>Map</c> would make those two gestures indistinguishable, and the zones list is precisely what
    /// you want when nothing on the map is selected.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.5, §3-U5.
    /// </summary>
    MapBackground,
}
