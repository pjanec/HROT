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

    public PendingDebugMutation(
        Entity target,
        int componentTypeId,
        bool isManaged,
        object payload,
        int sizeBytes)
    {
        Target          = target;
        ComponentTypeId = componentTypeId;
        IsManaged       = isManaged;
        Payload         = payload;
        SizeBytes       = sizeBytes;
    }
}
