using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// ⭐ How an attribute write was routed. Returned so a caller can SAY what happened —
/// ⛔ "it worked" and "someone else will do it" are different outcomes to an operator.
/// </summary>
public enum EntityWriteRoute
{
    /// <summary>⭐ This node owned the component and wrote it directly into ECS.</summary>
    Direct = 0,

    /// <summary>⭐ This node did not own it, so a change-request was published for the owner.</summary>
    Requested = 1,

    /// <summary>⚠ Nothing was attempted — a dead entity, or no request sink to publish through.</summary>
    Refused = 2,
}

/// <summary>
/// ⭐⭐⭐ <b>Axis-B item ③ — the subsystem-agnostic entity write path.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §2 *(the routing model — user ruling)* · §4
/// *(classDiagram)* · §6 ③ · §11.</para>
///
/// <para>⭐⭐ <b>The caller knows an entity and a target value. It does NOT know who owns the
/// component</b> — that is the whole point, and it is what lets one gizmo work on the editor *(a
/// one-node cluster that owns everything)* and on a distributed node alike.</para>
///
/// <para>⭐⭐⭐ <b>Why the SEAM lives in <c>Fdp.Toolkits</c> and the implementation does not</b>
/// *(moved here <c>2026-08-25</c>, <c>AX-007</c>)*. 📐 Measured: <c>EntityDragGizmo</c> lives in
/// <c>Hrot.Presentation</c>, which ⛔ does NOT reference <c>Hrot.Network.NED</c> — and giving it that
/// reference would drag CycloneDDS into the presentation layer to satisfy an interface that mentions no
/// network type at all. ⭐ The interface is the seam; the seam belongs where both sides can see it.
/// <c>AttributeEntityComponentWriter</c> — which needs the interpreter the network installers build —
/// stays in <c>Hrot.Network.NED</c>.</para>
/// </summary>
public interface IEntityComponentWriter
{
    /// <summary>
    /// Writes <paramref name="value"/> to the attribute <paramref name="attributeId"/> on
    /// <paramref name="entity"/> — directly when this node owns the target component, otherwise as a
    /// change-request for whoever does.
    /// </summary>
    EntityWriteRoute Write(Entity entity, ushort attributeId, double value);

    /// <summary>
    /// ⭐⭐ Writes SEVERAL attributes as ONE change, and that is not a convenience overload.
    ///
    /// <para>📌 <c>AX-007</c>: a drag commits <c>GeoLat</c> AND <c>GeoLon</c>. Sent as two single-attribute
    /// writes they become two independent change-requests, each of which the owner applies through the
    /// partial-update pre-fill path — so the entity visibly lands on the latitude first and the longitude
    /// a round trip later, and a request lost in between leaves it on a coordinate pair the operator never
    /// chose. ⭐ One call ⇒ one interpreter <c>Apply</c> ⇒ one scratchpad flush ⇒ one geodetic conversion,
    /// on both the local and the remote path.</para>
    ///
    /// <para>⚠ The route is reported for the batch as a whole: the changes address one entity, and
    /// ownership of a component is not per-attribute.</para>
    /// </summary>
    EntityWriteRoute Write(Entity entity, IReadOnlyList<EntityAttributeChange> changes);
}
