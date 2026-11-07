namespace Hrot.Common.Actions
{
    // ⚠ One enum per file: the CycloneDDS code generator writes one IDL per source file, and IDL enumerators share one scope
    //   (ActionGroup.Edit vs EditingMode.Edit, EmbarkOffer.None vs ActionTarget.None collided).
    /// <summary>⭐ CE-1230 — the embark row a unit's menu carries.</summary>
    public enum EmbarkOffer : byte { None = 0, Embark = 1, Disembark = 2 }
}
