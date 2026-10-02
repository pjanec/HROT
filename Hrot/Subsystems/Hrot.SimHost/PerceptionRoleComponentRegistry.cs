using Fdp.Core;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost;

/// <summary>
/// ⭐⭐⭐ ECS registration contract for nodes fulfilling the <b>Perception</b> role.
///
/// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.9a / §3.9b.</para>
///
/// <para>⛔⛔ <b>WHY THIS EXISTS, AND IT IS THE ROOT CAUSE OF <c>CE-259bf</c> — not a tidy-up.</b>
/// Four role registries already existed — <c>IgRoleComponentRegistry</c>,
/// <see cref="MuscleRoleComponentRegistry"/>, <c>NavigationSolverComponentRegistry</c> and
/// <c>CognitiveComponentRegistry</c> <i>(which IS the Brain role's set, under a name that does not say
/// so)</i> — ⛔ but <b>Perception had none</b>, and its components were filed inside the BRAIN's registry.
/// ⇒ ⭐⭐ <b>SimHost had to call the Brain's registry to obtain its OWN role's components</b>, which is how
/// a node that runs no cognitive system came to register the entire brain tier.</para>
///
/// <para>📐 <b>Measured <c>2026-09-12</c>:</b> <c>SimHostCapabilities.cs:79</c> registers
/// <c>EqsModule</c> and <c>Hrot.SimHost/Systems/EqsSolverSystem.cs</c> is SimHost's own — ⇒ the EQS
/// trio is <b>OWNED</b> by SimHost's Perception role, not read from a brain.</para>
///
/// <para>⚠ <b>This extraction is BEHAVIOUR-PRESERVING by construction.</b> Both
/// <c>SimHostComponentRegistry</c> and <c>CgfComponentRegistry</c> call it, so the set registered on
/// each host is byte-identical to before. ⛔ Deciding that CGF does <i>not</i> need Perception is a
/// SEPARATE change with its own evidence — this one only gives the role a home.</para>
/// </summary>
public static class PerceptionRoleComponentRegistry
{
    /// <summary>
    /// Registers the shared Perception-role component and event schema.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        // ── EQS sensing ───────────────────────────────────────────────────────
        // ⚠ These carried a "Brain-tier" comment in their previous home. That label was wrong:
        //   the solver runs on the node that declares Perception (SimHost), not on the Brain.
        world.RegisterComponent<EqsSensor>();
        world.RegisterComponent<EqsCognitiveBuffer>();
        world.RegisterManagedEvent<EqsResultUpdateEvent>();

        // Per-sensor cross-tick evaluation state (EQS Phase 5).
        world.RegisterComponent<SensorEvalState>();

        // ⭐ EqsSolverSystem submits RaycastRequestEvents via command-buffer playback and
        //   RaycastSolverSystem (Combat/Input) resolves them, publishing RaycastResultEvents.
        //   BOTH must be registered in every world hosting these systems or FdpEventBus.PublishRaw
        //   throws during harvest/flush.
        // ⚠ SimHostComponentRegistry also registers these directly; registration is idempotent, and
        //   the duplicate is left in place deliberately — removing it is a separate, measured step.
        world.RegisterEvent<RaycastRequestEvent>();
        world.RegisterEvent<RaycastResultEvent>();
    }
}
