using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Extension methods that add ECS-specific computed properties to DebugPrimitive.
    // These replace the former .Anchor and .Token instance properties that coupled
    // GizmoMap.Contracts.DebugPrimitive to Fdp.Core.Entity and PickToken.
    public static class EcsDebugPrimitiveExtensions
    {
        // Reconstructs the entity anchor from its split index + generation fields.
        public static Entity GetAnchor(this DebugPrimitive p)
            => new Entity(p.AnchorIndex, p.AnchorGeneration);

        // Computed pick token that uses the Anchor entity as the hit-test target.
        // IsValid returns true when AnchorIndex >= 0 and AnchorGeneration != 0,
        // i.e. the anchor entity is non-null.
        public static PickToken GetPickToken(this DebugPrimitive p)
            => new PickToken { Target = p.GetAnchor(), SubElementId = p.SubElementId };
    }
}
