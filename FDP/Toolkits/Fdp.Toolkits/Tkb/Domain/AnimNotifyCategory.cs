using System;

// CE-145 (DONE, 2026-08-31): moved here from Hrot.MuscleCharacter.Animation.Contracts
// because NotifyMarkerDefDto (in CharacterAnimationDefDto.cs, this folder) requires it,
// and renamed into this folder's namespace.
// docs/DESIGN_Entity_Creation_Unification.md 3.3.
namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Canonical event category enumeration for animation notifies, matching DD-3 §2.
    /// Each value maps to a distinct lifecycle or functional notify event.
    /// All values fit in a single byte.
    /// </summary>
    [Serializable]
    public enum AnimNotifyCategory : byte
    {
        /// <summary>Generic user-defined or unclassified marker event.</summary>
        Generic = 0,

        /// <summary>Footstep impact event emitted during locomotion.</summary>
        Footstep = 1,

        /// <summary>Hit window / melee-window opened on a character.</summary>
        HitWindowOpened = 2,

        /// <summary>Hit window / melee-window closed on a character.</summary>
        HitWindowClosed = 3,
    }
}
