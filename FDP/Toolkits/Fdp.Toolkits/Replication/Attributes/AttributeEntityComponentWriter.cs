using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐⭐⭐ <b>The one implementation, and it deliberately knows NOTHING about which component an attribute
/// maps to.</b>
///
/// <para>⚠ <b>The interface and <c>EntityWriteRoute</c> moved to <c>Fdp.Toolkits</c></b>
/// *(<c>Fdp.Toolkit.Replication.Patching</c>, <c>AX-007</c>)* so <c>Hrot.Presentation</c>'s
/// <c>EntityDragGizmo</c> can depend on the seam without depending on the network assembly. ⭐ This file
/// keeps the implementation, which needs the interpreter the network installers build.</para>
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
    private readonly BinaryInterpreter<EntityAttributeChange> _interpreter;
    private readonly Action<Entity, IReadOnlyList<EntityAttributeChange>>? _publishRequest;

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
    ///
    /// <para>⭐⭐ It takes the WHOLE change list, not one change: <c>AX-007</c>'s multi-attribute write must
    /// reach the owner as ONE request — see the interface's remarks on why.</para>
    /// </param>
    public AttributeEntityComponentWriter(
        EntityRepository repo,
        BinaryInterpreter<EntityAttributeChange> interpreter,
        Action<Entity, IReadOnlyList<EntityAttributeChange>>? publishRequest = null)
    {
        _repo           = repo ?? throw new ArgumentNullException(nameof(repo));
        _interpreter    = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _publishRequest = publishRequest;
    }

    /// <inheritdoc/>
    public EntityWriteRoute Write(Entity entity, ushort attributeId, double value)
        // ⭐⭐⭐ R-134 — FDP-INTERNAL ONLY. ⛔ This used to build a DDS `AttributeRecord` +
        //    `AttributeValueUnion` + `AttributeValueType.KindFloat64`, which put network structure in the
        //    internal write path — the as-built coupling AX-005a removes. 📄 design §11.2.
        => Write(entity, new[] { EntityAttributeChange.Double(attributeId, value) });

    /// <inheritdoc/>
    public EntityWriteRoute Write(Entity entity, IReadOnlyList<EntityAttributeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0) return EntityWriteRoute.Refused;
        if (!_repo.IsAlive(entity)) return EntityWriteRoute.Refused;

        // ── Attempt the LOCAL apply, through the owner's own path ──────────────
        var patchCtx = EcsPatchContext.Create(_repo, entity);
        var binaryCtx = _interpreter.CreateContext(patchCtx);
        binaryCtx.Repo   = _repo;
        binaryCtx.Entity = entity;

        // ⚠ An array, not `stackalloc`: `AttributeValue` carries a `string?`, so
        //   `EntityAttributeChange` is a MANAGED type and cannot live on the stack. ⭐ One allocation per
        //   operator gesture — this path is driven by a mouse release, not per tick.
        _interpreter.Apply(binaryCtx, AsArray(changes));

        if (patchCtx.HasAppliedAny)
            return EntityWriteRoute.Direct;

        // ── Not ours ⇒ ask the owner ───────────────────────────────────────────
        if (_publishRequest == null) return EntityWriteRoute.Refused;

        _publishRequest(entity, changes);
        return EntityWriteRoute.Requested;
    }

    /// <summary>⭐ Avoids a copy when the caller already handed us an array.</summary>
    private static EntityAttributeChange[] AsArray(IReadOnlyList<EntityAttributeChange> changes)
    {
        if (changes is EntityAttributeChange[] array) return array;

        var result = new EntityAttributeChange[changes.Count];
        for (int i = 0; i < changes.Count; i++) result[i] = changes[i];
        return result;
    }
}
