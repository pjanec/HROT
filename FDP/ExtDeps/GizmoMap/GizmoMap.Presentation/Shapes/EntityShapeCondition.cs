namespace GizmoMap.Presentation.Shapes
{
    [System.Flags]
    public enum EntityShapeCondition : uint
    {
        None = 0u,
        Damaged = 1u,
        Immobile = 2u
    }
}
