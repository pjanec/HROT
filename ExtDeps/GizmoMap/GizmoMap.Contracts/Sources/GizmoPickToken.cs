namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Network-stable pick token that identifies a gizmo interaction target
    // without ECS entity references. Replaces the ECS-based PickToken for
    // use in GizmoMap assemblies and DDS transport.
    public struct GizmoPickToken
    {
        public long  AnchorId;      // NetworkId / semantic object id (0 = invalid)
        public uint  SubElementId;  // gizmo sub-element index within the anchored entity
        public uint  StreamId;      // publisher stream discriminator (for multi-SimHost clusters)
        public bool  IsValid => AnchorId != 0;
    }
}
