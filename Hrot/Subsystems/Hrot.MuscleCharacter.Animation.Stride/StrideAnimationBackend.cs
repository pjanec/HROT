using System;
using System.Collections.Generic;
using System.Diagnostics;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.MuscleCharacter.Animation.Stride;

// ---------------------------------------------------------------------------
// Internal Stride-namespace types (ANC-P8-01, DD-1 §16 "no leakage").
// None of these types appear in any public API surface.
// ---------------------------------------------------------------------------

/// <summary>
/// Per-entity world-space placement. Mirrors what Stride.Engine.Entity.Transform
/// would carry. Updated by SetEntityTransform each tick (Option A, DD-1 §15.3).
/// Confined to the Hrot.MuscleCharacter.Animation.Stride namespace.
/// </summary>
internal struct StrideEntityTransform
{
    public float X, Y, Z;
    public float Yaw; // rotation around world-up axis (radians)
}

/// <summary>
/// Per-slot playback state owned by the blend tree builder.
/// Tracks elapsed time, blend curve, and which markers have fired.
/// </summary>
internal struct SlotPlaybackState
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
    public ulong FiredMarkerMask; // bit per marker, up to 64 markers per montage
}

/// <summary>Per-entity aim layer overlay state.</summary>
internal struct AimLayerState
{
    public bool IsActive;
    public bool IsReleasing;
    public float BlendWeight;
    public float BlendInTime;
    public float BlendOutTime;
    public float TargetX, TargetY, TargetZ;
    public byte Priority;
}

/// <summary>Per-entity stance transition state.</summary>
internal struct StanceTransitionState
{
    public byte CurrentStance;
    public byte TargetStance;
    public bool IsTransitioning;
    public float TransitionProgress;
    public float TransitionTotalSeconds;
}

/// <summary>
/// Per-entity blend tree builder. In full Stride integration this implements
/// Stride.Rendering.ProceduralModels.IBlendTreeBuilder; in the smoke backend it
/// simulates the same per-frame state machine without engine-level API calls.
/// All types are confined to Hrot.MuscleCharacter.Animation.Stride.
/// </summary>
internal sealed class PerEntityBlendTreeBuilder
{
    internal readonly SlotPlaybackState[] Slots = new SlotPlaybackState[8];
    internal AimLayerState Aim;
    internal StanceTransitionState Stance;
    internal readonly List<RawNotifyEvent> NotifyBuffer = new();

    /// <summary>
    /// Simulate Stride's BuildBlendTree callback (DD-1 §15.2).
    /// Updates blend weights for all active slots based on elapsed time.
    /// In real integration this pushes AnimationOperation entries.
    /// </summary>
    internal void BuildBlendTree()
    {
        for (int i = 0; i < 8; i++)
        {
            ref SlotPlaybackState slot = ref Slots[i];
            if (!slot.IsActive)
            {
                slot.BlendWeight = 0f;
                continue;
            }

            if (slot.BlendInTime > 0f && slot.ElapsedSeconds < slot.BlendInTime)
            {
                // Blend-in ramp
                slot.BlendWeight = slot.ElapsedSeconds / slot.BlendInTime;
            }
            else if (slot.InBlendOut && slot.BlendOutTime > 0f)
            {
                // Blend-out ramp: linear from 1.0 -> 0.0 over the blend-out window
                float remaining = slot.DurationSeconds - slot.ElapsedSeconds;
                slot.BlendWeight = slot.BlendOutTime > 0f
                    ? remaining / slot.BlendOutTime
                    : 0f;
            }
            else
            {
                slot.BlendWeight = 1f;
            }

            slot.BlendWeight = Math.Clamp(slot.BlendWeight, 0f, 1f);
        }
    }
}

// ---------------------------------------------------------------------------
// Public surface (ANC-P8-02): authored marker representation.
// Not a Stride type; lives in the .Stride namespace as the translation layer.
// ---------------------------------------------------------------------------

/// <summary>
/// Keyframed marker on a montage clip. Supplied by the asset import bridge at
/// startup via RegisterMontageMarkers (DD-1 §15.4). The Stride backend fires a
/// RawNotifyEvent when the clip playhead crosses TimeSeconds.
/// </summary>
public struct MontageMarker
{
    /// <summary>Time in the clip (seconds) at which this marker fires.</summary>
    public float TimeSeconds;

    /// <summary>Notify category emitted when the marker fires.</summary>
    public AnimNotifyCategory Kind;

    /// <summary>Hash of the marker name (for Generic notifies).</summary>
    public uint MarkerHash;

    /// <summary>Generic float payload passed through to RawNotifyEvent.</summary>
    public float PayloadFloat;

    /// <summary>Generic uint payload passed through to RawNotifyEvent.</summary>
    public uint PayloadUint;
}

// ---------------------------------------------------------------------------
// ANC-P8-01: StrideAnimationBackend
// ANC-P8-02: scene/transform + notify mapping
// ---------------------------------------------------------------------------

/// <summary>
/// Stride-specific implementation of IAnimationBackend (DD-1 §15.2).
/// Maintains a generation-safe per-entity entry pool and one
/// PerEntityBlendTreeBuilder per registered entity.
/// <para>
/// No Stride engine types are exposed in the public API. All internal engine
/// concepts (blend tree, clip evaluators, AnimationComponent) are encapsulated
/// within this namespace.
/// </para>
/// </summary>
public sealed class StrideAnimationBackend : IAnimationBackend
{
    // Entry pool capacity; sized conservatively for smoke/dev workloads.
    private const int MaxEntities = 256;

    // Duration used when no montage asset data is available (smoke level).
    private const float DefaultMontageDuration = 1.0f;

    // The backing entry pool and its free-slot stack (DD-1 §15.2).
    private struct Entry
    {
        public uint EntityId;
        public uint Generation;
        public bool InUse;
        public PerEntityBlendTreeBuilder Builder;  // never null when InUse
        public StrideEntityTransform Transform;    // ANC-P8-02 scene/transform
    }

    private readonly Entry[] _entries = new Entry[MaxEntities];
    private readonly Stack<int> _freeIndices = new(MaxEntities);

    // Marker schedule: MontageAssetId.Hash -> markers (ANC-P8-02, DD-1 §15.4).
    private readonly Dictionary<int, MontageMarker[]> _montageMarkers = new();

    private int _activeEntityCount;
    private float _lastTickMs;
    private float _peakTickMs;

    public StrideAnimationBackend()
    {
        // Seed free-index stack so slot 0 is allocated first (deterministic ordering).
        for (int i = MaxEntities - 1; i >= 0; i--)
            _freeIndices.Push(i);
    }

    // -----------------------------------------------------------------------
    // ANC-P8-02: marker/notify registration (asset bridge seam, DD-1 §15.4).
    // Not part of IAnimationBackend; called by the rendering bridge or tests.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Register keyframed clip markers for a montage. Called by the asset bridge
    /// at startup to supply the per-montage marker schedule. During Tick the
    /// backend fires a RawNotifyEvent when each marker's time is crossed.
    /// </summary>
    public void RegisterMontageMarkers(MontageAssetId montageId, MontageMarker[] markers)
    {
        _montageMarkers[montageId.Hash] = markers;
    }

    // -----------------------------------------------------------------------
    // ANC-P8-02: entity transform update (Option A, DD-1 §15.3).
    // Not part of IAnimationBackend; called by the rendering bridge after
    // backend.Tick() to write SimTransform -> StrideEntity.Transform.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Write the current world-space transform into the entity's backing Stride
    /// entity. Applies placement only; bone-pose transforms are owned by the
    /// blend tree builder.
    /// </summary>
    public void SetEntityTransform(AnimationBackendHandle handle,
        float x, float y, float z, float yaw)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return;
        ref Entry entry = ref _entries[idx];
        entry.Transform.X = x;
        entry.Transform.Y = y;
        entry.Transform.Z = z;
        entry.Transform.Yaw = yaw;
    }

    // -----------------------------------------------------------------------
    // IAnimationBackend -- registration lifecycle
    // -----------------------------------------------------------------------

    /// <summary>
    /// Optional initialization hook. Captures backend config for diagnostics;
    /// pool is pre-allocated in the constructor.
    /// </summary>
    public void Initialize(in AnimationBackendConfig config)
    {
        // Pool is already allocated. In production this would re-size based on
        // config.MaxEntities. Captured for future diagnostics integration.
    }

    public AnimationBackendHandle RegisterEntity(uint entityId, long characterDefHandle)
    {
        if (!_freeIndices.TryPop(out int idx))
            throw new InvalidOperationException(
                $"StrideAnimationBackend: entity pool exhausted (capacity {MaxEntities}).");

        // Increment generation; skip 0 so IsValid stays meaningful.
        uint generation = _entries[idx].Generation + 1;
        if (generation == 0)
            generation = 1;

        _entries[idx] = new Entry
        {
            EntityId = entityId,
            Generation = generation,
            InUse = true,
            Builder = new PerEntityBlendTreeBuilder(),
            Transform = default,
        };
        _activeEntityCount++;

        return new AnimationBackendHandle
        {
            Index = (uint)idx,
            Generation = generation,
        };
    }

    public void UnregisterEntity(AnimationBackendHandle handle)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return;

        uint gen = _entries[idx].Generation; // preserve for next-user generation bump
        _entries[idx] = default;
        _entries[idx].Generation = gen;

        _freeIndices.Push(idx);
        _activeEntityCount--;
    }

    public bool TryResolve(AnimationBackendHandle handle, out nint state)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
        {
            state = default;
            return false;
        }
        state = (nint)_entries[idx].EntityId;
        return true;
    }

    // -----------------------------------------------------------------------
    // IAnimationBackend -- playback control
    // -----------------------------------------------------------------------

    public void PlayMontageOnSlot(AnimationBackendHandle handle, in PlayMontageParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return;

        // Smoke level: slot 0 is the default for all montage plays.
        // In full integration the slot is resolved from montage asset metadata.
        ref SlotPlaybackState slot = ref _entries[idx].Builder.Slots[0];
        slot = new SlotPlaybackState
        {
            IsActive = true,
            MontageHash = @params.MontageId,
            ElapsedSeconds = 0f,
            DurationSeconds = DefaultMontageDuration,
            PlayRate = @params.PlayRate != 0f ? @params.PlayRate : 1f,
            BlendInTime = @params.BlendInTime,
            BlendOutTime = @params.BlendOutTime,
            BlendWeight = 0f,
            InBlendOut = false,
            FiredMarkerMask = 0,
        };
    }

    public void StopMontageOnSlot(AnimationBackendHandle handle, in StopMontageParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return;

        SlotPlaybackState[] slots = _entries[idx].Builder.Slots;
        for (int i = 0; i < 8; i++)
        {
            if (slots[i].IsActive)
            {
                slots[i].IsActive = false;
                slots[i].BlendWeight = 0f;
            }
        }
    }

    public void SetAimTargetPoint(AnimationBackendHandle handle, in LookAtPointParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return;

        ref AimLayerState aim = ref _entries[idx].Builder.Aim;
        aim.IsActive = true;
        aim.IsReleasing = false;
        aim.TargetX = @params.WorldPointX;
        aim.TargetY = @params.WorldPointY;
        aim.TargetZ = @params.WorldPointZ;
        aim.BlendInTime = @params.BlendInTime;
        aim.Priority = @params.Priority;
    }

    public void SetAimTargetEntity(AnimationBackendHandle handle, in LookAtEntityParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return;

        ref AimLayerState aim = ref _entries[idx].Builder.Aim;
        aim.IsActive = true;
        aim.IsReleasing = false;
        aim.BlendInTime = @params.BlendInTime;
        aim.Priority = @params.Priority;
        // World-space target resolved by rendering bridge on next tick;
        // no transform computation inside the backend.
    }

    public void ReleaseAim(AnimationBackendHandle handle, in ReleaseLookParams @params)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return;

        ref AimLayerState aim = ref _entries[idx].Builder.Aim;
        if (!aim.IsActive)
            return;
        aim.IsReleasing = true;
        aim.BlendOutTime = @params.BlendOutTime;
    }

    public void RequestStanceChange(AnimationBackendHandle handle,
        byte targetStance, float blendDurationSeconds)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return;

        ref StanceTransitionState stance = ref _entries[idx].Builder.Stance;
        if (stance.CurrentStance == targetStance)
            return;

        stance.TargetStance = targetStance;
        stance.IsTransitioning = true;
        stance.TransitionProgress = 0f;
        stance.TransitionTotalSeconds =
            blendDurationSeconds > 0f ? blendDurationSeconds : 0.3f;
    }

    // -----------------------------------------------------------------------
    // IAnimationBackend -- tick
    // -----------------------------------------------------------------------

    public void Tick(float deltaTime)
    {
        long startTicks = Stopwatch.GetTimestamp();

        for (int i = 0; i < MaxEntities; i++)
        {
            if (!_entries[i].InUse)
                continue;

            PerEntityBlendTreeBuilder builder = _entries[i].Builder;
            AdvanceSlots(builder, deltaTime);
            AdvanceAim(builder, deltaTime);
            AdvanceStance(builder, deltaTime);

            // Simulate Stride's per-frame AnimationProcessor callback (DD-1 §15.1).
            builder.BuildBlendTree();
        }

        double ms = (double)(Stopwatch.GetTimestamp() - startTicks) * 1000.0
            / Stopwatch.Frequency;
        _lastTickMs = (float)ms;
        if (_lastTickMs > _peakTickMs)
            _peakTickMs = _lastTickMs;
    }

    // -----------------------------------------------------------------------
    // IAnimationBackend -- notify drain (ANC-P8-02 drain path)
    // -----------------------------------------------------------------------

    public int DrainNotifies(Span<RawNotifyEvent> dest)
    {
        int total = 0;
        for (int i = 0; i < MaxEntities; i++)
        {
            if (!_entries[i].InUse)
                continue;

            List<RawNotifyEvent> buf = _entries[i].Builder.NotifyBuffer;
            int count = Math.Min(buf.Count, dest.Length - total);
            for (int j = 0; j < count; j++)
                dest[total + j] = buf[j];
            buf.Clear();
            total += count;
            if (total >= dest.Length)
                break;
        }
        return total;
    }

    public int DrainNotifies(AnimationBackendHandle handle, Span<RawNotifyEvent> dest)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return 0;

        List<RawNotifyEvent> buf = _entries[idx].Builder.NotifyBuffer;
        int count = Math.Min(buf.Count, dest.Length);
        for (int i = 0; i < count; i++)
            dest[i] = buf[i];
        buf.Clear();
        return count;
    }

    // -----------------------------------------------------------------------
    // IAnimationBackend -- query
    // -----------------------------------------------------------------------

    public bool GetCurrentStance(AnimationBackendHandle handle, out byte currentStance)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
        {
            currentStance = 0;
            return false;
        }
        currentStance = _entries[idx].Builder.Stance.CurrentStance;
        return true;
    }

    public AnimationBackendMetrics SnapshotMetrics()
    {
        int totalSlots = 0;
        int pendingNotifies = 0;

        for (int i = 0; i < MaxEntities; i++)
        {
            if (!_entries[i].InUse)
                continue;

            SlotPlaybackState[] slots = _entries[i].Builder.Slots;
            for (int s = 0; s < 8; s++)
            {
                if (slots[s].IsActive)
                    totalSlots++;
            }
            pendingNotifies += _entries[i].Builder.NotifyBuffer.Count;
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
        if (idx < 0)
            return false;

        SlotPlaybackState[] slots = _entries[idx].Builder.Slots;
        for (int i = 0; i < 8; i++)
        {
            if (slots[i].IsActive)
                return true;
        }
        return false;
    }

    public bool IsAnySlotInBlendOut(AnimationBackendHandle handle)
    {
        int idx = IndexOf(handle);
        if (idx < 0)
            return false;

        SlotPlaybackState[] slots = _entries[idx].Builder.Slots;
        for (int i = 0; i < 8; i++)
        {
            if (slots[i].IsActive && slots[i].InBlendOut)
                return true;
        }
        return false;
    }

    public void CrossfadeMontageOnSlot(AnimationBackendHandle handle, in PlayMontageParams @params)
        => PlayMontageOnSlot(handle, in @params);

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private int IndexOf(AnimationBackendHandle handle)
    {
        uint idx = handle.Index;
        if (idx >= MaxEntities)
            return -1;
        if (!_entries[idx].InUse)
            return -1;
        if (_entries[idx].Generation != handle.Generation)
            return -1;
        return (int)idx;
    }

    private void AdvanceSlots(PerEntityBlendTreeBuilder builder, float deltaTime)
    {
        for (int i = 0; i < 8; i++)
        {
            ref SlotPlaybackState slot = ref builder.Slots[i];
            if (!slot.IsActive)
                continue;

            float prevElapsed = slot.ElapsedSeconds;
            slot.ElapsedSeconds += deltaTime * slot.PlayRate;

            // ANC-P8-02: check marker crossings, push RawNotifyEvent on crossing.
            if (_montageMarkers.TryGetValue(slot.MontageHash, out MontageMarker[]? markers))
            {
                int limit = Math.Min(markers.Length, 64);
                for (int m = 0; m < limit; m++)
                {
                    ulong bit = 1UL << m;
                    if ((slot.FiredMarkerMask & bit) == 0 &&
                        prevElapsed < markers[m].TimeSeconds &&
                        slot.ElapsedSeconds >= markers[m].TimeSeconds)
                    {
                        slot.FiredMarkerMask |= bit;
                        builder.NotifyBuffer.Add(new RawNotifyEvent
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

            // Enter blend-out window when remaining time <= blend-out duration.
            float remaining = slot.DurationSeconds - slot.ElapsedSeconds;
            if (!slot.InBlendOut && slot.BlendOutTime > 0f && remaining <= slot.BlendOutTime)
                slot.InBlendOut = true;

            // Natural completion.
            if (slot.ElapsedSeconds >= slot.DurationSeconds)
            {
                slot.IsActive = false;
                slot.BlendWeight = 0f;
            }
        }
    }

    private static void AdvanceAim(PerEntityBlendTreeBuilder builder, float deltaTime)
    {
        ref AimLayerState aim = ref builder.Aim;
        if (!aim.IsActive)
            return;

        if (aim.IsReleasing)
        {
            float step = aim.BlendOutTime > 0f ? deltaTime / aim.BlendOutTime : 1f;
            aim.BlendWeight = Math.Max(0f, aim.BlendWeight - step);
            if (aim.BlendWeight <= 0f)
                aim.IsActive = false;
        }
        else
        {
            float step = aim.BlendInTime > 0f ? deltaTime / aim.BlendInTime : 1f;
            aim.BlendWeight = Math.Min(1f, aim.BlendWeight + step);
        }
    }

    private static void AdvanceStance(PerEntityBlendTreeBuilder builder, float deltaTime)
    {
        ref StanceTransitionState stance = ref builder.Stance;
        if (!stance.IsTransitioning)
            return;

        float step = stance.TransitionTotalSeconds > 0f
            ? deltaTime / stance.TransitionTotalSeconds
            : 1f;
        stance.TransitionProgress += step;

        if (stance.TransitionProgress >= 1f)
        {
            stance.CurrentStance = stance.TargetStance;
            stance.IsTransitioning = false;
            stance.TransitionProgress = 0f;
        }
    }
}
