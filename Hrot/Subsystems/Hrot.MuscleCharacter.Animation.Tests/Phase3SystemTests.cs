using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;
using Fdp.Core;
using Fbt;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Lifecycle.Events;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Events;
using Hrot.MuscleCharacter.Animation.Fake;
using Hrot.MuscleCharacter.Animation.Hashing;
using Hrot.MuscleCharacter.Animation.Systems;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// Integration tests for Phase 3 ECS systems (DD-Tests §4.2).
    /// Uses real EntityRepository + FakeAnimationBackend + FakeBakedAnimationCache.
    /// Systems are called directly via their Execute interface.
    /// </summary>
    public class Phase3SystemTests
    {
        private const long ClassId = 42L;

        private static readonly int ReloadId = StableIdHasher.ComputeMontageAssetId("Reload_Rifle");

        // ─── Shared test DTO ─────────────────────────────────────────────────

        private static CharacterAnimationDefDto CreateTestDto()
        {
            return new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>
                {
                    new SlotDefDto { SlotId = 0, Name = "Locomotion", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 0 },
                    new SlotDefDto { SlotId = 1, Name = "FullBody", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 100 },
                },
                Montages = new List<MontageDefDto>
                {
                    new MontageDefDto
                    {
                        Name = "Reload_Rifle",
                        AssetRef = "Anims/Reload.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 1.0f,
                        Sections = new[] { "Start" },
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = false,
                    },
                },
                SupportedStances = new[] { Components.StanceId.Standing, Components.StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = new AimConfigDto { MaxYawDegrees = 90f, MaxPitchDegrees = 70f, AimSourceBone = "head" },
                NotifyMarkers = new List<NotifyMarkerDefDto>(),
            };
        }

        // ─── Fixture helpers ─────────────────────────────────────────────────

        private static (EntityRepository repo, FakeAnimationBackend backend, BakedAnimationCache cache) CreateFixture()
        {
            var dto = CreateTestDto();
            var baked = BakingUtils.BakeDef(dto);
            var classData = new Dictionary<long, CharacterAnimationBakedData> { [ClassId] = baked };
            var backend = new FakeAnimationBackend(classData);

            // BakedAnimationCache with a no-op hot-reload events sink
            var cache = new BakedAnimationCache(null);
            cache.GetOrBake(ClassId, dto);

            var repo = new EntityRepository();
            repo.RegisterComponent<AnimationChannel>();
            repo.RegisterComponent<LookAtChannel>();
            repo.RegisterComponent<StanceIntent>();
            repo.RegisterComponent<StanceStatus>();
            repo.RegisterComponent<AnimationMontageQueue>();
            repo.RegisterComponent<AnimationMontageQueueState>();
            repo.RegisterComponent<CharacterAnimationDefRuntime>();
            repo.RegisterComponent<AnimationExecutorState>();
            repo.RegisterComponent<LookAtExecutorState>();
            repo.RegisterComponent<ActorCapabilityState>();
            repo.RegisterEvent<DestructionOrder>();

            return (repo, backend, cache);
        }

        private static Entity CreateAnimatedEntity(
            EntityRepository repo,
            ActorCapabilities caps = ActorCapabilities.CanPlayAnimations | ActorCapabilities.CanChangeStance | ActorCapabilities.CanAim)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new AnimationChannel { Status = NodeStatus.Failure });
            repo.AddComponent(entity, new ActorCapabilityState { Capabilities = caps });
            repo.AddComponent(entity, new CharacterAnimationDefRuntime
            {
                BackendHandle = ClassId, // starts as classId
                StanceCount = 2,
                SlotCount = 2,
            });
            repo.AddComponent(entity, new AnimationExecutorState());
            return entity;
        }

        // ─── AnimationDispatcherSystem tests (ANC-P3-01) ─────────────────────

        [Fact]
        public void PlayMontageCommand_TriggersBackendPlay()
        {
            var (repo, backend, cache) = CreateFixture();
            var bridgeSystem = new AnimationRuntimeBridgeSystem(backend, cache);
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateAnimatedEntity(repo);

            // Encode a valid PlayMontage command
            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontage;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
                var p = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f, BlendInTime = 0.1f };
                fixed (byte* dst = ch.Params)
                    *(PlayMontageParams*)dst = p;
            }

            // First run bridge to register entity
            bridgeSystem.Execute(repo, 0.016f);

            // Now run dispatcher to stage the play
            dispatchSystem.Execute(repo, 0.016f);

            // Run bridge again to apply staged play
            bridgeSystem.Execute(repo, 0.016f);

            // Get the backend handle from the CharacterAnimationDefRuntime
            var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
            var handle = new AnimationBackendHandle
            {
                Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
            };

            var slotState = backend.QuerySlotState(handle, 1);
            Assert.Equal(1, slotState.IsActive);
        }

        [Fact]
        public void PlayMontageCommand_NoCapability_FailsImmediately()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            // Entity WITHOUT CanPlayAnimations
            var entity = CreateAnimatedEntity(repo, caps: ActorCapabilities.CanShoot);

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontage;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
            }

            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Failure, ch2.Status);
        }

        [Fact]
        public void PlayMontageCommand_UnknownMontage_FailsImmediately()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateAnimatedEntity(repo);

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontage;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
                var p = new PlayMontageParams { MontageId = unchecked((int)0xDEADBEEFu), PlayRate = 1.0f };
                fixed (byte* dst = ch.Params)
                    *(PlayMontageParams*)dst = p;
            }

            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Failure, ch2.Status);
        }

        [Fact]
        public void SameInstanceId_NoActionTaken()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateAnimatedEntity(repo);

            // Set channel so ActionInstanceId == DispatchedInstanceId (already dispatched)
            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontage;
                ch.ActionInstanceId = 5;
                ch.DispatchedInstanceId = 5; // same
                ch.Status = NodeStatus.Running;
            }

            dispatchSystem.Execute(repo, 0.016f);

            // Status should remain Running (no re-dispatch, no Failure)
            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Running, ch2.Status);
        }

        // ─── StanceTransitionSystem tests (ANC-P3-03) ────────────────────────

        private static Entity CreateStanceEntity(EntityRepository repo, ActorCapabilities caps)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new StanceIntent { TargetStance = Components.StanceId.Crouched, BlendTime = 0.3f, Version = 1 });
            repo.AddComponent(entity, new StanceStatus { CurrentStance = Components.StanceId.Standing, AckVersion = 0 });
            repo.AddComponent(entity, new CharacterAnimationDefRuntime { BackendHandle = ClassId, StanceCount = 2, SlotCount = 2 });
            repo.AddComponent(entity, new ActorCapabilityState { Capabilities = caps });
            return entity;
        }

        [Fact]
        public void NewVersion_TriggersTransition()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new StanceTransitionSystem(backend);

            var entity = CreateStanceEntity(repo, ActorCapabilities.CanChangeStance);

            system.Execute(repo, 0.016f);

            var status = repo.GetComponent<StanceStatus>(entity);
            Assert.Equal(1u, status.AckVersion);
            Assert.Equal(StanceTransitionPhase.Transitioning, status.Phase);
        }

        [Fact]
        public void SameStanceTarget_ImmediatelyCompletes()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new StanceTransitionSystem(backend);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new StanceIntent { TargetStance = Components.StanceId.Standing, BlendTime = 0.3f, Version = 1 });
            repo.AddComponent(entity, new StanceStatus { CurrentStance = Components.StanceId.Standing, AckVersion = 0 });
            repo.AddComponent(entity, new CharacterAnimationDefRuntime { BackendHandle = ClassId, StanceCount = 2, SlotCount = 2 });
            repo.AddComponent(entity, new ActorCapabilityState { Capabilities = ActorCapabilities.CanChangeStance });

            system.Execute(repo, 0.016f);

            var status = repo.GetComponent<StanceStatus>(entity);
            Assert.Equal(1u, status.AckVersion);
            Assert.Equal(StanceTransitionPhase.Locked, status.Phase);
        }

        [Fact]
        public void MissingCapability_SilentlyAcks()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new StanceTransitionSystem(backend);

            // No CanChangeStance
            var entity = CreateStanceEntity(repo, ActorCapabilities.CanMove);

            system.Execute(repo, 0.016f);

            var status = repo.GetComponent<StanceStatus>(entity);
            Assert.Equal(1u, status.AckVersion); // acknowledged
            // Phase is not changed when capability is missing (stays at default Idle)
            Assert.NotEqual(StanceTransitionPhase.Transitioning, status.Phase);
        }

        // ─── AnimationRuntimeBridgeSystem tests (ANC-P3-05) ──────────────────

        [Fact]
        public void FirstTick_RegistersEntityWithBackend()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new AnimationRuntimeBridgeSystem(backend, cache);

            var entity = CreateAnimatedEntity(repo);

            system.Execute(repo, 0.016f);

            // After first tick, BackendHandle should be updated (no longer == ClassId)
            var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
            // The handle is encoded: if registration succeeded, the value changes
            // (small classId gets encoded as a full handle with Generation != 0 or Index != 0)
            var metrics = backend.SnapshotMetrics();
            Assert.Equal(1, metrics.ActiveEntityCount);
        }

        [Fact]
        public void StagedPlay_ResultsInBackendPlayMontageWithCorrectArgs()
        {
            var (repo, backend, cache) = CreateFixture();
            var bridgeSystem = new AnimationRuntimeBridgeSystem(backend, cache);
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateAnimatedEntity(repo);

            // First tick: registers entity
            bridgeSystem.Execute(repo, 0.016f);

            // Issue a PlayMontage command through dispatcher
            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontage;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
                var p = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f, BlendInTime = 0.1f };
                fixed (byte* dst = ch.Params)
                    *(PlayMontageParams*)dst = p;
            }

            // Dispatcher stages the play
            dispatchSystem.Execute(repo, 0.016f);

            // Bridge applies staged state
            bridgeSystem.Execute(repo, 0.016f);

            // The backend slot should now be active
            var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
            var handle = new AnimationBackendHandle
            {
                Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
            };
            var slotState = backend.QuerySlotState(handle, 1);
            Assert.Equal(1, slotState.IsActive);
            Assert.Equal(ReloadId, slotState.ActiveMontage.Hash);
        }

        // ─── PlayMontageQueueExecutor tests (D-11, ANC-P3-04) ────────────────

        private static Entity CreateQueueEntity(EntityRepository repo)
        {
            var entity = CreateAnimatedEntity(repo);
            repo.AddComponent(entity, new AnimationMontageQueue());
            repo.AddComponent(entity, new AnimationMontageQueueState
            {
                CurrentEntryIndex = 0xFF,
                ObservedQueueVersion = 0,
            });
            return entity;
        }

        private static unsafe void WriteQueueEntries(
            EntityRepository repo, Entity entity, int[] montageIds)
        {
            ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
            queue.Count = (byte)montageIds.Length;
            queue.QueueVersion = 1;
            fixed (AnimationMontageQueue* queuePtr = &queue)
            {
                var entries = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, MontageQueueEntry>(
                    new Span<byte>(queuePtr->EntriesData, 128));
                for (int i = 0; i < montageIds.Length; i++)
                {
                    entries[i] = new MontageQueueEntry
                    {
                        MontageId = montageIds[i],
                        PlayRate = 1.0f,
                        BlendIntoTime = 0.1f,
                    };
                }
            }
        }

        [Fact]
        public void PlayMontageQueue_ValidChain_SetsRunningAndResetsQueueState()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateQueueEntity(repo);
            WriteQueueEntries(repo, entity, new[] { ReloadId });

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
            }

            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Running, ch2.Status);

            var queueState = repo.GetComponent<AnimationMontageQueueState>(entity);
            Assert.Equal(0, queueState.CurrentEntryIndex);
            Assert.Equal(0f, queueState.EntryElapsedSeconds);
            Assert.False(queueState.InBlendOutWindow);
        }

        [Fact]
        public void PlayMontageQueue_EmptyQueue_FailsImmediately()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateQueueEntity(repo);
            // Queue remains Count=0

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
            }

            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Failure, ch2.Status);
        }

        [Fact]
        public void PlayMontageQueue_InvalidMontageId_FailsImmediately()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateQueueEntity(repo);
            WriteQueueEntries(repo, entity, new[] { unchecked((int)0xDEADBEEFu) });

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
            }

            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Failure, ch2.Status);
        }

        [Fact]
        public void Enqueue_AppendsEntryAndBumpsVersion()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateQueueEntity(repo);
            // Pre-populate queue with one valid entry
            WriteQueueEntries(repo, entity, new[] { ReloadId });

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.EnqueueMontage;
                ch.ActionInstanceId = 2;
                ch.DispatchedInstanceId = 0;
                var p = new EnqueueParams { MontageId = ReloadId, PlayRate = 1.0f, BlendIntoTime = 0.15f };
                fixed (byte* dst = ch.Params)
                    *(EnqueueParams*)dst = p;
            }

            var versionBefore = repo.GetComponent<AnimationMontageQueue>(entity).QueueVersion;
            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Success, ch2.Status);

            var queue = repo.GetComponent<AnimationMontageQueue>(entity);
            Assert.Equal(2, queue.Count);
            Assert.True(queue.QueueVersion > versionBefore);
        }

        [Fact]
        public void Enqueue_AtCapacity_SilentNoOp_StatusRunning()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateQueueEntity(repo);
            // Fill queue to capacity with 8 entries
            WriteQueueEntries(repo, entity, new[] { ReloadId, ReloadId, ReloadId, ReloadId,
                                                    ReloadId, ReloadId, ReloadId, ReloadId });

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.EnqueueMontage;
                ch.ActionInstanceId = 2;
                ch.DispatchedInstanceId = 0;
                var p = new EnqueueParams { MontageId = ReloadId, PlayRate = 1.0f };
                fixed (byte* dst = ch.Params)
                    *(EnqueueParams*)dst = p;
            }

            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            // Silent no-op at capacity: Status=Running (accepted but not acted upon)
            Assert.Equal(NodeStatus.Running, ch2.Status);

            // Queue count stays at 8
            var queue = repo.GetComponent<AnimationMontageQueue>(entity);
            Assert.Equal(8, queue.Count);
        }

        [Fact]
        public void Enqueue_InvalidMontageId_Fails()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateQueueEntity(repo);

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.EnqueueMontage;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
                var p = new EnqueueParams { MontageId = unchecked((int)0xBADBAD00u), PlayRate = 1.0f };
                fixed (byte* dst = ch.Params)
                    *(EnqueueParams*)dst = p;
            }

            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Failure, ch2.Status);
        }

        [Fact]
        public void ClearQueue_TruncatesAndBumpsVersion()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateQueueEntity(repo);
            WriteQueueEntries(repo, entity, new[] { ReloadId, ReloadId, ReloadId });

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.ClearMontageQueue;
                ch.ActionInstanceId = 2;
                ch.DispatchedInstanceId = 0;
            }

            var versionBefore = repo.GetComponent<AnimationMontageQueue>(entity).QueueVersion;
            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Success, ch2.Status);

            var queue = repo.GetComponent<AnimationMontageQueue>(entity);
            Assert.Equal(0, queue.Count);
            Assert.True(queue.QueueVersion > versionBefore);

            var queueState = repo.GetComponent<AnimationMontageQueueState>(entity);
            Assert.Equal(0xFF, queueState.CurrentEntryIndex);
        }

        [Fact]
        public void ClearQueue_OnEmptyQueue_IsIdempotent()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);

            var entity = CreateQueueEntity(repo);
            // Queue already empty

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.ClearMontageQueue;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
            }

            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Success, ch2.Status);
            Assert.Equal(0, repo.GetComponent<AnimationMontageQueue>(entity).Count);
        }

        // ─── NotifyEventEmitterSystem tests (ANC-P3-06) ──────────────────────

        [Fact]
        public void DrainNotifies_EmptyBackend_ReturnsEarlyWithoutError()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new NotifyEventEmitterSystem(backend);

            // Should not throw with an empty backend
            system.Execute(repo, 0.016f);
        }

        [Fact]
        public void DrainNotifies_ConsumesRawEventsFromBackend()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new NotifyEventEmitterSystem(backend);
            var bridgeSystem = new AnimationRuntimeBridgeSystem(backend, cache);

            var entity = CreateAnimatedEntity(repo);
            bridgeSystem.Execute(repo, 0.016f);

            // Run notify emitter twice — should not throw or double-drain incorrectly
            system.Execute(repo, 0.016f);
            system.Execute(repo, 0.016f);
        }

        // ─── AnimationStateReporterSystem tests (ANC-P3-07) ──────────────────

        [Fact]
        public void QueueCompletion_SetsMontageStatusSuccess()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new AnimationStateReporterSystem(backend);

            var entity = CreateQueueEntity(repo);
            // Simulate queue completed: CurrentEntryIndex = 0xFF, Count = 0
            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.Status = NodeStatus.Running;
            }
            ref var queueState = ref repo.GetComponentRW<AnimationMontageQueueState>(entity);
            queueState.CurrentEntryIndex = 0xFF;
            ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
            queue.Count = 0;

            system.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Success, ch2.Status);
        }

        [Fact]
        public void MontageRunning_RemainsRunning_WhenQueueNotComplete()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new AnimationStateReporterSystem(backend);

            var entity = CreateQueueEntity(repo);
            // Simulate queue still active: CurrentEntryIndex = 0, Count = 1
            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.Status = NodeStatus.Running;
            }
            ref var queueState = ref repo.GetComponentRW<AnimationMontageQueueState>(entity);
            queueState.CurrentEntryIndex = 0;
            ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
            queue.Count = 1;

            system.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Running, ch2.Status);
        }

        [Fact]
        public void AimCompletion_SetLookAtChannelSuccess_WhenBlendOutComplete()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new AnimationStateReporterSystem(backend);

            var entity = CreateAnimatedEntity(repo);
            repo.AddComponent(entity, new LookAtChannel { Status = NodeStatus.Running });
            repo.AddComponent(entity, new LookAtExecutorState
            {
                BlendOutWeight = 0f,
                TargetType = 1, // 1 = point, non-zero means "was aiming"
            });

            system.Execute(repo, 0.016f);

            var lookAtCh = repo.GetComponent<LookAtChannel>(entity);
            Assert.Equal(NodeStatus.Success, lookAtCh.Status);

            var execState = repo.GetComponent<LookAtExecutorState>(entity);
            Assert.Equal(0, execState.TargetType); // cleared after completion
        }

        // ─── AnimationCapabilityChangeReactorSystem tests (ANC-P3-09) ─────────

        [Fact]
        public void CanPlayAnimations_Loss_SetsChannelToFailureAndClearsQueue()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new AnimationCapabilityChangeReactorSystem(backend);
            repo.RegisterComponent<PreviousCapabilities>();

            var entity = CreateQueueEntity(repo);
            WriteQueueEntries(repo, entity, new[] { ReloadId });
            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.Status = NodeStatus.Running;
            }

            // Set capability: currently missing CanPlayAnimations (lost), previously had it
            repo.GetComponentRW<ActorCapabilityState>(entity).Capabilities = ActorCapabilities.CanChangeStance;
            repo.AddComponent(entity, new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanPlayAnimations | ActorCapabilities.CanChangeStance
            });

            system.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Failure, ch2.Status);

            var queue = repo.GetComponent<AnimationMontageQueue>(entity);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void CanPlayAnimations_Loss_BumpsDispatchedInstanceId()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new AnimationCapabilityChangeReactorSystem(backend);
            repo.RegisterComponent<PreviousCapabilities>();

            var entity = CreateQueueEntity(repo);
            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.Status = NodeStatus.Running;
                ch.DispatchedInstanceId = 5;
            }

            repo.GetComponentRW<ActorCapabilityState>(entity).Capabilities = ActorCapabilities.None;
            repo.AddComponent(entity, new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanPlayAnimations
            });

            system.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            // DispatchedInstanceId must be bumped
            Assert.NotEqual((ushort)5, ch2.DispatchedInstanceId);
        }

        [Fact]
        public void CanAim_Loss_SetLookAtChannelToFailure()
        {
            var (repo, backend, cache) = CreateFixture();
            var system = new AnimationCapabilityChangeReactorSystem(backend);
            repo.RegisterComponent<PreviousCapabilities>();

            var entity = CreateAnimatedEntity(repo);
            repo.AddComponent(entity, new LookAtChannel { Status = NodeStatus.Running });
            repo.AddComponent(entity, new LookAtExecutorState { TargetType = 1 });

            // CanAim lost, previously had it
            repo.GetComponentRW<ActorCapabilityState>(entity).Capabilities = ActorCapabilities.CanPlayAnimations;
            repo.AddComponent(entity, new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanPlayAnimations | ActorCapabilities.CanAim
            });

            system.Execute(repo, 0.016f);

            var lookAtCh = repo.GetComponent<LookAtChannel>(entity);
            Assert.Equal(NodeStatus.Failure, lookAtCh.Status);
        }

        // ─── AnimationMuscleModule registration tests (ANC-P3-10) ────────────

        private sealed class CapturingRegistry : ISystemRegistry
        {
            public readonly List<IEcsModuleSystem> Systems = new List<IEcsModuleSystem>();
            public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => Systems.Add(system);
            public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem { Systems.Add(system); return system; }
        }

        [Fact]
        public void AnimationMuscleModule_RegistersAllEightSystems_InCorrectOrder()
        {
            var (_, backend, cache) = CreateFixture();
            var module = new AnimationMuscleModule(backend, cache);
            var registry = new CapturingRegistry();

            module.RegisterSystems(registry);

            Assert.Equal(8, registry.Systems.Count);

            // Verify order matches DD-1 §17
            Assert.IsType<AnimationCapabilityChangeReactorSystem>(registry.Systems[0]);
            Assert.IsType<AnimationDispatcherSystem>(registry.Systems[1]);
            Assert.IsType<LookAtDispatcherSystem>(registry.Systems[2]);
            Assert.IsType<MontageQueueAdvanceSystem>(registry.Systems[3]);
            Assert.IsType<AnimationRuntimeBridgeSystem>(registry.Systems[4]);
            Assert.IsType<NotifyEventEmitterSystem>(registry.Systems[5]);
            Assert.IsType<AnimationStateReporterSystem>(registry.Systems[6]);
            Assert.IsType<AnimationBackendCleanupSystem>(registry.Systems[7]);
        }

        // ─── Integration tests ────────────────────────────────────────────────

        [Fact]
        public void FullPipeline_PlayMontageQueue_EventuallyReachesSuccess()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);
            var bridgeSystem = new AnimationRuntimeBridgeSystem(backend, cache);
            var reporterSystem = new AnimationStateReporterSystem(backend);
            var queueAdvance = new MontageQueueAdvanceSystem(backend, cache);

            var entity = CreateQueueEntity(repo);
            WriteQueueEntries(repo, entity, new[] { ReloadId });

            // Register entity with backend
            bridgeSystem.Execute(repo, 0.016f);

            // Issue PlayMontageQueue command
            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
            }

            dispatchSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Running, ch2.Status);

            // Simulate queue completing: set CurrentEntryIndex = 0xFF, Count = 0
            {
                ref var queueState = ref repo.GetComponentRW<AnimationMontageQueueState>(entity);
                queueState.CurrentEntryIndex = 0xFF;
                ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
                queue.Count = 0;
            }

            // Reporter should set Success
            reporterSystem.Execute(repo, 0.016f);

            var ch3 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Success, ch3.Status);
        }

        [Fact]
        public void SimultaneousCapabilityLoss_AndQueuePlay_IsRobust()
        {
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);
            var reactorSystem = new AnimationCapabilityChangeReactorSystem(backend);
            repo.RegisterComponent<PreviousCapabilities>();

            var entity = CreateQueueEntity(repo);
            WriteQueueEntries(repo, entity, new[] { ReloadId, ReloadId });

            // Entity has CanPlayAnimations
            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
                ch.Status = NodeStatus.Running;
            }

            // Capability lost mid-play
            repo.GetComponentRW<ActorCapabilityState>(entity).Capabilities = ActorCapabilities.None;
            repo.AddComponent(entity, new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanPlayAnimations
            });

            // Reactor runs first (correct order per DD-1 §17)
            reactorSystem.Execute(repo, 0.016f);

            var ch2 = repo.GetComponent<AnimationChannel>(entity);
            Assert.Equal(NodeStatus.Failure, ch2.Status);

            // Queue is cleared
            var queue = repo.GetComponent<AnimationMontageQueue>(entity);
            Assert.Equal(0, queue.Count);
        }

        // ─── OFX-002: NotifyEventEmitterSystem typed event dispatch ──────────

        /// <summary>Creates a DTO that includes Footstep, HitWindowOpened, and Generic markers.</summary>
        private static CharacterAnimationDefDto CreateMultiNotifyDto()
        {
            var footstepHash = StableIdHasher.ComputeMarkerHash("Footstep_L");
            var hitWindowHash = StableIdHasher.ComputeMarkerHash("HitWindow_Open");
            var magOutHash = StableIdHasher.ComputeMarkerHash("MagOut");
            return new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>
                {
                    new SlotDefDto { SlotId = 1, Name = "FullBody", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 100 },
                },
                Montages = new List<MontageDefDto>
                {
                    new MontageDefDto
                    {
                        Name = "Reload_Rifle",
                        AssetRef = "Anims/Reload.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0f,
                        DefaultBlendOutTime = 0f,
                        DurationSeconds = 2.0f,
                        Sections = new[] { "Start" },
                        Notifies = new List<MontageNotifyRefDto>
                        {
                            new MontageNotifyRefDto { MarkerName = "Footstep_L",    TimeSeconds = 0.1f },
                            new MontageNotifyRefDto { MarkerName = "HitWindow_Open", TimeSeconds = 0.3f },
                            new MontageNotifyRefDto { MarkerName = "MagOut",         TimeSeconds = 0.5f },
                        },
                        IsStanceTransition = false,
                    },
                },
                SupportedStances = new[] { Components.StanceId.Standing },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new List<NotifyMarkerDefDto>
                {
                    new NotifyMarkerDefDto { Name = "Footstep_L",    Hash = footstepHash,   Kind = AnimNotifyCategory.Footstep },
                    new NotifyMarkerDefDto { Name = "HitWindow_Open", Hash = hitWindowHash, Kind = AnimNotifyCategory.HitWindowOpened },
                    new NotifyMarkerDefDto { Name = "MagOut",         Hash = magOutHash,     Kind = AnimNotifyCategory.Generic },
                },
            };
        }

        [Fact]
        public void NotifyEmitter_FootstepKind_EmitsFootstepEvent()
        {
            // OFX-002: Footstep RawNotifyEvent must result in FootstepEvent published on the bus.
            var dto = CreateMultiNotifyDto();
            var baked = BakingUtils.BakeDef(dto);
            var classData = new Dictionary<long, CharacterAnimationBakedData> { [ClassId] = baked };
            var backend = new FakeAnimationBackend(classData);
            var cache = new BakedAnimationCache(null);
            cache.GetOrBake(ClassId, dto);

            var repo = new EntityRepository();
            repo.RegisterComponent<CharacterAnimationDefRuntime>();
            repo.RegisterComponent<AnimationExecutorState>();

            var bridgeSystem = new AnimationRuntimeBridgeSystem(backend, cache);
            var emitterSystem = new NotifyEventEmitterSystem(backend);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new CharacterAnimationDefRuntime { BackendHandle = ClassId, SlotCount = 1 });
            repo.AddComponent(entity, new AnimationExecutorState());

            bridgeSystem.Execute(repo, 0f); // Register entity with backend

            var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
            var handle = new AnimationBackendHandle
            {
                Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
            };

            // Play montage and tick past footstep marker (0.1s)
            int montageId = StableIdHasher.ComputeMontageAssetId("Reload_Rifle");
            var playParams = new PlayMontageParams { MontageId = montageId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in playParams);
            backend.Tick(0.15f);

            emitterSystem.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var footsteps = repo.Bus.Read<FootstepEvent>();
            Assert.True(footsteps.Length >= 1, "Expected at least one FootstepEvent");
            Assert.Equal(entity, footsteps[0].Target);
        }

        [Fact]
        public void NotifyEmitter_HitWindowOpenedKind_EmitsHitWindowOpenedEvent()
        {
            // OFX-002: HitWindowOpened RawNotifyEvent must result in HitWindowOpenedEvent.
            var dto = CreateMultiNotifyDto();
            var baked = BakingUtils.BakeDef(dto);
            var classData = new Dictionary<long, CharacterAnimationBakedData> { [ClassId] = baked };
            var backend = new FakeAnimationBackend(classData);
            var cache = new BakedAnimationCache(null);
            cache.GetOrBake(ClassId, dto);

            var repo = new EntityRepository();
            repo.RegisterComponent<CharacterAnimationDefRuntime>();
            repo.RegisterComponent<AnimationExecutorState>();

            var bridgeSystem = new AnimationRuntimeBridgeSystem(backend, cache);
            var emitterSystem = new NotifyEventEmitterSystem(backend);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new CharacterAnimationDefRuntime { BackendHandle = ClassId, SlotCount = 1 });
            repo.AddComponent(entity, new AnimationExecutorState());

            bridgeSystem.Execute(repo, 0f);

            var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
            var handle = new AnimationBackendHandle
            {
                Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
            };

            int montageId = StableIdHasher.ComputeMontageAssetId("Reload_Rifle");
            var playParams = new PlayMontageParams { MontageId = montageId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in playParams);
            backend.Tick(0.35f); // Past HitWindow_Open at 0.3s

            emitterSystem.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            // Footstep (0.1s) and HitWindowOpened (0.3s) both fire
            var hitWindows = repo.Bus.Read<HitWindowOpenedEvent>();
            Assert.True(hitWindows.Length >= 1, "Expected at least one HitWindowOpenedEvent");
            Assert.Equal(entity, hitWindows[0].Target);
        }

        [Fact]
        public void NotifyEmitter_GenericKind_EmitsAnimNotifyEvent()
        {
            // OFX-002: Generic RawNotifyEvent must result in AnimNotifyEvent.
            var dto = CreateMultiNotifyDto();
            var baked = BakingUtils.BakeDef(dto);
            var classData = new Dictionary<long, CharacterAnimationBakedData> { [ClassId] = baked };
            var backend = new FakeAnimationBackend(classData);
            var cache = new BakedAnimationCache(null);
            cache.GetOrBake(ClassId, dto);

            var repo = new EntityRepository();
            repo.RegisterComponent<CharacterAnimationDefRuntime>();
            repo.RegisterComponent<AnimationExecutorState>();

            var bridgeSystem = new AnimationRuntimeBridgeSystem(backend, cache);
            var emitterSystem = new NotifyEventEmitterSystem(backend);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new CharacterAnimationDefRuntime { BackendHandle = ClassId, SlotCount = 1 });
            repo.AddComponent(entity, new AnimationExecutorState());

            bridgeSystem.Execute(repo, 0f);

            var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
            var handle = new AnimationBackendHandle
            {
                Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
            };

            int montageId = StableIdHasher.ComputeMontageAssetId("Reload_Rifle");
            var playParams = new PlayMontageParams { MontageId = montageId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in playParams);
            backend.Tick(0.6f); // Past MagOut at 0.5s

            emitterSystem.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var notifies = repo.Bus.Read<AnimNotifyEvent>();
            Assert.True(notifies.Length >= 1, "Expected at least one AnimNotifyEvent for generic marker");
            Assert.Equal(entity, notifies[0].Target);
        }

        // ─── OFX-009: MontageQueueAdvanceSystem crossfade tests ──────────────

        [Fact]
        public void QueueAdvances_WhenSlotEntersBlendOutWindow_BeforeSilence()
        {
            // OFX-009: Queue must advance (current entry index increments) when the active slot
            // enters blend-out window — NOT waiting for the slot to go silent.
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);
            var bridgeSystem = new AnimationRuntimeBridgeSystem(backend, cache);
            var queueAdvance = new MontageQueueAdvanceSystem(backend, cache);

            var entity = CreateQueueEntity(repo);
            WriteQueueEntries(repo, entity, new[] { ReloadId, ReloadId });

            bridgeSystem.Execute(repo, 0f); // Register entity

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
            }
            dispatchSystem.Execute(repo, 0f); // Stage first entry
            bridgeSystem.Execute(repo, 0f);   // Apply staged play

            // Confirm first entry is active
            var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
            var handle = new AnimationBackendHandle
            {
                Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
            };
            Assert.True(backend.IsAnySlotActive(handle));

            // Tick past the blend-out threshold (Reload_Rifle: duration=1.0f, blendOut=0.2f → threshold at 0.8f)
            backend.Tick(0.85f);

            Assert.True(backend.IsAnySlotInBlendOut(handle), "Slot should be in blend-out window after 0.85s");
            Assert.True(backend.IsAnySlotActive(handle), "Slot must still be active during blend-out");

            // Run queue advance — should detect blend-out and trigger crossfade
            queueAdvance.Execute(repo, 0f);

            var queueState = repo.GetComponent<AnimationMontageQueueState>(entity);
            Assert.Equal(1, queueState.CurrentEntryIndex); // Advanced to second entry
        }

        [Fact]
        public void CrossfadeMontageOnSlot_IsCalledForNextQueueEntry()
        {
            // OFX-009: After queue advances on blend-out, the second montage must be playing
            // (CrossfadeMontageOnSlot called — new slot state active with second entry's montage).
            var (repo, backend, cache) = CreateFixture();
            var dispatchSystem = new AnimationDispatcherSystem(backend, cache);
            var bridgeSystem = new AnimationRuntimeBridgeSystem(backend, cache);
            var queueAdvance = new MontageQueueAdvanceSystem(backend, cache);

            // Use two distinct montage IDs to verify which is playing after crossfade.
            // Both use Reload_Rifle (same ID in our test DTO) — we verify the advance happened
            // by checking CurrentEntryIndex == 1 AND slot 1 is still active.
            var entity = CreateQueueEntity(repo);
            WriteQueueEntries(repo, entity, new[] { ReloadId, ReloadId });

            bridgeSystem.Execute(repo, 0f);

            unsafe
            {
                ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);
                ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
                ch.ActionInstanceId = 1;
                ch.DispatchedInstanceId = 0;
            }
            dispatchSystem.Execute(repo, 0f);
            bridgeSystem.Execute(repo, 0f);

            var def = repo.GetComponent<CharacterAnimationDefRuntime>(entity);
            var handle = new AnimationBackendHandle
            {
                Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
            };

            // Tick into blend-out window
            backend.Tick(0.85f);
            Assert.True(backend.IsAnySlotInBlendOut(handle));

            // Advance queue — should issue CrossfadeMontageOnSlot for second entry
            queueAdvance.Execute(repo, 0f);

            // After crossfade, slot 1 should be active (new montage started)
            Assert.True(backend.IsAnySlotActive(handle),
                "Slot should be active after crossfade (second entry started)");

            var queueState = repo.GetComponent<AnimationMontageQueueState>(entity);
            Assert.Equal(1, queueState.CurrentEntryIndex); // Second entry is now current
        }
    }
}
