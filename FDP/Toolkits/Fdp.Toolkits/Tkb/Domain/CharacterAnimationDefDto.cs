using System;
using System.Collections.Generic;
using Fdp.Toolkit.Tkb.Attributes;

// TKB DESCRIPTOR DTOs -- attached via TkbTemplate.AddDescriptor(), exactly like
// SensorCapabilitiesDto and VehicleParametersDto next door.
//
// CE-145 (DONE, 2026-08-31): these types moved here from
// Hrot/Subsystems/Hrot.MuscleCharacter.Animation/ and now carry this folder's namespace.
// The move let Hrot.Core host the shared UrbanCombat TKB catalogue, whose
// BuildMannequinAnimationDef() returns CharacterAnimationDefDto -- Hrot.Core must not
// reference a character-animation subsystem. The namespace was held at the old value for
// one commit so consumers compiled untouched; CE-145 completed the rename on Windows,
// where the Stride-tree consumers can actually be built.
// docs/DESIGN_Entity_Creation_Unification.md 3.3.
namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Enumeration of slot blending/compositing modes (DD-4 §2).
    /// Override replaces previous animations; Additive blends on top.
    /// </summary>
    [Serializable]
    public enum SlotCompositingMode : byte
    {
        /// <summary>Replace any previous animation on this slot.</summary>
        Override = 0,

        /// <summary>Blend additively on top of lower-priority slots.</summary>
        Additive = 1,
    }

    /// <summary>
    /// Slot definition for animation system architecture (DD-4 §2).
    /// Defines the bone mask, blending mode, and priority for one animation layer.
    /// </summary>
    [Serializable]
    public sealed record SlotDefDto
    {
        /// <summary>
        /// Stable byte ID (0..255) identifying this slot within the character class.
        /// Standard convention: Locomotion=0, FullBody=100, UpperBody=200, AimAdditive=400.
        /// </summary>
        public required byte SlotId { get; init; }

        /// <summary>
        /// Human-readable name for editor display ("FullBody", "UpperBody", etc.).
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Bones included in this slot's blend mask. Bone names match the skeleton
        /// hierarchy as exported from UE (e.g., "root", "spine", "head").
        /// </summary>
        public required IReadOnlyList<string> BoneMask { get; init; }

        /// <summary>
        /// Compositing mode: Override or Additive.
        /// </summary>
        public required SlotCompositingMode Mode { get; init; }

        /// <summary>
        /// Priority — higher wins on shared bones. Used for slot composition arbitration.
        /// </summary>
        public required int Priority { get; init; }
    }

    /// <summary>
    /// Marker reference on a montage (DD-4 §2).
    /// Identifies a notify marker that fires during playback, carrying optional payload.
    /// </summary>
    [Serializable]
    public sealed record MontageNotifyRefDto
    {
        /// <summary>
        /// The marker's stable name (must appear in CharacterAnimationDefDto.NotifyMarkers).
        /// </summary>
        public required string MarkerName { get; init; }

        /// <summary>
        /// Time in seconds from montage start when the marker fires.
        /// Informational; the backend evaluates this at runtime.
        /// </summary>
        public required float TimeSeconds { get; init; }

        /// <summary>
        /// Optional payload float (e.g., damage multiplier, footstep volume).
        /// </summary>
        public float PayloadFloat { get; init; }

        /// <summary>
        /// Optional payload byte (e.g., footstep foot index, hit-window ID).
        /// </summary>
        public byte PayloadByte { get; init; }
    }

    /// <summary>
    /// Montage definition for animation system (DD-4 §2).
    /// Specifies one playable animation clip with its metadata and notifies.
    /// </summary>
    [Serializable]
    public sealed record MontageDefDto
    {
        /// <summary>
        /// Stable string name. Hashed to MontageAssetId (int) for runtime.
        /// Must be unique within the character class.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Asset path or reference understood by the active backend
        /// (Stride: path to the imported AnimationClip; future proprietary
        /// backend: its own reference form).
        /// </summary>
        public required string AssetRef { get; init; }

        /// <summary>
        /// Which slot this montage plays on. Must reference a SlotId declared in Slots.
        /// </summary>
        public required byte Slot { get; init; }

        /// <summary>
        /// Default blend-in time, used when a PlayMontage command doesn't override.
        /// Time in seconds.
        /// </summary>
        public required float DefaultBlendInTime { get; init; }

        /// <summary>
        /// Default blend-out time. Time in seconds.
        /// </summary>
        public required float DefaultBlendOutTime { get; init; }

        /// <summary>
        /// Total montage duration in seconds (informational; editor uses for display,
        /// runtime queries the backend for actual playback).
        /// </summary>
        public required float DurationSeconds { get; init; }

        /// <summary>
        /// Section names in order. Index in this list = section index used in
        /// PlayMontageParams.StartSectionIndex.
        /// </summary>
        public required IReadOnlyList<string> Sections { get; init; }

        /// <summary>
        /// Markers carried on this montage that will fire as notify events at runtime.
        /// Used by the editor to populate WhenNode's Event Fired filter dropdown.
        /// </summary>
        public required IReadOnlyList<MontageNotifyRefDto> Notifies { get; init; }

        /// <summary>
        /// If true, this montage drives root-motion. Future-use flag;
        /// not yet read by DD-1's runtime.
        /// </summary>
        public bool UsesRootMotion { get; init; }

        /// <summary>
        /// If true, this montage is for stance transitions only and not exposed
        /// in the Blueprint editor's general PlayMontage picker.
        /// StanceTransitionDto references it directly.
        /// </summary>
        public bool IsStanceTransition { get; init; }
    }

    /// <summary>
    /// Stance transition configuration (DD-4 §2).
    /// Maps a pair of stances to a transition montage that animates between them.
    /// </summary>
    [Serializable]
    public sealed record StanceTransitionDto
    {
        /// <summary>
        /// Source stance ID.
        /// </summary>
        public required StanceId From { get; init; }

        /// <summary>
        /// Target stance ID.
        /// </summary>
        public required StanceId To { get; init; }

        /// <summary>
        /// Name of the montage (must appear in Montages with IsStanceTransition = true)
        /// that plays this transition.
        /// </summary>
        public required string TransitionMontageName { get; init; }

        /// <summary>
        /// Default blend time for this transition. Time in seconds.
        /// </summary>
        public required float DefaultBlendTime { get; init; }
    }

    /// <summary>
    /// Aim/look-at configuration (DD-4 §2).
    /// Specifies whether and how the character supports aim-offset.
    /// </summary>
    [Serializable]
    public sealed record AimConfigDto
    {
        /// <summary>
        /// Maximum aim yaw range relative to character facing (degrees).
        /// </summary>
        public required float MaxYawDegrees { get; init; }

        /// <summary>
        /// Maximum aim pitch range (degrees, up/down symmetric).
        /// </summary>
        public required float MaxPitchDegrees { get; init; }

        /// <summary>
        /// Bone driving the aim direction (typically head or neck).
        /// </summary>
        public required string AimSourceBone { get; init; }
    }

    /// <summary>
    /// Notify marker definition (DD-4 §2, §3.4).
    /// Registry entry for one marker that can appear on montages.
    /// </summary>
    [Serializable]
    public sealed record NotifyMarkerDefDto
    {
        /// <summary>
        /// Stable marker name as authored in UE.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Hash computed at import (via FNV1a32); stored here so editor doesn't recompute.
        /// See DD-4 §3.4 for hashing convention.
        /// </summary>
        public required uint Hash { get; init; }

        /// <summary>
        /// Discriminates between generic markers and typed notifies.
        /// Drives which FdpEventBus event the Muscle publishes (DD-3 maps these to typed events;
        /// generic markers go to AnimNotifyEvent).
        /// Uses the canonical AnimNotifyCategory enum declared in DD-3 §2.
        /// </summary>
        public required AnimNotifyCategory Kind { get; init; }
    }

    /// <summary>
    /// Top-level animation descriptor for a character class (DD-4 §2).
    /// Defines all animation data (slots, montages, stances, notifies) for one humanoid character.
    /// Deserialized from TKB JSON files and baked into runtime structures.
    /// </summary>
    [TkbDescriptor("Anim.CharacterDef")]
    [Serializable]
    public sealed record CharacterAnimationDefDto
    {
        /// <summary>
        /// Slot definitions for this character class. Determines which slots exist
        /// in AnimationExecutorState and how the backend composes them.
        /// </summary>
        public required IReadOnlyList<SlotDefDto> Slots { get; init; }

        /// <summary>
        /// Montages this character class can play. The Blueprint editor's montage-picker
        /// dropdown is filtered to this list (excluding IsStanceTransition ones).
        /// </summary>
        public required IReadOnlyList<MontageDefDto> Montages { get; init; }

        /// <summary>
        /// Stances this character class supports (subset of the universal StanceId enum).
        /// Stance pickers in the editor are filtered to this.
        /// </summary>
        public required IReadOnlyList<StanceId> SupportedStances { get; init; }

        /// <summary>
        /// Stance transition table: which montage drives transitions between each stance pair.
        /// Missing entries mean the transition is direct (snap, no blend).
        /// Used by StanceTransitionSystem (DD-1 §9).
        /// </summary>
        public required IReadOnlyList<StanceTransitionDto> StanceTransitions { get; init; }

        /// <summary>
        /// Aim/look-at configuration. Null/absent means this character class
        /// doesn't support aim-offset (LookAtChannel commands will fail
        /// with CanAim capability check).
        /// </summary>
        public AimConfigDto? AimConfig { get; init; }

        /// <summary>
        /// Notify marker registry — maps marker names authored on montages to stable hashes
        /// used in AnimNotifyEvent.MarkerHash. Populated by the asset import pipeline;
        /// baked here for editor display.
        /// </summary>
        public required IReadOnlyList<NotifyMarkerDefDto> NotifyMarkers { get; init; }
    }
}
