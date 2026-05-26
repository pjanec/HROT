using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fbt;
using Fdp.ModuleHost.Abstractions;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Events;
using Hrot.MuscleCharacter.Animation.Executors;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Advances the montage queue by detecting when the active slot has finished and staging the
    /// next entry (or marking the queue complete). Runs in Simulation before the bridge system
    /// so that a newly-staged entry is applied in the same frame it is detected. (ANC-P7-06)
    ///
    /// Two advancement cases are handled:
    ///   Case A: PlayMontageQueue is active and tracking is on — detect natural slot completion
    ///           and start next entry or mark queue done.
    ///   Case B: PlayMontage was active with a non-empty queue (IssueEnqueueMontage was called) —
    ///           when the PlayMontage slot finishes, begin the first queued entry.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class MontageQueueAdvanceSystem : IEcsModuleSystem
    {
        private readonly IAnimationBackend _backend;
        private readonly BakedAnimationCache _cache;

        public MontageQueueAdvanceSystem(IAnimationBackend backend, BakedAnimationCache cache)
        {
            _backend = backend;
            _cache = cache;
        }

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(MontageQueueAdvanceSystem)} requires direct EntityRepository access.");

            var q = repo.Query()
                .With<AnimationMontageQueue>()
                .With<AnimationMontageQueueState>()
                .With<CharacterAnimationDefRuntime>()
                .With<AnimationChannel>()
                .With<AnimationExecutorState>()
                .Build();

            foreach (var entity in q)
            {
                ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
                ref var queueState = ref repo.GetComponentRW<AnimationMontageQueueState>(entity);
                ref var execState = ref repo.GetComponentRW<AnimationExecutorState>(entity);
                ref var channel = ref repo.GetComponentRW<AnimationChannel>(entity);
                var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);

                // Skip if not yet registered with the backend
                if ((def.BackendHandle >> 32) == 0)
                    continue;

                if (queue.Count == 0)
                    continue;

                var handle = new AnimationBackendHandle
                {
                    Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                    Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
                };

                // Check if bridge has a pending play that hasn't been applied yet.
                // If so, the slot hasn't started — skip advancement this frame.
                fixed (byte* slotsPtr = execState.SlotsData)
                {
                    var staged = (StagedPlayIntent*)slotsPtr;
                    if (staged->HasPendingPlay != 0)
                        continue;
                }

                bool slotInactive = !_backend.IsAnySlotActive(handle);
                bool trackingActive = queueState.TrackingActive != 0;

                if (trackingActive && slotInactive)
                {
                    // Case A: Queue was running and the active entry just finished naturally.
                    int completedIndex = queueState.CurrentEntryIndex;
                    int completedMontageId = execState.LastActiveMontageId;

                    fixed (AnimationMontageQueue* queuePtr = &queue)
                    {
                        var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                            new Span<byte>(queuePtr->EntriesData, 128));

                        // Publish completion event for the entry that just finished
                        repo.Bus.Publish(new MontageEndedEvent(
                            target: entity,
                            montageId: completedMontageId,
                            actionInstanceId: channel.ActionInstanceId,
                            queueIndex: (byte)completedIndex,
                            endReason: MontageEndReason.NaturalEnd));

                        int nextIndex = completedIndex + 1;
                        if (nextIndex < queue.Count)
                        {
                            // Stage next queue entry for bridge to apply this frame
                            var nextEntry = entries[nextIndex];
                            StageQueueEntry(ref execState, in nextEntry);
                            queueState.CurrentEntryIndex = (byte)nextIndex;
                            queueState.EntryElapsedSeconds = 0f;
                            execState.LastActiveMontageId = nextEntry.MontageId;
                        }
                        else
                        {
                            // All entries done — mark queue idle and complete the channel action
                            queueState.CurrentEntryIndex = 0xFF;
                            queueState.TrackingActive = 0;
                            queueState.EntryElapsedSeconds = 0f;
                            channel.Status = NodeStatus.Success;
                        }
                    }
                }
                else if (!trackingActive && slotInactive &&
                         channel.ActiveAction == AnimationActionIds.PlayMontage &&
                         channel.Status == NodeStatus.Running)
                {
                    // Case B: A standalone PlayMontage just finished but the queue has pending entries
                    // (i.e., IssueEnqueueMontage was called while PlayMontage was active).
                    // Begin the first queued entry now.
                    int finishedMontageId = execState.LastActiveMontageId;

                    fixed (AnimationMontageQueue* queuePtr = &queue)
                    {
                        var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                            new Span<byte>(queuePtr->EntriesData, 128));

                        // Publish completion for the PlayMontage that just ended
                        repo.Bus.Publish(new MontageEndedEvent(
                            target: entity,
                            montageId: finishedMontageId,
                            actionInstanceId: channel.ActionInstanceId,
                            queueIndex: 0xFF,
                            endReason: MontageEndReason.NaturalEnd));

                        // Stage the first queued entry
                        var firstEntry = entries[0];
                        StageQueueEntry(ref execState, in firstEntry);
                        queueState.CurrentEntryIndex = 0;
                        queueState.EntryElapsedSeconds = 0f;
                        queueState.TrackingActive = 1;
                        execState.LastActiveMontageId = firstEntry.MontageId;
                    }
                }
            }
        }

        private static unsafe void StageQueueEntry(ref AnimationExecutorState state, in MontageQueueEntry entry)
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
                staging->HasPendingStop = 0;
            }
        }
    }
}
