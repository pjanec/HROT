using System;
using System.Numerics;
using Fdp.Core;
using Fbt;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.MuscleCharacter.Animation.Events
{
    // -----------------------------------------------------------------------
    // Reason a montage ends (DD-1 §18, DD-3 §3.1).
    // Shared by MontageEndedEvent and AnimationStateReporterSystem.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reason a running montage terminated, emitted on MontageEndedEvent.
    /// (DD-3 §3.1)
    /// </summary>
    public enum MontageEndReason : byte
    {
        NaturalEnd = 0,
        Interrupted = 1,
        BlendedOutByNext = 2,
        Failed = 3,
    }

    // -----------------------------------------------------------------------
    // Lifecycle events (synthesized by AnimationStateReporterSystem, DD-3 §3.1)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Emitted when a montage begins playing on a character.
    /// Synthesized from ECS state by AnimationStateReporterSystem.
    /// (DD-3 §3.1, EventId=8201)
    /// </summary>
    [EventId(8201)]
    [DataPolicy(DataPolicy.NoRecord)]
    public readonly struct MontageStartedEvent
    {
        public readonly Entity Target;
        [MontagePicker]
        public readonly int MontageId;
        /// <summary>Correlates to the issuing channel command.</summary>
        public readonly uint ActionInstanceId;
        /// <summary>0xFF = single-shot PlayMontage; else 0..N-1 queue position.</summary>
        public readonly byte QueueIndex;
    }

    /// <summary>
    /// Emitted when a montage finishes (naturally or otherwise).
    /// Synthesized from ECS state by AnimationStateReporterSystem.
    /// (DD-3 §3.1, EventId=8202)
    /// </summary>
    [EventId(8202)]
    [DataPolicy(DataPolicy.NoRecord)]
    public readonly struct MontageEndedEvent
    {
        public readonly Entity Target;
        [MontagePicker]
        public readonly int MontageId;
        public readonly uint ActionInstanceId;
        /// <summary>0xFF = single-shot PlayMontage; else 0..N-1 queue position.</summary>
        public readonly byte QueueIndex;
        public readonly MontageEndReason EndReason;

        public MontageEndedEvent(
            Entity target,
            int montageId,
            uint actionInstanceId,
            byte queueIndex,
            MontageEndReason endReason)
        {
            Target = target;
            MontageId = montageId;
            ActionInstanceId = actionInstanceId;
            QueueIndex = queueIndex;
            EndReason = endReason;
        }
    }

    /// <summary>
    /// Emitted when a montage advances from one section to the next.
    /// Synthesized from ECS state by AnimationStateReporterSystem.
    /// (DD-3 §3.1, EventId=8203)
    /// </summary>
    [EventId(8203)]
    [DataPolicy(DataPolicy.NoRecord)]
    public readonly struct MontageSectionAdvancedEvent
    {
        public readonly Entity Target;
        [MontagePicker]
        public readonly int MontageId;
        public readonly byte FromSectionIndex;
        public readonly byte ToSectionIndex;
    }

    /// <summary>
    /// Emitted when the character's active stance changes.
    /// Synthesized from ECS state by AnimationStateReporterSystem.
    /// (DD-3 §3.1, EventId=8204)
    /// </summary>
    [EventId(8204)]
    [DataPolicy(DataPolicy.NoRecord)]
    public readonly struct StanceChangedEvent
    {
        public readonly Entity Target;
        public readonly StanceId PreviousStance;
        public readonly StanceId NewStance;

        public StanceChangedEvent(Entity target, StanceId previousStance, StanceId newStance)
        {
            Target = target;
            PreviousStance = previousStance;
            NewStance = newStance;
        }
    }

    // -----------------------------------------------------------------------
    // Backend-drained events (from RawNotifyEvent via NotifyEventEmitterSystem,
    // DD-3 §3.2). IDs 8210-8213 per architect ruling (DD-3 §9.7 / TASK-DETAIL).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Emitted when a footstep contact fires on the animation backend.
    /// Muscle-local only — does NOT propagate across nodes (DD-3 §5.2).
    /// (DD-3 §3.2, EventId=8210)
    /// </summary>
    [EventId(8210)]
    [DataPolicy(DataPolicy.NoRecord)]
    public readonly struct FootstepEvent
    {
        public readonly Entity Target;
        public readonly Vector3 WorldPosition;
        /// <summary>0 = left foot, 1 = right foot.</summary>
        public readonly byte FootIndex;
        /// <summary>Surface type resolved by Muscle physics surface query.</summary>
        public readonly byte SurfaceTypeHint;

        public FootstepEvent(Entity target, Vector3 worldPosition, byte footIndex, byte surfaceTypeHint)
        {
            Target = target;
            WorldPosition = worldPosition;
            FootIndex = footIndex;
            SurfaceTypeHint = surfaceTypeHint;
        }
    }

    /// <summary>
    /// Emitted when a melee hit window opens on a montage.
    /// Drained from backend notify markers by NotifyEventEmitterSystem.
    /// (DD-3 §3.2, EventId=8211)
    /// </summary>
    [EventId(8211)]
    [DataPolicy(DataPolicy.NoRecord)]
    public readonly struct HitWindowOpenedEvent
    {
        public readonly Entity Target;
        [MontagePicker]
        public readonly int MontageId;
        /// <summary>Melee-attack hit-window identifier.</summary>
        public readonly byte WindowId;

        public HitWindowOpenedEvent(Entity target, int montageId, byte windowId)
        {
            Target = target;
            MontageId = montageId;
            WindowId = windowId;
        }
    }

    /// <summary>
    /// Emitted when a melee hit window closes on a montage.
    /// Drained from backend notify markers by NotifyEventEmitterSystem.
    /// (DD-3 §3.2, EventId=8212)
    /// </summary>
    [EventId(8212)]
    [DataPolicy(DataPolicy.NoRecord)]
    public readonly struct HitWindowClosedEvent
    {
        public readonly Entity Target;
        [MontagePicker]
        public readonly int MontageId;
        /// <summary>Melee-attack hit-window identifier.</summary>
        public readonly byte WindowId;

        public HitWindowClosedEvent(Entity target, int montageId, byte windowId)
        {
            Target = target;
            MontageId = montageId;
            WindowId = windowId;
        }
    }

    /// <summary>
    /// Generic catch-all animation notify event for markers not covered by
    /// a more specific typed event (footstep, hit-window, etc.).
    /// Drained from backend notify markers by NotifyEventEmitterSystem.
    /// (DD-3 §3.2, EventId=8213)
    /// </summary>
    [EventId(8213)]
    [DataPolicy(DataPolicy.NoRecord)]
    public readonly struct AnimNotifyEvent
    {
        public readonly Entity Target;
        [MontagePicker]
        public readonly int MontageId;
        /// <summary>
        /// Stable hash of the marker name authored on the montage asset.
        /// The [AnimMarkerPicker] attribute drives the Blueprint property drawer
        /// to render this as a marker-name dropdown sourced from
        /// IAnimationTkbQueries.GetAvailableMarkers(entityClass) rather than a raw
        /// numeric input. The drawer resolves the picked name to a hash at
        /// compile time (DD-4 §3.4 hashing). (DD-3 §3.3, §4.2)
        /// </summary>
        [AnimMarkerPicker]
        public readonly uint MarkerHash;
        public readonly float PayloadFloat;

        public AnimNotifyEvent(Entity target, int montageId, uint markerHash, float payloadFloat)
        {
            Target = target;
            MontageId = montageId;
            MarkerHash = markerHash;
            PayloadFloat = payloadFloat;
        }
    }
}
