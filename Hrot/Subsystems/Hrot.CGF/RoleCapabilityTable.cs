using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.SimHost;

namespace Hrot.CGF;

/// <summary>
/// ⭐ <c>CE-4270</c> B9: what a host hands the role table. Each value says what this host has. The table decides where each part sits.
/// </summary>
public sealed record RoleTableInputs
{
    /// <summary>The Brain's logic pack: every row has it.</summary>
    public required CgfLogicPack Brain { get; init; }

    /// <summary>The Muscle role's parts, built by <see cref="MuscleRoleInputs.Build"/>. Null means the node does not own the muscle (a Brain-only CGF) or supplies its own (<see cref="InjectedMuscle"/>).</summary>
    public MuscleRoleInputs.Parts? Muscle { get; init; }

    /// <summary>The node's one trajectory pool. Required when <see cref="Muscle"/> is set (CE-180: one pool for the kinematics and the solver).</summary>
    public TrajectoryPoolProvider? TrajectoryPool { get; init; }

    /// <summary>A muscle tier the hosting app supplies itself (Stride's mode 1). It goes into the Muscle's slot, and its entries de-duplicate against the table's by Key.</summary>
    public IReadOnlyList<INodeCapability>? InjectedMuscle { get; init; }

    /// <summary>SUMO traffic: <c>Serving()</c> on a node whose world simulates the cars (the editor, the fused server); <c>Mirroring()</c> on a Brain-only CGF; null for none (the injected arm).</summary>
    public INodeCapability? Traffic { get; init; }

    /// <summary>The gameplay deploy server (Player Join, R-4115). Null on a host that serves no players (the offline editor).</summary>
    public INodeCapability? GameplayServer { get; init; }
}

/// <summary>
/// ⭐⭐⭐ <c>CE-4270</c> B9 (<c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1ag, R-3077). This is the ONE table of the simulation roles' capabilities.
/// Every host that runs live simulation resolves its declared role against it:
/// <list type="bullet">
/// <item>CGF (the Brain)</item>
/// <item>the fused server (Brain | MuscleGround | Perception | NavigationSolver)</item>
/// <item>the editor (the fused set with the network off)</item>
/// <item>the editor hosted in Stride, with its injected muscle</item>
/// </list>
/// Before B9 the editor kept its own copy in <c>EditorCapabilities</c>, with its own Brain, MuscleGround and EQS-solver wrapper classes. That copy is how the editor once
/// lacked the navigation solver (CE-3017) and the EQS solver (EQS design §17.3).
/// </summary>
/// <remarks>
/// <para>⛔ <b>Order is execution order</b> (<c>ModuleHostKernel.RegisterModule</c> appends to a list the frame walks):
/// <list type="number">
/// <item>the shared head (HealthApplication first, CE-2144)</item>
/// <item>the Brain</item>
/// <item>the Muscle (trajectory-pool provider, MuscleGround, Perception, NavigationSolver), or the injected tier in its slot</item>
/// <item>the cross-role infrastructure (CE-221 / CE-3110)</item>
/// <item>the character body</item>
/// <item>traffic</item>
/// <item>the gameplay server</item>
/// </list>
/// The Brain comes before the MuscleGround because a host that fuses both de-duplicates systems by type, and the first copy wins (CE-165).</para>
/// <para>⭐ Every entry is keyed by the ROLE that composes it, so <c>Resolve(role)</c> drops what a role does not compose. The table builds no host's
/// parts. The host passes them, so the table states WHAT composes and the host decides HOW its parts are built.</para>
/// </remarks>
public static class RoleCapabilityTable
{
    /// <summary>The plan for <paramref name="inputs"/>. Resolve it with the node's declared role.</summary>
    public static NodeCompositionPlan Plan(RoleTableInputs inputs)
    {
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));
        if (inputs.Brain is null) throw new ArgumentNullException(nameof(inputs.Brain));
        if (inputs.Muscle != null && inputs.InjectedMuscle != null)
            throw new ArgumentException("A node has ONE muscle tier: either the table's (Muscle) or a supplied one (InjectedMuscle), never both.", nameof(inputs));
        if (inputs.Muscle != null && inputs.TrajectoryPool is null)
            throw new ArgumentException("The Muscle's parts need the node's one trajectory pool (CE-180).", nameof(inputs));

        // ⭐⭐ CE-2144 (R-2008): the ONE shared plan head, then the Brain.
        var plan = CoreInfrastructureCapabilities.LiveSimulationPlan(NodeRole.Brain)
            .Capability(NodeRole.Brain, new CgfCapabilities.Brain(inputs.Brain));

        // The Muscle role's entries, from the ONE step SimHost uses (B7b). A supplied tier goes in the same slot instead (S2b).
        if (inputs.Muscle != null) plan = plan.WithSolverEntries(inputs.Muscle, inputs.TrajectoryPool!);
        if (inputs.InjectedMuscle != null)
            foreach (INodeCapability capability in inputs.InjectedMuscle)
                plan = plan.Capability(NodeRole.MuscleGround, capability);

        plan = plan
            // ⭐ CE-221: cross-role infrastructure, LAST, so it keeps its tail-of-Simulation position (Resolve de-duplicates by Key, first wins).
            .Capability(NodeRole.Brain, new CoreInfrastructureCapabilities.UnitHierarchy())
            .Capability(NodeRole.Brain, new EqsResultUpdateCapability())
            // ⭐ CE-3110 (R-221/R-224): interactions (doors, embarkation) are cross-role too.
            .Capability(NodeRole.Brain, new CoreInfrastructureCapabilities.Interactions());

        // ⭐ CE-2121: the character body (stance) for the table's own muscle. A supplied tier brings its own.
        if (inputs.Muscle != null) plan = plan.Capability(NodeRole.MuscleGround, new AnimationMuscleCapability());
        // ⭐ AQ93 R-4047 / R-4056 and CE-4270 D3: the Brain's traffic. It serves the cars where this world simulates them and mirrors them where it does not.
        if (inputs.Traffic != null) plan = plan.Capability(NodeRole.Brain, inputs.Traffic);
        // ⭐ Player Join D-2 / §7c (R-4115): the gameplay deploy server.
        if (inputs.GameplayServer != null) plan = plan.Capability(NodeRole.Brain, inputs.GameplayServer);
        return plan;
    }
}
