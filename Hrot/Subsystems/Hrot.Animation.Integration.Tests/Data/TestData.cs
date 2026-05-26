using System.Collections.Generic;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Hashing;

namespace Hrot.Animation.Integration.Tests;

/// <summary>
/// Inline TKB test data for integration scenarios (ANC-P7-03).
/// Provides a minimal character definition with Walk and Run montages, two stances.
/// Used by AnimationIntegrationFixture and all scenario tests.
/// </summary>
public static class TestData
{
    /// <summary>
    /// Character class ID for integration tests.
    /// Intentionally different from Phase3SystemTests (42L) to avoid any cross-test interference.
    /// </summary>
    public const long ClassId = 100L;

    /// <summary>Stable montage asset ID for the "Walk" animation.</summary>
    public static readonly int WalkMontageId = StableIdHasher.ComputeMontageAssetId("Walk");

    /// <summary>Stable montage asset ID for the "Run" animation.</summary>
    public static readonly int RunMontageId = StableIdHasher.ComputeMontageAssetId("Run");

    /// <summary>
    /// Walk montage duration in seconds (approximately 30 frames at 60 Hz).
    /// Short enough to complete within the 100-frame budget used by Scenario 1.
    /// </summary>
    public const float WalkDurationSeconds = 0.5f;

    /// <summary>Run montage duration in seconds.</summary>
    public const float RunDurationSeconds = 0.4f;

    /// <summary>Stable hash for the "MagOut" notify marker (ANC-P7-05).</summary>
    public static readonly uint MagOutMarkerHash = StableIdHasher.ComputeMarkerHash("MagOut");

    /// <summary>Stable hash for the "Footstep_Left" notify marker (ANC-P7-10).</summary>
    public static readonly uint FootstepLeftMarkerHash = StableIdHasher.ComputeMarkerHash("Footstep_Left");

    /// <summary>Stable hash for the "Footstep_Right" notify marker (ANC-P7-10).</summary>
    public static readonly uint FootstepRightMarkerHash = StableIdHasher.ComputeMarkerHash("Footstep_Right");

    /// <summary>
    /// Creates the minimal character animation descriptor used by integration scenarios.
    ///
    /// Layout:
    ///   Slots  : 0=Locomotion (priority 0), 1=FullBody (priority 100)
    ///   Montages: Walk (slot 0, with footstep markers), Run (slot 0)
    ///   Stances : Standing, Crouched (no transition montages needed for stage-1 tests)
    ///   Aim     : 90 deg yaw / 70 deg pitch on "head" bone
    ///   Markers : MagOut (generic), Footstep_Left, Footstep_Right
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
                        // Footstep markers at approximate cadence: ~3 footsteps per 0.5s walk
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
                        // MagOut marker for Scenario 2 test (placed in Run for variety)
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
                new NotifyMarkerDefDto { Name = "MagOut", Hash = MagOutMarkerHash, Kind = AnimNotifyCategory.Generic },
                new NotifyMarkerDefDto { Name = "Footstep_Left", Hash = FootstepLeftMarkerHash, Kind = AnimNotifyCategory.Footstep },
                new NotifyMarkerDefDto { Name = "Footstep_Right", Hash = FootstepRightMarkerHash, Kind = AnimNotifyCategory.Footstep },
            },
        };
    }
}
