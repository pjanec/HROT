using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Perception.Events
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
        /// <summary>Entity index of the observer (the entity performing the vision check).</summary>
        public int ObserverEntityIndex;

        /// <summary>Entity index of the potential target.</summary>
        public int TargetEntityIndex;
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
        /// <summary>Entity index of the observer that can see the target.</summary>
        public int ObserverEntityIndex;

        /// <summary>Entity index of the confirmed visible target.</summary>
        public int TargetEntityIndex;
    }
}
