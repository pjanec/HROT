using System;
using System.Collections.Generic;
using System.Diagnostics;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.Stride.Animation;

/// <summary>
/// Real Stride implementation of <see cref="IAnimationBackend"/> (STR-P4-T1, DD-1 §15).
/// Replaces the BATCH-01 P0 stub. Mirrors <c>FakeAnimationBackend</c>'s deterministic
/// semantics (generation-safe per-entity handle pool, locomotion-speed→blend derivation,
/// 8-slot montage state machine with blend-in/out windows, notify draining, stance
/// transitions) but, when a real Stride <see cref="PerEntityBlendTreeBuilder"/> is attached
/// to an entity, drives that builder's idle/walk/run + montage overlay each tick.
///
/// <para><b>Testable vs GPU-bound split (the core seam, DD-1 §15):</b></para>
/// <list type="bullet">
///   <item><description><b>Testable, headless (this class + <see cref="LocomotionBlend"/>):</b>
///     entity registration, the speed→idle/walk/run blend weights, the montage slot state
///     machine (active / blend-out window / natural completion), notify crossings, and stance
///     transitions. None of this touches a <c>GraphicsDevice</c>, so it is unit-tested directly.</description></item>
///   <item><description><b>GPU-bound (the optional <see cref="PerEntityBlendTreeBuilder"/>):</b>
///     creating <c>AnimationClipEvaluator</c>s from the <c>Blender</c>, installing the custom
///     <c>BlendTreeBuilder</c>, and the actual <c>AnimationComponent</c> pose composition. Only
///     instantiated inside the running Stride app; verified by the human run + BATCH-14 harness.</description></item>
/// </list>
///
/// <para>Root-motion hooks (DD-1 §19: <c>ExtractRootMotionDelta</c>,
/// <c>RootMotionApplicatorSystem</c>) are intentionally <b>not implemented</b> this pass, per
/// the design (a strict additive change to the contract later).</para>
///
/// <para>No Stride engine types appear on the public surface of <see cref="IAnimationBackend"/>;
/// Stride concepts are confined to <see cref="PerEntityBlendTreeBuilder"/> (DD-1 §16).</para>
/// </summary>
public sealed class StrideAnimationBackend : IAnimationBackend
{
    /// <summary>Minimum planar speed (m/s) below which footstep cadence pauses (mirrors FakeAnimationBackend).</summary>
    public const float MinFootstepSpeed = 0.3f;

    /// <summary>Distance (m) between footstep notify emissions while moving (mirrors FakeAnimationBackend).</summary>
    public const float FootstepStrideMeters = 0.9f;

    private const int MaxSlots = 8;
    private const float DefaultMontageDuration = 1.0f;

    /// <summary>Pure, headless per-slot montage playback state (no Stride types).</summary>
    public struct SlotState
    {
        public bool IsActive;
        public int MontageHash;
        public float ElapsedSeconds;
        public float DurationSeconds;
        public float PlayRate;
        public float BlendInTime;
        public float BlendOutTime;
        public float BlendWeight;
        public bool InBlendOut;
        public byte SectionIndex;
        public ulong FiredNotifyMask;
    }

    /// <summary>Notify schedule entry for one montage clip marker (asset-bridge supplied).</summary>
    private readonly struct MontageMarker
    {
        public MontageMarker(float t, AnimNotifyCategory kind, uint hash, float payloadF, uint payloadU)
        { TimeSeconds = t; Kind = kind; MarkerHash = hash; PayloadFloat = payloadF; PayloadUint = payloadU; }
        public readonly float TimeSeconds;
        public readonly AnimNotifyCategory Kind;
        public readonly uint MarkerHash;
        public readonly float PayloadFloat;
        public readonly uint PayloadUint;
    }

    // Per-entity behavioral state (class so slot array is mutated in place).
    private sealed class EntityState
    {
        public long CharacterDefHandle;
        public readonly SlotState[] Slots = new SlotState[MaxSlots];

        // Locomotion inputs + derived blend (the testable seam).
        public float HorizontalVelX;
        public float HorizontalVelZ;
        public float VerticalVelocity;
        public bool IsGrounded;
        public LocomotionBlendWeights Locomotion = LocomotionBlend.FromSpeed(0f);
        public double LocomotionNormalizedTime;
        public float DistanceSinceLastFootstep;
        public byte NextFootIndex;

        // Stance transition.
        public byte CurrentStance;
        public byte TargetStance;
        public bool IsTransitioning;
        public float TransitionProgress;
        public float TransitionTotalSeconds;

        // Aim layer (parity with the contract; not GPU-applied this pass).
        public bool AimActive;
        public bool AimReleasing;
        public float AimBlendWeight;
        public float AimBlendInTime;
        public float AimBlendOutTime;

        public readonly List<RawNotifyEvent> PendingNotifies = new();

        // Optional GPU-bound builder (null in headless tests).
        public PerEntityBlendTreeBuilder? Builder;
    }

    private struct Entry
    {
        public uint EntityId;
        public uint Generation;
        public bool InUse;
        public EntityState State;
    }

    private readonly Entry[] _entries;
    private readonly Stack<int> _freeIndices;
    private readonly Dictionary<int, MontageMarker[]> _montageMarkers = new();

    private int _activeEntityCount;
    private float _lastTickMs;
    private float _peakTickMs;

    private readonly int _maxEntities;

    /// <summary>Create a backend with the default entity pool capacity (256).</summary>
    public StrideAnimationBackend() : this(256) { }

    /// <summary>Create a backend with an explicit entity pool capacity.</summary>
    public StrideAnimationBackend(int maxEntities)
    {
        if (maxEntities <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntities));
        _maxEntities = maxEntities;
        _entries = new Entry[maxEntities];
        _freeIndices = new Stack<int>(maxEntities);
        for (int i = maxEntities - 1; i >= 0; i--)
            _freeIndices.Push(i);
    }

    /// <summary>Optional init hook (pool pre-allocated in ctor); captured for diagnostics parity.</summary>
    public void Initialize(in AnimationBackendConfig config) { }

    // ── Asset bridge seams (not part of IAnimationBackend) ──────────────────

    /// <summary>
    /// Register the keyframed notify markers for a montage (asset bridge, DD-1 §15.4).
    /// During <see cref="Tick"/> the backend fires a <see cref="RawNotifyEvent"/> when an
    /// active slot's playhead crosses a marker's time.
    /// </summary>
    public void RegisterMontageMarkers(int montageHash, params (float time, AnimNotifyCategory kind, uint markerHash, float payloadFloat, uint payloadUint)[] markers)
    {
        var arr = new MontageMarker[markers.Length];
        for (int i = 0; i < markers.Length; i++)
            arr[i] = new MontageMarker(markers[i].time, markers[i].kind, markers[i].markerHash, markers[i].payloadFloat, markers[i].payloadUint);
        _montageMarkers[montageHash] = arr;
    }

    /// <summary>
    /// Attach the GPU-bound <see cref="PerEntityBlendTreeBuilder"/> for an entity. Called by
    /// the Stride visual binding bridge once the entity's <c>AnimationComponent</c> exists.
    /// Headless tests leave this unset, so the backend's behavioral logic runs without a GPU.
    /// </summary>
    public void AttachBlendTreeBuilder(AnimationBackendHandle handle, PerEntityBlendTreeBuilder builder)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return;
        _entries[idx].State.Builder = builder;
    }

    // ── IAnimationBackend — registration lifecycle ──────────────────────────

    public AnimationBackendHandle RegisterEntity(uint entityId, long characterDefHandle)
    {
        if (!_freeIndices.TryPop(out int idx))
            throw new InvalidOperationException(
                $"StrideAnimationBackend: entity pool exhausted (capacity {_maxEntities}).");

        uint generation = _entries[idx].Generation + 1;
        if (generation == 0) generation = 1;

        _entries[idx] = new Entry
        {
            EntityId = entityId,
            Generation = generation,
            InUse = true,
            State = new EntityState { CharacterDefHandle = characterDefHandle },
        };
        _activeEntityCount++;

        return new AnimationBackendHandle { Index = (uint)idx, Generation = generation };
    }

    public void UnregisterEntity(AnimationBackendHandle handle)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return;

        _entries[idx].State.Builder?.ReleaseEvaluators();

        uint gen = _entries[idx].Generation;
        _entries[idx] = default;
        _entries[idx].Generation = gen;

        _freeIndices.Push(idx);
        _activeEntityCount--;
    }

    public bool TryResolve(AnimationBackendHandle handle, out nint state)
    {
        int idx = IndexOf(handle);
        if (idx < 0) { state = default; return false; }
        state = (nint)_entries[idx].EntityId;
        return true;
    }

    // ── IAnimationBackend — playback control ────────────────────────────────

    public void PlayMontageOnSlot(AnimationBackendHandle handle, in PlayMontageParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return;

        // Slot resolution: full-body montages land on slot 0 by convention for the
        // idle/walk/run + jump bring-up (DD-4 §2 slot 0 = Locomotion/FullBody). The
        // descriptor's per-montage Slot is honored by the dispatcher upstream; here we
        // place it deterministically so the state machine is testable.
        int slotIdx = 0;
        ref SlotState slot = ref _entries[idx].State.Slots[slotIdx];

        float duration = DefaultMontageDuration;
        // If markers exist, derive a duration that comfortably contains them so the
        // blend-out window math is meaningful even without asset metadata.
        if (_montageMarkers.TryGetValue(@params.MontageId, out var markers))
        {
            for (int m = 0; m < markers.Length; m++)
                if (markers[m].TimeSeconds + 0.1f > duration)
                    duration = markers[m].TimeSeconds + 0.1f;
        }

        slot = new SlotState
        {
            IsActive = true,
            MontageHash = @params.MontageId,
            ElapsedSeconds = 0f,
            DurationSeconds = duration,
            PlayRate = @params.PlayRate != 0f ? @params.PlayRate : 1f,
            BlendInTime = @params.BlendInTime,
            BlendOutTime = @params.BlendOutTime,
            BlendWeight = 0f,
            InBlendOut = false,
            SectionIndex = @params.StartSectionIndex,
            FiredNotifyMask = 0,
        };
    }

    public void StopMontageOnSlot(AnimationBackendHandle handle, in StopMontageParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return;

        // Force the blend-out window (mirror FakeAnimationBackend §3.3): do not hard-clear.
        // Natural completion deactivates the slot once elapsed >= duration.
        float blendOut = @params.BlendOutTime > 0f ? @params.BlendOutTime : 0f;
        SlotState[] slots = _entries[idx].State.Slots;
        for (int i = 0; i < MaxSlots; i++)
        {
            if (!slots[i].IsActive) continue;
            slots[i].BlendOutTime = blendOut;
            slots[i].ElapsedSeconds = MathF.Max(
                slots[i].ElapsedSeconds, slots[i].DurationSeconds - blendOut);
            slots[i].InBlendOut = true;
        }
    }

    public void CrossfadeMontageOnSlot(AnimationBackendHandle handle, in PlayMontageParams @params)
        => PlayMontageOnSlot(handle, in @params);

    public void SetAimTargetPoint(AnimationBackendHandle handle, in LookAtPointParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return;
        var s = _entries[idx].State;
        s.AimActive = true; s.AimReleasing = false; s.AimBlendInTime = @params.BlendInTime;
    }

    public void SetAimTargetEntity(AnimationBackendHandle handle, in LookAtEntityParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return;
        var s = _entries[idx].State;
        s.AimActive = true; s.AimReleasing = false; s.AimBlendInTime = @params.BlendInTime;
    }

    public void ReleaseAim(AnimationBackendHandle handle, in ReleaseLookParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return;
        var s = _entries[idx].State;
        if (!s.AimActive) return;
        s.AimReleasing = true; s.AimBlendOutTime = @params.BlendOutTime;
    }

    public void RequestStanceChange(AnimationBackendHandle handle, byte targetStance, float blendDurationSeconds)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return;
        var s = _entries[idx].State;
        if (s.CurrentStance == targetStance) return;
        s.TargetStance = targetStance;
        s.IsTransitioning = true;
        s.TransitionProgress = 0f;
        s.TransitionTotalSeconds = blendDurationSeconds > 0f ? blendDurationSeconds : 0.3f;
    }

    /// <summary>
    /// Update an entity's locomotion inputs (m/s + grounded). Recomputes the idle/walk/run
    /// blend immediately via <see cref="LocomotionBlend"/>. Non-interface method, called by
    /// <c>AnimationRuntimeBridgeSystem</c> (DD-1 §10) from <c>SimVelocity</c>; signature mirrors
    /// <c>FakeAnimationBackend.UpdateLocomotionInputs</c>.
    /// </summary>
    public void UpdateLocomotionInputs(
        AnimationBackendHandle handle,
        float horizontalVelX, float horizontalVelZ, float verticalVelocity, bool isGrounded)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return;
        var s = _entries[idx].State;
        s.HorizontalVelX = horizontalVelX;
        s.HorizontalVelZ = horizontalVelZ;
        s.VerticalVelocity = verticalVelocity;
        s.IsGrounded = isGrounded;
        s.Locomotion = LocomotionBlend.FromVelocity(horizontalVelX, horizontalVelZ);
    }

    /// <summary>Query the current locomotion blend for an entity (test/diagnostic seam).</summary>
    public LocomotionBlendWeights QueryLocomotion(AnimationBackendHandle handle)
    {
        int idx = IndexOf(handle);
        return idx < 0 ? default : _entries[idx].State.Locomotion;
    }

    /// <summary>Query the playback state of a slot for an entity (test/diagnostic seam).</summary>
    public SlotState QuerySlotState(AnimationBackendHandle handle, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots) return default;
        int idx = IndexOf(handle);
        return idx < 0 ? default : _entries[idx].State.Slots[slotIndex];
    }

    /// <summary>
    /// Per-entity backend→builder hook (STR-P4 live-glue, DD-1 §15). Returns the exact
    /// locomotion blend + phase the backend would push to a
    /// <see cref="PerEntityBlendTreeBuilder"/> this frame (the same values
    /// <see cref="Tick"/> hands to <see cref="PerEntityBlendTreeBuilder.SetLocomotion"/>).
    /// The headless live-glue binder pumps these into the GPU-bound builder; tests assert
    /// they match what <see cref="LocomotionBlend"/> computed for a given speed.
    /// Returns <c>false</c> if the handle is stale/unregistered.
    /// </summary>
    /// <param name="handle">The entity's backend handle.</param>
    /// <param name="weights">The idle/walk/run blend the backend computed for this entity.</param>
    /// <param name="normalizedTime">The 0..1 locomotion cycle phase advanced by the tick.</param>
    public bool TryGetLocomotionBlend(
        AnimationBackendHandle handle,
        out LocomotionBlendWeights weights,
        out double normalizedTime)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
        {
            weights = default;
            normalizedTime = 0.0;
            return false;
        }
        EntityState s = _entries[idx].State;
        weights = s.Locomotion;
        normalizedTime = s.LocomotionNormalizedTime;
        return true;
    }

    /// <summary>
    /// Per-entity montage-overlay hook (STR-P4 live-glue, DD-1 §15). Returns the exact
    /// montage overlay state the backend would push to a
    /// <see cref="PerEntityBlendTreeBuilder"/> this frame (the same values
    /// <see cref="Tick"/> hands to <see cref="PerEntityBlendTreeBuilder.SetMontage"/>):
    /// the active slot-0 montage hash, its overlay weight (0 when no montage is active),
    /// and its normalized 0..1 playhead. Returns <c>false</c> if the handle is
    /// stale/unregistered.
    /// </summary>
    /// <param name="handle">The entity's backend handle.</param>
    /// <param name="montageHash">The active slot-0 montage's asset-id hash.</param>
    /// <param name="weight">The overlay weight (0 when no montage is active this frame).</param>
    /// <param name="normalizedTime">The 0..1 montage playhead (elapsed / duration).</param>
    public bool TryGetMontageOverlay(
        AnimationBackendHandle handle,
        out int montageHash,
        out float weight,
        out double normalizedTime)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
        {
            montageHash = 0;
            weight = 0f;
            normalizedTime = 0.0;
            return false;
        }
        ref SlotState slot0 = ref _entries[idx].State.Slots[0];
        montageHash = slot0.MontageHash;
        weight = slot0.IsActive ? slot0.BlendWeight : 0f;
        normalizedTime = slot0.DurationSeconds > 0f ? slot0.ElapsedSeconds / slot0.DurationSeconds : 0.0;
        return true;
    }

    // ── IAnimationBackend — tick ────────────────────────────────────────────

    public void Tick(float deltaTime)
    {
        long startTicks = Stopwatch.GetTimestamp();

        for (int i = 0; i < _maxEntities; i++)
        {
            if (!_entries[i].InUse) continue;
            EntityState s = _entries[i].State;

            AdvanceLocomotion(s, deltaTime);
            AdvanceSlots(s, deltaTime);
            AdvanceStance(s, deltaTime);
            AdvanceAim(s, deltaTime);
            AdvanceFootsteps(s, deltaTime);

            // Push the headless-computed state into the GPU-bound builder (if attached).
            // The exact same values are exposed to the live-glue binder via
            // TryGetLocomotionBlend / TryGetMontageOverlay (the testable backend→builder hook).
            if (s.Builder != null)
            {
                s.Builder.SetLocomotion(s.Locomotion, s.LocomotionNormalizedTime);
                ref SlotState slot0 = ref s.Slots[0];
                s.Builder.SetMontage(slot0.MontageHash, slot0.IsActive ? slot0.BlendWeight : 0f,
                    slot0.DurationSeconds > 0f ? slot0.ElapsedSeconds / slot0.DurationSeconds : 0.0);
            }
        }

        double ms = (double)(Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        _lastTickMs = (float)ms;
        if (_lastTickMs > _peakTickMs) _peakTickMs = _lastTickMs;
    }

    private static void AdvanceLocomotion(EntityState s, float deltaTime)
    {
        // Advance the locomotion phase as a normalized 0..1 cycle. Real clip durations
        // are applied GPU-side by the builder; here we keep a deterministic phase so the
        // builder's NewPush time is stable. Cycle speed scales softly with motion.
        float speed = MathF.Sqrt(s.HorizontalVelX * s.HorizontalVelX + s.HorizontalVelZ * s.HorizontalVelZ);
        double cyclesPerSecond = speed <= LocomotionBlend.IdleSpeed ? 0.5 : 1.0 + speed * 0.25;
        s.LocomotionNormalizedTime = (s.LocomotionNormalizedTime + deltaTime * cyclesPerSecond) % 1.0;
        if (s.LocomotionNormalizedTime < 0) s.LocomotionNormalizedTime += 1.0;
    }

    private void AdvanceSlots(EntityState s, float deltaTime)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            ref SlotState slot = ref s.Slots[i];
            if (!slot.IsActive) continue;

            float prevElapsed = slot.ElapsedSeconds;
            slot.ElapsedSeconds += deltaTime * slot.PlayRate;

            // Notify crossings.
            if (_montageMarkers.TryGetValue(slot.MontageHash, out var markers))
            {
                int limit = Math.Min(markers.Length, 64);
                for (int m = 0; m < limit; m++)
                {
                    ulong bit = 1UL << m;
                    if ((slot.FiredNotifyMask & bit) == 0 &&
                        prevElapsed < markers[m].TimeSeconds &&
                        slot.ElapsedSeconds >= markers[m].TimeSeconds)
                    {
                        slot.FiredNotifyMask |= bit;
                        s.PendingNotifies.Add(new RawNotifyEvent
                        {
                            Kind = markers[m].Kind,
                            MarkerHash = markers[m].MarkerHash,
                            TimeSeconds = markers[m].TimeSeconds,
                            PayloadFloat = markers[m].PayloadFloat,
                            PayloadUint = markers[m].PayloadUint,
                        });
                    }
                }
            }

            // Blend weight + blend-out window (mirror FakeAnimationBackend §4.1).
            if (slot.ElapsedSeconds < slot.BlendInTime)
                slot.BlendWeight = slot.BlendInTime > 0f ? slot.ElapsedSeconds / slot.BlendInTime : 1f;
            else if (slot.BlendOutTime > 0f && slot.ElapsedSeconds > slot.DurationSeconds - slot.BlendOutTime)
            {
                float remain = slot.DurationSeconds - slot.ElapsedSeconds;
                slot.BlendWeight = MathF.Max(0f, remain / slot.BlendOutTime);
                slot.InBlendOut = true;
            }
            else
                slot.BlendWeight = 1f;

            // Natural completion.
            if (slot.ElapsedSeconds >= slot.DurationSeconds)
            {
                slot.IsActive = false;
                slot.ElapsedSeconds = 0f;
                slot.InBlendOut = false;
                slot.BlendWeight = 0f;
            }
        }
    }

    private static void AdvanceStance(EntityState s, float deltaTime)
    {
        if (!s.IsTransitioning) return;
        float step = s.TransitionTotalSeconds > 0f ? deltaTime / s.TransitionTotalSeconds : 1f;
        s.TransitionProgress += step;
        if (s.TransitionProgress >= 1f)
        {
            s.CurrentStance = s.TargetStance;
            s.IsTransitioning = false;
            s.TransitionProgress = 0f;
        }
    }

    private static void AdvanceAim(EntityState s, float deltaTime)
    {
        if (!s.AimActive) return;
        if (s.AimReleasing)
        {
            float step = s.AimBlendOutTime > 0f ? deltaTime / s.AimBlendOutTime : 1f;
            s.AimBlendWeight = MathF.Max(0f, s.AimBlendWeight - step);
            if (s.AimBlendWeight <= 0f) s.AimActive = false;
        }
        else
        {
            float step = s.AimBlendInTime > 0f ? deltaTime / s.AimBlendInTime : 1f;
            s.AimBlendWeight = MathF.Min(1f, s.AimBlendWeight + step);
        }
    }

    private static void AdvanceFootsteps(EntityState s, float deltaTime)
    {
        float speed = MathF.Sqrt(s.HorizontalVelX * s.HorizontalVelX + s.HorizontalVelZ * s.HorizontalVelZ);
        if (!s.IsGrounded || speed < MinFootstepSpeed)
        {
            s.DistanceSinceLastFootstep = 0f;
            return;
        }

        s.DistanceSinceLastFootstep += speed * deltaTime;
        if (s.DistanceSinceLastFootstep >= FootstepStrideMeters)
        {
            s.DistanceSinceLastFootstep -= FootstepStrideMeters;
            byte footIndex = s.NextFootIndex;
            s.PendingNotifies.Add(new RawNotifyEvent
            {
                Kind = AnimNotifyCategory.Footstep,
                MarkerHash = 0,
                TimeSeconds = 0f,
                PayloadFloat = 0f,
                PayloadUint = footIndex,
            });
            s.NextFootIndex = (byte)(1 - s.NextFootIndex);
        }
    }

    // ── IAnimationBackend — notify drain ────────────────────────────────────

    public int DrainNotifies(Span<RawNotifyEvent> dest)
    {
        int total = 0;
        for (int i = 0; i < _maxEntities && total < dest.Length; i++)
        {
            if (!_entries[i].InUse) continue;
            var buf = _entries[i].State.PendingNotifies;
            int count = Math.Min(buf.Count, dest.Length - total);
            for (int j = 0; j < count; j++) dest[total + j] = buf[j];
            buf.Clear();
            total += count;
        }
        return total;
    }

    public int DrainNotifies(AnimationBackendHandle handle, Span<RawNotifyEvent> dest)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return 0;
        var buf = _entries[idx].State.PendingNotifies;
        int count = Math.Min(buf.Count, dest.Length);
        for (int i = 0; i < count; i++) dest[i] = buf[i];
        buf.Clear();
        return count;
    }

    // ── IAnimationBackend — query ───────────────────────────────────────────

    public bool GetCurrentStance(AnimationBackendHandle handle, out byte currentStance)
    {
        int idx = IndexOf(handle);
        if (idx < 0) { currentStance = 0; return false; }
        currentStance = _entries[idx].State.CurrentStance;
        return true;
    }

    public AnimationBackendMetrics SnapshotMetrics()
    {
        int totalSlots = 0, pendingNotifies = 0;
        for (int i = 0; i < _maxEntities; i++)
        {
            if (!_entries[i].InUse) continue;
            SlotState[] slots = _entries[i].State.Slots;
            for (int s = 0; s < MaxSlots; s++)
                if (slots[s].IsActive) totalSlots++;
            pendingNotifies += _entries[i].State.PendingNotifies.Count;
        }
        return new AnimationBackendMetrics
        {
            ActiveEntityCount = _activeEntityCount,
            TotalActiveSlotsCount = totalSlots,
            PendingNotifyCount = pendingNotifies,
            LastTickMs = _lastTickMs,
            PeakTickMs = _peakTickMs,
        };
    }

    public bool IsAnySlotActive(AnimationBackendHandle handle)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return false;
        SlotState[] slots = _entries[idx].State.Slots;
        for (int i = 0; i < MaxSlots; i++)
            if (slots[i].IsActive) return true;
        return false;
    }

    public bool IsAnySlotInBlendOut(AnimationBackendHandle handle)
    {
        int idx = IndexOf(handle);
        if (idx < 0) return false;
        SlotState[] slots = _entries[idx].State.Slots;
        for (int i = 0; i < MaxSlots; i++)
            if (slots[i].IsActive && slots[i].InBlendOut) return true;
        return false;
    }

    // ── private ─────────────────────────────────────────────────────────────

    private int IndexOf(AnimationBackendHandle handle)
    {
        uint idx = handle.Index;
        if (idx >= _maxEntities) return -1;
        if (!_entries[idx].InUse) return -1;
        if (_entries[idx].Generation != handle.Generation) return -1;
        return (int)idx;
    }
}
