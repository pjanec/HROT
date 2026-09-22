using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Behavior;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    [Flags]
    public enum ActorCapabilities : byte
    {
        None = 0,
        CanMove = 1,
        CanShoot = 2,
        CanInteract = 4,
        CanPlayAnimations = 8,
        CanChangeStance = 16,
        CanAim = 32
    }

    /// <summary>
    /// Shadow component that records the capability bitmask from the previous frame.
    /// Used by <c>HsmDamageBridgeSystem</c> to detect transitions (e.g. CanMove → cleared).
    /// Must be initialised to the entity's initial capabilities at spawn.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.PreviousCapabilities)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct PreviousCapabilities
    {
        public ActorCapabilities Capabilities;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ActorCapabilityState)]
    public struct ActorCapabilityState
    {
        public ActorCapabilities Capabilities;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BehaviorState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct BehaviorState
    {
        public int ActiveBehaviorHash;
        public uint InstanceId; // Preemption token
        public byte BrainTier;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SimTier)]
    public struct SimTier
    {
        public byte Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = BehaviorConstants.BrainBlackboardByteSize)]
    [ComponentId(GlobalComponentIds.BrainBlackboard)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct BrainBlackboard
    {
        /// <summary>
        /// Polymorphic behavior parameter payload at the start of the blackboard.
        /// AI developers project their specific DTO (e.g. <c>FireAtTargetParams</c>) onto
        /// this region using <c>Unsafe.As</c>.  Must not exceed
        /// <see cref="BehaviorConstants.MaxBehaviorParamByteSize"/> bytes.
        /// </summary>
        [FieldOffset(0)]
        public fixed byte BehaviorParameters[BehaviorConstants.MaxBehaviorParamByteSize];

        // ⛔⛔ THE TAIL MOVED OUT — `O2` (2026-09-20). ExpectedThreatLevel and the two interrupt
        //   registers now live in BrainInterrupts. They are ENTITY FACTS: one per entity, true
        //   regardless of which behaviour is running — whereas everything above is per-occurrence
        //   behaviour params. Keeping them in one struct meant BehaviorIngressSystem's transactional
        //   parse shadow-copied the interrupts along with the params on every behaviour switch.
        // 📄 R-41 (bytes 126/127) and R-39 (the param-region size) are updated in RULINGS.md.
    }

    /// <summary>
    /// ⭐⭐ <b>Per-entity cognitive facts — <c>O2</c>'s half of the <c>BrainBlackboard</c> split.</b>
    ///
    /// <para>These are true of the ENTITY, not of whatever behaviour is currently running: a threat
    /// rating for the waypoint it is on, and edge-triggered interrupts raised by one system and
    /// cleared by another at end of frame. ⛔ They were bytes 120/126/127 of <c>BrainBlackboard</c>,
    /// so a behaviour switch's shadow-copy carried them; now they simply persist.</para>
    ///
    /// <para>⚠ <b>The interrupt protocol is unchanged</b>: <c>CognitiveInterruptSystem</c> sets,
    /// <c>CognitiveCleanupSystem</c> clears at end of frame. Only the home moved.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BrainInterrupts)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct BrainInterrupts
    {
        /// <summary>
        /// Per-waypoint threat/danger level written by <c>RouteContextSystem</c>.
        /// A value of 0 means unknown/default; higher values indicate increasing danger.
        /// </summary>
        public byte ExpectedThreatLevel;

        /// <summary>
        /// MobilityLost edge-triggered interrupt. Set to 1 by <c>CognitiveInterruptSystem</c> on the
        /// tick <c>CanMove</c> transitions from set to cleared. Cleared back to 0 by
        /// <c>CognitiveCleanupSystem</c> at end of frame.
        /// </summary>
        public byte Interrupt_MobilityLost;

        /// <summary>Reserved for future hardware-level interrupt.</summary>
        public byte Interrupt_Reserved;
    }

    // ⛔⛔⛔ `Blackboard1024` WAS HERE, AND IT IS RETIRED — `P4`-① (`2026-09-22`).
    //
    // 📄 The argument is DESIGN_Occurrence_Scoped_Storage.md §30.13, in one line: it had three
    //    tenants and every one left by a NAMED decision.
    //      ① AiPrimitive working state → the Blueprint tier ladder under a partition allocator.
    //         The architect explicitly REJECTED retrofitting an allocator onto this component
    //         (.dev/_DONE/btree-ai-action-binding/SLICE2-DESIGN.md:18), and the move also lifted
    //         SLICE1's "exactly one stateful AiPrimitive per entity" limit.
    //      ② squad / commander state → its own [ComponentId] component, SquadCognitiveState.
    //         `O1` (2026-09-20) deleted `Project(ref Blackboard1024)` because it made "has a
    //         Blackboard1024" an accidental proxy for "is a commander with squad state".
    //      ③ behaviour param OVERFLOW (`HeavyDtoType` / `[SharedAiHeavyAction]`) → never adopted.
    //         Null at every production site; non-null only in two ExtDeps attribute unit tests.
    //
    // 📐 Settled with Roslyn before deleting, because a text sweep cannot prove an absence for a
    //    type reached through a generic: `Project<T>` had 11 references and EVERY ONE was a test;
    //    `Memory` had 8 (2 tests, the declaration, the four surfaces P4-① deletes, and one
    //    always-false gate); `ByteSize` had 1, its own declaration. ⇒ no BTree consumer, no HSM
    //    consumer, no production consumer of any kind. ⚠ A stale comment in SquadCognitiveState.cs
    //    claimed "BTree and HSM still use it" — that claim was FALSE and is recorded as such.
    //
    // ⚠ Its component id 74 stays RESERVED in GlobalComponentIds rather than being reused, so a
    //   stale recording cannot bind it to a different component.
}
