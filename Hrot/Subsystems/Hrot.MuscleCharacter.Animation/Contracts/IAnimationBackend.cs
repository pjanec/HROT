using System;
using System.Runtime.InteropServices;

namespace Hrot.MuscleCharacter.Animation.Contracts
{
    /// <summary>
    /// Generation-safe entity handle for the animation backend.
    /// Protects against stale references after entity unregistration.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AnimationBackendHandle
    {
        /// <summary>Backend-specific entity index or pool slot.</summary>
        public uint Index;

        /// <summary>Generation counter; incremented on entity reuse.</summary>
        public uint Generation;

        /// <summary>Check if this handle is still valid (must call TryResolve to confirm).</summary>
        public bool IsValid => Index != 0xFFFFFFFF;

        public override bool Equals(object? obj)
        {
            if (obj is not AnimationBackendHandle h)
                return false;
            return Index == h.Index && Generation == h.Generation;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Index, Generation);
        }

        public static bool operator ==(AnimationBackendHandle a, AnimationBackendHandle b)
            => a.Equals(b);

        public static bool operator !=(AnimationBackendHandle a, AnimationBackendHandle b)
            => !a.Equals(b);
    }

    /// <summary>
    /// Animation montage playback state tracking.
    /// Describes the lifecycle of a single montage in a slot.
    /// </summary>
    [Serializable]
    public enum MontagePlaybackState : byte
    {
        /// <summary>Slot is empty; no montage playing.</summary>
        Inactive = 0,

        /// <summary>Montage is actively playing.</summary>
        Active = 1,

        /// <summary>Montage is in blend-out phase, transitioning to inactive or next montage.</summary>
        BlendingOut = 2,
    }

    /// <summary>
    /// Slot identifier for a montage playback slot.
    /// Indices 0-7 map to the 8 concurrent playback slots.
    /// </summary>
    [Serializable]
    public enum SlotId : byte
    {
        Slot0 = 0,
        Slot1 = 1,
        Slot2 = 2,
        Slot3 = 3,
        Slot4 = 4,
        Slot5 = 5,
        Slot6 = 6,
        Slot7 = 7,
    }

    /// <summary>
    /// Stable montage asset ID (hash of the montage name).
    /// Engine-agnostic: no reference to animation assets or Stride types.
    /// </summary>
    [Serializable]
    public struct MontageAssetId
    {
        /// <summary>Hash value identifying the montage (stable across runs).</summary>
        public int Hash;

        public override bool Equals(object? obj)
        {
            if (obj is not MontageAssetId m)
                return false;
            return Hash == m.Hash;
        }

        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }

        public static bool operator ==(MontageAssetId a, MontageAssetId b)
            => a.Hash == b.Hash;

        public static bool operator !=(MontageAssetId a, MontageAssetId b)
            => a.Hash != b.Hash;
    }

    /// <summary>
    /// Stance transition progress tracking.
    /// Describes the current progress of a stance transition blend.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StanceTransitionState
    {
        /// <summary>Source stance before transition began.</summary>
        public byte FromStance;

        /// <summary>Target stance after transition.</summary>
        public byte ToStance;

        /// <summary>Transition progress (0.0 = start, 1.0 = complete).</summary>
        public float Progress;

        /// <summary>Total transition time in seconds.</summary>
        public float TotalDuration;
    }

    /// <summary>
    /// Raw animation notify event emitted by the backend.
    /// Contains the event type and timing information.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RawNotifyEvent
    {
        /// <summary>Event category (Generic, Footstep, HitWindowOpened, HitWindowClosed).</summary>
        public AnimNotifyCategory Kind;

        /// <summary>Hash of the marker name (for generic notifies).</summary>
        public uint MarkerHash;

        /// <summary>Time in seconds when the notify fired (relative to slot start).</summary>
        public float TimeSeconds;

        /// <summary>Generic float payload (repurposed for different event types).</summary>
        public float PayloadFloat;

        /// <summary>Generic integer payload.</summary>
        public uint PayloadUint;
    }

    /// <summary>
    /// Configuration struct for IAnimationBackend initialization.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AnimationBackendConfig
    {
        /// <summary>Maximum number of concurrent entities (determines pool size).</summary>
        public int MaxEntities;

        /// <summary>Maximum number of concurrent notifies in the drain buffer.</summary>
        public int MaxNotifyEvents;

        /// <summary>Default blend-in time for montages (seconds).</summary>
        public float DefaultBlendInTime;

        /// <summary>Default blend-out time for montages (seconds).</summary>
        public float DefaultBlendOutTime;

        /// <summary>Default playback speed multiplier.</summary>
        public float DefaultPlayRate;
    }

    /// <summary>
    /// Performance metrics snapshot from the backend.
    /// Tracks timing and state for diagnostics and optimization.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AnimationBackendMetrics
    {
        /// <summary>Number of currently-registered entities.</summary>
        public int ActiveEntityCount;

        /// <summary>Total number of active playback slots across all entities.</summary>
        public int TotalActiveSlotsCount;

        /// <summary>Number of pending notify events queued for drain.</summary>
        public int PendingNotifyCount;

        /// <summary>Time spent in last Tick() call (milliseconds).</summary>
        public float LastTickMs;

        /// <summary>Peak tick duration observed (milliseconds).</summary>
        public float PeakTickMs;
    }

    /// <summary>
    /// Animation backend abstraction contract.
    /// Implemented by FakeAnimationBackend (Phase 1) and StrideAnimationBackend (Phase 8).
    /// No engine-specific types exposed beyond this interface.
    /// </summary>
    public interface IAnimationBackend
    {
        /// <summary>
        /// Register an entity with the backend, allocating a handle and initializing state.
        /// Called once per entity at spawn or when animation capabilities first become available.
        /// </summary>
        /// <param name="entityId">Stable entity identifier (typically a network ID or local ECS entity).</param>
        /// <param name="characterDefHandle">Backend handle from CharacterAnimationDefRuntime.</param>
        /// <returns>A generation-safe handle for use in subsequent calls.</returns>
        AnimationBackendHandle RegisterEntity(uint entityId, long characterDefHandle);

        /// <summary>
        /// Unregister an entity from the backend, cleaning up all playback state.
        /// Called when the entity is destroyed or loses animation capability.
        /// </summary>
        /// <param name="handle">Handle returned by RegisterEntity; becomes invalid after this call.</param>
        void UnregisterEntity(AnimationBackendHandle handle);

        /// <summary>
        /// Resolve a handle to the current entity state (if still valid).
        /// Called before operations to confirm the handle hasn't staled.
        /// </summary>
        /// <param name="handle">Handle to resolve.</param>
        /// <param name="state">Output: the backend's internal state for this entity (opaque to caller).</param>
        /// <returns>True if handle is valid and entity is registered; false if handle is stale.</returns>
        bool TryResolve(AnimationBackendHandle handle, out nint state);

        /// <summary>
        /// Queue a montage to play on a specific slot.
        /// If the slot is already active, crossfade settings determine blend behavior.
        /// </summary>
        void PlayMontageOnSlot(AnimationBackendHandle handle, in PlayMontageParams @params);

        /// <summary>
        /// Stop playback on a specific slot, blending to neutral.
        /// </summary>
        void StopMontageOnSlot(AnimationBackendHandle handle, in StopMontageParams @params);

        /// <summary>
        /// Set aim target to a world-space point.
        /// </summary>
        void SetAimTargetPoint(AnimationBackendHandle handle, in LookAtPointParams @params);

        /// <summary>
        /// Set aim target to a dynamic entity.
        /// </summary>
        void SetAimTargetEntity(AnimationBackendHandle handle, in LookAtEntityParams @params);

        /// <summary>
        /// Release aim, blending back to neutral over time.
        /// </summary>
        void ReleaseAim(AnimationBackendHandle handle, in ReleaseLookParams @params);

        /// <summary>
        /// Request a stance transition (standing → crouched, etc.).
        /// The backend handles blending; caller observes progress via the return state.
        /// </summary>
        void RequestStanceChange(AnimationBackendHandle handle, byte targetStance, float blendDurationSeconds);

        /// <summary>
        /// Advance all registered entities by deltaTime (seconds).
        /// Updates blending, playback progress, slot state, and emits notifies.
        /// Called once per frame from AnimationRuntimeBridgeSystem.
        /// </summary>
        /// <param name="deltaTime">Time delta in seconds.</param>
        void Tick(float deltaTime);

        /// <summary>
        /// Drain pending notify events into the provided buffer.
        /// Events older than the buffer capacity are silently dropped.
        /// </summary>
        /// <param name="dest">Output buffer for notify events.</param>
        /// <param name="destCapacity">Maximum number of events to write.</param>
        /// <returns>Number of events written to dest (0 to destCapacity).</returns>
        int DrainNotifies(Span<RawNotifyEvent> dest);

        /// <summary>
        /// Drain pending notify events for a specific entity into the provided buffer.
        /// Returns only events that originated from this entity's slots.
        /// </summary>
        /// <param name="handle">Handle of the entity to drain notifies for.</param>
        /// <param name="dest">Output buffer for notify events.</param>
        /// <returns>Number of events written to dest.</returns>
        int DrainNotifies(AnimationBackendHandle handle, Span<RawNotifyEvent> dest);

        /// <summary>
        /// Returns the current stance of the entity as reported by the backend.
        /// Reflects the completed stance after any in-progress transition finishes.
        /// </summary>
        /// <param name="handle">Handle of the entity to query.</param>
        /// <param name="currentStance">The backend's current stable stance value.</param>
        /// <returns>True if handle is valid; false if handle is stale or unregistered.</returns>
        bool GetCurrentStance(AnimationBackendHandle handle, out byte currentStance);

        /// <summary>
        /// Capture a performance snapshot for diagnostics.
        /// </summary>
        /// <returns>Current metrics.</returns>
        AnimationBackendMetrics SnapshotMetrics();

        /// <summary>
        /// Returns true if any playback slot for this entity is currently active (playing or blending out).
        /// Used by AnimationStateReporterSystem to detect natural single-montage completion.
        /// </summary>
        bool IsAnySlotActive(AnimationBackendHandle handle);
    }
}
