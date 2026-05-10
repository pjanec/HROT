namespace GizmoMap.Presentation.Shapes
{
    public interface IEntityShapeLibrary
    {
        EntityShapeProfile GetShape(string? shapeName, ulong fallbackDisType);
    }
}
