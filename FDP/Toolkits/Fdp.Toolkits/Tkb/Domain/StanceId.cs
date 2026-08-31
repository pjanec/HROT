using System;

// CE-145 (DONE, 2026-08-31): moved here from
// Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Components/ReplicatedComponents.cs so that
// CharacterAnimationDefDto -- a TKB descriptor DTO that references it -- could live beside the
// other TKB descriptor DTOs in this folder, which in turn lets Hrot.Core host the UrbanCombat
// TKB catalogue without referencing a character-animation subsystem. Renamed into this
// folder's namespace by CE-145.
// docs/DESIGN_Entity_Creation_Unification.md 3.3.
namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Enumeration of supported stance modes for character locomotion and posture.
    /// Maps to TKB animation descriptor stance definitions (DD-4 §3.2).
    /// </summary>
    [Serializable]
    public enum StanceId : byte
    {
        /// <summary>Standing upright (default).</summary>
        Standing = 0,

        /// <summary>Crouched / half-height.</summary>
        Crouched = 1,

        /// <summary>Prone / fully horizontal.</summary>
        Prone = 2,
    }
}
