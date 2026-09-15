namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Network-stable pick token that identifies a gizmo interaction target
    // without ECS entity references. Replaces the ECS-based PickToken for
    // use in GizmoMap assemblies and DDS transport.
    public struct GizmoPickToken
    {
        public long  AnchorId;      // NetworkId / semantic object id (0 = invalid)
        public uint  SubElementId;  // gizmo sub-element index within the anchored entity

        // ⭐⭐⭐ RESERVED — "publisher stream discriminator (for multi-SimHost clusters)", which is what
        //   this field has always claimed to be. Production writes 0 everywhere.
        //
        //   ⛔⛔ HISTORY, 2026-09-11 — IT CARRIED AN ECS GENERATION, AND `AnchorIndex` SAT BESIDE IT.
        //     Those two fields were an in-process payload: the ECS handle of the entity whose pick box
        //     was hit, forwarded so a consumer could rebuild an `Entity` without a lookup. Their comment
        //     said "never compare these", which is a bad smell in a struct field, and the user asked the
        //     right question: *"why is it there in the first place when we decided to use network ids?"*
        //
        //   ⭐⭐ THE ANSWER WAS: it is NOT necessary, and the reasoning that kept it was wrong.
        //     The justification on record was *"ReplayBrowser has no NetworkEntityMap, so a map lookup
        //     would silently drop its selection"* — i.e. the gizmo identity model was weakened to
        //     accommodate ONE module that lacked a service every other ECS module has. 🔒 User:
        //     *"replaybrowser is ecs module like any else. i do not want such exceptions."*
        //   📐 Measured: nothing prevented it. `NetworkEntityMap` is already a world singleton
        //     (`CgfSubsystem.cs:637`, `SimHostApp.cs:546`, `EditorSubsystem.cs:1122`) and the "rebuild it
        //     from ECS state" routine already existed — buried in a private lambda whose own comment
        //     claimed to *"unify NetworkEntityMap resync for all subsystems"* while being reachable by
        //     exactly one of them.
        //   ⇒ 📄 `docs/DESIGN_Gizmo_Anchor_Identity.md` §6.7. The ECS handle is gone from this contract,
        //     `PickToken` carries the network id too, and each consumer resolves it in ITS OWN world —
        //     which is where a world exists and where the resolve belongs.
        public uint  StreamId;

        // Routing discriminator set by the terminal from the picked primitive's GizmoTypeId field;
        // 0 for legacy or entity-local primitives that predate composite-key routing.
        public uint  GizmoTypeId;
        /// <summary>
        /// ⭐⭐ <c>&gt; 0</c>, matching <c>PickToken.IsValid</c> — see that type for the measurement.
        /// ⛔ It was <c>!= 0</c>, which calls the terminal's <c>-1</c> CANVAS SENTINEL valid. ⚠ No
        /// production reader was measured for this one *(only rails)*, so this is consistency rather
        /// than a live fix — but two tokens for one concept disagreeing about what "valid" means is
        /// exactly the trap the anchor-identity work exists to remove. 📄 §6.7.
        /// </summary>
        public bool  IsValid => AnchorId > 0;
    }
}
