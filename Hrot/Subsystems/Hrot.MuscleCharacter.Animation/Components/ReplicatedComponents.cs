using System;
using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.MuscleCharacter.Animation.Components
{
    // ⚠ `StanceId` MOVED OUT of this file on 2026-08-31 → FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/StanceId.cs
    //   and it is now in namespace `Fdp.Toolkit.Tkb.Domain` (CE-145, done 2026-08-31) — hence the
    //   `using Fdp.Toolkit.Tkb.Domain;` above. It moved because CharacterAnimationDefDto (a TKB descriptor
    //   DTO referencing it) had to sit beside the other TKB DTOs so Hrot.Core could host the UrbanCombat
    //   catalogue without referencing this subsystem.
    //   📄 docs/DESIGN_Entity_Creation_Unification.md §3.3.

    // ⚠ StanceTransitionPhase, StanceIntent and StanceStatus MOVED to FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/StanceComponents.cs
    //   (2026-10-07, AQ85 / R-216): the hit model and the AI's estimate in Fdp.Toolkits read the LOGICAL stance. Same
    //   namespace, same ComponentIds, same layout — no reference changed. Precedent: StanceId (CE-145).

    /// <summary>
    /// Animation channel: the Brain's REQUEST for one-shot montage playback (DD-1 §5.1).
    /// ⭐ <c>CE-513</c> / <c>R-180</c> (<c>Architect_Question_80</c>): only the Brain writes this component (Brain
    /// ownership group, wire descriptor 100). The Muscle's report lives in <see cref="AnimationChannelStatus"/> —
    /// one writer per component, the <see cref="StanceIntent"/>/<see cref="StanceStatus"/> shape.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimationChannel)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct AnimationChannel
    {
        /// <summary>Current action ID (see AnimationActionIds). 0 = no action pending.</summary>
        public ushort ActiveAction;

        /// <summary>Behavior instance ID that issued this action (for dispatcher routing).</summary>
        public uint BehaviorInstanceId;

        /// <summary>Action instance token; bumped on each new action to preempt stale requests.</summary>
        public uint ActionInstanceId;

        /// <summary>32-byte action parameter payload (PlayMontageParams, StopMontageParams, etc.).</summary>
        public fixed byte Params[BehaviorConstants.ActionParamsByteSize];
    }

    /// <summary>
    /// The Muscle's REPORT on the <see cref="AnimationChannel"/> request it last picked up (MuscleGround ownership
    /// group, wire descriptor 101). ⭐ <c>CE-513</c> / <c>R-180</c>.
    /// <para>⚠ <see cref="Status"/> is only meaningful for the request whose <see cref="AnimationChannel.ActionInstanceId"/>
    /// equals <see cref="DispatchedInstanceId"/>; the default (<see cref="NodeStatus.Failure"/> = 0) means "nothing
    /// picked up yet".</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimationChannelStatus)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct AnimationChannelStatus
    {
        /// <summary>The <see cref="AnimationChannel.ActionInstanceId"/> the Muscle last dispatched.</summary>
        public uint DispatchedInstanceId;

        /// <summary>Lifecycle status of that action: Running, Success, Failure.</summary>
        public NodeStatus Status;
    }

    /// <summary>
    /// Look-at (aim) channel: the Brain's REQUEST for the aim/look-at overlay (DD-1 §5.1), concurrent with montage
    /// playback. ⭐ <c>CE-513</c> / <c>R-180</c>: Brain-written only (descriptor 102); the report is
    /// <see cref="LookAtChannelStatus"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LookAtChannel)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct LookAtChannel
    {
        /// <summary>Current action ID (see LookAtActionIds). 0 = no action pending.</summary>
        public ushort ActiveAction;

        /// <summary>Behavior instance ID that issued this action (for dispatcher routing).</summary>
        public uint BehaviorInstanceId;

        /// <summary>Action instance token; bumped on each new action to preempt stale requests.</summary>
        public uint ActionInstanceId;

        /// <summary>32-byte action parameter payload (LookAtPointParams, LookAtEntityParams, etc.).</summary>
        public fixed byte Params[BehaviorConstants.ActionParamsByteSize];
    }

    /// <summary>The Muscle's REPORT on the <see cref="LookAtChannel"/> request (descriptor 103). ⭐ <c>CE-513</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LookAtChannelStatus)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct LookAtChannelStatus
    {
        /// <summary>The <see cref="LookAtChannel.ActionInstanceId"/> the Muscle last dispatched.</summary>
        public uint DispatchedInstanceId;

        /// <summary>Lifecycle status of that action: Running, Success, Failure.</summary>
        public NodeStatus Status;
    }

    /// <summary>
    /// ⭐ <c>CE-513</c> — the Muscle executors' working view of one montage action: the request (read-only by
    /// convention) and the report the executor writes. NOT a component: the dispatcher builds it on the stack from
    /// <see cref="AnimationChannel"/> + <see cref="AnimationChannelStatus"/> and writes back only
    /// <see cref="Report"/>, so the generic <c>IActionExecutor&lt;TChannel&gt;</c> contract is unchanged.
    /// </summary>
    public struct AnimationChannelWork
    {
        /// <summary>The Brain's request (do not write).</summary>
        public AnimationChannel Request;

        /// <summary>The Muscle's report — what the executor writes.</summary>
        public AnimationChannelStatus Report;
    }

    /// <summary>⭐ <c>CE-513</c> — the look-at executors' working view; see <see cref="AnimationChannelWork"/>.</summary>
    public struct LookAtChannelWork
    {
        /// <summary>The Brain's request (do not write).</summary>
        public LookAtChannel Request;

        /// <summary>The Muscle's report — what the executor writes.</summary>
        public LookAtChannelStatus Report;
    }


    /// <summary>
    /// Single entry in a montage queue (used inside AnimationMontageQueue.Entries [InlineArray]).
    /// Each entry carries configuration for one montage in a chained sequence.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MontageQueueEntry
    {
        /// <summary>Stable ID (hash) of the montage to play.</summary>
        public int MontageId;

        /// <summary>Time in seconds to crossfade from the previous entry into this one.</summary>
        public float BlendIntoTime;

        /// <summary>Playback speed multiplier (1.0 = normal, 2.0 = double-speed, etc.).</summary>
        public float PlayRate;

        /// <summary>Section index to begin this montage (0 = first section).</summary>
        public byte StartSectionIndex;

        /// <summary>Bitfield flags for playback behavior (e.g. UseRootMotion).</summary>
        public byte Flags;

        private ushort _pad1;
    }

    /// <summary>
    /// Brain-authored montage queue component carrying a sequence of montages to chain.
    /// Entries are mutated directly by AiPrimitive nodes using [InlineArray] Span-cast pattern.
    /// Replicates Count and Entries from Brain → Muscle; QueueVersion dirty-triggers replication.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimationMontageQueue)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct AnimationMontageQueue
    {
        /// <summary>Number of valid entries in Entries (0 = empty queue).</summary>
        public byte Count;

        /// <summary>Version number bumped on any mutation; signals Muscle executor to re-scan queue.</summary>
        public uint QueueVersion;

        /// <summary>
        /// Inline array buffer holding up to 8 montage queue entries (16 bytes each = 128 bytes).
        /// Each entry: MontageId (4) + BlendIntoTime (4) + PlayRate (4) + StartSectionIndex (1) + Flags (1) + padding (2).
        /// Use Span-cast pattern (Pattern A/B from mini §4.3) for safe mutation.
        /// Cast as: Span&lt;MontageQueueEntry&gt; entries = MemoryMarshal.Cast&lt;byte, MontageQueueEntry&gt;(new Span&lt;byte&gt;(Entries, 128));
        /// </summary>
        public fixed byte EntriesData[128];  // 8 * 16 bytes
    }

    /// <summary>
    /// Muscle-authored queue playback state component tracking executor progress through the queue.
    /// Replicates from Muscle → Brain so Brain can observe which entry is currently playing.
    /// Companion to AnimationMontageQueue (Brain-side queue spec).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimationMontageQueueState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct AnimationMontageQueueState
    {
        /// <summary>Index of the currently-playing queue entry (0xFF = no entry active / queue idle).</summary>
        public byte CurrentEntryIndex;

        /// <summary>Elapsed time in seconds of the currently-playing entry.</summary>
        public float EntryElapsedSeconds;

        /// <summary>Flag indicating entry is in blend-out window (crossfading to next).</summary>
        [MarshalAs(UnmanagedType.I1)]
        public bool InBlendOutWindow;

        /// <summary>
        /// Set to 1 by MontageQueueAdvanceSystem when it has staged an entry's play.
        /// Cleared to 0 when the queue finishes all entries or is reset.
        /// Used to distinguish "entry staged but not started yet" from "entry finished".
        /// </summary>
        public byte TrackingActive;

        /// <summary>Version last observed by executor; when != AnimationMontageQueue.QueueVersion, queue was mutated.</summary>
        public uint ObservedQueueVersion;
    }
}
