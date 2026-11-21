using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// BP-205 — the ImGui id scope the Details panel pushes around a node's drawer.
///
/// <para>
/// ⭐ <b>The defect.</b> Every drawer labels its widgets by role — <c>"Format"</c>, <c>"Level"</c>,
/// <c>"Result Type"</c> — and ImGui derives a widget's identity from its label within the current id
/// stack. <c>BlueprintDetailsWindow</c> called <c>session.Draw()</c> with <b>no scope at all</b>, so a
/// <c>Print String</c>'s "Format" field and a <c>Format String</c>'s "Format" field were, to ImGui, the
/// <b>same widget</b>. Selecting the second node handed it the first node's live input buffer — the
/// user saw a <c>Format String</c> holding a <c>Print String</c>'s text.
/// </para>
///
/// <para>
/// ⚠ <b>Fixed at the composition root, not in the drawers.</b> Pushing a per-node id once in the panel
/// covers every drawer that exists and every drawer anyone adds later. Renaming labels to be unique per
/// node kind would fix the two nodes that were noticed and leave the rule unstated, which is how
/// <c>ImGuiBufferText</c>'s family (BP-86) kept recurring.
/// </para>
///
/// <para>
/// Deliberately free of any ImGui dependency, exactly as <c>ContinuousEditCoalescer</c> is: the
/// <i>rule</i> — distinct per node, stable across frames for the same node — is what can be wrong, and
/// keeping it here makes it headlessly testable. The ImGui call that consumes it is one line.
/// </para>
/// </summary>
internal static class DetailsIdScope
{
    /// <summary>
    /// The id pushed before drawing <paramref name="node"/>'s Details.
    ///
    /// <para>
    /// ⚠ Keyed on the node's GUID, not its kind: two nodes of the SAME kind must not share a buffer
    /// either. Selecting one Print String after another was the same defect and the same surprise.
    /// </para>
    /// </summary>
    internal static string For(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return "bp-details-" + node.Id.ToString("N");
    }
}
