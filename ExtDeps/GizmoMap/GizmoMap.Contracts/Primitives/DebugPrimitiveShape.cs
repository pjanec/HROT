namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public enum DebugPrimitiveShape : byte
    {
        Line               = 0,
        Sphere             = 1,
        Box2D              = 2,
        Arrow              = 3,
        Text               = 4,
        EntityBadge        = 5,
        Icon               = 6,
        ComponentInspector = 7,
        SemanticShape      = 8,  // Entity semantic profile primitive (DIS type / tactical shape)
        MilStd2525         = 9,  // NATO MIL-STD-2525 symbology frame
        SpatialAnchor      = 10, // Pre-resolved world position + orientation; severs SimTransform dependency
        ContextMenuBinding = 11, // Non-visual meta-primitive: binds an interned JSON menu hash to a NetworkId
        InputCaptureBinding = 12 // Non-visual meta-primitive: declares that the bound token wants raw HW events
    }
}
