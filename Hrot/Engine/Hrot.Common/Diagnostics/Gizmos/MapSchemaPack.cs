using Fdp.Core;

namespace Hrot.Common.Diagnostics.Gizmos;

/// <summary>
/// <b>ST-022 — the SCHEMA half of uniform gizmo membership</b> (Q52 §2).
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
/// <para>⚠ All five types live in <c>Fdp.Toolkits</c>, which every host already references, so this adds
/// <b>zero project edges</b>.</para>
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
        // Brain tier. ⭐ Required by HillAttackGizmo (Hrot.AI.Behaviors). Tier is IRRELEVANT to whether a
        // host registers the TYPE: 🔒 "ig is not meant to draw brain tier gizmos. brain components are not
        // instantiated on IG" -- and they are not, so the projector matches nothing there.
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBlackboard>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();

        // ⭐ Brain-tier too, and named explicitly by the user as the case that proves the rule: "navigation
        // intent is for sure brain tier -- but what for is that important if we should support all and
        // decide on current presence of component?" Required by Hrot.Common's own NavigationTargetGizmo.
        world.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();

        // 🔴 These two are required by IG's OWN projectors (EqsSensorGizmo, ProjectilePresentationGizmo),
        // which IG declared while never registering their components -- a plain omission, not a policy
        // question, and live for however long.
        world.RegisterComponent<Fdp.Toolkit.Spatial.Eqs.EqsSensor>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
    }
}
