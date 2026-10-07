using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

// ⭐ AQ85 (R-216) — the stance components live in the toolkit, beside StanceId, so the toolkit's hit model, its AI estimate and
//   perception can read the LOGICAL stance (the brain's StanceIntent.TargetStance, applied the tick it is ordered — DESIGN_Building_
//   Interiors.md §3f). ⚠ The namespace stays Hrot.MuscleCharacter.Animation.Components on purpose: every existing reference compiles
//   unchanged and the wire/replication contract (ComponentIds 222/223, sequential layout) is untouched. Moved from
//   Hrot.MuscleCharacter.Animation/Components/ReplicatedComponents.cs, as StanceId was (CE-145).
namespace Hrot.MuscleCharacter.Animation.Components
{
    /// <summary>
    /// Stance transition phase tracking for multi-frame blend sequences.
    /// Synchronizes between Brain intent and Muscle execution.
    /// </summary>
    [Serializable]
    public enum StanceTransitionPhase : byte
    {
        /// <summary>No active transition; current stance is stable.</summary>
        Idle = 0,

        /// <summary>Transition blend in progress.</summary>
        Transitioning = 1,

        /// <summary>Transition complete and locked (final state written).</summary>
        Locked = 2,
    }

    /// <summary>
    /// Brain-authored stance intention descriptor.
    /// Brain writes the target stance; Muscle initiates transition blend.
    /// Replicates from Brain → Muscle; Muscle writes back StanceStatus.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.StanceIntent)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct StanceIntent
    {
        /// <summary>Target stance (Standing, Crouched, Prone).</summary>
        public StanceId TargetStance;

        /// <summary>Blend duration in seconds for smooth transition (0 = immediate).</summary>
        public float BlendTime;

        /// <summary>Version counter; bumped each time intent changes (triggers Muscle transition).</summary>
        public uint Version;
    }

    /// <summary>
    /// Muscle-authored stance status descriptor.
    /// Tracks current stance and transition progress; replicates from Muscle → Brain.
    /// Brain observes this via ValueChanged to know when transitions are complete.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.StanceStatus)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct StanceStatus
    {
        /// <summary>Current stable stance (Standing, Crouched, Prone).</summary>
        public StanceId CurrentStance;

        /// <summary>Transition phase: Idle, Transitioning, Locked.</summary>
        public StanceTransitionPhase Phase;

        /// <summary>Progress of active transition blend (0.0 = start, 1.0 = complete).</summary>
        public float TransitionProgress;

        /// <summary>Version observed by Muscle (ack counter to Brain's StanceIntent.Version).</summary>
        public uint AckVersion;
    }
}
