using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Events
{
    // ⛔ CE-3062 — AudioStimulusEvent and TargetHeardEvent are RETIRED with AudioPerceptionSystem: nothing in production published
    //   a stimulus or read a heard event. Hearing is the acoustic sensor now (Sensors/AcousticPerception.cs → SoundContactEvent).
    //   📄 docs/DESIGN_Thermal_And_Acoustic_Sensing.md §6 A.

    // ── SensorTrackStatus ─────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the DDS <c>SensorTrackState.State</c> wire contract (Lost=0, Acquired=1).
    /// Used by <see cref="SensorTrackStateEvent"/> to carry the track transition across the
    /// Muscle-to-Brain boundary without relying on the internal <c>SensorContactState</c> enum.
    /// </summary>
    public enum SensorTrackStatus : byte
    {
        /// <summary>Target is no longer detected — boost stops and decay takes over.</summary>
        Lost = 0,
        /// <summary>Target has been acquired — boost begins in the Brain tier.</summary>
        Acquired = 1,
    }

    // ── SensorTrackStateEvent ─────────────────────────────────────────────────────

    /// <summary>
    /// Global FDP event that bridges the Muscle-tier sensor debounce result to the
    /// Brain-tier cognitive buffer, replacing the DDS transport in networkless setups.
    /// <para>
    /// Published by the EQS solver's memory stage (<c>SensorMemoryStage</c>) when the UNION of a unit's acquired
    /// contacts over all its perception sensors gains or loses a target — <see cref="SensorTrackStatus.Acquired"/> or
    /// <see cref="SensorTrackStatus.Lost"/> (docs/DESIGN_Sensors_And_Doctrine.md §5.4).
    /// </para>
    /// <para>
    /// In networked deployments the egress translator converts this event to a DDS
    /// <c>SensorTrackState</c> sample; the ingress translator on the remote node
    /// reconstructs it from the DDS sample. In the networkless Editor the event travels
    /// directly on the world bus so that <see cref="Systems.ActiveSensorTracksUpdateSystem"/>
    /// can update <see cref="Components.ActiveSensorTracks"/> without any network layer.
    /// </para>
    /// </summary>
    [EventId(PerceptionConstants.SensorTrackStateEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct SensorTrackStateEvent
    {
        /// <summary>Observer entity (local ECS handle on the publishing node).</summary>
        public Entity Observer;

        /// <summary>Target entity (local ECS handle on the publishing node).</summary>
        public Entity Target;

        /// <summary>Whether the target was acquired or lost.</summary>
        public SensorTrackStatus State;

        // 3-byte implicit pad between byte and float

        /// <summary>Last-known X position of the target (metres, ground plane).</summary>
        public float PositionX;

        /// <summary>Last-known Y position of the target (metres, ground plane).</summary>
        public float PositionY;

        /// <summary>⭐ <c>CE-3060</c> — the kinds of the unit's sensors that hold this target (OR). 0 on a Lost; a reader treats
        /// 0 on an Acquired as Visual (an older writer).</summary>
        public Components.SensorModality Modality;
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
