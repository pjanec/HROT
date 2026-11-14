using System.Collections.Generic;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Hashing;

namespace Hrot.Animation.Network.Integration.Tests;

/// <summary>
/// Inline TKB test data for network integration scenarios (ANC-P8-04).
/// Mirrors the stage-1 TestData from Hrot.Animation.Integration.Tests to keep the
/// new project self-contained and free from the heavyweight Hrot.SimHost dependency.
/// </summary>
internal static class TestData
{
    /// <summary>Character class ID used across all network integration tests.</summary>
    public const long ClassId = 100L;

    /// <summary>Stable montage asset ID for the "Walk" animation.</summary>
    public static readonly int WalkMontageId = StableIdHasher.ComputeMontageAssetId("Walk");

    /// <summary>Stable montage asset ID for the "Run" animation.</summary>
    public static readonly int RunMontageId = StableIdHasher.ComputeMontageAssetId("Run");

    /// <summary>Walk montage duration in seconds (approximately 30 frames at 60 Hz).</summary>
    public const float WalkDurationSeconds = 0.5f;

    /// <summary>Run montage duration in seconds.</summary>
    public const float RunDurationSeconds = 0.4f;

    /// <summary>Stable hash for the "MagOut" notify marker.</summary>
    public static readonly uint MagOutMarkerHash = StableIdHasher.ComputeMarkerHash("MagOut");

    /// <summary>Stable hash for the "Footstep_Left" notify marker.</summary>
    public static readonly uint FootstepLeftMarkerHash = StableIdHasher.ComputeMarkerHash("Footstep_Left");

    /// <summary>Stable hash for the "Footstep_Right" notify marker.</summary>
    public static readonly uint FootstepRightMarkerHash = StableIdHasher.ComputeMarkerHash("Footstep_Right");

    /// <summary>
    /// Creates the minimal character animation descriptor used by network integration scenarios.
    /// Identical to the stage-1 version: Walk (3 footstep markers), Run (MagOut marker),
    /// Standing/Crouched stances, no transition montages.
    /// </summary>
    public static CharacterAnimationDefDto CreateCharacterDef()
    {
        return new CharacterAnimationDefDto
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
                },
                new SlotDefDto
                {
                    SlotId = 1,
                    Name = "FullBody",
                    BoneMask = new[] { "root" },
                    Mode = SlotCompositingMode.Override,
                    Priority = 100,
                },
            },
            Montages = new List<MontageDefDto>
            {
                new MontageDefDto
                {
                    Name = "Walk",
                    AssetRef = "Anims/Walk.clip",
                    Slot = 0,
                    DefaultBlendInTime = 0.1f,
                    DefaultBlendOutTime = 0.1f,
                    DurationSeconds = WalkDurationSeconds,
                    Sections = new[] { "Default" },
                    Notifies = new List<MontageNotifyRefDto>
                    {
                        new MontageNotifyRefDto { MarkerName = "Footstep_Left", TimeSeconds = 0.1f },
                        new MontageNotifyRefDto { MarkerName = "Footstep_Right", TimeSeconds = 0.25f },
                        new MontageNotifyRefDto { MarkerName = "Footstep_Left", TimeSeconds = 0.4f },
                    },
                    IsStanceTransition = false,
                },
                new MontageDefDto
                {
                    Name = "Run",
                    AssetRef = "Anims/Run.clip",
                    Slot = 0,
                    DefaultBlendInTime = 0.1f,
                    DefaultBlendOutTime = 0.1f,
                    DurationSeconds = RunDurationSeconds,
                    Sections = new[] { "Default" },
                    Notifies = new List<MontageNotifyRefDto>
                    {
                        new MontageNotifyRefDto { MarkerName = "MagOut", TimeSeconds = 0.2f },
                    },
                    IsStanceTransition = false,
                },
            },
            SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },
            StanceTransitions = new List<StanceTransitionDto>(),
            AimConfig = new AimConfigDto
            {
                MaxYawDegrees = 90f,
                MaxPitchDegrees = 70f,
                AimSourceBone = "head",
            },
            NotifyMarkers = new List<NotifyMarkerDefDto>
            {
                new NotifyMarkerDefDto
                    { Name = "MagOut", Hash = MagOutMarkerHash, Kind = AnimNotifyCategory.Generic },
                new NotifyMarkerDefDto
                    { Name = "Footstep_Left", Hash = FootstepLeftMarkerHash, Kind = AnimNotifyCategory.Footstep },
                new NotifyMarkerDefDto
                    { Name = "Footstep_Right", Hash = FootstepRightMarkerHash, Kind = AnimNotifyCategory.Footstep },
            },
        };
    }
}
