using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Hashing;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Validation;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// Phase 2 test suite covering DTOs, hashing, baking, and validation (ANC-P2-01 through ANC-P2-08).
    /// Tests verify DTO deserialization, stable ID hashing, baking algorithm, and validators.
    /// </summary>
    public class Phase2DescriptorTests
    {
        // ─────────────────────────────────────────────────────────────
        // ANC-P2-01: CharacterAnimationDefDto + nested DTOs
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void CharacterAnimationDefDto_CanBeInstantiated()
        {
            // Arrange & Act
            var dto = new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>
                {
                    new SlotDefDto
                    {
                        SlotId = 0,
                        Name = "Locomotion",
                        BoneMask = new[] { "root" },
                        Mode = SlotCompositingMode.Override,
                        Priority = 0,
                    }
                },
                Montages = new List<MontageDefDto>
                {
                    new MontageDefDto
                    {
                        Name = "Reload_Rifle",
                        AssetRef = "Animations/Reload.clip",
                        Slot = 0,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 3.4f,
                        Sections = new[] { "Start", "End" },
                        Notifies = new List<MontageNotifyRefDto>
                        {
                            new MontageNotifyRefDto
                            {
                                MarkerName = "MagOut",
                                TimeSeconds = 0.8f,
                                PayloadByte = 0,
                            }
                        },
                        UsesRootMotion = false,
                        IsStanceTransition = false,
                    }
                },
                SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new List<NotifyMarkerDefDto>
                {
                    new NotifyMarkerDefDto
                    {
                        Name = "MagOut",
                        Hash = 0xA1B2C3D4,
                        Kind = AnimNotifyCategory.Generic,
                    }
                },
            };

            // Assert
            Assert.NotNull(dto);
            Assert.Equal(1, dto.Slots.Count);
            Assert.Equal(1, dto.Montages.Count);
            Assert.Equal("Reload_Rifle", dto.Montages[0].Name);
            Assert.Null(dto.AimConfig);
        }

        [Fact]
        public void SlotDefDto_WithAdditiveMode()
        {
            // Arrange & Act
            var slot = new SlotDefDto
            {
                SlotId = 100,
                Name = "UpperBody",
                BoneMask = new[] { "spine", "head" },
                Mode = SlotCompositingMode.Additive,
                Priority = 100,
            };

            // Assert
            Assert.Equal(SlotCompositingMode.Additive, slot.Mode);
            Assert.Equal(100, slot.Priority);
        }

        [Fact]
        public void MontageNotifyRefDto_WithPayloads()
        {
            // Arrange & Act
            var notify = new MontageNotifyRefDto
            {
                MarkerName = "Footstep_Left",
                TimeSeconds = 0.9f,
                PayloadFloat = 1.5f,
                PayloadByte = 0,
            };

            // Assert
            Assert.Equal("Footstep_Left", notify.MarkerName);
            Assert.Equal(0.9f, notify.TimeSeconds);
            Assert.Equal(1.5f, notify.PayloadFloat);
        }

        // ─────────────────────────────────────────────────────────────
        // ANC-P2-02: Stable ID hashing (determinism, known vectors)
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void StableIdHasher_ComputeMontageAssetId_IsDeterministic()
        {
            // Arrange
            string montageName = "Reload_Rifle";

            // Act
            int id1 = StableIdHasher.ComputeMontageAssetId(montageName);
            int id2 = StableIdHasher.ComputeMontageAssetId(montageName);
            int id3 = StableIdHasher.ComputeMontageAssetId(montageName);

            // Assert
            Assert.Equal(id1, id2);
            Assert.Equal(id2, id3);
            Assert.True(id1 >= 0);  // Must be positive (masked to 31 bits)
        }

        [Fact]
        public void StableIdHasher_ComputeMontageAssetId_DifferentNamesProduceDifferentIds()
        {
            // Arrange
            string name1 = "Reload_Rifle";
            string name2 = "Reload_Pistol";

            // Act
            int id1 = StableIdHasher.ComputeMontageAssetId(name1);
            int id2 = StableIdHasher.ComputeMontageAssetId(name2);

            // Assert
            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public void StableIdHasher_ComputeMarkerHash_IsDeterministic()
        {
            // Arrange
            string markerName = "Footstep_Left";

            // Act
            uint hash1 = StableIdHasher.ComputeMarkerHash(markerName);
            uint hash2 = StableIdHasher.ComputeMarkerHash(markerName);

            // Assert
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void StableIdHasher_ComputeMarkerHash_DifferentNamesProduceDifferentHashes()
        {
            // Arrange
            string name1 = "Footstep_Left";
            string name2 = "Footstep_Right";

            // Act
            uint hash1 = StableIdHasher.ComputeMarkerHash(name1);
            uint hash2 = StableIdHasher.ComputeMarkerHash(name2);

            // Assert
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void StableIdHasher_ComputeMontageAssetId_IsPositive()
        {
            // Arrange & Act
            int id = StableIdHasher.ComputeMontageAssetId("TestMontage");

            // Assert
            Assert.True(id >= 0, "ID must be non-negative (31-bit positive int)");
            Assert.True(id <= 0x7FFFFFFF, "ID must fit in 31 bits");
        }

        // ─────────────────────────────────────────────────────────────
        // ANC-P2-05: Baking algorithm + BakeForTest
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void BakingUtils_BakeDef_BuildsMontageDict()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            var baked = BakingUtils.BakeDef(dto);

            // Assert
            Assert.NotEmpty(baked.MontageDict);
            Assert.True(baked.MontageDict.Count >= 2);  // At least Reload_Rifle and Vault_Low
        }

        [Fact]
        public void BakingUtils_BakeDef_PopulatesSupportedStances()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            var baked = BakingUtils.BakeDef(dto);

            // Assert
            Assert.NotEmpty(baked.SupportedStances);
            Assert.Contains((byte)StanceId.Standing, baked.SupportedStances);
            Assert.Contains((byte)StanceId.Crouched, baked.SupportedStances);
        }

        [Fact]
        public void BakingUtils_BakeDef_BuildsTransitionMap()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            var baked = BakingUtils.BakeDef(dto);

            // Assert
            Assert.NotEmpty(baked.TransitionMap);
            Assert.True(baked.TransitionMap.ContainsKey(((byte)StanceId.Standing, (byte)StanceId.Crouched)));
        }

        [Fact]
        public void BakingUtils_BakeDef_SortSlotsByPriority()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            var baked = BakingUtils.BakeDef(dto);

            // Assert
            Assert.NotEmpty(baked.Slots);
            // Verify slots are sorted by priority (ascending)
            for (int i = 1; i < baked.Slots.Count; i++)
            {
                Assert.True(baked.Slots[i - 1].Priority <= baked.Slots[i].Priority);
            }
        }

        [Fact]
        public void BakingUtils_BakeDef_SnapshotsAimConfig()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            var baked = BakingUtils.BakeDef(dto);

            // Assert
            Assert.NotNull(baked.AimSnapshot);
            Assert.Equal(90f, baked.AimSnapshot.MaxYawDegrees);
            Assert.Equal(70f, baked.AimSnapshot.MaxPitchDegrees);
        }

        [Fact]
        public void BakingUtils_BakeDef_WithoutAimConfig()
        {
            // Arrange
            var dto = new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto> { CreateSlot(0, "Locomotion", 0) },
                Montages = new List<MontageDefDto> { CreateMontage("Test") },
                SupportedStances = new[] { StanceId.Standing },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new List<NotifyMarkerDefDto>(),
            };

            // Act
            var baked = BakingUtils.BakeDef(dto);

            // Assert
            Assert.Null(baked.AimSnapshot);
        }


        // ─────────────────────────────────────────────────────────────
        // ANC-P2-06: Query API (GetPlayableMontages, GetAvailableMarkers, etc.)
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void BakedAnimationData_MontageInfo_Stores()
        {
            // Arrange & Act
            var montageInfo = new MontageInfo
            {
                Name = "Reload_Rifle",
                AssetRef = "clip",
                Slot = 200,
                Duration = 3.4f,
                DefaultBlendInTime = 0.1f,
                DefaultBlendOutTime = 0.2f,
                Sections = new[] { "Start", "End" },
                Notifies = new List<NotifyInfo>(),
                IsStanceTransition = false,
                UsesRootMotion = false,
            };

            // Assert
            Assert.Equal("Reload_Rifle", montageInfo.Name);
            Assert.Equal(200, montageInfo.Slot);
            Assert.False(montageInfo.IsStanceTransition);
        }

        // ─────────────────────────────────────────────────────────────
        // ANC-P2-07: Validators (ANIM006, ANIM007)
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void AnimationValidators_ValidateDto_RejectsInvalidTransitionMontage()
        {
            // Arrange
            var dto = new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto> { CreateSlot(0, "Locomotion", 0) },
                Montages = new List<MontageDefDto>
                {
                    CreateMontage("Reload_Rifle"),
                    CreateMontage("Idle", isStanceTransition: true),
                },
                SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>
                {
                    new StanceTransitionDto
                    {
                        From = StanceId.Standing,
                        To = StanceId.Crouched,
                        TransitionMontageName = "NonExistent_Transition",  // Invalid!
                        DefaultBlendTime = 0.3f,
                    }
                },
                AimConfig = null,
                NotifyMarkers = new List<NotifyMarkerDefDto>(),
            };

            // Act
            var messages = AnimationValidators.ValidateDto(dto);

            // Assert
            Assert.NotEmpty(messages);
            var anim006Error = messages.FirstOrDefault(m => m.RuleId == "ANIM006");
            Assert.NotNull(anim006Error);
            Assert.Equal(ValidationSeverity.Error, anim006Error.Severity);
        }

        [Fact]
        public void AnimationValidators_ValidateDto_RejectsInvalidMarker()
        {
            // Arrange
            var dto = new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto> { CreateSlot(0, "Locomotion", 0) },
                Montages = new List<MontageDefDto>
                {
                    new MontageDefDto
                    {
                        Name = "Reload_Rifle",
                        AssetRef = "clip",
                        Slot = 0,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 3.4f,
                        Sections = new[] { "Start" },
                        Notifies = new List<MontageNotifyRefDto>
                        {
                            new MontageNotifyRefDto
                            {
                                MarkerName = "NonExistent_Marker",  // Invalid!
                                TimeSeconds = 1.0f,
                            }
                        },
                        IsStanceTransition = false,
                    }
                },
                SupportedStances = new[] { StanceId.Standing },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new List<NotifyMarkerDefDto>
                {
                    new NotifyMarkerDefDto
                    {
                        Name = "ValidMarker",
                        Hash = 0x12345678,
                        Kind = AnimNotifyCategory.Generic,
                    }
                },
            };

            // Act
            var messages = AnimationValidators.ValidateDto(dto);

            // Assert
            Assert.NotEmpty(messages);
            var anim007Error = messages.FirstOrDefault(m => m.RuleId == "ANIM007");
            Assert.NotNull(anim007Error);
            Assert.Equal(ValidationSeverity.Error, anim007Error.Severity);
        }

        [Fact]
        public void AnimationValidators_ValidateDto_PassesValidDto()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            var messages = AnimationValidators.ValidateDto(dto);

            // Assert
            Assert.Empty(messages);
        }

        [Fact]
        public void AnimationValidators_MontageExists_ReturnsTrueForValidMontage()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            bool exists = AnimationValidators.MontageExists(dto, "Reload_Rifle");

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public void AnimationValidators_MontageExists_ReturnsFalseForInvalidMontage()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            bool exists = AnimationValidators.MontageExists(dto, "NonExistent");

            // Assert
            Assert.False(exists);
        }

        [Fact]
        public void AnimationValidators_StanceIsSupported_ReturnsTrueForSupportedStance()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            bool supported = AnimationValidators.StanceIsSupported(dto, (byte)StanceId.Standing);

            // Assert
            Assert.True(supported);
        }

        [Fact]
        public void AnimationValidators_SupportsAim_ReturnsTrueForSniperClass()
        {
            // Arrange
            var dto = CreateSniperDto();

            // Act
            bool supportsAim = AnimationValidators.SupportsAim(dto);

            // Assert
            Assert.True(supportsAim);
        }

        // ─────────────────────────────────────────────────────────────
        // Helper methods
        // ─────────────────────────────────────────────────────────────

        private CharacterAnimationDefDto CreateSniperDto()
        {
            return new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>
                {
                    CreateSlot((byte)0, "Locomotion", 0),
                    CreateSlot((byte)1, "FullBody", 100),
                    CreateSlot((byte)2, "UpperBody", 200),
                    CreateSlot((byte)3, "AimAdditive", 4),
                },
                Montages = new List<MontageDefDto>
                {
                    new MontageDefDto
                    {
                        Name = "Reload_Rifle",
                        AssetRef = "Animations/Reload.clip",
                        Slot = 2,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 3.4f,
                        Sections = new[] { "Start", "Insert", "Close" },
                        Notifies = new List<MontageNotifyRefDto>
                        {
                            new MontageNotifyRefDto { MarkerName = "MagOut", TimeSeconds = 0.8f },
                            new MontageNotifyRefDto { MarkerName = "MagIn", TimeSeconds = 2.1f }
                        },
                        IsStanceTransition = false,
                    },
                    new MontageDefDto
                    {
                        Name = "Vault_Low",
                        AssetRef = "Animations/Vault.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.15f,
                        DurationSeconds = 1.2f,
                        Sections = new[] { "Approach", "Vault", "Land" },
                        Notifies = new List<MontageNotifyRefDto>
                        {
                            new MontageNotifyRefDto { MarkerName = "Footstep_Left", TimeSeconds = 0.9f }
                        },
                        IsStanceTransition = false,
                    },
                    new MontageDefDto
                    {
                        Name = "Trans_StandToCrouch",
                        AssetRef = "Animations/Trans_StandToCrouch.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.1f,
                        DurationSeconds = 0.5f,
                        Sections = Array.Empty<string>(),
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = true,
                    },
                },
                SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>
                {
                    new StanceTransitionDto
                    {
                        From = StanceId.Standing,
                        To = StanceId.Crouched,
                        TransitionMontageName = "Trans_StandToCrouch",
                        DefaultBlendTime = 0.3f,
                    }
                },
                AimConfig = new AimConfigDto
                {
                    MaxYawDegrees = 90f,
                    MaxPitchDegrees = 70f,
                    AimSourceBone = "head",
                },
                NotifyMarkers = new List<NotifyMarkerDefDto>
                {
                    new NotifyMarkerDefDto { Name = "MagOut", Hash = 0xA1B2C3D4, Kind = AnimNotifyCategory.Generic },
                    new NotifyMarkerDefDto { Name = "MagIn", Hash = 0xB1C2D3E4, Kind = AnimNotifyCategory.Generic },
                    new NotifyMarkerDefDto { Name = "Footstep_Left", Hash = 0xC1D2E3F4, Kind = AnimNotifyCategory.Footstep },
                    new NotifyMarkerDefDto { Name = "Footstep_Right", Hash = 0xD1E2F3A4, Kind = AnimNotifyCategory.Footstep },
                },
            };
        }

        private SlotDefDto CreateSlot(byte slotId, string name, int priority)
        {
            return new SlotDefDto
            {
                SlotId = slotId,
                Name = name,
                BoneMask = new[] { "root" },
                Mode = SlotCompositingMode.Override,
                Priority = priority,
            };
        }

        private MontageDefDto CreateMontage(string name, bool isStanceTransition = false)
        {
            return new MontageDefDto
            {
                Name = name,
                AssetRef = $"Animations/{name}.clip",
                Slot = 0,
                DefaultBlendInTime = 0.1f,
                DefaultBlendOutTime = 0.2f,
                DurationSeconds = 2.0f,
                Sections = new[] { "Start", "End" },
                Notifies = new List<MontageNotifyRefDto>(),
                IsStanceTransition = isStanceTransition,
            };
        }
    }
}
