using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Fake.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.MuscleCharacter.Animation.Fake;

/// <summary>
/// ANC-P1 Phase 1: Behavioral FakeAnimationBackend for deterministic testing.
/// Stores per-entity state in a dictionary (not ECS-backed) for test isolation.
/// Optional class-data map allows montage lookup for behavioral tests.
/// </summary>
public sealed class FakeAnimationBackend : IAnimationBackend
{
    // Constants for footstep cadence (DD-Fake §5)
    public const float MinFootstepSpeed = 0.3f;
    public const float FootstepStrideMeters = 0.9f;

    // Per-entity behavioral state (class, not struct, so slots[] is mutable in place)
    private sealed class EntityBehavioralState
    {
        public long CharacterDefHandle;
        public FakeSlotState[] Slots = new FakeSlotState[8];
        public FakeStanceState Stance;
        public FakeAimState Aim;
        public float HorizontalVelX;
        public float HorizontalVelZ;
        public float VerticalVelocity;
        public bool IsGrounded;
        public float DistanceSinceLastFootstep;
        public byte NextFootIndex;
        public List<RawNotifyEvent> PendingNotifies = new List<RawNotifyEvent>();
    }

    private readonly IReadOnlyDictionary<long, CharacterAnimationBakedData>? _classData;

    // Optional ECS repository for FakeAnimBackendState component write-through (OFX-003).
    private EntityRepository? _repo;
    // Maps entityIndex -> full Entity handle for ECS component operations.
    private readonly Dictionary<uint, Entity> _entityIndexToEntity = new();

    // Handle bookkeeping: handleIndex -> (generation, entityId)
    private readonly Dictionary<uint, (uint Generation, uint EntityId)> _handleSlots = new();
    private readonly Dictionary<uint, EntityBehavioralState> _entityStates = new();
    private uint _nextGeneration = 1;
    private uint _nextHandleIndex = 1;

    /// <summary>
    /// Number of live entries in the entityIndex -> Entity map.
    /// Exposed for test verification of the UnregisterEntity leak fix (FIX2-014).
    /// </summary>
    public int EntityIndexMapCount => _entityIndexToEntity.Count;

    /// <summary>
    /// Create a minimal backend with no montage data (smoke tests).
    /// </summary>
    public FakeAnimationBackend()
    {
        _classData = null;
    }

    /// <summary>
    /// Create a behavioral backend with per-class baked data for montage lookup.
    /// </summary>
    /// <param name="classData">Baked animation data keyed by characterDefHandle (classId).</param>
    public FakeAnimationBackend(IReadOnlyDictionary<long, CharacterAnimationBakedData> classData)
    {
        _classData = classData;
    }

    public void Initialize(in AnimationBackendConfig config)
    {
        // No-op for fake backend
    }

    /// <summary>
    /// Injects an EntityRepository so RegisterEntity will also populate
    /// the <see cref="FakeAnimBackendState"/> ECS component (OFX-003).
    /// Must be called before registering entities.
    /// </summary>
    public void SetEntityRepository(EntityRepository repo)
    {
        _repo = repo;
        repo.RegisterComponent<FakeAnimBackendState>();
    }

    public AnimationBackendHandle RegisterEntity(uint entityId, long characterDefHandle)
    {
        uint handleIndex = _nextHandleIndex++;
        uint generation = _nextGeneration++;

        var handle = new AnimationBackendHandle { Index = handleIndex, Generation = generation };
        _handleSlots[handleIndex] = (generation, entityId);

        var state = new EntityBehavioralState { CharacterDefHandle = characterDefHandle };
        _entityStates[entityId] = state;

        // OFX-003: when EntityRepository is injected, also populate the ECS component.
        if (_repo != null)
        {
            var entity = _repo.GetEntityByIndex((int)entityId);
            _entityIndexToEntity[entityId] = entity;
            _repo.AddComponent(entity, new FakeAnimBackendState { Generation = generation });
        }

        return handle;
    }

    public void UnregisterEntity(AnimationBackendHandle handle)
    {
        if (!_handleSlots.TryGetValue(handle.Index, out var slot))
            return;

        if (slot.Generation != handle.Generation)
            return;

        _handleSlots.Remove(handle.Index);
        _entityStates.Remove(slot.EntityId);
        _entityIndexToEntity.Remove(slot.EntityId);
    }

    public bool TryResolve(AnimationBackendHandle handle, out nint state)
    {
        if (!_handleSlots.TryGetValue(handle.Index, out var slot))
        {
            state = default;
            return false;
        }

        if (slot.Generation != handle.Generation)
        {
            state = default;
            return false;
        }

        state = (nint)slot.EntityId;
        return true;
    }

    // Internal: resolve handle to entity behavioral state
    private bool TryResolveToState(AnimationBackendHandle handle, out EntityBehavioralState? behavioralState)
    {
        if (!_handleSlots.TryGetValue(handle.Index, out var slot) ||
            slot.Generation != handle.Generation)
        {
            behavioralState = null;
            return false;
        }

        return _entityStates.TryGetValue(slot.EntityId, out behavioralState);
    }

    public void PlayMontageOnSlot(AnimationBackendHandle handle, in PlayMontageParams @params)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return;

        if (_classData == null)
            return;

        if (!_classData.TryGetValue(state.CharacterDefHandle, out var bakedData))
            return;

        if (!bakedData.MontageDict.TryGetValue(@params.MontageId, out var montageInfo))
            return;

        float playRate = @params.PlayRate != 0f ? @params.PlayRate : 1f;
        float blendIn = @params.BlendInTime != 0f ? @params.BlendInTime : montageInfo.DefaultBlendInTime;
        float blendOut = @params.BlendOutTime != 0f ? @params.BlendOutTime : montageInfo.DefaultBlendOutTime;

        int slotIdx = montageInfo.Slot;
        if (slotIdx < 0 || slotIdx >= 8)
            return;

        state.Slots[slotIdx] = new FakeSlotState
        {
            IsActive = 1,
            ActiveMontage = new MontageAssetId { Hash = @params.MontageId },
            ElapsedSeconds = 0f,
            TotalDurationSeconds = montageInfo.Duration,
            BlendInTime = blendIn,
            BlendOutTime = blendOut,
            PlayRate = playRate,
            CurrentSectionIndex = @params.StartSectionIndex,
            InBlendOutWindow = 0,
            BlendWeight = 0f,
            FiredNotifyMask = 0,
        };
    }

    public void StopMontageOnSlot(AnimationBackendHandle handle, in StopMontageParams @params)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return;

        // DD-Fake §3.3: force blend-out window instead of hard-clearing the slot.
        // Natural completion (AdvanceSlots) will deactivate the slot once elapsed >= total.
        float blendOut = @params.BlendOutTime > 0f ? @params.BlendOutTime : 0f;
        for (int i = 0; i < 8; i++)
        {
            if (state.Slots[i].IsActive == 0)
                continue;

            state.Slots[i].BlendOutTime = blendOut;
            state.Slots[i].ElapsedSeconds = MathF.Max(
                state.Slots[i].ElapsedSeconds,
                state.Slots[i].TotalDurationSeconds - blendOut);
            state.Slots[i].InBlendOutWindow = 1;
        }
    }

    public void SetAimTargetPoint(AnimationBackendHandle handle, in LookAtPointParams @params)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return;

        state.Aim.IsActive = 1;
        state.Aim.TargetWorldAimPoint = new System.Numerics.Vector3(
            @params.WorldPointX, @params.WorldPointY, @params.WorldPointZ);
        state.Aim.BlendInTime = @params.BlendInTime;
        state.Aim.Priority = @params.Priority;
        state.Aim.IsReleasing = 0;
    }

    public void SetAimTargetEntity(AnimationBackendHandle handle, in LookAtEntityParams @params)
    {
        // Entity-based aim: store intent without world-space resolution (no world context in fake)
        if (!TryResolveToState(handle, out var state) || state == null)
            return;

        state.Aim.IsActive = 1;
        state.Aim.BlendInTime = @params.BlendInTime;
        state.Aim.Priority = @params.Priority;
        state.Aim.IsReleasing = 0;
    }

    public void ReleaseAim(AnimationBackendHandle handle, in ReleaseLookParams @params)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return;

        state.Aim.IsReleasing = 1;
        state.Aim.BlendOutTime = @params.BlendOutTime;
    }

    public void RequestStanceChange(AnimationBackendHandle handle, byte targetStance, float blendDurationSeconds)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return;

        if (state.Stance.CurrentStance == targetStance)
            return;

        state.Stance.TargetStance = targetStance;
        state.Stance.IsTransitioning = 1;
        state.Stance.TransitionProgress = 0f;
        state.Stance.TransitionTotalSeconds = blendDurationSeconds > 0f ? blendDurationSeconds : 0.3f;
    }

    public void Tick(float deltaTime)
    {
        foreach (var (entityId, state) in _entityStates)
        {
            AdvanceSlots(state, deltaTime);
            AdvanceAim(state, deltaTime);
            AdvanceStance(state, deltaTime);
            AdvanceFootsteps(state, deltaTime);

            // Mirror per-tick state to FakeAnimBackendState ECS component (OFX-003 / FIX2-014).
            if (_repo != null && _entityIndexToEntity.TryGetValue(entityId, out var entity))
                MirrorToEcs(entity, state);
        }
    }

    private void MirrorToEcs(Entity entity, EntityBehavioralState state)
    {
        ref readonly var existing = ref _repo!.GetComponentRO<FakeAnimBackendState>(entity);
        var newState = new FakeAnimBackendState
        {
            Generation = existing.Generation,
            TotalTicks = existing.TotalTicks + 1,
            Aim = state.Aim,
            Stance = state.Stance,
            HorizontalSpeed = MathF.Sqrt(
                state.HorizontalVelX * state.HorizontalVelX +
                state.HorizontalVelZ * state.HorizontalVelZ),
            LocalHorizontalVelocity = new System.Numerics.Vector2(state.HorizontalVelX, state.HorizontalVelZ),
            VerticalVelocity = state.VerticalVelocity,
            IsGrounded = (byte)(state.IsGrounded ? 1 : 0),
            DistanceSinceLastFootstep = state.DistanceSinceLastFootstep,
            NextFootIndex = state.NextFootIndex,
            PendingNotifyCount = (byte)Math.Min(state.PendingNotifies.Count, 16),
        };
        for (int i = 0; i < 8; i++)
            newState.Slots[i] = state.Slots[i];
        int notifyCount = Math.Min(state.PendingNotifies.Count, 16);
        for (int i = 0; i < notifyCount; i++)
            newState.PendingNotifies[i] = state.PendingNotifies[i];
        _repo!.SetComponent(entity, newState);
    }

    private void AdvanceSlots(EntityBehavioralState state, float deltaTime)
    {
        if (_classData == null)
            return;

        if (!_classData.TryGetValue(state.CharacterDefHandle, out var bakedData))
            return;

        for (int i = 0; i < 8; i++)
        {
            ref var slot = ref state.Slots[i];
            if (slot.IsActive == 0)
                continue;

            float prevElapsed = slot.ElapsedSeconds;
            slot.ElapsedSeconds += deltaTime * slot.PlayRate;

            // Check notify crossings
            if (bakedData.MontageDict.TryGetValue(slot.ActiveMontage.Hash, out var montageInfo))
            {
                for (int n = 0; n < montageInfo.Notifies.Count; n++)
                {
                    var notify = montageInfo.Notifies[n];
                    ulong bit = 1UL << n;
                    if ((slot.FiredNotifyMask & bit) == 0 &&
                        prevElapsed < notify.TimeSeconds &&
                        slot.ElapsedSeconds >= notify.TimeSeconds)
                    {
                        slot.FiredNotifyMask |= bit;
                        state.PendingNotifies.Add(new RawNotifyEvent
                        {
                            Kind = notify.Kind,
                            MarkerHash = notify.MarkerHash,
                            TimeSeconds = notify.TimeSeconds,
                            PayloadFloat = notify.PayloadFloat,
                            PayloadUint = (uint)slot.ActiveMontage.Hash,
                        });
                    }
                }
            }

            // Check blend-out window
            float remaining = slot.TotalDurationSeconds - slot.ElapsedSeconds;
            if (slot.BlendOutTime > 0f && remaining <= slot.BlendOutTime)
                slot.InBlendOutWindow = 1;

            // Blend weight computation (DD-Fake §4.1)
            if (slot.ElapsedSeconds < slot.BlendInTime)
                slot.BlendWeight = slot.BlendInTime > 0f ? slot.ElapsedSeconds / slot.BlendInTime : 1f;
            else if (slot.ElapsedSeconds > slot.TotalDurationSeconds - slot.BlendOutTime)
            {
                float remain = slot.TotalDurationSeconds - slot.ElapsedSeconds;
                slot.BlendWeight = slot.BlendOutTime > 0f ? MathF.Max(0f, remain / slot.BlendOutTime) : 0f;
                slot.InBlendOutWindow = 1;
            }
            else
                slot.BlendWeight = 1f;

            // Natural completion
            if (slot.ElapsedSeconds >= slot.TotalDurationSeconds)
            {
                slot.IsActive = 0;
                slot.ElapsedSeconds = 0f;
                slot.InBlendOutWindow = 0;
            }
        }
    }

    private static void AdvanceAim(EntityBehavioralState state, float deltaTime)
    {
        if (state.Aim.IsActive == 0)
            return;

        if (state.Aim.IsReleasing != 0)
        {
            float step = state.Aim.BlendOutTime > 0f ? deltaTime / state.Aim.BlendOutTime : 1f;
            state.Aim.BlendWeight = Math.Max(0f, state.Aim.BlendWeight - step);
            if (state.Aim.BlendWeight <= 0f)
                state.Aim.IsActive = 0;
        }
        else
        {
            float step = state.Aim.BlendInTime > 0f ? deltaTime / state.Aim.BlendInTime : 1f;
            state.Aim.BlendWeight = Math.Min(1f, state.Aim.BlendWeight + step);
        }
    }

    private static void AdvanceStance(EntityBehavioralState state, float deltaTime)
    {
        if (state.Stance.IsTransitioning == 0)
            return;

        float progress = state.Stance.TransitionProgress +
            (state.Stance.TransitionTotalSeconds > 0f ? deltaTime / state.Stance.TransitionTotalSeconds : 1f);

        if (progress >= 1f)
        {
            state.Stance.CurrentStance = state.Stance.TargetStance;
            state.Stance.IsTransitioning = 0;
            state.Stance.TransitionProgress = 0f;
        }
        else
        {
            state.Stance.TransitionProgress = progress;
        }
    }

    private void AdvanceFootsteps(EntityBehavioralState state, float deltaTime)
    {
        float speed = MathF.Sqrt(
            state.HorizontalVelX * state.HorizontalVelX +
            state.HorizontalVelZ * state.HorizontalVelZ);

        if (!state.IsGrounded || speed < MinFootstepSpeed)
        {
            // Reset accumulated distance when stationary so first step after
            // moving starts from zero (DD-Fake §5).
            state.DistanceSinceLastFootstep = 0f;
            return;
        }

        state.DistanceSinceLastFootstep += speed * deltaTime;

        // Use `if`, not `while`: at most one footstep per tick (DD-Fake §5).
        if (state.DistanceSinceLastFootstep >= FootstepStrideMeters)
        {
            state.DistanceSinceLastFootstep -= FootstepStrideMeters;

            // Look up a footstep marker hash from baked data if available
            uint markerHash = 0;
            byte footIndex = state.NextFootIndex;
            if (_classData != null &&
                _classData.TryGetValue(state.CharacterDefHandle, out var bakedData))
            {
                foreach (var montage in bakedData.MontageDict.Values)
                {
                    foreach (var notify in montage.Notifies)
                    {
                        if (notify.Kind == AnimNotifyCategory.Footstep)
                        {
                            markerHash = notify.MarkerHash;
                            break;
                        }
                    }
                    if (markerHash != 0)
                        break;
                }
            }

            state.PendingNotifies.Add(new RawNotifyEvent
            {
                Kind = AnimNotifyCategory.Footstep,
                MarkerHash = markerHash,
                TimeSeconds = 0f,
                PayloadFloat = 0f,
                PayloadUint = footIndex,
            });

            state.NextFootIndex = (byte)(1 - state.NextFootIndex);
        }
    }

    /// <summary>
    /// Update locomotion inputs for an entity (non-interface method for behavioral tests).
    /// </summary>
    public void UpdateLocomotionInputs(
        AnimationBackendHandle handle,
        float horizontalVelX,
        float horizontalVelZ,
        float verticalVelocity,
        bool isGrounded)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return;

        state.HorizontalVelX = horizontalVelX;
        state.HorizontalVelZ = horizontalVelZ;
        state.VerticalVelocity = verticalVelocity;
        state.IsGrounded = isGrounded;
    }

    /// <summary>
    /// Query the slot state for a specific entity and slot index (non-interface method for tests).
    /// Returns default FakeSlotState if handle is invalid or slot index is out of range.
    /// </summary>
    public FakeSlotState QuerySlotState(AnimationBackendHandle handle, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 8)
            return default;

        if (!TryResolveToState(handle, out var state) || state == null)
            return default;

        return state.Slots[slotIndex];
    }

    /// <summary>
    /// Returns the backend's current stable stance for an entity.
    /// Reflects the completed stance value after any in-progress transition finishes.
    /// </summary>
    public bool GetCurrentStance(AnimationBackendHandle handle, out byte currentStance)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
        {
            currentStance = 0;
            return false;
        }
        currentStance = state.Stance.CurrentStance;
        return true;
    }

    /// <summary>
    /// Query the aim/look-at layer state for a specific entity (non-interface method for tests).
    /// Returns default FakeAimState if handle is invalid.
    /// </summary>
    public FakeAimState QueryAimState(AnimationBackendHandle handle)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return default;
        return state.Aim;
    }

    /// <summary>
    /// Query the stance transition state for a specific entity (non-interface method for tests).
    /// Returns default FakeStanceState if handle is invalid.
    /// </summary>
    public FakeStanceState QueryStanceState(AnimationBackendHandle handle)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return default;
        return state.Stance;
    }

    /// <summary>
    /// Drain pending notifies for a specific entity (IAnimationBackend implementation).
    /// Returns number of events written. Excess events are silently dropped.
    /// </summary>
    public int DrainNotifies(AnimationBackendHandle handle, Span<RawNotifyEvent> dest)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return 0;

        int count = Math.Min(state.PendingNotifies.Count, dest.Length);
        for (int i = 0; i < count; i++)
            dest[i] = state.PendingNotifies[i];

        state.PendingNotifies.Clear();
        return count;
    }

    /// <summary>
    /// IAnimationBackend.DrainNotifies: drains from ALL entities' pending notify queues.
    /// </summary>
    public int DrainNotifies(Span<RawNotifyEvent> dest)
    {
        int total = 0;
        foreach (var state in _entityStates.Values)
        {
            int count = Math.Min(state.PendingNotifies.Count, dest.Length - total);
            for (int i = 0; i < count; i++)
                dest[total + i] = state.PendingNotifies[i];
            total += count;
            state.PendingNotifies.Clear();
            if (total >= dest.Length)
                break;
        }
        return total;
    }

    public AnimationBackendMetrics SnapshotMetrics()
    {
        int totalSlots = 0;
        int pendingNotifies = 0;
        foreach (var state in _entityStates.Values)
        {
            foreach (var slot in state.Slots)
            {
                if (slot.IsActive != 0)
                    totalSlots++;
            }
            pendingNotifies += state.PendingNotifies.Count;
        }

        return new AnimationBackendMetrics
        {
            ActiveEntityCount = _entityStates.Count,
            TotalActiveSlotsCount = totalSlots,
            PendingNotifyCount = pendingNotifies,
            LastTickMs = 0,
            PeakTickMs = 0,
        };
    }

    /// <summary>
    /// Returns true if any of the 8 playback slots for this entity is currently active (IsActive != 0).
    /// </summary>
    public bool IsAnySlotActive(AnimationBackendHandle handle)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return false;

        foreach (var slot in state.Slots)
        {
            if (slot.IsActive != 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if any slot for this entity has entered its blend-out window (OFX-009).
    /// </summary>
    public bool IsAnySlotInBlendOut(AnimationBackendHandle handle)
    {
        if (!TryResolveToState(handle, out var state) || state == null)
            return false;

        foreach (var slot in state.Slots)
        {
            if (slot.IsActive != 0 && slot.InBlendOutWindow != 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Crossfade-replace: semantically equivalent to PlayMontageOnSlot
    /// but used by the queue advance path for chaining (OFX-009, DD-1 §7).
    /// </summary>
    public void CrossfadeMontageOnSlot(AnimationBackendHandle handle, in PlayMontageParams @params)
        => PlayMontageOnSlot(handle, in @params);

    /// <summary>
    /// Clears all entity state from both the internal dictionary and, when an
    /// EntityRepository was injected, from the ECS component store (OFX-003).
    /// </summary>
    public void ResetWorld()
    {
        if (_repo != null)
        {
            foreach (var (_, entity) in _entityIndexToEntity)
            {
                if (_repo.HasComponent<FakeAnimBackendState>(entity))
                    _repo.RemoveComponent<FakeAnimBackendState>(entity);
            }
            _entityIndexToEntity.Clear();
        }

        _handleSlots.Clear();
        _entityStates.Clear();
        _nextHandleIndex = 1;
        _nextGeneration = 1;
    }
}
