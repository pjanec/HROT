using System;

namespace Hrot.Common.Actions
{
    // ⚠ One enum per file: the CycloneDDS code generator writes one IDL per source file, and IDL enumerators share one scope.

    /// <summary>
    /// ⭐ B-11 A3 (docs/UX/DESIGN_Action_Catalogue.md §8, Editing UX §3.13) — the run modes an action is OFFERED in; elsewhere it is hidden (it can never
    /// apply there — a greyed row is for "applies here, but not now / not on this station").
    /// </summary>
    [Flags]
    public enum ActionModes
    {
        InEdit = 1,
        InPreview = 2,
        InLive = 4,
        InReview = 8,
        Running = InPreview | InLive,
        Always = InEdit | InPreview | InLive | InReview,
    }
}
