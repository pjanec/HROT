using System;
using Fdp.Core;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Describes a single component mutation staged by the operator while the
/// simulation is paused. Applied at the N+1 tick boundary when the operator
/// clicks Step or Continue.
/// </summary>
public readonly struct PendingDebugMutation
{
    /// <summary>The entity whose component is to be mutated.</summary>
    public readonly Entity Target;

    /// <summary>Component type id resolved via ComponentTypeRegistry.</summary>
    public readonly int ComponentTypeId;

    /// <summary>
    /// True for managed-reference components (classes); false for unmanaged structs.
    /// </summary>
    public readonly bool IsManaged;

    /// <summary>
    /// Boxed payload: either a boxed unmanaged struct or a managed class reference.
    /// </summary>
    public readonly object Payload;

    /// <summary>
    /// Size in bytes for unmanaged structs (Marshal.SizeOf of the component type).
    /// 0 for managed components.
    /// </summary>
    public readonly int SizeBytes;

    /// <summary>
    /// ⭐⭐ Ruling 14 — byte offset of a SURGICAL field write inside the component, or <c>-1</c> for a
    /// whole-component write.
    ///
    /// <para>
    /// 🔴 <b>Why the distinction is load-bearing.</b> The staged payload is built from what the editor
    /// saw while PAUSED, which is the <b>pre-tick</b> snapshot; the drain runs <b>after</b>
    /// <c>_liveRepo.SyncFrom(_postTickSnapshot)</c>. ⇒ a whole-component write puts every field the
    /// designer did not touch back to its pre-tick value — on the shared <c>Blackboard1024</c> that
    /// reverts BTree and HSM state by a tick, silently.
    /// </para>
    /// </summary>
    public readonly int ByteOffset;

    /// <summary>True when this mutation patches a byte range rather than replacing the component.</summary>
    public bool IsFieldWrite => ByteOffset >= 0;

    public PendingDebugMutation(
        Entity target,
        int componentTypeId,
        bool isManaged,
        object payload,
        int sizeBytes,
        int byteOffset = -1)
    {
        Target          = target;
        ComponentTypeId = componentTypeId;
        IsManaged       = isManaged;
        Payload         = payload;
        SizeBytes       = sizeBytes;
        ByteOffset      = byteOffset;
    }
}
