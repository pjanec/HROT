namespace Hrot.Common.Actions
{
    // ⚠ One enum per file: the CycloneDDS code generator writes one IDL per source file, and IDL enumerators share one scope.

    /// <summary>
    /// ⭐ B-11 A7 (docs/UX/DESIGN_Action_Catalogue.md §2.1 <c>Modes</c>) — what a station is shown of an action or a Details tab right now: it is
    /// <see cref="Offered"/>, shown <see cref="Greyed"/> with a reason (it applies here, not now or not on this station), or <see cref="Hidden"/>
    /// (it can never apply in this mode).
    /// </summary>
    public enum ActionOffer : byte
    {
        Offered = 0,
        Greyed = 1,
        Hidden = 2,
    }
}
