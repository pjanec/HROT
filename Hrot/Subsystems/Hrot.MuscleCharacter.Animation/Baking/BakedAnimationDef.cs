using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Hashing;

namespace Hrot.MuscleCharacter.Animation.Baking
{
    /// <summary>
    /// Runtime information for one montage (DD-4 §4).
    /// Baked from MontageDefDto for fast runtime lookups.
    /// </summary>
    public sealed class MontageInfo
    {
        /// <summary>Stable string name from the DTO.</summary>
        public required string Name { get; init; }

        /// <summary>Asset reference (backend-specific).</summary>
        public required string AssetRef { get; init; }

        /// <summary>Slot ID this montage plays on.</summary>
        public required byte Slot { get; init; }

        /// <summary>Total duration in seconds.</summary>
        public required float Duration { get; init; }

        /// <summary>Default blend-in time.</summary>
        public required float DefaultBlendInTime { get; init; }

        /// <summary>Default blend-out time.</summary>
        public required float DefaultBlendOutTime { get; init; }

        /// <summary>Section names (indices match StartSectionIndex in PlayMontageParams).</summary>
        public required IReadOnlyList<string> Sections { get; init; }

        /// <summary>Notify markers on this montage.</summary>
        public required List<NotifyInfo> Notifies { get; init; }

        /// <summary>If true, this montage is for stance transitions only.</summary>
        public bool IsStanceTransition { get; init; }

        /// <summary>If true, this montage drives root-motion.</summary>
        public bool UsesRootMotion { get; init; }
    }

    /// <summary>
    /// Runtime information for one notify marker (DD-4 §4).
    /// Baked from MontageNotifyRefDto for fast runtime lookups.
    /// </summary>
    public sealed class NotifyInfo
    {
        /// <summary>Stable string name of the marker.</summary>
        public required string Name { get; init; }

        /// <summary>Marker hash (FNV1a32).</summary>
        public required uint MarkerHash { get; init; }

        /// <summary>Time in seconds from montage start.</summary>
        public required float TimeSeconds { get; init; }

        /// <summary>Marker category (Generic, Footstep, HitWindow*, etc.).</summary>
        public required AnimNotifyCategory Kind { get; set; }

        /// <summary>Optional payload float.</summary>
        public float PayloadFloat { get; init; }

        /// <summary>Optional payload byte.</summary>
        public byte PayloadByte { get; init; }
    }

    /// <summary>
    /// Slot runtime information (DD-4 §4).
    /// </summary>
    public sealed class SlotInfo
    {
        /// <summary>Slot ID.</summary>
        public required byte SlotId { get; init; }

        /// <summary>Human-readable name.</summary>
        public required string Name { get; init; }

        /// <summary>Bones in the mask.</summary>
        public required IReadOnlyList<string> BoneMask { get; init; }

        /// <summary>Override or Additive compositing.</summary>
        public required SlotCompositingMode Mode { get; init; }

        /// <summary>Priority for conflict arbitration.</summary>
        public required int Priority { get; init; }
    }

    /// <summary>
    /// Snapshot of aim configuration (DD-4 §4).
    /// </summary>
    public sealed class AimSnapshot
    {
        /// <summary>Maximum aim yaw range (degrees).</summary>
        public required float MaxYawDegrees { get; init; }

        /// <summary>Maximum aim pitch range (degrees).</summary>
        public required float MaxPitchDegrees { get; init; }

        /// <summary>Bone driving the aim direction.</summary>
        public required string AimSourceBone { get; init; }
    }

    /// <summary>
    /// Baked animation data for one character class (DD-4 §4.1).
    /// Built once per class and shared across all entities of that class.
    /// Contains all lookup tables needed at runtime.
    /// </summary>
    public sealed class CharacterAnimationBakedData
    {
        /// <summary>
        /// Montages keyed by MontageAssetId (from StableIdHasher.ComputeMontageAssetId).
        /// </summary>
        public required Dictionary<int, MontageInfo> MontageDict { get; init; }

        /// <summary>
        /// Supported stance IDs for this class.
        /// </summary>
        public required HashSet<byte> SupportedStances { get; init; }

        /// <summary>
        /// Stance transitions keyed by (fromStanceId, toStanceId) tuple.
        /// Value is the transition montage name (must exist in MontageDict).
        /// </summary>
        public required Dictionary<(byte, byte), string> TransitionMap { get; init; }

        /// <summary>
        /// Slots sorted by priority (ascending, per DD-4 §4).
        /// </summary>
        public required List<SlotInfo> Slots { get; init; }

        /// <summary>
        /// Aim configuration snapshot (null if no aim support).
        /// </summary>
        public AimSnapshot? AimSnapshot { get; init; }
    }

    /// <summary>
    /// Baking algorithm and utilities for converting DTOs to runtime structures (DD-4 §4, DD-4 §8.3).
    /// </summary>
    public static class BakingUtils
    {
        /// <summary>
        /// Parse a stance ID from a string name ("Standing", "Crouched", "Prone").
        /// Returns the corresponding StanceId enum value.
        /// </summary>
        private static byte ParseStanceId(StanceId stanceId)
        {
            return (byte)stanceId;
        }

        /// <summary>
        /// Bake a DTO into runtime baked data.
        /// Builds montage dict, stance set, transition table, slot table (sorted by priority), aim config.
        /// Per DD-4 §4, this is the canonical baking algorithm.
        /// </summary>
        /// <param name="dto">The animation descriptor DTO.</param>
        /// <returns>Baked runtime data for fast lookups.</returns>
        public static CharacterAnimationBakedData BakeDef(CharacterAnimationDefDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            var montageDict = new Dictionary<int, MontageInfo>();
            var supportedStances = new HashSet<byte>();
            var transitionMap = new Dictionary<(byte, byte), string>();
            var slots = new List<SlotInfo>();

            // 1. Populate montage dict with stable IDs
            foreach (var montageDef in dto.Montages)
            {
                int assetId = StableIdHasher.ComputeMontageAssetId(montageDef.Name);
                montageDict[assetId] = new MontageInfo
                {
                    Name = montageDef.Name,
                    AssetRef = montageDef.AssetRef,
                    Slot = montageDef.Slot,
                    Duration = montageDef.DurationSeconds,
                    DefaultBlendInTime = montageDef.DefaultBlendInTime,
                    DefaultBlendOutTime = montageDef.DefaultBlendOutTime,
                    Sections = montageDef.Sections,
                    UsesRootMotion = montageDef.UsesRootMotion,
                    IsStanceTransition = montageDef.IsStanceTransition,
                    Notifies = montageDef.Notifies.Select(n => new NotifyInfo
                    {
                        Name = n.MarkerName,
                        MarkerHash = StableIdHasher.ComputeMarkerHash(n.MarkerName),
                        TimeSeconds = n.TimeSeconds,
                        PayloadFloat = n.PayloadFloat,
                        PayloadByte = n.PayloadByte,
                        // Kind will be filled below
                        Kind = AnimNotifyCategory.Generic,
                    }).ToList(),
                };
            }

            // 2. Fill in marker kinds from the NotifyMarkers registry.
            //    DEBT D-13: NotifyMarkerDefDto carries both `Kind` and `Hash`. The
            //    baker computes the *runtime* hash from the name (see line above);
            //    the DTO's `Hash` field is purely **informational** (editor display
            //    of the precomputed hash, when the import pipeline includes one).
            //    The runtime never trusts it — `StableIdHasher.ComputeMarkerHash(name)`
            //    is authoritative for `AnimNotifyEvent.MarkerHash` routing. This
            //    resolves the D-13 question ("is the field dead or should be
            //    respected") in favour of "informational, kept for editor UX".
            var markerDict = new Dictionary<string, AnimNotifyCategory>();
            foreach (var marker in dto.NotifyMarkers)
            {
                markerDict[marker.Name] = marker.Kind;
            }

            // Update notifies with correct kinds
            foreach (var montage in montageDict.Values)
            {
                foreach (var notify in montage.Notifies)
                {
                    if (markerDict.TryGetValue(notify.Name, out var kind))
                    {
                        notify.Kind = kind;
                    }
                }
            }

            // 3. Build stance set and transition map
            foreach (var trans in dto.StanceTransitions)
            {
                byte fromId = ParseStanceId(trans.From);
                byte toId = ParseStanceId(trans.To);
                supportedStances.Add(fromId);
                supportedStances.Add(toId);
                transitionMap[(fromId, toId)] = trans.TransitionMontageName;
            }

            // Add any stances from SupportedStances that weren't in transitions
            foreach (var stanceId in dto.SupportedStances)
            {
                supportedStances.Add((byte)stanceId);
            }

            // 4. Sort slots by priority (ascending)
            slots = dto.Slots
                .OrderBy(s => s.Priority)
                .Select(s => new SlotInfo
                {
                    SlotId = s.SlotId,
                    Name = s.Name,
                    BoneMask = s.BoneMask,
                    Mode = s.Mode,
                    Priority = s.Priority,
                })
                .ToList();

            // 5. Snapshot aim config (if present)
            var aimSnapshot = dto.AimConfig != null ? new AimSnapshot
            {
                MaxYawDegrees = dto.AimConfig.MaxYawDegrees,
                MaxPitchDegrees = dto.AimConfig.MaxPitchDegrees,
                AimSourceBone = dto.AimConfig.AimSourceBone,
            } : null;

            return new CharacterAnimationBakedData
            {
                MontageDict = montageDict,
                SupportedStances = supportedStances,
                TransitionMap = transitionMap,
                Slots = slots,
                AimSnapshot = aimSnapshot,
            };
        }

        /// <summary>
        /// Public test API: Bake a DTO directly without caching or registration.
        /// Exposed via [InternalsVisibleTo("Hrot.Animation.Integration.Tests")].
        /// Used by integration tests to verify baking correctness.
        /// </summary>
        /// <param name="dto">The animation descriptor DTO.</param>
        /// <returns>Baked runtime data, equivalent to production baking.</returns>
        internal static CharacterAnimationBakedData BakeForTest(CharacterAnimationDefDto dto)
        {
            return BakeDef(dto);
        }
    }
}
