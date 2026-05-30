using System;
using Fdp.Core;
using Fbt;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Handles mid-action capability loss for animation-specific capabilities
    /// (CanPlayAnimations, CanAim, CanChangeStance), detecting high→low
    /// transitions and taking corrective action to prevent orphaned play state.
    /// (ANC-P3-09, DD-1 §13, §20.6)
    ///
    /// <para>
    /// Runs in <see cref="SystemPhase.Input"/> per the v241 architect ruling.
    /// <b>Must</b> run before <see cref="CognitiveInterruptSystem"/>, which is
    /// the single canonical writer of <see cref="PreviousCapabilities"/> — this
    /// system reads the shadow component to detect transitions but
    /// <b>must not</b> mutate it. The <see cref="UpdateBeforeAttribute"/>
    /// dependency below enforces the ordering inside the Input phase and
    /// prevents the data race the architect flagged in v240.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [UpdateBefore(typeof(CognitiveInterruptSystem))]
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
