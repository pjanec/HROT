using System.Collections.Generic;

namespace GizmoMap.Presentation.Shapes
{
    public sealed class EntityShapeProfile
    {
        public string Name { get; init; } = "_fallback";
        public IReadOnlyList<PolylineDefinition> Elements { get; init; } = System.Array.Empty<PolylineDefinition>();
    }
}
