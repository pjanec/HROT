using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Executors;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.MuscleCharacter.Animation.Executors
{
    /// <summary>
    /// Executor for AnimationActionIds.PlayMontage (ANC-P3-01, DD-1 §6).
    /// Validates montage exists in baked data, then stages play intent in AnimationExecutorState.
    /// Montage is applied to backend by AnimationRuntimeBridgeSystem.
    /// </summary>
    public sealed class PlayMontageExecutor : IActionExecutor<AnimationChannel>
    {
        private readonly IAnimationBackend _backend;
        private readonly BakedAnimationCache _cache;

        public PlayMontageExecutor(IAnimationBackend backend, BakedAnimationCache cache)
        {
            _backend = backend;
            _cache = cache;
        }

        public unsafe void OnEnter(Entity entity, ref AnimationChannel channel, EntityRepository world)
        {
            PlayMontageParams p;
            fixed (byte* src = channel.Params)
                p = *(PlayMontageParams*)src;

            // DEBT D-21: A zero MontageId at this point is almost always a bug
            // (uninitialised Params blob, forgotten WriteParams, or test fixture that
            // bumps ActionInstanceId without filling the params struct). Without this
            // guard, the executor would silently stage a play of "montage 0" and
            // downstream tests would pass vacuously while no real montage played.
            // Fail the channel to surface the bug.
            System.Diagnostics.Debug.Assert(
                p.MontageId != 0,
                $"PlayMontageExecutor.OnEnter: entity {entity.Index} has MontageId=0. " +
                "Likely cause: Params blob not written, or written without WriteParams<PlayMontageParams>(...).");
            if (p.MontageId == 0)
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // Validate montage ID against baked data (uses BackendHandle as classId)
            if (world.HasComponent<CharacterAnimationDefRuntime>(entity))
            {
                var def = world.GetComponent<CharacterAnimationDefRuntime>(entity);
                if (_cache.TryGetCached(def.BackendHandle, out var bakedData) && bakedData != null)
                {
                    if (!bakedData.MontageDict.ContainsKey(p.MontageId))
                    {
                        channel.Status = NodeStatus.Failure;
                        return;
                    }
                }
                // If baked data not yet in cache (first tick before bridge), trust the command
            }

            // Stage the play params in AnimationExecutorState for bridge to apply
            if (world.HasComponent<AnimationExecutorState>(entity))
            {
                ref var execState = ref world.GetComponentRW<AnimationExecutorState>(entity);
                StagePlayParams(ref execState, in p);
                execState.LastActiveMontageId = p.MontageId;
            }

            channel.Status = NodeStatus.Running;
        }

        public void Execute(Entity entity, ref AnimationChannel channel, EntityRepository world, float dt)
        {
            // Nothing to drive per-tick; bridge system handles backend state
        }

        public unsafe void OnExit(Entity entity, ref AnimationChannel channel, EntityRepository world)
        {
            // Stage a stop command in the executor state so bridge can clean up
            if (world.HasComponent<AnimationExecutorState>(entity))
            {
                ref var execState = ref world.GetComponentRW<AnimationExecutorState>(entity);
                ClearStagedPlay(ref execState);
            }
        }

        private static unsafe void StagePlayParams(ref AnimationExecutorState state, in PlayMontageParams p)
        {
            // Store the PlayMontageParams in the first slot of SlotsData (reused as staging area)
            // The bridge system reads this to call backend.PlayMontageOnSlot
            fixed (byte* dst = state.SlotsData)
            {
                // Mark slot 0 as "pending play" (MontageId != 0) with play params
                var staging = (StagedPlayIntent*)dst;
                staging->MontageId = p.MontageId;
                staging->PlayRate = p.PlayRate != 0f ? p.PlayRate : 1f;
                staging->BlendInTime = p.BlendInTime;
                staging->BlendOutTime = p.BlendOutTime;
                staging->StartSectionIndex = p.StartSectionIndex;
                staging->HasPendingPlay = 1;
            }
        }

        private static unsafe void ClearStagedPlay(ref AnimationExecutorState state)
        {
            fixed (byte* dst = state.SlotsData)
            {
                var staging = (StagedPlayIntent*)dst;
                staging->HasPendingPlay = 0;
                staging->MontageId = 0;
            }
        }
    }

    /// <summary>
    /// Executor for AnimationActionIds.StopMontage (ANC-P3-01, DD-1 §6).
    /// Stages a stop intent for bridge to apply.
    /// </summary>
    public sealed class StopMontageExecutor : IActionExecutor<AnimationChannel>
    {
        private readonly IAnimationBackend _backend;

        public StopMontageExecutor(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public unsafe void OnEnter(Entity entity, ref AnimationChannel channel, EntityRepository world)
        {
            StopMontageParams p;
            fixed (byte* src = channel.Params)
                p = *(StopMontageParams*)src;

            int interruptedMontageId = 0;

            // Stage stop intent and capture last active montage ID for interrupt event
            if (world.HasComponent<AnimationExecutorState>(entity))
            {
                ref var execState = ref world.GetComponentRW<AnimationExecutorState>(entity);
                interruptedMontageId = execState.LastActiveMontageId;
                StageStop(ref execState, in p);
            }

            // Publish interrupt event before setting Success so listeners see it while channel is still active
            if (interruptedMontageId != 0)
            {
                world.Bus.Publish(new MontageEndedEvent(
                    target: entity,
                    montageId: interruptedMontageId,
                    actionInstanceId: channel.ActionInstanceId,
                    queueIndex: 0xFF,
                    endReason: MontageEndReason.Interrupted));
            }

            channel.Status = NodeStatus.Success;
        }

        public void Execute(Entity entity, ref AnimationChannel channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref AnimationChannel channel, EntityRepository world) { }

        private static unsafe void StageStop(ref AnimationExecutorState state, in StopMontageParams p)
        {
            fixed (byte* dst = state.SlotsData)
            {
                var staging = (StagedPlayIntent*)dst;
                staging->HasPendingStop = 1;
                staging->StopBlendOutTime = p.BlendOutTime;
            }
        }
    }

    /// <summary>
    /// Executor for AnimationActionIds.PlayMontageQueue (ANC-P3-01, DD-1 §6.3, §7).
    /// Validates the queued montage chain already written to AnimationMontageQueue, then
    /// resets AnimationMontageQueueState to start playback from index 0.
    /// Actual backend play is deferred to AnimationRuntimeBridgeSystem.
    /// </summary>
    public sealed class PlayMontageQueueExecutor : IActionExecutor<AnimationChannel>
    {
        private readonly IAnimationBackend _backend;
        private readonly BakedAnimationCache _cache;

        public PlayMontageQueueExecutor(IAnimationBackend backend, BakedAnimationCache cache)
        {
            _backend = backend;
            _cache = cache;
        }

        public void OnEnter(Entity entity, ref AnimationChannel channel, EntityRepository world)
        {
            // Queue entries must have been written to AnimationMontageQueue by the Brain before issuing this command
            if (!world.HasComponent<AnimationMontageQueue>(entity))
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            ref var queue = ref world.GetComponentRW<AnimationMontageQueue>(entity);

            // Empty queue is a malformed command (DD-1 §6.3 step 3)
            if (queue.Count == 0)
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // ANIM012: chain length must not exceed 8
            if (queue.Count > 8)
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // Validate all montage IDs against baked data
            if (world.HasComponent<CharacterAnimationDefRuntime>(entity))
            {
                var def = world.GetComponent<CharacterAnimationDefRuntime>(entity);
                if (_cache.TryGetCached(def.BackendHandle, out var bakedData) && bakedData != null)
                {
                    unsafe
                    {
                        fixed (AnimationMontageQueue* queuePtr = &queue)
                        {
                            var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                                new Span<byte>(queuePtr->EntriesData, 128));

                            for (int i = 0; i < queue.Count; i++)
                            {
                                if (!bakedData.MontageDict.ContainsKey(entries[i].MontageId))
                                {
                                    channel.Status = NodeStatus.Failure;
                                    return;
                                }
                            }
                        }
                    }
                }
                // If baked data not yet in cache (first tick before bridge), trust the command
            }

            // Reset queue state to start from entry 0 (DD-1 §6.3 step 6)
            if (world.HasComponent<AnimationMontageQueueState>(entity))
            {
                ref var queueState = ref world.GetComponentRW<AnimationMontageQueueState>(entity);
                queueState.CurrentEntryIndex = 0;
                queueState.EntryElapsedSeconds = 0f;
                queueState.InBlendOutWindow = false;
                queueState.TrackingActive = 0;
                queueState.ObservedQueueVersion = queue.QueueVersion;
            }

            // Stage first entry's play so the bridge applies it this frame
            if (world.HasComponent<AnimationExecutorState>(entity))
            {
                ref var execState = ref world.GetComponentRW<AnimationExecutorState>(entity);
                unsafe
                {
                    fixed (AnimationMontageQueue* queuePtr = &queue)
                    {
                        var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                            new System.Span<byte>(queuePtr->EntriesData, 128));
                        var entry = entries[0];
                        StageFirstQueueEntry(ref execState, in entry);
                        execState.LastActiveMontageId = entry.MontageId;
                    }
                }

                // Mark tracking as active so MontageQueueAdvanceSystem knows the queue has started
                if (world.HasComponent<AnimationMontageQueueState>(entity))
                {
                    ref var queueState = ref world.GetComponentRW<AnimationMontageQueueState>(entity);
                    queueState.TrackingActive = 1;
                }
            }

            channel.Status = NodeStatus.Running;
        }

        public void Execute(Entity entity, ref AnimationChannel channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref AnimationChannel channel, EntityRepository world) { }

        private static unsafe void StageFirstQueueEntry(ref AnimationExecutorState state, in MontageQueueEntry entry)
        {
            fixed (byte* dst = state.SlotsData)
            {
                var staging = (StagedPlayIntent*)dst;
                staging->MontageId = entry.MontageId;
                staging->PlayRate = entry.PlayRate != 0f ? entry.PlayRate : 1f;
                staging->BlendInTime = entry.BlendIntoTime;
                staging->BlendOutTime = 0f;
                staging->StartSectionIndex = entry.StartSectionIndex;
                staging->HasPendingPlay = 1;
            }
        }
    }

    /// <summary>
    /// Executor for AnimationActionIds.EnqueueMontage (ANC-P3-01, DD-1 §6).
    /// Appends a single montage entry to the currently-running AnimationMontageQueue.
    /// Silent no-op if queue is at capacity (Count == 8); Status=Running in that case.
    /// </summary>
    public sealed class EnqueueExecutor : IActionExecutor<AnimationChannel>
    {
        private readonly BakedAnimationCache _cache;

        public EnqueueExecutor(BakedAnimationCache cache)
        {
            _cache = cache;
        }

        public unsafe void OnEnter(Entity entity, ref AnimationChannel channel, EntityRepository world)
        {
            EnqueueParams p;
            fixed (byte* src = channel.Params)
                p = *(EnqueueParams*)src;

            // Validate montage ID against baked data
            if (world.HasComponent<CharacterAnimationDefRuntime>(entity))
            {
                var def = world.GetComponent<CharacterAnimationDefRuntime>(entity);
                if (_cache.TryGetCached(def.BackendHandle, out var bakedData) && bakedData != null)
                {
                    if (!bakedData.MontageDict.ContainsKey(p.MontageId))
                    {
                        channel.Status = NodeStatus.Failure;
                        return;
                    }
                }
            }

            if (!world.HasComponent<AnimationMontageQueue>(entity))
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            ref var queue = ref world.GetComponentRW<AnimationMontageQueue>(entity);

            // At capacity: silent no-op per spec, Status=Running (command accepted, not acted upon)
            if (queue.Count >= 8)
            {
                channel.Status = NodeStatus.Running;
                return;
            }

            // Append entry via Span-cast mutation (fixed required for fixed-buffer in ref struct)
            unsafe
            {
                fixed (AnimationMontageQueue* queuePtr = &queue)
                {
                    var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                        new Span<byte>(queuePtr->EntriesData, 128));

                    entries[queue.Count] = new MontageQueueEntry
                    {
                        MontageId = p.MontageId,
                        BlendIntoTime = p.BlendIntoTime,
                        PlayRate = p.PlayRate != 0f ? p.PlayRate : 1f,
                        StartSectionIndex = p.StartSectionIndex,
                        Flags = p.Flags,
                    };
                }
            }
            queue.Count++;

            // Bump QueueVersion to signal bridge/advance system of the mutation
            queue.QueueVersion++;

            channel.Status = NodeStatus.Success;
        }

        public void Execute(Entity entity, ref AnimationChannel channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref AnimationChannel channel, EntityRepository world) { }
    }

    /// <summary>
    /// Executor for AnimationActionIds.ClearMontageQueue (ANC-P3-01).
    /// Truncates the AnimationMontageQueue to zero entries and bumps QueueVersion.
    /// Note: Brain-side direct mutation is the preferred approach per DD-1 §6.4.
    /// This executor provides a channel-command equivalent for Muscle-side truncation.
    /// </summary>
    public sealed class ClearQueueExecutor : IActionExecutor<AnimationChannel>
    {
        public void OnEnter(Entity entity, ref AnimationChannel channel, EntityRepository world)
        {
            if (!world.HasComponent<AnimationMontageQueue>(entity))
            {
                channel.Status = NodeStatus.Success;
                return;
            }

            ref var queue = ref world.GetComponentRW<AnimationMontageQueue>(entity);
            queue.Count = 0;
            queue.QueueVersion++;

            // Reset queue state to indicate no active entry
            if (world.HasComponent<AnimationMontageQueueState>(entity))
            {
                ref var queueState = ref world.GetComponentRW<AnimationMontageQueueState>(entity);
                queueState.CurrentEntryIndex = 0xFF;
                queueState.ObservedQueueVersion = queue.QueueVersion;
            }

            channel.Status = NodeStatus.Success;
        }

        public void Execute(Entity entity, ref AnimationChannel channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref AnimationChannel channel, EntityRepository world) { }
    }

    /// <summary>
    /// Staging layout at the start of AnimationExecutorState.SlotsData.
    /// Used to communicate from dispatcher executor to bridge system.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct StagedPlayIntent
    {
        public int MontageId;
        public float PlayRate;
        public float BlendInTime;
        public float BlendOutTime;
        public byte StartSectionIndex;
        public byte HasPendingPlay;   // 1 = bridge should call PlayMontageOnSlot
        public byte HasPendingStop;   // 1 = bridge should call StopMontageOnSlot
        public byte _pad;
        public float StopBlendOutTime;
    }
}
