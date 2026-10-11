namespace GizmoMap.Presentation.Shapes
{
    [System.Flags]
    public enum EntityShapeCondition : uint
    {
        None = 0u,
        Damaged = 1u,
        Immobile = 2u,

        /// <summary>⭐ W2 (Hrot docs/DESIGN_Visual_Effects.md §7 VE-O) — a wreck or a body: the renderer draws the MIL-STD-2525
        /// destroyed marking, an X through the shape, whatever the profile.</summary>
        Destroyed = 4u
    }
}
