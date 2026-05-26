using System;
using System.Collections.Generic;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Descriptors;

namespace Hrot.Editor.AiShared.Catalog
{
    /// <summary>
    /// Editor query API for animation TKB descriptors (DD-4 §5, DD-4 §9.6).
    /// Provides design-time access to animation metadata for Blueprint editor UI support,
    /// picker filtering, and validation.
    /// </summary>
    public interface IAnimationTkbQueries
    {
        /// <summary>
        /// All montages available to this entity class, excluding stance-transition montages.
        /// Stance-transition montages are hidden from the general PlayMontage picker
        /// (they're only accessible via StanceTransitionDto references).
        /// </summary>
        /// <param name="entityClass">Entity class name or identifier.</param>
        /// <returns>List of playable montages (never null; may be empty).</returns>
        IReadOnlyList<MontageDefDto> GetPlayableMontages(string entityClass);

        /// <summary>
        /// Look up a montage by name. Returns null if not in the class's def.
        /// </summary>
        /// <param name="entityClass">Entity class name or identifier.</param>
        /// <param name="montageName">Montage name to find.</param>
        /// <returns>Montage definition or null if not found.</returns>
        MontageDefDto? GetMontage(string entityClass, string montageName);

        /// <summary>
        /// Stances supported by this class.
        /// </summary>
        /// <param name="entityClass">Entity class name or identifier.</param>
        /// <returns>List of StanceId values (never null; may be empty).</returns>
        IReadOnlyList<StanceId> GetSupportedStances(string entityClass);

        /// <summary>
        /// Whether the class supports aim/look-at overlay.
        /// </summary>
        /// <param name="entityClass">Entity class name or identifier.</param>
        /// <returns>True if AimConfig is declared; false otherwise.</returns>
        bool SupportsAim(string entityClass);

        /// <summary>
        /// All notify markers usable by this class (union over all its montages' Notifies).
        /// Used by WhenNode's AnimNotifyEvent filter UI to populate the marker dropdown.
        /// </summary>
        /// <param name="entityClass">Entity class name or identifier.</param>
        /// <returns>List of marker definitions (never null; may be empty).</returns>
        IReadOnlyList<NotifyMarkerDefDto> GetAvailableMarkers(string entityClass);

        /// <summary>
        /// Reverse lookup hash → name for editor display and runtime resolution.
        /// </summary>
        /// <param name="entityClass">Entity class name or identifier.</param>
        /// <param name="hash">Marker hash to resolve.</param>
        /// <returns>Marker name if found; null otherwise.</returns>
        string? GetMarkerName(string entityClass, uint hash);

        /// <summary>
        /// Resolve a montage name to its runtime MontageAssetId.
        /// Used by the Blueprint compiler when generating PlayMontageParams from a montage-picker selection.
        /// </summary>
        /// <param name="entityClass">Entity class name or identifier.</param>
        /// <param name="montageName">Montage name to resolve.</param>
        /// <returns>Stable montage ID for the named montage.</returns>
        /// <exception cref="KeyNotFoundException">If montage not found in the class.</exception>
        int ResolveMontageId(string entityClass, string montageName);
    }
}
