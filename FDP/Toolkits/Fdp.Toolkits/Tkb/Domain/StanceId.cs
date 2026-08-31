using System;

// ⚠ NAMESPACE IS DELIBERATELY `Hrot.MuscleCharacter.Animation.Components`, not `Fdp.Toolkit.Tkb.Domain`.
//
// CE-145 (interim state, 2026-08-31): this type moved here from
// Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Components/ReplicatedComponents.cs so that
// CharacterAnimationDefDto — a TKB descriptor DTO that references it — could live beside the other
// TKB descriptor DTOs in this folder. That in turn lets Hrot.Core host the UrbanCombat TKB catalogue
// without Hrot.Core taking a reference on a character-animation subsystem.
//
// The namespace was PRESERVED so that all 53 consumer files keep compiling untouched (C# binds on
// namespace identity, not assembly). Renaming it to Fdp.Toolkit.Tkb.Domain is CE-145, deferred to a
// Windows/Visual-Studio session where the 6 affected Stride-tree files can actually be compiled.
// 📄 docs/DESIGN_Entity_Creation_Unification.md §3.3.
namespace Hrot.MuscleCharacter.Animation.Components
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
