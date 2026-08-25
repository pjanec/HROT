using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Patching;
using Hrot.NED.Messages;

namespace Hrot.SimHost.Installers;

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
/// *(classDiagram)* · §6 ③.</para>
///
/// <para>⭐⭐ <b>The caller knows an entity and a target value. It does NOT know who owns the
/// component</b> — that is the whole point, and it is what lets one gizmo work on the editor *(a
/// one-node cluster that owns everything)* and on a distributed node alike.</para>
/// </summary>
public interface IEntityComponentWriter
{
    /// <summary>
    /// Writes <paramref name="value"/> to the attribute <paramref name="attributeId"/> on
    /// <paramref name="entity"/> — directly when this node owns the target component, otherwise as a
    /// change-request for whoever does.
    /// </summary>
    EntityWriteRoute Write(Entity entity, ushort attributeId, double value);
}

/// <summary>
/// ⭐⭐⭐ <b>The one implementation, and it deliberately knows NOTHING about which component an attribute
/// maps to.</b>
///
/// <para>⭐⭐⭐ <b>The mechanism, which is the interesting part.</b> It does not ask *"do I own
/// <c>SimTransform</c>?"* — that would duplicate, in a second place, the attribute→component knowledge
/// that already lives in the installers. Instead it <b>attempts the local apply through the very same
/// <see cref="BinaryInterpreter{TRecord}"/> the OWNER uses</b>, and then asks
/// <see cref="EcsPatchContext.HasAppliedAny"/> whether anything landed:</para>
///
/// <list type="bullet">
/// <item><description>⭐ <b>something landed</b> ⇒ this node owned the component ⇒ <c>Direct</c>. The
/// conversion that ran is the installer's — 📌 <c>HeadingDegToRotation</c>, not a copy of it.</description></item>
/// <item><description>⭐ <b>nothing landed</b> ⇒ <c>UXI-30</c>'s gate refused ⇒ publish the record as a
/// change-request ⇒ <c>Requested</c>.</description></item>
/// </list>
///
/// <para>⇒ ⭐⭐ <b>ONE conversion implementation serves both the local and the remote path</b>, and adding a
/// second attribute needs no change here at all. ⛔ The alternative — a `switch` on attribute id inside
/// the writer — would be exactly the duplicate the installers exist to prevent.</para>
///
/// <para>⚠ <b>The change flag, and why this class does not decide it.</b> The design notes that a direct
/// <c>SimTransform</c> write needs NO change flag *(its egress translator diffs <c>lastSent</c> every
/// tick)*, while other components may. ⭐ The installer's handler is what marks — or does not mark — the
/// descriptor dirty, so the decision stays with the component that knows. 📌 The design's own words:
/// *"the helper ASKS the component whether it needs one — it does not assume."*</para>
///
/// <para>⚠⚠ <b>What this cannot do:</b> the local attempt is not free of side effects if a future
/// installer does partial work before its first ECS touch. 📐 Measured for the two shipped installers and
/// the new heading one: every handler's first act is the gated <c>GetUnmanagedComponent</c> or a
/// scratchpad write that the flusher then discards when unowned. ⭐ Stated rather than assumed, because
/// this is the one property the "attempt then check" shape rests on.</para>
/// </summary>
public sealed class AttributeEntityComponentWriter : IEntityComponentWriter
{
    private readonly EntityRepository _repo;
    private readonly BinaryInterpreter<AttributeRecord> _interpreter;
    private readonly Action<Entity, AttributeRecord>? _publishRequest;

    /// <summary>
    /// Creates the writer.
    /// </summary>
    /// <param name="repo">The local ECS world.</param>
    /// <param name="interpreter">
    /// ⭐ The SAME interpreter the request applier uses — <c>AttributeCompilerFactory.BuildBinaryInterpreter</c>.
    /// ⛔ Not a second one: a divergent interpreter would convert differently on the two paths.
    /// </param>
    /// <param name="publishRequest">
    /// ⭐ Publishes a change-request for an unowned component. ⚠ <see langword="null"/> on a host with no
    /// request egress — a one-node editor, say — where an unowned write has nobody to ask. ⇒ the writer
    /// then answers <see cref="EntityWriteRoute.Refused"/> rather than pretending, which is what lets a
    /// caller SAY the write went nowhere.
    /// </param>
    public AttributeEntityComponentWriter(
        EntityRepository repo,
        BinaryInterpreter<AttributeRecord> interpreter,
        Action<Entity, AttributeRecord>? publishRequest = null)
    {
        _repo           = repo ?? throw new ArgumentNullException(nameof(repo));
        _interpreter    = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _publishRequest = publishRequest;
    }

    /// <inheritdoc/>
    public EntityWriteRoute Write(Entity entity, ushort attributeId, double value)
    {
        if (!_repo.IsAlive(entity)) return EntityWriteRoute.Refused;

        var record = new AttributeRecord
        {
            AttributeId = attributeId,
            // ⭐ Float64 — the Geo* family's value type, and what the heading handler reads.
            Value = new AttributeValueUnion
            {
                ValueType   = AttributeValueType.KindFloat64,
                DoubleValue = value,
            },
        };

        // ── Attempt the LOCAL apply, through the owner's own path ──────────────
        var patchCtx = EcsPatchContext.Create(_repo, entity);
        var binaryCtx = _interpreter.CreateContext(patchCtx);
        binaryCtx.Repo   = _repo;
        binaryCtx.Entity = entity;

        // ⚠ A one-element array, not `stackalloc`: AttributeRecord carries a [DdsManaged] union, so it
        //   is a MANAGED type and cannot live on the stack. ⭐ One allocation per operator gesture — this
        //   path is driven by a mouse release, not per tick.
        var one = new[] { record };
        _interpreter.Apply(binaryCtx, one);

        if (patchCtx.HasAppliedAny)
            return EntityWriteRoute.Direct;

        // ── Not ours ⇒ ask the owner ───────────────────────────────────────────
        if (_publishRequest == null) return EntityWriteRoute.Refused;

        _publishRequest(entity, record);
        return EntityWriteRoute.Requested;
    }
}
