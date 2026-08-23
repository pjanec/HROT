using Fdp.Core;

namespace Hrot.Common.Diagnostics.Gizmos;

/// <summary>
/// <b>ST-022 / ST-027 — the SCHEMA half of uniform gizmo membership</b> (Q52 §2, DESIGN_Uniform_Gizmo_Membership §3 ①).
///
/// <para>Registers the component types that the gizmo projector families require but that a host's own
/// role registry has no reason to declare. ⭐ <b>Registering a type is not putting data on an entity</b>
/// (<c>UX_Tasks_Detail.md</c> Correction 47): after this runs, <c>StatelessGizmoRegistry</c> is satisfied
/// and every one of these projectors still draws <b>nothing</b> on a host whose entities do not carry the
/// component. That is the design, not a loophole — 🔒 the user's rule is <i>"support all and decide on
/// current presence of component"</i>, so membership is uniform and the <b>draw</b> decision is made at
/// runtime, by data.</para>
///
/// <para><b>Why this exists at all.</b> A host declares projector FAMILIES (a generated
/// <c>GizmoRegistrar.RegisterAll</c> per assembly), and <c>StatelessGizmoRegistry.Register</c> throws when
/// a projector's required component type is unknown to the world. IG declares four families — including
/// two of its OWN projectors, <c>EqsSensorGizmo</c> and <c>ProjectilePresentationGizmo</c> — whose
/// components it never registered, so <c>--mode ig</c> died in bootstrap before its first frame
/// (<c>ST-020</c>). <c>--mode all</c> masked it: SimHost's registries put the same schema on the shared
/// world, so IG was satisfied by accident of co-tenancy.</para>
///
/// <para>⛔ <b>The registry still throws, deliberately.</b> Making it skip absent components was rejected
/// (🔒 <i>"no losening"</i>): it is a bootstrap-time SCHEMA check, and turning it into a silent skip would
/// let a genuine typo drop a gizmo with no signal — the failure mode this programme exists to close.</para>
///
/// <para><b>Home.</b> Here rather than in <c>Hrot.IG</c>, beside the projectors it serves and in the one
/// assembly every host already references — because the fix generalises. UXI-23's <c>MapInteractionPack</c>
/// will make all five hosts declare all four families, and would crash all five exactly as
/// <c>--mode ig</c> crashes today; this is the half that stops that, landed early, in a place every host
/// can call. Q52 §4.1 draws it as the single schema entry point for that reason.</para>
///
/// <para>⭐ <b>ST-027 widened this from 5 types to all 15</b> — the full union required by the 18
/// projectors across the 6 families, because every host now declares every family
/// (<c>MapGizmoPack</c>). The first 5 closed IG's gap alone; the other 10 are what the other four hosts
/// need once they stop curating.</para>
///
/// <para>⚠ <b>Zero new project edges.</b> 14 of the 15 were already reachable from <c>Hrot.Common</c>
/// (<c>Fdp.Core</c>, <c>Fdp.Toolkits</c>, <c>Hrot.Core</c>). The 15th, <c>VisualEffectState</c>, sat in the
/// <c>Hrot.IG</c> assembly, which <c>Hrot.Common</c> cannot reference — so it moved to <c>Hrot.Core</c>
/// beside its four siblings, keeping its namespace and its <c>[ComponentId]</c>.</para>
/// </summary>
public static class MapSchemaPack
{
    /// <summary>
    /// Registers the projector-required component schema on <paramref name="world"/>.
    ///
    /// <para>⚠ <b>Call in the host's component-registration phase</b> (for a
    /// <c>SharedApplicationBootstrapper</c> host that is Phase 2, <c>RegisterDomainComponents</c>) — it
    /// must run before the gizmo registrars in Phase 6d, which is what validates against it. Idempotent
    /// in the sense that matters: a host whose role registry already declares one of these may call both,
    /// because <see cref="EntityRepository.RegisterComponent{T}"/> tolerates a repeat registration.</para>
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        // ── Fdp.Core ────────────────────────────────────────────────────────────────────────────
        world.RegisterComponent<Fdp.Core.SimTransform>();

        // ── Fdp.Toolkits: brain tier ────────────────────────────────────────────────────────────
        // ⭐ Tier is IRRELEVANT to whether a host registers the TYPE: 🔒 "ig is not meant to draw brain
        // tier gizmos. brain components are not instantiated on IG" -- and they are not, so those
        // projectors match nothing there. NavigationIntent is the user's own example of the point:
        // "navigation intent is for sure brain tier -- but what for is that important if we should
        // support all and decide on current presence of component?"
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBlackboard>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
        world.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();

        // ── Fdp.Toolkits: perception, combat, spatial ───────────────────────────────────────────
        world.RegisterComponent<Fdp.Toolkit.Perception.Components.PerceptionReceptor>();
        world.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
        world.RegisterComponent<Fdp.Toolkit.Spatial.Eqs.EqsSensor>();

        // ── Fdp.Toolkits: replication identity ──────────────────────────────────────────────────
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();

        // ── Hrot.Core (namespace Hrot.IG.Components, assembly Hrot.Core) ────────────────────────
        // ⚠ ST-027: VisualEffectState was the ONLY one of the 15 in the Hrot.IG ASSEMBLY, which
        // Hrot.Common cannot reference (Hrot.IG -> Hrot.Common already exists, so the reverse is a
        // cycle). It moved to Hrot.Core beside its four siblings, namespace unchanged.
        world.RegisterComponent<Hrot.IG.Components.CullingState>();
        world.RegisterComponent<Hrot.IG.Components.SelectionState>();
        world.RegisterComponent<Hrot.IG.Components.IgHealthState>();
        world.RegisterComponent<Hrot.IG.Components.MapOverlayStyle>();
        world.RegisterComponent<Hrot.IG.Components.VisualEffectState>();
    }
}
