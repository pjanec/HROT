using System;
using Fdp.Core;
using Fbt;
using Fdp.ModuleHost.Abstractions;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Events;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Monitors animation executor state for natural completions and synthesizes events.
    /// Emits MontageStarted/Ended/SectionAdvanced and StanceChanged events.
    /// Writes Status=Success on queue or montage completion.
    /// Runs late in PostSimulation, after backend tick and notify drain.
    /// (ANC-P3-07, DD-1 §18, §17)
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class AnimationStateReporterSystem : IEcsModuleSystem
    {
        private readonly IAnimationBackend _backend;

        public AnimationStateReporterSystem(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(AnimationStateReporterSystem)} requires direct EntityRepository access.");

            var q = repo.Query()
                .With<CharacterAnimationDefRuntime>()
                .With<AnimationChannel>()
                .Build();

            foreach (var entity in q)
            {
                var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
                ref var channel = ref repo.GetComponentRW<AnimationChannel>(entity);

                // Safety-net: if queue has run to completion (advance system sets TrackingActive=0 and
                // CurrentEntryIndex=0xFF), ensure channel is Success even if advance system missed a frame.
                // This check does not require a valid backend handle.
                if (channel.Status == NodeStatus.Running && repo.HasComponent<AnimationMontageQueueState>(entity))
                {
                    ref var queueState = ref repo.GetComponentRW<AnimationMontageQueueState>(entity);
                    ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity);

                    if (queueState.CurrentEntryIndex == 0xFF && queue.Count == 0)
                    {
                        channel.Status = NodeStatus.Success;
                    }
                }

                // Check LookAtChannel completion — does not require a valid backend handle.
                if (repo.HasComponent<LookAtChannel>(entity) && repo.HasComponent<LookAtExecutorState>(entity))
                {
                    ref var lookAtChannel = ref repo.GetComponentRW<LookAtChannel>(entity);
                    ref var lookAtExec = ref repo.GetComponentRW<LookAtExecutorState>(entity);

                    if (lookAtExec.BlendOutWeight <= 0f && lookAtExec.TargetType != 0)
                    {
                        // Aim has fully blended out after release
                        lookAtChannel.Status = NodeStatus.Success;
                        lookAtExec.TargetType = 0;
                    }
                }

                // Skip entities whose BackendHandle is still a raw classId (not yet registered).
                // After AnimationRuntimeBridgeSystem registers the entity, the high 32 bits are >= 1.
                if ((def.BackendHandle >> 32) == 0)
                    continue;

                // Decode the backend handle
                var handle = new AnimationBackendHandle
                {
                    Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                    Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
                };

                // Detect natural single-montage completion.
                // When the backend slot becomes inactive (slot elapsed >= duration), no slot is active.
                // Guard: action was dispatched (DispatchedInstanceId == ActionInstanceId != 0),
                //        action is PlayMontage, channel is still Running, no slot is active,
                //        and the queue is empty (if enqueued entries exist, MontageQueueAdvanceSystem handles them).
                bool noQueueEntries = !repo.HasComponent<AnimationMontageQueue>(entity) ||
                                      repo.GetComponent<AnimationMontageQueue>(entity).Count == 0;
                if (channel.Status == NodeStatus.Running &&
                    channel.ActiveAction == AnimationActionIds.PlayMontage &&
                    channel.ActionInstanceId != 0 &&
                    channel.DispatchedInstanceId == channel.ActionInstanceId &&
                    !_backend.IsAnySlotActive(handle) &&
                    noQueueEntries)
                {
                    unsafe
                    {
                        PlayMontageParams p;
                        fixed (byte* src = channel.Params)
                            p = *(PlayMontageParams*)src;

                        channel.Status = NodeStatus.Success;
                        repo.Bus.Publish(new MontageEndedEvent(
                            target: entity,
                            montageId: p.MontageId,
                            actionInstanceId: channel.ActionInstanceId,
                            queueIndex: 0xFF, // 0xFF = single-shot PlayMontage (not from a queue)
                            endReason: MontageEndReason.NaturalEnd));
                    }
                }

                // Detect stance transition completion.
                // StanceTransitionSystem starts transitions; we detect completion here after the backend tick.
                if (repo.HasComponent<StanceStatus>(entity) && repo.HasComponent<StanceIntent>(entity))
                {
                    ref var stanceStatus = ref repo.GetComponentRW<StanceStatus>(entity);
                    var stanceIntent = repo.GetComponent<StanceIntent>(entity);

                    if (stanceStatus.Phase == StanceTransitionPhase.Transitioning)
                    {
                        if (_backend.GetCurrentStance(handle, out byte currentStance) &&
                            currentStance == (byte)stanceIntent.TargetStance)
                        {
                            StanceId oldStance = stanceStatus.CurrentStance;
                            stanceStatus.CurrentStance = (StanceId)currentStance;
                            stanceStatus.Phase = StanceTransitionPhase.Idle;
                            repo.Bus.Publish(new StanceChangedEvent(entity, oldStance, (StanceId)currentStance));
                        }
                    }
                }

            }
        }
    }
}
