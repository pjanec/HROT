using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// Generic transient component carrying per-entity debug bit-flags grouped by
    /// subsystem. The first group is <see cref="Behavior"/>; future groups for
    /// physics/network/etc. can be appended as additional fields of the same shape.
    /// </summary>
    /// <remarks>
    /// Lives in the FDP toolkit layer (not <c>Hrot.Common</c>) so the BTree/HSM tick
    /// systems inside <c>Fdp.Toolkits</c> can read it without forcing an upward
    /// project reference. <c>StructEdit</c> natively renders <c>[Flags]</c> enums as
    /// per-bit checkboxes, so no custom inspector renderer is needed.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(BehaviorApplicationComponentIds.DebugState)]
    [DataPolicy(DataPolicy.Transient)]
    public struct DebugState
    {
        public BehaviorDebugFlags Behavior;
    }
}
