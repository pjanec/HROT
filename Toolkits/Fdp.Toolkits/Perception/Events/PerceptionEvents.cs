using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace Fdp.Toolkit.Perception.Events
{
    // ── AudioStimulusEvent ────────────────────────────────────────────────────────

    /// <summary>
    /// Published when an entity emits a sound that other entities can potentially hear.
    /// Consumed by <see cref="Systems.AudioPerceptionSystem"/> on the main thread.
    /// </summary>
    [EventId(PerceptionConstants.AudioStimulusEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStimulusEvent
    {
        /// <summary>World-space origin of the sound (XYZ; Z is elevation).</summary>
        public Vector3 Origin;

        /// <summary>
        /// Effective radius (meters) of the event.
        /// Used as the spatial-hash query radius to find candidate listeners.
        /// Listeners outside this radius cannot hear the event regardless of their own
        /// <see cref="Components.PerceptionReceptor.HearingRange"/>.
        /// </summary>
        public float Intensity;

        /// <summary>Entity index of the entity that produced the sound.</summary>
        public int SourceEntityIndex;
    }

    // ── LosCheckRequestEvent ──────────────────────────────────────────────────────

    /// <summary>
    /// Emitted by <see cref="Systems.VisionBroadphaseSystem"/> when a potential target
    /// passes the faction + FOV broadphase filter.
    /// Consumed by <see cref="Systems.LosRequestBatchingSystem"/> to queue a line-of-sight
    /// ray or (in mock mode) to directly emit <see cref="TargetVisibleEvent"/>.
    /// </summary>
    [EventId(PerceptionConstants.LosCheckRequestEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct LosCheckRequestEvent
    {
        /// <summary>The observer entity performing the LOS check (full handle: index + generation).</summary>
        public Entity Observer;

        /// <summary>The potential target entity (full handle: index + generation).</summary>
        public Entity Target;
    }

    // ── TargetVisibleEvent ────────────────────────────────────────────────────────

    /// <summary>
    /// Published when line-of-sight from an observer to a target is confirmed (or assumed in
    /// mock mode). Consumed by <see cref="Systems.ThreatEvaluationSystem"/> to boost the
    /// observer's <see cref="Components.TargetMemory"/>.
    /// </summary>
    [EventId(PerceptionConstants.TargetVisibleEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct TargetVisibleEvent
    {
        /// <summary>The observer entity that has confirmed LOS to <see cref="Target"/>.</summary>
        public Entity Observer;

        /// <summary>The target entity confirmed visible to <see cref="Observer"/>.</summary>
        public Entity Target;
    }

    // ── TargetHeardEvent ──────────────────────────────────────────────────────────

    /// <summary>
    /// Published by <see cref="Systems.AudioPerceptionSystem"/> when an entity successfully
    /// detects an audio stimulus.
    /// Consumed by <see cref="Systems.ThreatEvaluationSystem"/> to update
    /// <see cref="Components.TargetMemory"/> on the Brain tier.
    /// </summary>
    [EventId(PerceptionConstants.TargetHeardEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct TargetHeardEvent
    {
        /// <summary>The entity that heard the sound.</summary>
        public Entity Listener;

        /// <summary>Entity index of the entity that produced the sound (same as <see cref="AudioStimulusEvent.SourceEntityIndex"/>).</summary>
        public int SourceEntityIndex;

        // 4-byte pad implicit from Entity (8 bytes) + int (4 bytes) = 12 bytes → aligns Origin to 16.

        /// <summary>World-space origin of the detected sound.</summary>
        public Vector3 Origin;
    }

    // ── SeedTargetCommand ─────────────────────────────────────────────────────────

    /// <summary>
    /// Unmanaged command that externally boosts the threat score of a specific target in
    /// the perceiver's <see cref="Components.TargetMemory"/>. Consumed by
    /// <c>ThreatEvaluationSystem</c> during its ingress pass.
    ///
    /// <para>Use this when a higher-level system (e.g. a mission planner or player command)
    /// needs to force-focus the perceiver on a chosen entity regardless of organic detection.
    /// </para>
    /// </summary>
    [EventId(PerceptionConstants.SeedTargetCommandId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct SeedTargetCommand
    {
        /// <summary>The entity whose <see cref="Components.TargetMemory"/> should be updated.</summary>
        public Entity Perceiver;

        /// <summary>The target entity to seed into <see cref="Perceiver"/>'s memory.</summary>
        public Entity Target;

        /// <summary>Additive threat-score boost applied on top of any existing score.</summary>
        public float ScoreBoost;
    }
}
