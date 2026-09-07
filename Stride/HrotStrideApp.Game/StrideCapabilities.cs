#nullable enable
using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Components;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;

namespace HrotStrideApp;

/// <summary>
/// Stride's role-selected capabilities — host (e) of the capability seam, after SimHost
/// (<c>§4.1s</c>), IG (<c>§4.1t</c>) and CGF (<c>§4.1x</c>).
/// </summary>
/// <remarks>
/// <para><b>What this is for.</b> Both Stride shells — mode 1's <c>EditorStrideSubsystem</c> and
/// mode 2's node shell — compose the same units. Until now each hand-assembled them, which is how
/// two modes of one product drift apart. This is the single declaration both resolve, so a unit
/// added for one mode is present in the other by construction rather than by memory.
/// See <c>docs/DESIGN_Stride_Node_Modes.md</c> §4.</para>
///
/// <para><b>⛔ REGISTRATION ORDER IS EXECUTION ORDER.</b> <c>ModuleHostKernel.RegisterModule</c>
/// appends to a plain list the frame loop walks in sequence, so any split that reorders
/// registrations is a behaviour change, not a refactor. That constraint shapes two decisions below
/// and is the reason neither is tidier than it looks.</para>
///
/// <para><b>Why this lives in the host project rather than in <c>Hrot.Stride.Core</c>.</b>
/// <c>CE-205</c> named <c>Hrot.Stride.Core</c>, reasoning that it is the lower library both shells
/// reference. Measured, that placement inverts the layering: the muscle set is assembled from
/// <c>CombatModule</c>, <c>RouteTrajectorySyncSystem</c> and <c>PersonalRouteAuthoringSystem</c>,
/// all of which live in <b><c>Hrot.SimHost</c></b>, plus <c>EqsResultUpdateSystem</c> from
/// <c>Hrot.CGF</c> — so moving the declaration down would drag two whole node subsystems into a
/// thin Stride adapter library that today references only Fdp.Core, Fdp.Toolkits and Hrot.Core.
/// The decisive point is precedent: <b>all three hosts already on the seam keep their capabilities
/// in their own project</b> — <c>Hrot.SimHost/SimHostCapabilities.cs</c>,
/// <c>Hrot.IG/IgCapabilities.cs</c>, <c>Hrot.CGF/CgfCapabilities.cs</c>. Stride's host project is
/// where both its shells live, so this is the same shape, and it costs no new project reference.
/// Folded back into the design; see <c>DESIGN_Stride_Node_Modes.md</c> §4.1.</para>
/// </remarks>
public static class StrideCapabilities
{
    /// <summary>
    /// The role a Stride node declares.
    ///
    /// <para><b>⚠ Deliberately NOT <c>SimHost.DefaultRole</c>, and the difference is
    /// <c>NavigationSolver</c>.</b> SimHost has a separable <c>EngineBackedNavigationModule</c> it can
    /// declare as its own capability. Stride's navigation is DotRecast, and it is threaded
    /// <i>through</i> the muscle set — <c>StrideKinematicsModule</c> owns the trajectory pool that
    /// <c>NavigationIntentBridgeSystem</c> and <c>RouteTrajectorySyncSystem</c> write into, and those
    /// two systems register in the middle of the Simulation phase between the damage systems and the
    /// kinematics systems. Lifting them into a separate capability would move them in the
    /// registration list, which by the rule above is a behaviour change. So navigation is declared
    /// as part of <see cref="MuscleGround"/> and the flag is not claimed separately: the resolved
    /// set is then exactly what runs, which is the property that matters.</para>
    ///
    /// <para>⭐ <c>ImageGenerator</c> is deliberately absent. Stride does not present a 2-D map, and
    /// dropping the flag is only safe because <c>CE-211</c> made dead reckoning unconditional — it
    /// used to be the flag that switched smoothing on. See <c>DESIGN_Stride_Node_Modes.md</c> §6.</para>
    /// </summary>
    public const NodeRole DefaultRole = NodeRole.MuscleGround | NodeRole.Perception;

    /// <summary>
    /// Builds the plan both shells resolve.
    /// </summary>
    /// <param name="muscleSet">
    /// The already-constructed Stride muscle set. Passed in rather than built here for the same
    /// reason <see cref="SimHostCapabilities"/> takes its pack: the caller owns the crowd provider's
    /// lifetime and needs the individual objects to wire the physics bracket, so constructing a
    /// second set here would mean two of everything.
    /// </param>
    /// <param name="publishPerceptionModule">
    /// Optional sink for the constructed <c>CognitiveSpatialModule</c>. Diagnostics read the module
    /// directly on the other hosts; this hands it back the same way rather than inventing a second
    /// lookup path.
    /// </param>
    public static NodeCompositionPlan Build(
        StrideMuscleModuleSet muscleSet,
        Action<CognitiveSpatialModule>? publishPerceptionModule = null)
    {
        if (muscleSet is null) throw new ArgumentNullException(nameof(muscleSet));

        return new NodeCompositionPlan()
            .Capability(NodeRole.MuscleGround, new MuscleGround(muscleSet))
            .Capability(NodeRole.Perception,   new PerceptionSolver())
            .Capability(NodeRole.Perception,   new PerceptionSpatial(publishPerceptionModule));
    }

    /// <summary>
    /// The Stride muscle tier: physics-backed kinematics, combat, damage, and the DotRecast
    /// navigation bridge — registered as the one <c>StrideMuscleModule</c> that already exists.
    /// </summary>
    /// <remarks>
    /// Registering the module whole, rather than unpacking its systems into the phase lists, is what
    /// preserves the hand-written order its <c>RegisterSystems</c> documents phase by phase.
    /// </remarks>
    public sealed class MuscleGround : INodeCapability
    {
        private readonly StrideMuscleModuleSet _set;

        internal MuscleGround(StrideMuscleModuleSet set) => _set = set;

        public string Key => CapabilityKeys.MuscleGround;

        /// <summary>
        /// <b>Empty, and that is a measurement rather than an oversight.</b> SimHost declares
        /// <c>ResourceKeys.TrajectoryPool</c> because its navigation module and its kinematics
        /// systems are separate capabilities that must share one pool. Stride's set threads a single
        /// pool internally — <c>StrideMuscleModules.Build</c> hands
        /// <c>strideKinematics.TrajectoryPool</c> to both consumers — so there is exactly one and no
        /// provider is needed to enforce it. ⚠ <c>CE-181</c> tracks the related hazard
        /// (<c>StrideKinematicsModule</c>'s <c>?? new TrajectoryPoolManager()</c> fallback); declaring
        /// a need here that nothing provides would fail resolution rather than fix it.
        /// </summary>
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        public void Register(HrotNodeContext context, NodeBootValues values)
            => context.Kernel.RegisterModule(new StrideMuscleModule(_set));
    }

    /// <summary>
    /// The EQS solver — perception's query half, adopted verbatim from SimHost.
    /// </summary>
    /// <remarks>
    /// <para><b>This closes a real hole (<c>CE-206</c>), not a tidiness one.</b> Stride already
    /// registers <c>EqsResultUpdateSystem</c> — the <i>consumer</i> of EQS results — with no
    /// <c>EqsModule</c> to produce any. It looks like it works today only because CGF's brain sits in
    /// the same world in both existing modes and solves them. In mode 2 CGF is a separate node and
    /// that cover is gone, which is why this is a prerequisite of <c>CE-207</c> rather than a
    /// follow-up.</para>
    /// </remarks>
    public sealed class PerceptionSolver : INodeCapability
    {
        public string Key => CapabilityKeys.Perception;
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        public void Register(HrotNodeContext context, NodeBootValues values)
            => context.Kernel.RegisterModule(new EqsModule());
    }

    /// <summary>
    /// Perception's spatial half: area-query materialisation and the cognitive grid.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠ This ships with SimHost's 2-D line of sight, deliberately and temporarily.</b>
    /// <c>LosRequestBatchingSystem</c> resolves visibility with an inline segment-circle sweep that
    /// knows nothing about terrain or height — so a Stride node will report LOS that does not match
    /// what is visibly occluded in its own 3-D window. That is an accepted transitional limitation
    /// with a task against it (<c>CE-210</c>), and any report claiming Stride perception works must
    /// say so.</para>
    ///
    /// <para>Separate from <see cref="PerceptionSolver"/> for the same reason SimHost splits them:
    /// the two register at different points and merging them would reorder the list.</para>
    /// </remarks>
    public sealed class PerceptionSpatial : INodeCapability
    {
        private readonly Action<CognitiveSpatialModule>? _publishModule;

        internal PerceptionSpatial(Action<CognitiveSpatialModule>? publishModule)
            => _publishModule = publishModule;

        public string Key => CapabilityKeys.Perception + ":spatial";
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        public void Register(HrotNodeContext context, NodeBootValues values)
        {
            context.Kernel.RegisterGlobalSystem(new AreaQueryResultMaterializationSystem());

            var module = new CognitiveSpatialModule(
                context.World,
                colliderRadiusReader: static (view, e) => view.HasComponent<PhysicsCollider>(e)
                    ? view.GetComponentRO<PhysicsCollider>(e).Radius
                    : 0f);

            _publishModule?.Invoke(module);
            context.Kernel.RegisterModule(module);
        }
    }
}
