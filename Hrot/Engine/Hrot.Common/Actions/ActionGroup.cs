namespace Hrot.Common.Actions
{
    // ⚠ One enum per file: the CycloneDDS code generator writes one IDL per source file, and IDL enumerators share one scope
    //   (ActionGroup.Edit vs EditingMode.Edit, EmbarkOffer.None vs ActionTarget.None collided).
    /// <summary>
    /// ⭐ B-11 A1 (docs/UX/DESIGN_Action_Catalogue.md §6) — the fixed order of a menu's groups. A separator stands between two non-empty groups,
    /// never anywhere else (📐 measured: that rule reproduces every menu <c>MenuJsonFor</c> built by hand).
    /// </summary>
    public enum ActionGroup { Orders, Embark, View, Edit, Debug, Destructive, Run, Tool }   // ⭐ A6 — Run: the run-mode moves (first in every mode); ⭐ B-17 P2 — Tool: the armed tool's own menu (Finish · Remove last point · Cancel), alone in its menu
}
