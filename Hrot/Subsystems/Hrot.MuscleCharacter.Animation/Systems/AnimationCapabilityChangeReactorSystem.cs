using System;
using Fdp.Core;
using Fbt;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Handles mid-action capability loss for animation capabilities.
    /// Detects high→low transitions in CanPlayAnimations, CanAim, CanChangeStance
    /// and takes corrective action to prevent orphaned play state.
    /// Runs early in Simulation, before dispatchers, so they see consistent state.
    /// (ANC-P3-09, DD-1 §13, §20.6)
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class AnimationCapabilityChangeReactorSystem : IEcsModuleSystem
    {
        private readonly IAnimationBackend _backend;

        public AnimationCapabilityChangeReactorSystem(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(AnimationCapabilityChangeReactorSystem)} requires direct EntityRepository access.");

            var q = repo.Query()
                .With<ActorCapabilityState>()
                .With<PreviousCapabilities>()
                .Build();

            foreach (var entity in q)
            {
                var current = repo.GetComponent<ActorCapabilityState>(entity);
                var previous = repo.GetComponent<PreviousCapabilities>(entity);

                // Detect high→low transitions
                var lostCapabilities = previous.Capabilities & ~current.Capabilities;

                if (lostCapabilities == 0)
                    continue; // No capability loss on this entity

                // Handle CanPlayAnimations loss
                if ((lostCapabilities & ActorCapabilities.CanPlayAnimations) != 0)
                {
                    HandlePlayAnimationsLoss(repo, entity);
                }

                // Handle CanAim loss
                if ((lostCapabilities & ActorCapabilities.CanAim) != 0)
                {
                    HandleAimLoss(repo, entity);
                }

                // Handle CanChangeStance loss
                // (Deferred to Phase 4 — for now, in-flight transitions are allowed to complete)
            }
        }

        private void HandlePlayAnimationsLoss(EntityRepository repo, Entity entity)
        {
            if (!repo.HasComponent<AnimationChannel>(entity))
                return;

            ref var channel = ref repo.GetComponentRW<AnimationChannel>(entity);

            // Force-stop all active slots with short blend-out (0.1s configurable)
            if (repo.HasComponent<CharacterAnimationDefRuntime>(entity) &&
                repo.HasComponent<AnimationExecutorState>(entity))
            {
                var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
                ref var execState = ref repo.GetComponentRW<AnimationExecutorState>(entity);

                // Stage forced stops for all active slots
                StageForceStop(ref execState, blendOutTime: 0.1f);
            }

            // Set channel status to Failure
            channel.Status = NodeStatus.Failure;

            // Bump DispatchedInstanceId so next command isn't ignored as duplicate
            channel.DispatchedInstanceId++;

            // Clear queue if present
            if (repo.HasComponent<AnimationMontageQueueState>(entity))
            {
                ref var queueState = ref repo.GetComponentRW<AnimationMontageQueueState>(entity);
                ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity);

                queueState.CurrentEntryIndex = 0xFF;
                queue.Count = 0;
            }
        }

        private void HandleAimLoss(EntityRepository repo, Entity entity)
        {
            if (!repo.HasComponent<LookAtChannel>(entity))
                return;

            ref var channel = ref repo.GetComponentRW<LookAtChannel>(entity);

            // Stage ReleaseAim if executor present
            if (repo.HasComponent<LookAtExecutorState>(entity))
            {
                ref var exec = ref repo.GetComponentRW<LookAtExecutorState>(entity);
                exec.TargetType = 0; // 0 = none, release immediately
                exec.BlendOutWeight = 1.0f; // Start blending out
            }

            // Set channel status to Failure
            channel.Status = NodeStatus.Failure;

            // Bump DispatchedInstanceId
            channel.DispatchedInstanceId++;
        }

        private static unsafe void StageForceStop(ref AnimationExecutorState state, float blendOutTime)
        {
            // Write to staging buffer at the start of SlotsData
            fixed (byte* dst = state.SlotsData)
            {
                var staging = (StagedPlayIntent*)dst;
                staging->HasPendingStop = 1;
                staging->StopBlendOutTime = blendOutTime;
            }
        }

        /// <summary>
        /// Staging layout for capability loss forced stops.
        /// </summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct StagedPlayIntent
        {
            public int MontageId;
            public float PlayRate;
            public float BlendInTime;
            public float BlendOutTime;
            public byte StartSectionIndex;
            public byte HasPendingPlay;
            public byte HasPendingStop;
            public byte _pad;
            public float StopBlendOutTime;
        }
    }
}
