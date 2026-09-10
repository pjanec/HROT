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

        // ⭐⭐⭐ S3/S5 (DESIGN_Gizmo_Anchor_Identity.md §6) — AN IN-PROCESS PAYLOAD, NOT AN IDENTITY.
        //   AnchorId above is the IDENTITY (a network id) and is the only thing compared, routed or sent
        //   over the wire. These two fields are a LOCAL SHORTCUT so the consumer-side adapter can rebuild
        //   the ECS handle without a lookup — the producer already had it, so it costs nothing.
        //   ⛔ NEVER compare on these, and NEVER put them on the wire: an Entity index is process-local,
        //     which is exactly the cross-node defect S1/S2 removed.
        //   ⭐ Why a payload and not a map lookup: ReplayBrowser composes SelectionInteractionSystem and
        //     has NO NetworkEntityMap at all, so a resolve-at-the-boundary design would silently drop its
        //     selection. Measured 2026-09-10.
        public int   AnchorIndex;   // ECS entity index  — payload only
        // (the generation travels in StreamId for the same purpose)
        // Routing discriminator set by the terminal from the picked primitive's GizmoTypeId field;
        // 0 for legacy or entity-local primitives that predate composite-key routing.
        public uint  GizmoTypeId;
        public bool  IsValid => AnchorId != 0;
    }
}
