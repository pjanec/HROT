using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Navigation.EngineBacked;
using Fdp.Toolkit.Navigation.Modules;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;

namespace Hrot.SimHost;

/// <summary>
/// ⭐⭐⭐ <c>CE-4270</c> B7b (<c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1ag, R-3077, R-174) — <b>the MuscleGround role's node inputs, ONE
/// implementation for every host whose DECLARED role composes the Muscle.</b> What <see cref="SimHostNodeBootstrapper"/> wrote inline (the trajectory-pool
/// resource, the GlobalTime seed, the solver parts and their plan entries, the navigation providers after Initialize, the SimHost translator set) lives here,
/// and both SimHost's bootstrapper and CGF's (booted as the fused server, <c>Brain | MuscleGround | Perception | NavigationSolver</c>) call it — keyed by the
/// role, never copied.
/// </summary>
public static class MuscleRoleInputs
{
    /// <summary>Whether <paramref name="role"/> composes the ground muscle (the kinematics that borrow the trajectory pool the navigation solver writes).</summary>
    public static bool ComposesMuscle(NodeRole role) => (role & NodeRole.MuscleGround) == NodeRole.MuscleGround;

    /// <summary>CE-199 — the DECLARATION half of the resource seam: the keys a Muscle role allocates (the trajectory pool), none otherwise.</summary>
    public static IReadOnlyList<string> DeclaredResourceKeys(NodeRole role)
        => ComposesMuscle(role) ? new[] { ResourceKeys.TrajectoryPool } : Array.Empty<string>();

    /// <summary>CE-199 — the INSTANCE half: the node's one trajectory-pool provider for a Muscle role, none otherwise.</summary>
    public static IReadOnlyList<INodeResourceProvider> ResolveResources(NodeRole role, TrajectoryPoolProvider trajectoryPool)
        => ComposesMuscle(role) ? new INodeResourceProvider[] { trajectoryPool } : Array.Empty<INodeResourceProvider>();

    /// <summary>The <see cref="GlobalTime"/> singleton a Muscle node starts with (SimHost's seed).</summary>
    public static void SeedGlobalTime(EntityRepository world, float simulationRateHz)
        => world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 1.0f / simulationRateHz, TimeScale = 1.0f });

    /// <summary>The solver parts one Muscle node composes, all over the node's ONE trajectory pool and navmesh (CE-180: two pools ⇒ routes nobody follows).</summary>
    public sealed class Parts
    {
        internal Parts(SimHostCoreLogicPack coreLogicPack, EngineBackedNavigationModule navigationModule, Func<NavigationSolverModule> solverFactory)
        {
            CoreLogicPack    = coreLogicPack;
            NavigationModule = navigationModule;
            SolverFactory    = solverFactory;
            NavigationSolver = new SimHostCapabilities.NavigationSolver(navigationModule, solverFactory);
        }

        public SimHostCoreLogicPack CoreLogicPack { get; }
        public EngineBackedNavigationModule NavigationModule { get; }
        public Func<NavigationSolverModule> SolverFactory { get; }
        /// <summary>⭐ CE-4270 B9 — the ONE navigation-solver capability of these parts (the editor reads back the modules it registered).</summary>
        public SimHostCapabilities.NavigationSolver NavigationSolver { get; }
    }

    /// <summary>
    /// Builds the muscle pack, the engine-backed navigation module and the background solver's factory over <paramref name="trajectoryPool"/> and
    /// <paramref name="navmesh"/>. ⭐ W6 — the solver reads the road holder lazily: <paramref name="orchestration"/> may not exist until BuildOrchestration.
    /// </summary>
    public static Parts Build(
        Fdp.Toolkit.Replication.Services.NetworkEntityMap entityMap,
        CarKinem.Road.RoadNetworkBlob roadNetwork,
        TrajectoryPoolProvider trajectoryPool,
        Fdp.Toolkit.Navigation.SwitchableNavmeshProvider navmesh,
        Func<NodeBootstrapper?> orchestration)
    {
        if (trajectoryPool is null) throw new ArgumentNullException(nameof(trajectoryPool));
        if (navmesh is null) throw new ArgumentNullException(nameof(navmesh));
        var pool = trajectoryPool.Pool;
        var pack = new SimHostCoreLogicPack(entityMap, roadNetwork, pool);
        var navModule = new EngineBackedNavigationModule(roadNetwork, pool, navmesh);
        NavigationSolverModule BuildSolver() => new(roadNetwork, pool, navmesh, roadNetworkHolder: orchestration()?.RoadNetworkHolder);
        return new Parts(pack, navModule, BuildSolver);
    }

    /// <summary>
    /// The Muscle role's plan entries, in SimHost's order: the trajectory-pool provider, then MuscleGround · Perception (the EQS solver) · NavigationSolver —
    /// each keyed by the role that composes it, so a plan resolved for a role without them composes none.
    /// </summary>
    public static NodeCompositionPlan WithSolverEntries(this NodeCompositionPlan plan, Parts parts, TrajectoryPoolProvider trajectoryPool)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (parts is null) throw new ArgumentNullException(nameof(parts));
        return plan
            .Provider(trajectoryPool)
            .Capability(NodeRole.MuscleGround,     new SimHostCapabilities.MuscleGround(parts.CoreLogicPack))
            .Capability(NodeRole.Perception,       new SimHostCapabilities.PerceptionSolver())
            .Capability(NodeRole.NavigationSolver, parts.NavigationSolver);
    }

    /// <summary>
    /// After <c>Kernel.Initialize</c>: the navigation module's providers (they need its <c>RegisterSystems</c> to have run). ⛔ CE-197 — GUARDED on the
    /// NavigationSolver capability actually being composed: a node without it never registered the module, and the call would throw.
    /// </summary>
    public static void RegisterNavigationProviders(IReadOnlyList<INodeCapability> composed, EngineBackedNavigationModule? navigationModule, EntityRepository world)
    {
        bool composedTheSolver = false;
        foreach (INodeCapability capability in composed)
            if (capability.Key == CapabilityKeys.NavigationSolver)
                composedTheSolver = true;

        if (composedTheSolver)
            navigationModule!.RegisterProviders(world);
    }

    /// <summary>
    /// The SimHost translator set (time-sync / combat / mission-control auxiliaries, perception over the ghost system, pathfinding over the node's trajectory
    /// pool — each pack picks its halves from the configured factory's role) plus the stance wire for <paramref name="stanceRole"/>. No participant or no
    /// factory ⇒ nothing (an offline node shares one world).
    /// </summary>
    public static void RegisterTranslators(HrotNodeContext context, INetworkFactory? configuredFactory,
                                           CarKinem.Trajectory.TrajectoryPoolManager trajectoryPool, NodeRole stanceRole)
    {
        if (context.Participant == null || configuredFactory == null) return;

        configuredFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(context.Kernel);
        configuredFactory.CreateSimHostPerceptionTranslators(context.GhostCreationSystem).RegisterOn(context.Kernel);
        configuredFactory.CreateSimHostPathfindingTranslators(trajectoryPool).RegisterOn(context.Kernel);
        // ⭐ CE-2121 slice ② — the stance wire's half for this role. 📄 docs/DESIGN_Decision_Layer.md §3.3g.
        Hrot.Animation.Replication.AnimationReplicationModule.RegisterStanceOn(
            context.Kernel, context.Participant, context.EntityMap, stanceRole);
    }
}
