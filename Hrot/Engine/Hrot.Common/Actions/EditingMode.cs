namespace Hrot.Common.Actions
{
    // ⚠ One enum per file: the CycloneDDS code generator writes one IDL per source file, and IDL enumerators share one scope
    //   (ActionGroup.Edit vs EditingMode.Edit, EmbarkOffer.None vs ActionTarget.None collided).
    /// <summary>
    /// ⭐ B-11 A1 (design §3 row 9) — the one run state an action is offered in. ⚠ A1 derives it from what today's menus read (a recording shown
    /// ⇒ <see cref="Review"/>; orders offered ⇒ <see cref="Live"/>; else <see cref="Edit"/>); A3 derives it from the run-mode choice and tells
    /// <see cref="Preview"/> apart.
    /// </summary>
    public enum EditingMode { Edit, Preview, Live, Review }
}
