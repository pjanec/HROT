using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Common.Events;

/// <summary>
/// How a <see cref="SelectionChangeRequest"/> combines with the selection that already exists.
/// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.1 — <c>UXI-11</c> slice <c>S-2</c>.
/// </summary>
public enum SelectionChangeMode
{
    /// <summary>The request's set becomes the whole selection. The default click.</summary>
    Replace = 0,

    /// <summary>The request's set joins the existing selection. Ctrl/Shift-click, additive box-select.</summary>
    Add = 1,

    /// <summary>The request's set leaves the existing selection. Ctrl-click on a selected entity.</summary>
    Remove = 2,

    /// <summary>The selection empties. <c>Entities</c> is ignored.</summary>
    Clear = 3,
}

/// <summary>
/// <b>The one request every surface publishes to change the selection.</b>
/// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.1 / §2.7.2 — <c>UXI-11</c> slice <c>S-2</c>.
///
/// <para>⭐⭐⭐ <b>Only <c>SelectionRequestSystem</c> consumes it, and only that system writes the
/// truth</b> (§2.7.3 rule 1). A panel, a map tool, the orbat and the remote-map dispatcher all publish
/// this and know nothing else about selection — ⛔ in particular they do not reach for
/// <c>ISelectionState</c> and they do not touch the <c>SelectionState</c> component.</para>
///
/// <para>⚠ <b>Entity-addressed on purpose.</b> 🔒 §2.7.3 rule 7: selection is <b>HOST-LOCAL</b>; DDS
/// carries it only for direct 2-D map control and is <b>translated at the boundary</b>. ⇒ the internal
/// request speaks the host's own handle. ⭐ The network-id-addressed form is
/// <see cref="SelectEntityCommand"/>, which the same system consumes as a <see cref="Replace"/> — that
/// is the boundary, and it is one line.</para>
///
/// <para>⛔ <b>A MANAGED event, and it has to be.</b> A blittable struct cannot carry a set, and the
/// whole point of <c>S-2</c> is that a rubber band selects N entities in one request. 🔒 <c>R-134</c>:
/// no DDS type in the internal path — this is a plain FDP record, not a topic.</para>
///
/// <para>⚠ <b>One frame of latency is expected</b>, not a defect: the publisher's change is visible
/// after the bus swaps and the system runs. 📄 §2.5 — <em>"one-frame latencies are structural"</em>.
/// ⛔ A caller that reads the selection back in the SAME frame it requested a change will see the old
/// value; the two host-facade seams that do so are named in §2.7.7.</para>
/// </summary>
public sealed class SelectionChangeRequest
{
    /// <summary>
    /// The entities the request is about. ⚠ Ignored when <see cref="Mode"/> is
    /// <see cref="SelectionChangeMode.Clear"/>. Never null.
    /// </summary>
    public IReadOnlyList<Entity> Entities { get; init; } = System.Array.Empty<Entity>();

    /// <summary>How <see cref="Entities"/> combines with what is already selected.</summary>
    public SelectionChangeMode Mode { get; init; } = SelectionChangeMode.Replace;

    /// <summary>
    /// Free-text origin, for diagnostics only. ⛔ Nothing may branch on it — a request is a request,
    /// whoever sent it (§2.7.3 rule 2).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>Replace the whole selection with one entity — the ordinary click.</summary>
    public static SelectionChangeRequest ReplaceWith(Entity entity, string? reason = null)
        => new() { Entities = new[] { entity }, Mode = SelectionChangeMode.Replace, Reason = reason };

    /// <summary>Replace the whole selection with a set — a rubber band, a multi-pick.</summary>
    public static SelectionChangeRequest ReplaceWith(IReadOnlyList<Entity> entities, string? reason = null)
        => new() { Entities = entities, Mode = SelectionChangeMode.Replace, Reason = reason };

    /// <summary>Empty the selection.</summary>
    public static SelectionChangeRequest ClearAll(string? reason = null)
        => new() { Mode = SelectionChangeMode.Clear, Reason = reason };
}
