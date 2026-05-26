using System;
using System.Collections.Generic;
using Xunit;
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
            var (backend, handle) = CreateBackendWithEntity();
            var play = new PlayMontageParams { MontageId = ReloadId, PlayRate = 1.0f };
            backend.PlayMontageOnSlot(handle, in play);

            var stop = new StopMontageParams { BlendOutTime = 0f };
            backend.StopMontageOnSlot(handle, in stop);

            var slotBeforeTick = backend.QuerySlotState(handle, 1);
            backend.Tick(0.5f);
            var slotAfterTick = backend.QuerySlotState(handle, 1);

            Assert.Equal(0, slotAfterTick.IsActive);
            Assert.Equal(slotBeforeTick.ElapsedSeconds, slotAfterTick.ElapsedSeconds);
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
            // Force 3 footsteps by ticking enough distance for 3 strides
            // At 2 m/s, each stride = 0.45s. 3 strides = 1.35s, use dt = 1.4f
            var (backend, handle) = CreateBackendWithEntity();
            backend.UpdateLocomotionInputs(handle, 2f, 0f, 0f, true);

            backend.Tick(1.4f);

            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[5];
            int n = backend.DrainNotifies(handle, buf);
            Assert.Equal(3, n);
        }

        [Fact]
        public void DrainNotifies_HandlesSmallerDestBuffer()
        {
            // Force 5 footsteps by ticking > 5 strides worth
            // At 2 m/s, 5 strides = 2.25s, use dt = 2.3f
            var (backend, handle) = CreateBackendWithEntity();
            backend.UpdateLocomotionInputs(handle, 2f, 0f, 0f, true);

            backend.Tick(2.3f);

            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[3];
            int n = backend.DrainNotifies(handle, buf);
            Assert.Equal(3, n);
        }
    }
}
