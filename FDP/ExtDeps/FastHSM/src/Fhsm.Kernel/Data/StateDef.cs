using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// State definition (ROM). Exactly 32 bytes.
    /// Defines the topology and behavior of a single state.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct StateDef
    {
        // === Topology (12 bytes) ===
        [FieldOffset(0)] public ushort ParentIndex;         // Parent state (0xFFFF = root)
        [FieldOffset(2)] public ushort FirstChildIndex;     // First child state
        [FieldOffset(4)] public ushort ChildCount;          // Number of children
        [FieldOffset(6)] public ushort FirstTransitionIndex; // First transition index
        [FieldOffset(8)] public ushort TransitionCount;     // Number of transitions
        [FieldOffset(10)] public byte Depth;                // Hierarchy depth (0-16)
        [FieldOffset(11)] public byte RegionCount;          // Orthogonal regions

        // === Actions (6 bytes) ===
        [FieldOffset(12)] public ushort OnEntryActionId;    // Entry action (0 = none)
        [FieldOffset(14)] public ushort OnExitActionId;     // Exit action (0 = none)
        [FieldOffset(16)] public ushort ActivityActionId;   // Update/activity (0 = none)

        // === Flags & Metadata (6 bytes) ===
        [FieldOffset(18)] public StateFlags Flags;          // Behavior flags (2 bytes)
        [FieldOffset(20)] public ushort HistorySlotIndex;   // History slot (0xFFFF = none)
        [FieldOffset(22)] public ushort TimerSlotIndex;     // Timer slot (0xFFFF = none)

        // === Regions (4 bytes) ===
        [FieldOffset(24)] public ushort RegionStartIndex;   // First region index
        [FieldOffset(26)] public ushort InitialChildIndex;  // Initial child (0xFFFF = none)

        // === Navigation & Extra (4 bytes) ===
        [FieldOffset(28)] public byte OutputLaneMask;       // Output lanes this state writes to
        [FieldOffset(29)] public byte Reserved29;           // Padding
        [FieldOffset(30)] public ushort TimerActionId;      // Timer action (0 = none)

        // Total: 32 bytes
    }
}
