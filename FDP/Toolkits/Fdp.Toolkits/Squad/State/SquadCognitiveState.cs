using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Squad
{
    // ── ElementPartition — 32 bytes ──────────────────────────────────────────────
    // MemberElementIndexArray(16) + LastRepartitionTick(4) + _pad(12) = 32 bytes.

    /// <summary>
    /// Inline array of 16 element-index bytes, one per squad member (by roster index).
    /// Element 0 = covering, 1 = bounding, 2 = overwatch, etc.
    /// </summary>
    [InlineArray(16)]
    public struct MemberElementIndexArray
    {
#pragma warning disable CS0169
        private byte _element;
#pragma warning restore CS0169
    }

    /// <summary>
    /// Records which squad element each member belongs to and when the last
    /// repartition occurred.  32 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ElementPartition
    {
        /// <summary>Per-member element index (by roster slot). 0=covering, 1=bounding, 2=overwatch.</summary>
        public MemberElementIndexArray MemberElements;
        /// <summary>Simulation tick when the last element repartition ran.</summary>
        public uint LastRepartitionTick;
        private uint _pad0;
        private uint _pad1;
        private uint _pad2;
    }

    // ── SlotState — 8 bytes ──────────────────────────────────────────────────────
    // ElementIndex(1) + SlotKind(1) + Flags(2) + LastTransitionTick(4) = 8 bytes.

    /// <summary>
    /// Per-element-slot state: which squad element owns it, its kind, flags, and
    /// last transition tick.  8 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SlotState
    {
        /// <summary>Element index owning this slot (matches <see cref="MemberElementIndexArray"/> values).</summary>
        public byte ElementIndex;
        /// <summary>Slot-kind discriminator (e.g. 0=move, 1=cover, 2=overwatch).</summary>
        public byte SlotKind;
        /// <summary>Bitmask of per-slot flags (occupied, rotation-pending, etc.).</summary>
        public ushort Flags;
        /// <summary>Tick at which the last slot-state transition was committed.</summary>
        public uint LastTransitionTick;
    }

    /// <summary>
    /// Inline array of 12 <see cref="SlotState"/> entries (12 * 8 = 96 bytes).
    /// Indexed by slot id (not member roster index).
    /// </summary>
    [InlineArray(12)]
    public struct SlotAssignmentArray
    {
#pragma warning disable CS0169
        private SlotState _element;
#pragma warning restore CS0169
    }

    // ── RoleSlot — 2 bytes ───────────────────────────────────────────────────────

    /// <summary>
    /// Compact per-member role assignment.  2 bytes (RoleId + pad).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RoleSlot
    {
        /// <summary>Role identifier assigned to this member (0 = unassigned).</summary>
        public byte RoleId;
        private byte _pad;
    }

    /// <summary>
    /// Inline array of 16 <see cref="RoleSlot"/>s (16 * 2 = 32 bytes).
    /// Indexed by roster slot.
    /// </summary>
    [InlineArray(16)]
    public struct RoleAssignmentArray
    {
#pragma warning disable CS0169
        private RoleSlot _element;
#pragma warning restore CS0169
    }

    // ── SquadContact — 32 bytes ──────────────────────────────────────────────────
    // EntityId(8) + Position(12) + ThreatScore(4) + LastSeenTick(4) + Flags(2) + SourceMembersMask(2) = 32 bytes.

    /// <summary>
    /// A single merged contact in the squad's shared situational-awareness pool.
    /// 32 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadContact
    {
        /// <summary>Packed entity handle of the perceived contact.</summary>
        public long EntityId;
        /// <summary>Last known 3-D world position (X, Y, Z).</summary>
        public float PositionX;
        /// <summary>Last known 3-D world position (X, Y, Z).</summary>
        public float PositionY;
        /// <summary>Last known 3-D world position (X, Y, Z).</summary>
        public float PositionZ;
        /// <summary>Merged threat score in [0, 1] (max across reporting members).</summary>
        public float ThreatScore;
        /// <summary>Tick at which the most recent sighting was recorded.</summary>
        public uint LastSeenTick;
        /// <summary>Per-contact status flags (e.g. visible, suppressed).</summary>
        public ushort Flags;
        /// <summary>Bitmask of which squad members have reported this contact (bit i = member slot i).</summary>
        public ushort SourceMembersMask;
    }

    /// <summary>
    /// Inline array of 16 <see cref="SquadContact"/>s (16 * 32 = 512 bytes).
    /// </summary>
    [InlineArray(16)]
    public struct SquadContactPoolSlots
    {
#pragma warning disable CS0169
        private SquadContact _element;
#pragma warning restore CS0169
    }

    // ── SquadContactPool — 592 bytes ─────────────────────────────────────────────
    // Count(4) + LastMergeTick(4) + _memberEpochChecksum(8) + _reserved(8*8=64) + Contacts(512) = 592 bytes.

    /// <summary>
    /// Shared situational-awareness contact pool for the squad.  592 bytes.
    /// Insertion-sorted by threat score (descending); capacity-bounded at 16 contacts.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadContactPool
    {
        /// <summary>Number of valid contacts in <see cref="Contacts"/> (0..16).</summary>
        public int Count;
        /// <summary>Tick at which the last perception-merge pass ran.</summary>
        public uint LastMergeTick;
        /// <summary>XOR checksum of all subordinate TargetMemory.ChangeEpoch values
        /// at the last merge tick. The merge system uses this to detect any structural
        /// change in any member's TargetMemory without storing per-member values.</summary>
        internal ulong _memberEpochChecksum;
        /// <summary>Tick at which the last ManeuverSelect scorer pass ran.</summary>
        public uint LastManeuverSelectTick;
        private uint _r1hi;
        private ulong _r2;
        private ulong _r3;
        private ulong _r4;
        private ulong _r5;
        private ulong _r6;
        private ulong _r7;
        private ulong _r8;
        /// <summary>Per-contact data, sorted by descending ThreatScore.</summary>
        public SquadContactPoolSlots Contacts;
    }

    // ── SquadCognitiveState — 1024 bytes ─────────────────────────────────────────
    // Scalars@0(16) + Elements@16(32) + Slots@48(96) + Roles@144(32)
    //   + Assignment@176(256) + Contacts@432(592) = 1024 bytes.

    /// <summary>
    /// Squad movement mode broadcast to members by <see cref="SquadMovementModeBroadcastSystem"/>.
    /// </summary>
    public enum MovementMode : byte
    {
        Normal  = 0,
        Covered = 1,
        Fast    = 2
    }

    /// <summary>
    /// Full squad working state projected onto a commander's <see cref="Blackboard1024"/>.
    /// A single contiguous 1024-byte claim; use <see cref="Project"/> to obtain a ref.
    /// </summary>
    /// <remarks>
    /// Sub-region byte offsets (verified by <c>SquadCognitiveStateLayoutTests</c>):
    /// <list type="bullet">
    ///   <item>Scalars : 0 (16 B)</item>
    ///   <item>Elements: 16 (32 B)</item>
    ///   <item>Slots   : 48 (96 B)</item>
    ///   <item>Roles   : 144 (32 B)</item>
    ///   <item>Assignment: 176 (256 B)</item>
    ///   <item>Contacts: 432 (592 B)</item>
    /// </list>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct SquadCognitiveState
    {
        // ---- Scalars (offset 0, 16 bytes) ----
        /// <summary>Active maneuver kind (catalog entry §8).</summary>
        public ushort ManeuverKind;
        /// <summary>Current squad-HSM phase identifier.</summary>
        public ushort PhaseId;
        /// <summary>FeatureId of the danger area / feature currently being worked.</summary>
        public uint ActiveFeatureId;
        /// <summary>Tick at which the current phase was entered (dwell/timeout guard).</summary>
        public uint PhaseEnteredTick;
        /// <summary>Scalar flags. Bit 0 = mission-override active.</summary>
        // Flags bit layout:
        // bit 0  = MissionOverrideBit (set by Phase 3)
        // bit 1  = (reserved)
        // ...
        // bits 8-9 = MovementMode (0=Normal, 1=Covered, 2=Fast)
        public uint Flags;

        // ---- Elements (offset 16, 32 bytes) ----
        /// <summary>Element-partition: member-to-element index and repartition cadence.</summary>
        public ElementPartition Elements;

        // ---- Slots (offset 48, 96 bytes) ----
        /// <summary>Per-element-slot state (rotation, burn, ownership).</summary>
        public SlotAssignmentArray Slots;

        // ---- Roles (offset 144, 32 bytes) ----
        /// <summary>Per-member role assignments (indexed by roster slot).</summary>
        public RoleAssignmentArray Roles;

        // ---- Assignment (offset 176, 256 bytes) ----
        /// <summary>
        /// Per-subordinate target assignment slots (16 * 16 bytes = 256 bytes).
        /// Shared with the threat-matrix assignment system.
        /// </summary>
        public AssignmentSlotArray Assignment;

        // ---- Contacts (offset 432, 592 bytes) ----
        /// <summary>Shared situational-awareness contact pool (merged from all member perceptions).</summary>
        public SquadContactPool Contacts;

        /// <summary>
        /// Projects the 1024-byte blackboard memory as a ref to <see cref="SquadCognitiveState"/>.
        /// </summary>
        public static ref SquadCognitiveState Project(ref Blackboard1024 bb)
            => ref Blackboard1024.Project<SquadCognitiveState>(ref bb);
    }

    // ── SquadCognitiveStateOffsets — compile-time byte-offset constants ──────────

    /// <summary>
    /// Byte offsets of the major sub-regions within <see cref="SquadCognitiveState"/>.
    /// Used for unsafe pointer arithmetic in tests and low-level diagnostics.
    /// </summary>
    public static class SquadCognitiveStateOffsets
    {
        public const int Scalars    =   0;
        public const int Elements   =  16;
        public const int Slots      =  48;
        public const int Roles      = 144;
        public const int Assignment = 176;
        public const int Contacts   = 432;
        public const int TotalSize  = 1024;
    }

    // ── SquadStateMarker — ECS tag component ────────────────────────────────────

    /// <summary>
    /// Zero-data ECS tag marking an entity that carries a <see cref="SquadCognitiveState"/>
    /// projected onto its <see cref="Blackboard1024"/>.
    /// Required for component-ID determinism across assemblies (Phase R0).
    /// </summary>
    [ComponentId(GlobalComponentIds.SquadStateMarker)]
    [StructLayout(LayoutKind.Sequential, Size = 1)]
    public struct SquadStateMarker { }
}
