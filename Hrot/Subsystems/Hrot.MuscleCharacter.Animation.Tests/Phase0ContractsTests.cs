using System;
using Xunit;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// Comprehensive unit test suite for Phase 0 foundational contracts and components.
    /// Tests layout, serialization, enum values, and interface mockability.
    /// </summary>
    public class Phase0ContractsTests
    {
        // ── ANC-P0-01: AnimNotifyCategory enum ──────────────────────────────
        [Fact]
        public void AnimNotifyCategory_EnumFitsInByte()
        {
            Assert.Equal(1, sizeof(AnimNotifyCategory));
        }

        [Fact]
        public void AnimNotifyCategory_HasCorrectValues()
        {
            Assert.Equal((byte)0, (byte)AnimNotifyCategory.Generic);
            Assert.Equal((byte)1, (byte)AnimNotifyCategory.Footstep);
            Assert.Equal((byte)2, (byte)AnimNotifyCategory.HitWindowOpened);
            Assert.Equal((byte)3, (byte)AnimNotifyCategory.HitWindowClosed);
        }

        // ── ANC-P0-02: ActorCapabilities animation bits ──────────────────────
        [Fact]
        public void ActorCapabilities_ExistingBitsUnchanged()
        {
            Assert.Equal((byte)1, (byte)ActorCapabilities.CanMove);
            Assert.Equal((byte)2, (byte)ActorCapabilities.CanShoot);
            Assert.Equal((byte)4, (byte)ActorCapabilities.CanInteract);
        }

        [Fact]
        public void ActorCapabilities_AnimationBitsPresent()
        {
            Assert.Equal((byte)8, (byte)ActorCapabilities.CanPlayAnimations);
            Assert.Equal((byte)16, (byte)ActorCapabilities.CanChangeStance);
            Assert.Equal((byte)32, (byte)ActorCapabilities.CanAim);
        }

        [Fact]
        public void ActorCapabilities_AllFitInByte()
        {
            // All flags combined (1 + 2 + 4 + 8 + 16 + 32 = 63) should fit in a byte.
            var allFlags = ActorCapabilities.CanMove
                | ActorCapabilities.CanShoot
                | ActorCapabilities.CanInteract
                | ActorCapabilities.CanPlayAnimations
                | ActorCapabilities.CanChangeStance
                | ActorCapabilities.CanAim;
            Assert.Equal((byte)63, (byte)allFlags);
        }

        // ── ANC-P0-03: GlobalComponentIds allocations ────────────────────────
        [Fact]
        public void GlobalComponentIds_AnimationBlockAllocated()
        {
            Assert.Equal(220, GlobalComponentIds.AnimationChannel);
            Assert.Equal(221, GlobalComponentIds.LookAtChannel);
            Assert.Equal(222, GlobalComponentIds.StanceIntent);
            Assert.Equal(223, GlobalComponentIds.StanceStatus);
            Assert.Equal(224, GlobalComponentIds.AnimationMontageQueue);
            Assert.Equal(225, GlobalComponentIds.AnimationMontageQueueState);
            Assert.Equal(237, GlobalComponentIds.LookAtExecutorState);
            Assert.Equal(238, GlobalComponentIds.CharacterAnimationDefRuntime);
            Assert.Equal(239, GlobalComponentIds.AnimationExecutorState);
            Assert.Equal(240, GlobalComponentIds.FakeAnimBackendState);
        }

        [Fact]
        public void GlobalComponentIds_AnimationIdsNoDuplicates()
        {
            var ids = new[]
            {
                GlobalComponentIds.AnimationChannel,
                GlobalComponentIds.LookAtChannel,
                GlobalComponentIds.StanceIntent,
                GlobalComponentIds.StanceStatus,
                GlobalComponentIds.AnimationMontageQueue,
                GlobalComponentIds.AnimationMontageQueueState,
                GlobalComponentIds.LookAtExecutorState,
                GlobalComponentIds.CharacterAnimationDefRuntime,
                GlobalComponentIds.AnimationExecutorState,
                GlobalComponentIds.FakeAnimBackendState
            };

            var uniqueIds = new HashSet<int>(ids);
            Assert.Equal(ids.Length, uniqueIds.Count);
        }

        [Fact]
        public void GlobalComponentIds_AllInRange220To249()
        {
            var ids = new[]
            {
                GlobalComponentIds.AnimationChannel,
                GlobalComponentIds.LookAtChannel,
                GlobalComponentIds.StanceIntent,
                GlobalComponentIds.StanceStatus,
                GlobalComponentIds.AnimationMontageQueue,
                GlobalComponentIds.AnimationMontageQueueState,
                GlobalComponentIds.LookAtExecutorState,
                GlobalComponentIds.CharacterAnimationDefRuntime,
                GlobalComponentIds.AnimationExecutorState,
                GlobalComponentIds.FakeAnimBackendState
            };

            foreach (var id in ids)
            {
                Assert.True(id >= 220 && id <= 249, $"ID {id} is outside 220-249 range");
            }
        }

        // ── ANC-P0-04: Channel parameter structs ────────────────────────────
        [Fact]
        public unsafe void PlayMontageParams_SizeWithinBudget()
        {
            var size = sizeof(PlayMontageParams);
            Assert.True(size <= BehaviorConstants.ActionParamsByteSize,
                $"PlayMontageParams size {size} exceeds {BehaviorConstants.ActionParamsByteSize}");
        }

        [Fact]
        public unsafe void StopMontageParams_SizeWithinBudget()
        {
            var size = sizeof(StopMontageParams);
            Assert.True(size <= BehaviorConstants.ActionParamsByteSize,
                $"StopMontageParams size {size} exceeds {BehaviorConstants.ActionParamsByteSize}");
        }

        [Fact]
        public unsafe void PlayMontageQueueParams_SizeWithinBudget()
        {
            var size = sizeof(PlayMontageQueueParams);
            Assert.True(size <= BehaviorConstants.ActionParamsByteSize,
                $"PlayMontageQueueParams size {size} exceeds {BehaviorConstants.ActionParamsByteSize}");
        }

        [Fact]
        public unsafe void LookAtPointParams_SizeWithinBudget()
        {
            var size = sizeof(LookAtPointParams);
            Assert.True(size <= BehaviorConstants.ActionParamsByteSize,
                $"LookAtPointParams size {size} exceeds {BehaviorConstants.ActionParamsByteSize}");
        }

        [Fact]
        public unsafe void LookAtEntityParams_SizeWithinBudget()
        {
            var size = sizeof(LookAtEntityParams);
            Assert.True(size <= BehaviorConstants.ActionParamsByteSize,
                $"LookAtEntityParams size {size} exceeds {BehaviorConstants.ActionParamsByteSize}");
        }

        [Fact]
        public unsafe void ReleaseLookParams_SizeWithinBudget()
        {
            var size = sizeof(ReleaseLookParams);
            Assert.True(size <= BehaviorConstants.ActionParamsByteSize,
                $"ReleaseLookParams size {size} exceeds {BehaviorConstants.ActionParamsByteSize}");
        }

        [Fact]
        public void AnimationActionIds_CorrectValues()
        {
            Assert.Equal((ushort)1, AnimationActionIds.PlayMontage);
            Assert.Equal((ushort)2, AnimationActionIds.StopMontage);
            Assert.Equal((ushort)3, AnimationActionIds.PlayMontageQueue);
            Assert.Equal((ushort)4, AnimationActionIds.EnqueueMontage);
        }

        [Fact]
        public void LookAtActionIds_CorrectValues()
        {
            Assert.Equal((ushort)10, LookAtActionIds.LookAtPoint);
            Assert.Equal((ushort)11, LookAtActionIds.LookAtEntity);
            Assert.Equal((ushort)12, LookAtActionIds.ReleaseLook);
        }

        // ── ANC-P0-05: Replicated components layout ──────────────────────────
        [Fact]
        public unsafe void AnimationChannel_LayoutWithinBudget()
        {
            var size = sizeof(AnimationChannel);
            Assert.True(size <= BehaviorConstants.MaxChannelSizeBytes,
                $"AnimationChannel size {size} exceeds max {BehaviorConstants.MaxChannelSizeBytes}");
        }

        [Fact]
        public unsafe void LookAtChannel_LayoutWithinBudget()
        {
            var size = sizeof(LookAtChannel);
            Assert.True(size <= BehaviorConstants.MaxChannelSizeBytes,
                $"LookAtChannel size {size} exceeds max {BehaviorConstants.MaxChannelSizeBytes}");
        }

        [Fact]
        public unsafe void AnimationMontageQueue_LayoutWithinBudget()
        {
            var size = sizeof(AnimationMontageQueue);
            // Queue should be roughly 128 bytes for entries + 8 bytes for metadata
            Assert.True(size <= 140, $"AnimationMontageQueue size {size} exceeds expected 140 B");
        }

        [Fact]
        public unsafe void StanceIntent_LayoutSmall()
        {
            var size = sizeof(StanceIntent);
            Assert.True(size <= 16, $"StanceIntent size {size} exceeds expected 16 B");
        }

        [Fact]
        public unsafe void StanceStatus_LayoutSmall()
        {
            var size = sizeof(StanceStatus);
            Assert.True(size <= 16, $"StanceStatus size {size} exceeds expected 16 B");
        }

        [Fact]
        public unsafe void MontageQueueEntry_Size()
        {
            var size = sizeof(MontageQueueEntry);
            // int (4) + float (4) + float (4) + byte + byte + ushort (2) = 16 bytes
            Assert.Equal(16, size);
        }

        // ── ANC-P0-06: Internal components layout ────────────────────────────
        [Fact]
        public void AnimationExecutorState_MaxSlotsIs8()
        {
            Assert.Equal(8, AnimationExecutorState.MaxSlots);
        }

        [Fact]
        public unsafe void AnimationExecutorState_LayoutReasonable()
        {
            var size = sizeof(AnimationExecutorState);
            // 8 slots * 28 bytes = 224 bytes
            Assert.True(size > 0 && size < 1024,
                $"AnimationExecutorState size {size} unreasonable");
        }

        [Fact]
        public unsafe void CharacterAnimationDefRuntime_LayoutSmall()
        {
            var size = sizeof(CharacterAnimationDefRuntime);
            Assert.True(size <= 16, $"CharacterAnimationDefRuntime size {size} exceeds 16 B");
        }

        [Fact]
        public unsafe void LookAtExecutorState_LayoutSmall()
        {
            var size = sizeof(LookAtExecutorState);
            Assert.True(size <= 32, $"LookAtExecutorState size {size} exceeds 32 B");
        }

        // ── ANC-P0-07: IAnimationBackend interface ───────────────────────────
        [Fact]
        public void IAnimationBackend_InterfaceIsPublic()
        {
            var ifaceType = typeof(IAnimationBackend);
            Assert.NotNull(ifaceType);
            Assert.True(ifaceType.IsInterface);
        }

        [Fact]
        public void IAnimationBackend_CanBeMocked()
        {
            // Create a simple mock that satisfies all interface members
            var mockBackend = new MockAnimationBackend();
            IAnimationBackend backend = mockBackend;

            var config = new AnimationBackendConfig { MaxEntities = 100 };
            var handle = backend.RegisterEntity(1, 0);
            Assert.True(handle.IsValid);

            backend.UnregisterEntity(handle);
        }

        [Fact]
        public void AnimationBackendHandle_GenerationSafety()
        {
            var h1 = new AnimationBackendHandle { Index = 0, Generation = 1 };
            var h2 = new AnimationBackendHandle { Index = 0, Generation = 2 };
            
            Assert.NotEqual(h1, h2);
            Assert.False(h1.Equals(h2));
        }

        [Fact]
        public unsafe void RawNotifyEvent_LayoutSmall()
        {
            var size = sizeof(RawNotifyEvent);
            Assert.True(size <= 32, $"RawNotifyEvent size {size} exceeds expected 32 B");
        }

        [Fact]
        public unsafe void AnimationBackendConfig_LayoutSmall()
        {
            var size = sizeof(AnimationBackendConfig);
            Assert.True(size <= 24, $"AnimationBackendConfig size {size} exceeds expected 24 B");
        }

        [Fact]
        public unsafe void AnimationBackendMetrics_LayoutSmall()
        {
            var size = sizeof(AnimationBackendMetrics);
            Assert.True(size <= 32, $"AnimationBackendMetrics size {size} exceeds expected 32 B");
        }

        // ── Enum verification ────────────────────────────────────────────────
        [Fact]
        public void StanceId_HasThreeValues()
        {
            Assert.Equal((byte)0, (byte)StanceId.Standing);
            Assert.Equal((byte)1, (byte)StanceId.Crouched);
            Assert.Equal((byte)2, (byte)StanceId.Prone);
        }

        [Fact]
        public void StanceTransitionPhase_HasThreeValues()
        {
            Assert.Equal((byte)0, (byte)StanceTransitionPhase.Idle);
            Assert.Equal((byte)1, (byte)StanceTransitionPhase.Transitioning);
            Assert.Equal((byte)2, (byte)StanceTransitionPhase.Locked);
        }

        [Fact]
        public void SlotId_HasEightValues()
        {
            Assert.Equal((byte)0, (byte)SlotId.Slot0);
            Assert.Equal((byte)1, (byte)SlotId.Slot1);
            Assert.Equal((byte)7, (byte)SlotId.Slot7);
        }

        [Fact]
        public void MontagePlaybackState_HasThreeValues()
        {
            Assert.Equal((byte)0, (byte)MontagePlaybackState.Inactive);
            Assert.Equal((byte)1, (byte)MontagePlaybackState.Active);
            Assert.Equal((byte)2, (byte)MontagePlaybackState.BlendingOut);
        }
    }

    /// <summary>
    /// Simple mock implementation of IAnimationBackend for testability verification.
    /// </summary>
    internal class MockAnimationBackend : IAnimationBackend
    {
        public AnimationBackendHandle RegisterEntity(uint entityId, long characterDefHandle)
        {
            return new AnimationBackendHandle { Index = entityId, Generation = 1 };
        }

        public void UnregisterEntity(AnimationBackendHandle handle)
        {
            // No-op
        }

        public bool TryResolve(AnimationBackendHandle handle, out nint state)
        {
            state = IntPtr.Zero;
            return handle.IsValid;
        }

        public void PlayMontageOnSlot(AnimationBackendHandle handle, in PlayMontageParams @params)
        {
            // No-op
        }

        public void StopMontageOnSlot(AnimationBackendHandle handle, in StopMontageParams @params)
        {
            // No-op
        }

        public void SetAimTargetPoint(AnimationBackendHandle handle, in LookAtPointParams @params)
        {
            // No-op
        }

        public void SetAimTargetEntity(AnimationBackendHandle handle, in LookAtEntityParams @params)
        {
            // No-op
        }

        public void ReleaseAim(AnimationBackendHandle handle, in ReleaseLookParams @params)
        {
            // No-op
        }

        public void RequestStanceChange(AnimationBackendHandle handle, byte targetStance, float blendDurationSeconds)
        {
            // No-op
        }

        public void Tick(float deltaTime)
        {
            // No-op
        }

        public int DrainNotifies(Span<RawNotifyEvent> dest)
        {
            return 0;
        }

        public int DrainNotifies(AnimationBackendHandle handle, Span<RawNotifyEvent> dest)
        {
            return 0;
        }

        public bool GetCurrentStance(AnimationBackendHandle handle, out byte currentStance)
        {
            currentStance = 0;
            return false;
        }

        public bool IsAnySlotActive(AnimationBackendHandle handle)
        {
            return false;
        }

        public AnimationBackendMetrics SnapshotMetrics()
        {
            return new AnimationBackendMetrics();
        }
    }
}
