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

        // ⭐⭐⭐ WHY THESE EXIST AT ALL, given that identity is the network id (asked 2026-09-11, and it
        //   is the right question — a field whose comment says "do not compare this" has to justify
        //   itself). 📄 DESIGN_Gizmo_Anchor_Identity.md §6.2 ③ and §6.6.
        //
        //   📐 THE CONSTRAINT: the two consumers of a pick are ECS systems that need an `Entity`, not an
        //     id — SelectionInteractionSystem writes SelectionState, and DataDrivenGizmoSystem.FindGizmo
        //     uses Dictionary<Entity, IEntityStatefulGizmo>. So SOMETHING must turn the picked network id
        //     into a local Entity. Measured options, 2026-09-10:
        //       ① the producer hands its handle along (these fields) — free, no lookup, no host wiring;
        //       ② a NetworkEntityMap lookup — O(1), but ⛔ ReplayBrowser HAS NO MAP (it falls back to
        //          NetworkIdResolver.FindEntityByNetworkId, ReplayBrowserSubsystem.cs:991);
        //       ③ FindEntityByNetworkId everywhere — ⛔ a LINEAR SCAN over all entities, which C5 forbids.
        //     ⇒ ① was chosen: it needs nothing passed per host, so it cannot be silently forgotten in
        //       one of them — the SILENT-DEFAULT failure that produced CE-259y.
        //
        //   ⭐⭐ SO THE CONTRACT IS NOT "do not compare" — IT IS "VALID ONLY FOR A PRIMITIVE EMITTED IN
        //     THIS PROCESS", and since 2026-09-11 that is ENFORCED, not asserted: the single place
        //     foreign primitives enter a local buffer — DebugPrimitivesIngressTranslator — STRIPS them
        //     (CE-259af). A received primitive therefore arrives with StreamId == 0, the adapter yields
        //     an invalid token, and the interaction is published over DDS to the OWNING node, where
        //     GizmoInteractionIngressTranslator resolves the network id through its map. ⇒ it degrades
        //     onto the designed remote path instead of selecting a locally-plausible WRONG entity.
        //
        //   ⛔ AnchorId above remains the ONLY identity: the only thing compared, routed, or sent over
        //     the wire. ⛔ Never compare on these, and never marshal them as an identity.
        public int   AnchorIndex;   // ECS entity index  — payload only
        // (the generation travels in StreamId for the same purpose)
        // Routing discriminator set by the terminal from the picked primitive's GizmoTypeId field;
        // 0 for legacy or entity-local primitives that predate composite-key routing.
        public uint  GizmoTypeId;
        public bool  IsValid => AnchorId != 0;
    }
}
