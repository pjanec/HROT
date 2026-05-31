using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Fake;
using Hrot.MuscleCharacter.Animation.Fake.Components;
using Hrot.MuscleCharacter.Animation.Hashing;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// Phase 1 behavioral tests for FakeAnimationBackend (DD-Tests §3.2).
    /// Tests use FakeAnimationBackend's query API (QuerySlotState, DrainNotifies).
    /// </summary>
    public class Phase1BackendBehaviorTests
    {
        // Test montage names and IDs
        private static readonly int ReloadId = StableIdHasher.ComputeMontageAssetId("Reload_Rifle");
        private static readonly int VaultId = StableIdHasher.ComputeMontageAssetId("Vault_Low");

        // Notify marker hashes computed via the same algorithm used by BakingUtils
        private static readonly uint MagOutHash = StableIdHasher.ComputeMarkerHash("MagOut");
        private static readonly uint FootstepLeftHash = StableIdHasher.ComputeMarkerHash("Footstep_Left");

        private const long ClassId = 1L;

        /// <summary>
        /// Creates a test DTO:
        /// - Reload_Rifle on slot 1, duration 1.0f, notify MagOut at 0.5f
        /// - Vault_Low on slot 1, duration 2.0f (for overwrite test)
        /// - Footstep_Left marker for footstep cadence tests
        /// </summary>
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
                        AssetRef = "Animations/Reload.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 1.0f,
                        Sections = new[] { "Start", "End" },
                        Notifies = new List<MontageNotifyRefDto>
                        {
                            new MontageNotifyRefDto { MarkerName = "MagOut", TimeSeconds = 0.5f },
                        },
                        IsStanceTransition = false,
                    },
                    new MontageDefDto
                    {
                        Name = "Vault_Low",
                        AssetRef = "Animations/Vault.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.05f,
                        DefaultBlendOutTime = 0.1f,
                        DurationSeconds = 2.0f,
                        Sections = new[] { "Start" },
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = false,
                    },
                },
                SupportedStances = new[] { Components.StanceId.Standing, Components.StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new List<NotifyMarkerDefDto>
                {
                    new NotifyMarkerDefDto { Name = "MagOut", Hash = MagOutHash, Kind = AnimNotifyCategory.Generic },
                    new NotifyMarkerDefDto { Name = "Footstep_Left", Hash = FootstepLeftHash, Kind = AnimNotifyCategory.Footstep },
                },
            };
        }

        private static (FakeAnimationBackend backend, AnimationBackendHandle handle) CreateBackendWithEntity()
        {
            var dto = CreateTestDto();
            var baked = BakingUtils.BakeDef(dto);
            var classData = new Dictionary<long, CharacterAnimationBakedData> { [ClassId] = baked };
            var backend = new FakeAnimationBackend(classData);
            var handle = backend.RegisterEntity(1u, ClassId);
            return (backend, handle);
        }

        // ─── PlayMontage tests ───────────────────────────────────────────────

        [Fact]
        public void PlayMontage_SetsSlotActive()
        {
            var (backend, handle) = CreateBackendWithEntity();
            var p = new PlayMontageParams { MontageId = ReloadId, BlendInTime = 0.1f, PlayRate = 1.0f };

            backend.PlayMontageOnSlot(handle, in p);

            var slot = backend.QuerySlotState(handle, 1);
            Assert.Equal(1, slot.IsActive);
            Assert.Equal(ReloadId, slot.ActiveMontage.Hash);
            Assert.Equal(0f, slot.ElapsedSeconds);
        }

        [Fact]
        public void PlayMontage_OverwritesPreviousMontageInSameSlot()
        {
            var (backend, handle) = CreateBackendWithEntity();
            var reload = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in reload);
            backend.Tick(0.5f);

            var vault = new PlayMontageParams { MontageId = VaultId, BlendInTime = 0.05f, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in vault);

            var slot = backend.QuerySlotState(handle, 1);
            Assert.Equal(VaultId, slot.ActiveMontage.Hash);
            Assert.Equal(0f, slot.ElapsedSeconds);
            Assert.Equal(0UL, slot.FiredNotifyMask);
        }

        [Fact]
        public void PlayMontage_UnknownMontage_NoOps()
        {
            var (backend, handle) = CreateBackendWithEntity();
            var p = new PlayMontageParams { MontageId = unchecked((int)0x99999999u), PlayRate = 1.0f };

            backend.PlayMontageOnSlot(handle, in p);

            var slot = backend.QuerySlotState(handle, 1);
            Assert.Equal(0, slot.IsActive);
        }

        // ─── Tick advancement tests ──────────────────────────────────────────

        [Fact]
        public void Tick_AdvancesElapsedTimeByDeltaTimesPlayRate()
        {
            var (backend, handle) = CreateBackendWithEntity();
            var p = new PlayMontageParams { MontageId = VaultId, PlayRate = 2.0f };
            backend.PlayMontageOnSlot(handle, in p);

            backend.Tick(0.5f);

            var slot = backend.QuerySlotState(handle, 1);
            Assert.InRange(slot.ElapsedSeconds, 0.99f, 1.01f);
        }

        [Fact]
        public void Tick_DeactivatesSlotOnNaturalCompletion()
        {
            var (backend, handle) = CreateBackendWithEntity();
            // Reload_Rifle duration = 1.0f
            var p = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in p);

            backend.Tick(1.5f);

            var slot = backend.QuerySlotState(handle, 1);
            Assert.Equal(0, slot.IsActive);
        }

        [Fact]
        public void Tick_DoesNotAdvanceInactiveSlots()
        {
            // OFX-004 update: StopMontageOnSlot(BlendOutTime=0f) triggers blend-out, slot stays active
            // until the tick advances it past TotalDurationSeconds, then deactivates naturally.
            var (backend, handle) = CreateBackendWithEntity();
            var play = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in play);

            var stop = new StopMontageParams { BlendOutTime = 0f };
            backend.StopMontageOnSlot(handle, in stop);

            // After stop: still active, InBlendOutWindow=1, ElapsedSeconds advanced to TotalDuration
            var slotAfterStop = backend.QuerySlotState(handle, 1);
            Assert.Equal(1, slotAfterStop.IsActive);
            Assert.Equal(1, slotAfterStop.InBlendOutWindow);

            // One tick deactivates the slot naturally (elapsed crosses TotalDurationSeconds)
            backend.Tick(0.5f);
            var slotAfterFirstTick = backend.QuerySlotState(handle, 1);
            Assert.Equal(0, slotAfterFirstTick.IsActive);

            // Verify second tick does not advance a deactivated slot
            backend.Tick(0.5f);
            var slotAfterSecondTick = backend.QuerySlotState(handle, 1);
            Assert.Equal(0, slotAfterSecondTick.IsActive);
            Assert.Equal(0f, slotAfterSecondTick.ElapsedSeconds);
        }

        // ─── Notify firing tests ─────────────────────────────────────────────

        [Fact]
        public void Tick_FiresNotifyWhenElapsedCrossesTimeSeconds()
        {
            var (backend, handle) = CreateBackendWithEntity();
            // Reload_Rifle has MagOut notify at 0.5f
            var p = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in p);

            backend.Tick(0.6f);

            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[16];
            int n = backend.DrainNotifies(handle, buf);
            Assert.True(n >= 1);
            Assert.Contains(buf[..n].ToArray(), e => e.MarkerHash == MagOutHash);
        }

        [Fact]
        public void Notify_FiresExactlyOncePerPlay()
        {
            var (backend, handle) = CreateBackendWithEntity();
            var p = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in p);

            backend.Tick(1.0f);
            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[16];
            backend.DrainNotifies(handle, buf);

            // Tick again past notify time (montage is done after 1.0f with duration=1.0f, so replay needed)
            // Replay to test second play doesn't re-fire from old notify
            // After drain, tick more - slot already deactivated after first tick(1.0f)
            backend.Tick(0.1f);
            int n2 = backend.DrainNotifies(handle, buf);
            Assert.Equal(0, n2);
        }

        [Fact]
        public void PlayMontage_ResetsFiredNotifyMask()
        {
            var (backend, handle) = CreateBackendWithEntity();
            var p = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in p);
            backend.Tick(1.0f);
            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[16];
            backend.DrainNotifies(handle, buf);

            // Replay same montage
            backend.PlayMontageOnSlot(handle, in p);
            backend.Tick(0.6f);
            int n2 = backend.DrainNotifies(handle, buf);

            Assert.True(n2 >= 1, "Notify should fire again after replaying the montage");
        }

        // ─── Footstep cadence test ────────────────────────────────────────────

        [Fact]
        public void Footstep_EmitsAtStrideDistance()
        {
            // At 2 m/s, one stride (0.9m) takes 0.45s; use dt=0.46f to cross the threshold
            var (backend, handle) = CreateBackendWithEntity();
            backend.UpdateLocomotionInputs(handle, horizontalVelX: 2f, horizontalVelZ: 0f, verticalVelocity: 0f, isGrounded: true);

            backend.Tick(0.46f);

            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[16];
            int n = backend.DrainNotifies(handle, buf);
            Assert.True(n >= 1, "Expected at least one footstep notify");
            bool hasFootstep = false;
            for (int i = 0; i < n; i++)
            {
                if (buf[i].Kind == AnimNotifyCategory.Footstep)
                {
                    hasFootstep = true;
                    break;
                }
            }
            Assert.True(hasFootstep, "Expected a Footstep kind notify");
        }

        // ─── DrainNotifies buffer tests ──────────────────────────────────────

        [Fact]
        public void DrainNotifies_ReturnsUpToBufferSize()
        {
            // OFX-022: AdvanceFootsteps uses 'if' not 'while' — at most one footstep per tick.
            // At 2 m/s, stride = 0.9m; dt=0.46f -> 0.92m > 0.9m => one footstep per tick.
            // Need 3 separate ticks to accumulate 3 footsteps.
            var (backend, handle) = CreateBackendWithEntity();
            backend.UpdateLocomotionInputs(handle, 2f, 0f, 0f, true);

            backend.Tick(0.46f);
            backend.Tick(0.46f);
            backend.Tick(0.46f);

            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[5];
            int n = backend.DrainNotifies(handle, buf);
            Assert.Equal(3, n);
        }

        [Fact]
        public void DrainNotifies_HandlesSmallerDestBuffer()
        {
            // OFX-022: one footstep per tick; use 5 ticks to accumulate 5 footsteps,
            // drain with a buffer of 3 — expects exactly 3 (capped to buffer size).
            var (backend, handle) = CreateBackendWithEntity();
            backend.UpdateLocomotionInputs(handle, 2f, 0f, 0f, true);

            for (int i = 0; i < 5; i++)
                backend.Tick(0.46f);

            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[3];
            int n = backend.DrainNotifies(handle, buf);
            Assert.Equal(3, n);
        }

        // ─── OFX-003: FakeAnimBackendState ECS component ─────────────────────

        [Fact]
        public void SetEntityRepository_RegisterEntity_AddsEcsComponent()
        {
            // After SetEntityRepository + RegisterEntity, FakeAnimBackendState is readable
            // from the ECS repository on the corresponding entity (OFX-003).
            var dto = CreateTestDto();
            var baked = BakingUtils.BakeDef(dto);
            var classData = new Dictionary<long, CharacterAnimationBakedData> { [ClassId] = baked };
            var backend = new FakeAnimationBackend(classData);

            var repo = new EntityRepository();
            var entity = repo.CreateEntity();

            backend.SetEntityRepository(repo);
            backend.RegisterEntity((uint)entity.Index, ClassId);

            Assert.True(repo.HasComponent<FakeAnimBackendState>(entity),
                "FakeAnimBackendState component must exist after RegisterEntity");
        }

        [Fact]
        public void ResetWorld_RemovesEcsComponents()
        {
            // After ResetWorld, FakeAnimBackendState is removed from the entity (OFX-003).
            var dto = CreateTestDto();
            var baked = BakingUtils.BakeDef(dto);
            var classData = new Dictionary<long, CharacterAnimationBakedData> { [ClassId] = baked };
            var backend = new FakeAnimationBackend(classData);

            var repo = new EntityRepository();
            var entity = repo.CreateEntity();

            backend.SetEntityRepository(repo);
            backend.RegisterEntity((uint)entity.Index, ClassId);

            Assert.True(repo.HasComponent<FakeAnimBackendState>(entity));

            backend.ResetWorld();

            Assert.False(repo.HasComponent<FakeAnimBackendState>(entity),
                "FakeAnimBackendState component must be removed after ResetWorld");
        }

        // ─── OFX-004: StopMontageOnSlot blend-out ────────────────────────────

        [Fact]
        public void StopMontageOnSlot_WithBlendOut_SetsInBlendOutWindow_SlotStillActive()
        {
            // After StopMontageOnSlot with BlendOutTime > 0, slot is still active
            // with InBlendOutWindow == 1 (OFX-004, DD-Fake §4.2).
            var (backend, handle) = CreateBackendWithEntity();
            var play = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in play);
            backend.Tick(0.3f); // Advance some time

            var stop = new StopMontageParams { BlendOutTime = 0.2f };
            backend.StopMontageOnSlot(handle, in stop);

            var slot = backend.QuerySlotState(handle, 1);
            Assert.Equal(1, slot.IsActive);
            Assert.Equal(1, slot.InBlendOutWindow);
        }

        [Fact]
        public void StopMontageOnSlot_WithBlendOut_SlotCompletesNaturally()
        {
            // After stopping with BlendOutTime=0.2f, the slot completes after the blend-out
            // window elapses (OFX-004).
            var (backend, handle) = CreateBackendWithEntity();
            var play = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in play);
            backend.Tick(0.3f);

            var stop = new StopMontageParams { BlendOutTime = 0.2f };
            backend.StopMontageOnSlot(handle, in stop);

            // Tick past blend-out window
            backend.Tick(0.5f);

            var slot = backend.QuerySlotState(handle, 1);
            Assert.Equal(0, slot.IsActive);
        }

        // ─── OFX-005: BlendWeight computation ────────────────────────────────

        [Fact]
        public void BlendWeight_IsZero_BeforeBlendInCompletes()
        {
            // BlendWeight should be 0 (or ramping) before blend-in completes (OFX-005).
            var (backend, handle) = CreateBackendWithEntity();
            // BlendInTime defaults to montage DefaultBlendInTime = 0.1f
            var play = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f, BlendInTime = 0.2f };
            backend.PlayMontageOnSlot(handle, in play);

            // Before ANY tick, ElapsedSeconds=0 < BlendInTime=0.2f, so BlendWeight = 0/0.2 = 0
            var slotBefore = backend.QuerySlotState(handle, 1);
            Assert.Equal(0f, slotBefore.BlendWeight);
        }

        [Fact]
        public void BlendWeight_IsOne_DuringHoldPhase()
        {
            // During the hold phase (past blend-in, before blend-out), BlendWeight == 1.0 (OFX-005).
            var (backend, handle) = CreateBackendWithEntity();
            // BlendInTime=0.1f, TotalDuration=1.0f, DefaultBlendOut=0.2f
            var play = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f, BlendInTime = 0.1f };
            backend.PlayMontageOnSlot(handle, in play);

            // Tick to 0.5f: past blend-in (0.1f), before blend-out window (1.0-0.2=0.8f)
            backend.Tick(0.5f);

            var slot = backend.QuerySlotState(handle, 1);
            Assert.Equal(1f, slot.BlendWeight);
        }

        [Fact]
        public void BlendWeight_DecreasesUnderOne_DuringBlendOut()
        {
            // During blend-out, BlendWeight should be < 1 and > 0 (OFX-005).
            var (backend, handle) = CreateBackendWithEntity();
            // Reload_Rifle: Duration=1.0f, DefaultBlendOut=0.2f; blend-out window starts at 0.8f
            var play = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in play);

            // Tick to 0.9f: inside blend-out window (0.8..1.0f)
            backend.Tick(0.9f);

            var slot = backend.QuerySlotState(handle, 1);
            Assert.InRange(slot.BlendWeight, 0.01f, 0.99f);
        }

        // ─── OFX-022: AdvanceFootsteps stationary distance reset ─────────────

        [Fact]
        public void AdvanceFootsteps_StationaryEntity_ResetsDistanceAccumulation()
        {
            // When the entity becomes stationary, DistanceSinceLastFootstep must reset to 0
            // so the next step after movement starts fresh (OFX-022, DD-Fake §5).
            var (backend, handle) = CreateBackendWithEntity();

            // Walk for a partial stride
            backend.UpdateLocomotionInputs(handle, 1f, 0f, 0f, true); // 1 m/s
            backend.Tick(0.4f); // distance = 0.4f < 0.9f stride, no footstep emitted

            // Stop moving — should reset distance
            backend.UpdateLocomotionInputs(handle, 0f, 0f, 0f, true);
            backend.Tick(0.1f);

            // Move again at full speed: distance should start from 0, no ghost footstep
            backend.UpdateLocomotionInputs(handle, 2f, 0f, 0f, true);
            backend.Tick(0.1f); // 0.2m — not enough for a stride

            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[8];
            int n = backend.DrainNotifies(handle, buf);
            Assert.Equal(0, n);
        }

        // ─── OFX-023: Aim blend weight and stance transition tests ───────────

        [Fact]
        public void Tick_RampsAimBlendWeight()
        {
            // Aim blend weight should increase toward 1.0 over ticks after SetAimTargetPoint
            // (ANC-P1-06, OFX-023).
            var (backend, handle) = CreateBackendWithEntity();

            // Request aim with a blend-in time of 0.4f
            var aimParams = new LookAtPointParams
            {
                WorldPointX = 10f,
                WorldPointY = 0f,
                WorldPointZ = 0f,
                BlendInTime = 0.4f,
                Priority = 1,
            };
            backend.SetAimTargetPoint(handle, in aimParams);

            // After first tick (0.1f): weight should be 0.1/0.4 = 0.25
            backend.Tick(0.1f);
            var aim1 = backend.QueryAimState(handle);
            Assert.InRange(aim1.BlendWeight, 0.2f, 0.3f);

            // After second tick (0.1f more): weight should be ~0.5
            backend.Tick(0.1f);
            var aim2 = backend.QueryAimState(handle);
            Assert.True(aim2.BlendWeight > aim1.BlendWeight,
                "BlendWeight must increase with each tick during blend-in");

            // After enough ticks to complete blend-in: weight should reach 1.0
            backend.Tick(0.3f);
            var aimFull = backend.QueryAimState(handle);
            Assert.Equal(1f, aimFull.BlendWeight);
        }

        [Fact]
        public void Tick_CompletesStanceTransition()
        {
            // Stance transition should complete and commit CurrentStance to TargetStance
            // after the transition duration elapses (ANC-P1-06, OFX-023).
            var (backend, handle) = CreateBackendWithEntity();

            // Trigger a stance transition: Standing (0) -> Crouched (1), duration 0.3f
            backend.RequestStanceChange(handle, (byte)Components.StanceId.Crouched, 0.3f);

            var stanceBefore = backend.QueryStanceState(handle);
            Assert.Equal(1, stanceBefore.IsTransitioning);
            Assert.Equal((byte)Components.StanceId.Crouched, stanceBefore.TargetStance);

            // Tick past the transition duration
            backend.Tick(0.4f);

            var stanceAfter = backend.QueryStanceState(handle);
            Assert.Equal(0, stanceAfter.IsTransitioning);
            Assert.Equal((byte)Components.StanceId.Crouched, stanceAfter.CurrentStance);
            Assert.InRange(stanceAfter.TransitionProgress, 0f, 0.01f);
        }
    }
}
