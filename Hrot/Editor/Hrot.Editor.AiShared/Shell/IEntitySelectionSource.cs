using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L0.4</c> — WHICH ENTITIES ARE SELECTED, asked of the WORLD rather than of a copy.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L0.4</c>
/// *(<i>"entity selection: DELETE two copies, read <c>SelectionState</c> from the World"</i>)* ·
/// §2's <c>classDiagram</c> *(<c>PerspectiveWorkspace ..&gt; World : reads entity selection</c>,
/// <c>World o-- "0..*" SelectionState</c>)* · 📌 <c>R-122</c>.
///
/// <para>⭐⭐ <b>The design record, cited rather than inferred</b> —
/// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §0/§2.1: <c>SelectionState</c> <i>"is already correct for
/// multi-select"</i>, is written by <c>SelectionInteractionSystem</c> *(click · rubber-band)* and read
/// by the ring gizmos; ⛔ <c>ISelectionState</c>/<c>DefaultSelectionState</c>'s <c>HashSet</c> and
/// <c>SimHostInspectorAdapter</c> are marked 🔴 <b>"the defect — a second, parallel in-memory
/// store"</b>. ⇒ ⭐ <b>the World is the truth and everything else is a view.</b></para>
///
/// <para>⚠⚠ <b>SCOPE, stated so nobody reads more into this than it does.</b> ⛔ This does NOT perform
/// <c>UX_Feature_Selection.md</c>'s <c>ISelectionState</c> → <c>EcsSelectionState</c> migration — that
/// is <c>UXI-11</c>'s own programme, and §2.1 is explicit that the interface <b>keeps its shape</b>.
/// ⛔ Nor does it delete <c>EntityInspectorPanel</c>'s <c>HashSet</c>: 📄 §6 <c>L6.3</c> deletes that
/// one, by name, when the Components view wraps it. ⭐ What this does is make <b>the Details
/// context</b> read the World, so no view is ever fed a copy.</para>
///
/// <para>⭐⭐⭐ <b>THE SAME-INSTANCE CONTRACT IS PART OF THE INTERFACE, not an optimisation.</b>
/// 📄 §6 <c>L0.4</c>, verbatim: <i>"⚠ return the <b>same list instance</b> when unchanged, or every
/// view rebuilds per frame."</i> ⛔ A fresh list per frame would make every <see cref="DetailsContext"/>
/// unequal to the last and defeat §2b's pan guarantee <b>through the ENTITY field</b> — ⚠ which is the
/// same defect <c>L0.1</c> fixed on the selection field, arriving by a different door.</para>
/// </summary>
public interface IEntitySelectionSource
{
    /// <summary>
    /// ⭐ The selected entities, primary first. ⛔ Never <see langword="null"/>; empty means nothing is
    /// selected.
    /// <para>⭐⭐ <b>Implementations MUST return the same instance while the selection is unchanged</b> —
    /// see the interface remarks. ⚠ This is a contract a caller relies on, ⛔ not advice.</para>
    /// </summary>
    IReadOnlyList<Entity> Selected();
}

/// <summary>
/// ⭐ <b>Nothing is selected, ever.</b> ⚠ The honest default for a host with no World — headless tests,
/// and the standalone constructions that predate the shell.
///
/// <para>⛔⛔ <b>It is NOT a licence for a production caller to skip the real one.</b> 📌 The
/// <c>2026-08-16</c> rule: <i>"a production caller that HAS a dependency must PASS it"</i>, and the
/// control is <b>a rail on the CONSTRUCTED object</b> — see
/// <c>TheEntityContextReadsTheWorldTests</c>, which asserts the production registrar's source is a real
/// one and not this.</para>
/// </summary>
public sealed class EmptyEntitySelection : IEntitySelectionSource
{
    public static readonly EmptyEntitySelection Instance = new();

    private static readonly IReadOnlyList<Entity> None = Array.Empty<Entity>();

    /// <summary>⭐ Always the SAME instance — the contract holds trivially.</summary>
    public IReadOnlyList<Entity> Selected() => None;
}
