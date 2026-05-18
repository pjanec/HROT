using System.Numerics;

namespace GizmoMap.Presentation.Shapes
{
    public readonly struct PolylineDefinition
    {
        public Vector3[] LocalVertices { get; init; }
        public bool IsClosed { get; init; }
        public bool IsFilled { get; init; }
        public float LineThickness { get; init; }
        public EntityShapeCondition ShowWhen { get; init; }
        public EntityShapeCondition HideWhen { get; init; }
    }
}
