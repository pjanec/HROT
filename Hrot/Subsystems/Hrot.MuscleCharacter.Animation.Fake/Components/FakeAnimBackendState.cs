using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Numerics;
using Fdp.Core;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.MuscleCharacter.Animation.Fake.Components;

/// <summary>
/// ANC-P1-01: Unmanaged Tier-1 component holding all per-entity state for the FakeAnimationBackend.
/// Total size ~1 KB, fits comfortably under Tier-1's 64 KB hard limit.
/// </summary>
[ComponentId(GlobalComponentIds.FakeAnimBackendState)]
[DataPolicy(DataPolicy.NoSave)]
[StructLayout(LayoutKind.Sequential)]
public struct FakeAnimBackendState
{
    /// <summary>Matches AnimationBackendHandle.Generation. Detects stale handles across cycles.</summary>
    public uint Generation;

    /// <summary>Total Tick() calls this entity has been part of. Useful for debugging.</summary>
    public long TotalTicks;

    /// <summary>Fixed table of 8 slots — DD-1's MaxSlots.</summary>
    public FakeSlotsBuffer Slots;

    public FakeAimState Aim;
    public FakeStanceState Stance;

    // --- Locomotion inputs (for footstep cadence + diagnostics) ---
    /// <summary>Magnitude of LocalHorizontalVelocity.</summary>
    public float HorizontalSpeed;
    /// <summary>Local-space, +X = forward.</summary>
    public Vector2 LocalHorizontalVelocity;
    public float VerticalVelocity;
    /// <summary>Bool-as-byte for deterministic layout.</summary>
    public byte IsGrounded;
    public float DistanceSinceLastFootstep;
    /// <summary>0 = left, 1 = right; alternates.</summary>
    public byte NextFootIndex;

    // --- Pending notify ring (drained each tick by NotifyEventEmitterSystem) ---
    /// <summary>Number of live entries in PendingNotifies. Drained to 0 each tick under normal operation.</summary>
    public byte PendingNotifyCount;

    /// <summary>
    /// Inline buffer of pending notify events. Mutate using Pattern A (Span-cast) or Pattern B
    /// (Get→Mutate→SetComponent) per DD-1 §4.3. Overflow is a hard assert (§6).
    /// </summary>
    public FakePendingNotifyBuffer PendingNotifies;
}

/// <summary>8-slot buffer using C# 12 [InlineArray].</summary>
[InlineArray(8)]
public struct FakeSlotsBuffer
{
    private FakeSlotState _e0;
}

/// <summary>16-entry notify buffer using C# 12 [InlineArray].</summary>
[InlineArray(16)]
public struct FakePendingNotifyBuffer
{
    private RawNotifyEvent _e0;
}

/// <summary>Per-slot playback state (within a montage slot).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FakeSlotState
{
    /// <summary>Bool-as-byte: 0 = inactive, 1 = active.</summary>
    public byte IsActive;
    /// <summary>Currently playing montage ID; 0 = none.</summary>
    public MontageAssetId ActiveMontage;
    /// <summary>Elapsed time in current montage (seconds).</summary>
    public float ElapsedSeconds;
    /// <summary>Total duration of the active montage (seconds).</summary>
    public float TotalDurationSeconds;
    /// <summary>Blend-in duration (time to reach full weight from blend-in weight).</summary>
    public float BlendInTime;
    /// <summary>Blend-out duration (time to reach zero weight).</summary>
    public float BlendOutTime;
    /// <summary>Playback speed multiplier.</summary>
    public float PlayRate;
    /// <summary>Current montage section index.</summary>
    public byte CurrentSectionIndex;
    /// <summary>Bool-as-byte: 1 = slot is in blend-out phase.</summary>
    public byte InBlendOutWindow;
    /// <summary>Blend weight [0..1].</summary>
    public float BlendWeight;
    /// <summary>
    /// Bitmask: bit i = notify i in this slot's montage has already fired this play.
    /// Prevents double-fire (64-bit = max 64 notifies per montage, vastly more than needed).
    /// </summary>
    public ulong FiredNotifyMask;
}

/// <summary>Aim/look-at state (concurrent with montage slots).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FakeAimState
{
    /// <summary>Bool-as-byte: 1 = aim is active or releasing.</summary>
    public byte IsActive;
    /// <summary>Current aim point (lerped toward TargetWorldAimPoint each tick).</summary>
    public Vector3 WorldAimPoint;
    /// <summary>Requested aim point.</summary>
    public Vector3 TargetWorldAimPoint;
    /// <summary>Blend-in duration (snap to blend-in weight on first-acquire).</summary>
    public float BlendInTime;
    /// <summary>Blend-out duration (ramp to zero on release).</summary>
    public float BlendOutTime;
    /// <summary>Priority (for arbitration if multiple look-at requests exist).</summary>
    public byte Priority;
    /// <summary>Blend weight [0..1].</summary>
    public float BlendWeight;
    /// <summary>Bool-as-byte: 1 = aim is releasing (ramping blend to 0).</summary>
    public byte IsReleasing;
}

/// <summary>Stance transition state.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FakeStanceState
{
    /// <summary>Current active stance.</summary>
    public byte CurrentStance;
    /// <summary>Target stance for in-progress transition.</summary>
    public byte TargetStance;
    /// <summary>Bool-as-byte: 1 = transition in progress.</summary>
    public byte IsTransitioning;
    /// <summary>Transition progress [0..1].</summary>
    public float TransitionProgress;
    /// <summary>Total transition duration (seconds).</summary>
    public float TransitionTotalSeconds;
}
