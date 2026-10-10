namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public enum CoordinateSpace : byte
    {
        World       = 0,
        Screen      = 1,
        EntityLocal = 2,
        /// <summary>
        /// ⭐ CE-1033 S5b (docs/DESIGN_Map_3D_Mode.md §3.6, M14) — drawn INTO an entity's card: a small screen-space canvas above
        /// the entity, created because a primitive targets it. <c>AnchorIndex</c> = the entity's network id; <c>ZIndex</c> = the
        /// row (0 = card-wide); coordinates are card-local PIXELS, <c>SizeMode.ScreenPercent</c> = fractions of the card.
        /// ⚠ A terminal that does not know this value must SKIP the primitive, never draw it as world text.
        /// </summary>
        EntityCard = 3
    }
}
