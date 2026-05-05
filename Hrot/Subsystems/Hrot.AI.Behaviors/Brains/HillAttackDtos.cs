using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    // ── Phase 2: Hill Attack Data Contracts ─────────────────────────────────────
    // Commander and subordinate tank DTOs for the PlatoonHillAttack and
    // HullDownAttackRun behaviors.

    // ─────────────────────────────────────────────────────────────────────────────
    // Commander DTOs (TASK-HA005)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Static configuration parameters for the PlatoonHillAttack commander behavior.
    ///
    /// <para><b>Field layout (52 bytes, fits within the 60-byte
    /// <c>BrainBlackboard</c> parameter region):</b></para>
    /// <list type="bullet">
    ///   <item><c>TargetAreaEntity</c> (Entity, 8 bytes) at offset 0.</item>
    ///   <item>Firing-line segment: <c>StartX, StartY, EndX, EndY</c> (16 bytes) at offset 8.</item>
    ///   <item>Baseline segment: <c>BaselineStartX, BaselineStartY, BaselineEndX, BaselineEndY</c>
    ///     (16 bytes) at offset 24.</item>
    ///   <item>Attack trajectory: <c>AttackDirX, AttackDirY, TankSpacing</c> (12 bytes) at offset 40.</item>
    /// </list>
    /// Total = 8 + 16 + 16 + 12 = 52 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlatoonHillAttackParams
    {
        // TargetAreaEntity first to ensure 8-byte alignment with no padding before floats.

        /// <summary>
        /// ECS entity of the target area polygon (hosts <c>EditablePolyline</c> component).
        /// Resolved at dispatch time; not stored as a network ID here since this lives in
        /// the static param region and is populated on the Brain node by the ingress system.
        /// </summary>
        public Entity TargetAreaEntity;     // 8 bytes, offset 0

        /// <summary>Start of the firing-line segment (X coordinate, metres).</summary>
        public float StartX;               // 4, offset 8

        /// <summary>Start of the firing-line segment (Y coordinate, metres).</summary>
        public float StartY;               // 4, offset 12

        /// <summary>End of the firing-line segment (X coordinate, metres).</summary>
        public float EndX;                 // 4, offset 16

        /// <summary>End of the firing-line segment (Y coordinate, metres).</summary>
        public float EndY;                 // 4, offset 20

        /// <summary>Start of the baseline retreat segment (X coordinate, metres).</summary>
        public float BaselineStartX;       // 4, offset 24

        /// <summary>Start of the baseline retreat segment (Y coordinate, metres).</summary>
        public float BaselineStartY;       // 4, offset 28

        /// <summary>End of the baseline retreat segment (X coordinate, metres).</summary>
        public float BaselineEndX;         // 4, offset 32

        /// <summary>End of the baseline retreat segment (Y coordinate, metres).</summary>
        public float BaselineEndY;         // 4, offset 36

        /// <summary>Normalised attack direction (X component).</summary>
        public float AttackDirX;           // 4, offset 40

        /// <summary>Normalised attack direction (Y component).</summary>
        public float AttackDirY;           // 4, offset 44

        /// <summary>Spacing (metres) between adjacent firing-line slots.</summary>
        public float TankSpacing;          // 4, offset 48
        // Total: 52 bytes
    }

    /// <summary>
    /// Single-field blackboard wrapper used as <c>TBlackboard</c> in the PlatoonHillAttack
    /// <c>BTreeBuilder</c> expression-binding overloads.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlatoonHillAttackBlackboard
    {
        /// <summary>Static configuration parameters for the hill attack.</summary>
        public PlatoonHillAttackParams Params;
    }

    /// <summary>
    /// Mutable working state for the PlatoonHillAttack commander behavior, projected
    /// via <c>Unsafe.As</c> onto <c>Blackboard1024.Memory</c> at the start of each tick.
    ///
    /// <para>All <c>fixed</c> arrays are sized to 8 entries, matching
    /// <c>UnitRoster.MaxSubordinates / 2</c> (maximum wave size from a 16-subordinate platoon).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HillAttackMutableState
    {
        // Fields ordered to satisfy natural alignment and avoid implicit padding.

        /// <summary>
        /// <c>RequestId</c> returned by <c>AreaQueryBatchHelper.RequestAreaQuery</c>.
        /// Stored between <c>Action_RequestAreaQuery</c> and <c>Condition_IsAreaQueryResolved</c>.
        /// </summary>
        public long CachedEqsRequestId;              // 8, offset 0

        /// <summary>Total number of firing-line slots computed from segment length / TankSpacing.</summary>
        public int TotalSlots;                        // 4, offset 8

        /// <summary>
        /// <c>TargetGroupHandle</c> from the EQS result.
        /// Stored between <c>Condition_IsAreaQueryResolved</c> and <c>Action_DispatchWaveWithTargets</c>.
        /// Initialized to -1.
        /// </summary>
        public int CachedTargetGroupHandle;           // 4, offset 12

        /// <summary>Number of tanks currently executing the attack run in the current wave.</summary>
        public int ActiveAttackerCount;               // 4, offset 16

        /// <summary>Firing-line slot indices permanently blocked by wrecks.</summary>
        public ushort BurnedSlotsMask;                // 2, offset 20

        /// <summary>Firing-line slot indices occupied by the current wave.</summary>
        public ushort WaveUsedSlotsMask;              // 2, offset 22

        /// <summary>Baseline slot indices currently reserved by live tanks.</summary>
        public ushort BaselineReservedMask;           // 2, offset 24

        /// <summary>Wave index (0 or 1); toggles between waves.</summary>
        public byte CurrentWave;                      // 1, offset 26

        // Consume trailing padding for EQS timeout tracking.
        private byte _pad0;                           // 1, offset 27

        /// <summary>
        /// Simulation time at which the current EQS request was submitted.
        /// Used by commander timeout recovery in Condition_IsAreaQueryResolved.
        /// </summary>
        public float EqsRequestTime;                  // 4, offset 28

        // offset 32 — 8-byte aligned, required for fixed long array.

        /// <summary>
        /// Packed <c>Entity.PackedValue</c> handles for the current wave's attackers.
        /// SoA layout; decoupled from <c>UnitRoster</c> ordering.
        /// </summary>
        public fixed long ActiveEntityPacked[8];      // 64, offset 32

        /// <summary>Firing-line slot index for each active attacker.</summary>
        public fixed byte ActiveSlotIndex[8];         // 8, offset 96

        /// <summary>Baseline slot index to which each attacker must return.</summary>
        public fixed byte ReturnBaselineSlotIndex[8]; // 8, offset 104

        /// <summary>
        /// Per-attacker flag; 0 = run not yet started, 1 = run started.
        /// Set by <c>Condition_IsWaveCompleted</c> the first time an attacker's
        /// <c>BehaviorState.ActiveBehaviorHash</c> matches <c>HullDownAttackRun</c>.
        /// Prevents false completion detection during the one-frame ingress delay.
        /// </summary>
        public fixed byte HasStartedRun[8];           // 8, offset 112
        // Total: 120 bytes (well within 1024-byte Blackboard1024.ByteSize)
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Subordinate Tank DTOs (TASK-HA006)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-tank parameters for the HullDownAttackRun subordinate behavior.
    ///
    /// <para><b>Field layout (52 bytes, well within the 60-byte param region):</b></para>
    /// <list type="bullet">
    ///   <item>Firing slot: <c>SlotX, SlotY</c> (8 bytes) at offset 0.</item>
    ///   <item>Baseline slot: <c>BaselineX, BaselineY</c> (8 bytes) at offset 8.</item>
    ///   <item>Attack direction: <c>AttackDirX, AttackDirY</c> (8 bytes) at offset 16.</item>
    ///   <item>Kinematic limits: <c>ApproachSpeed, CreepSpeed</c> (8 bytes) at offset 24.</item>
    ///   <item>Assigned target: <c>TargetNetworkId</c> (long, 8 bytes) at offset 32.</item>
    ///   <item>Shot quota: <c>MaxRounds</c> and <c>RoundsFired</c> (8 bytes) at offsets 40 and 44.</item>
    ///   <item>Tracking: <c>LastObservedAmmo</c> (4 bytes) at offset 48.</item>
    /// </list>
    /// Total = 52 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HullDownAttackParams
    {
        /// <summary>X coordinate of the assigned firing-line slot (metres).</summary>
        public float SlotX;              // 4, offset 0

        /// <summary>Y coordinate of the assigned firing-line slot (metres).</summary>
        public float SlotY;              // 4, offset 4

        /// <summary>X coordinate of the assigned baseline retreat slot (metres).</summary>
        public float BaselineX;          // 4, offset 8

        /// <summary>Y coordinate of the assigned baseline retreat slot (metres).</summary>
        public float BaselineY;          // 4, offset 12

        /// <summary>Normalised attack direction (X component, pre-computed by commander).</summary>
        public float AttackDirX;         // 4, offset 16

        /// <summary>Normalised attack direction (Y component, pre-computed by commander).</summary>
        public float AttackDirY;         // 4, offset 20

        /// <summary>Approach speed used while the tank is more than the threshold distance from the slot (m/s).</summary>
        public float ApproachSpeed;      // 4, offset 24

        /// <summary>Creep speed used while within the threshold distance of the slot (m/s).</summary>
        public float CreepSpeed;         // 4, offset 28

        /// <summary>
        /// Network-stable replication ID of the assigned target entity.
        /// Resolved to a local ECS <c>Entity</c> via <c>NetworkEntityMap</c> at runtime.
        /// Never stores a local generational entity handle (would become stale across frames).
        /// </summary>
        public long TargetNetworkId;     // 8, offset 32 (8-byte aligned)

        /// <summary>Maximum rounds to fire before the node returns success. 0 means unlimited.</summary>
        public int MaxRounds;            // 4, offset 40

        /// <summary>Rounds fired so far during this behavior run.</summary>
        public int RoundsFired;          // 4, offset 44

        /// <summary>
        /// Last observed ammo value used to detect discrete shots via ammo drop.
        /// Initialized to -1 on parse and set on first tick.
        /// </summary>
        public int LastObservedAmmo;     // 4, offset 48
        // Total: 52 bytes
    }

    /// <summary>
    /// Single-field blackboard wrapper used as <c>TBlackboard</c> in the HullDownAttackRun
    /// <c>BTreeBuilder</c> expression-binding overloads.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HullDownAttackBlackboard
    {
        /// <summary>Per-tank static configuration for the attack run.</summary>
        public HullDownAttackParams Params;
    }
}
