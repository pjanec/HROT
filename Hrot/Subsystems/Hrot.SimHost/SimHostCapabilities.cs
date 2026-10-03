using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation.EngineBacked;
using Fdp.Toolkit.Navigation.Modules;
using Fdp.Toolkit.Physics.Components;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost;

/// <summary>
/// SimHost's role-selected capabilities.
/// </summary>
/// <remarks>
/// <para><b><c>B4b</c> step 2 — the capability axis.</b> These replace the hand-written module list in
/// <c>RegisterSpawningPipeline</c>. Each one is what a <see cref="NodeRole"/> flag actually <i>means</i>
/// for this host, and each declares the shared resources it borrows so the node can allocate them once.</para>
///
/// <para>⛔⛔ <b>THE ORDER OF THESE CAPABILITIES IS LOAD-BEARING, AND THAT IS A MEASUREMENT, NOT A
/// PREFERENCE.</b> <c>ModuleHostKernel.RegisterModule</c> appends to a plain <c>List</c>
/// (<c>_modules.Add(entry)</c>) which the frame loop iterates in order — so <b>registration order is
/// execution order</b>. Any capability split that reorders registrations is therefore <i>not</i>
/// behaviour-preserving, which is the whole constraint <c>B1</c>–<c>B4</c> operate under.</para>
///
/// <para>⚠ <b>That is why perception appears as TWO capabilities.</b> Today SimHost registers
/// <c>EqsModule</c>, then the navigation module, then <c>CognitiveSpatialModule</c> — perception concerns
/// interleaved <i>around</i> navigation *(<c>AreaQueryResultMaterializationSystem</c> sat before the module
/// until the AreaQuery pipeline was retired, 2026-10-01)*. Collapsing
/// them into one contiguous <c>Perception</c> capability would move the navigation module later in the
/// list. Whether that interleaving is meaningful or merely historical is <b>not measured</b>, so this
/// split preserves it exactly rather than guessing. ⇒ <b>a follow-up should establish whether the two
/// halves can be merged</b>; until then, the shape here is the honest one.</para>
/// </remarks>
public static class SimHostCapabilities
{
    /// <summary>The Muscle-tier ground simulation: the core logic pack's systems and its module.</summary>
    /// <remarks>
    /// This is the one capability that contributes to <b>both</b> boot steps — its systems go into the
    /// togglable phase groups (<c>PopulateSystems</c>) and the pack itself registers as a module. That
    /// is why <see cref="INodeCapability"/> carries two hooks: they mirror the two steps the base's
    /// boot plan already declares, rather than inventing a third place to compose.
    /// </remarks>
    internal sealed class MuscleGround : INodeCapability
    {
        private readonly SimHostCoreLogicPack _pack;

        internal MuscleGround(SimHostCoreLogicPack pack) => _pack = pack;

        public string Key => CapabilityKeys.MuscleGround;

        /// <summary>The pool the pack's kinematics systems read routes from.</summary>
        public IReadOnlyList<string> Needs { get; } = new[] { ResourceKeys.TrajectoryPool };

        public void PopulateSystems(
            HrotNodeContext context,
            List<IEcsModuleSystem> input,
            List<IEcsModuleSystem> sim,
            List<IEcsModuleSystem> postSim)
        {
            foreach (IEcsModuleSystem s in _pack.InputSystems)          input.Add(s);
            foreach (IEcsModuleSystem s in _pack.SimulationSystems)     sim.Add(s);
            foreach (IEcsModuleSystem s in _pack.PostSimulationSystems) postSim.Add(s);
        }

        public void Register(HrotNodeContext context, NodeBootValues values)
            => context.Kernel.RegisterModule(_pack);
    }

    /// <summary>The EQS solver — perception's off-thread query half.</summary>
    /// <remarks>Separate from <see cref="PerceptionSpatial"/> only to preserve registration order; see
    /// the type-level remarks.</remarks>
    internal sealed class PerceptionSolver : INodeCapability
    {
        public string Key => CapabilityKeys.Perception;
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        public void Register(HrotNodeContext context, NodeBootValues values)
            => EqsSolverStartup.Register(context);
    }

    /// <summary>On-demand pathfinding, backed by the engine's navmesh and road graph.</summary>
    /// <remarks>⭐ <c>CE-3017</c> — public so the editor (≡ CGF solo, <c>R-182</c>) composes THIS capability rather than a
    /// copy of it (<c>R-174</c>). The other SimHost capabilities stay internal.</remarks>
    public sealed class NavigationSolver : INodeCapability
    {
        private readonly EngineBackedNavigationModule _module;
        private readonly Func<NavigationSolverModule>? _solverFactory;
        private readonly List<IEcsModule> _registered = new();

        /// <param name="solverFactory">⭐ W6 (docs/DESIGN_Terrain_World.md §5) — builds the path SOLVER. Before it,
        /// no production host composed <see cref="NavigationSolverModule"/>, so a <c>PathfindingRequestEvent</c>
        /// was published and never answered. A factory, not an instance: the road holder it needs exists only
        /// after orchestration is built.</param>
        public NavigationSolver(EngineBackedNavigationModule module, Func<NavigationSolverModule>? solverFactory = null)
        {
            _module = module;
            _solverFactory = solverFactory;
        }

        public string Key => CapabilityKeys.NavigationSolver;

        /// <summary>The pool this capability WRITES resolved routes into, by handle.</summary>
        /// <remarks>
        /// The same pool <see cref="MuscleGround"/> reads them back from — two pools here mean routes
        /// that resolve and vehicles that never follow them (<c>CE-180</c>). Declaring the need is what
        /// makes the node allocate exactly one and hand it to both.
        /// </remarks>
        public IReadOnlyList<string> Needs { get; } = new[] { ResourceKeys.TrajectoryPool };

        /// <summary>The modules <see cref="Register"/> registered, in order — what a host that hot-swaps its logic tier
        /// (the editor's <c>SwitchToExternalAsync</c>) must uninstall with it. Empty before <see cref="Register"/>.</summary>
        public IReadOnlyList<IEcsModule> RegisteredModules => _registered;

        public void Register(HrotNodeContext context, NodeBootValues values)
        {
            _registered.Clear();
            _registered.Add(_module);
            if (_solverFactory != null) _registered.Add(_solverFactory());
            foreach (IEcsModule module in _registered) context.Kernel.RegisterModule(module);
        }
    }

    /// <summary>Perception's spatial half: the cognitive grid systems.</summary>
    internal sealed class PerceptionSpatial : INodeCapability
    {
        private readonly Action<CognitiveSpatialModule> _publishModule;

        internal PerceptionSpatial(Action<CognitiveSpatialModule> publishModule)
            => _publishModule = publishModule;

        public string Key => CapabilityKeys.Perception + ":spatial";
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        public void Register(HrotNodeContext context, NodeBootValues values)
        {
            // ⭐ 3-D sight through the resident terrain world (§4.3, R-182) and a perception grid that follows it (§4.4,
            //    CE-3018) — one factory for every terrain host (docs/DESIGN_Terrain_World.md).
            var module = CognitiveSpatialModule.ForTerrainHost(context.World);

            // The host still exposes this module publicly (diagnostics read it), so hand it back.
            // ⚠ Migration boundary, like NodeBootPlan.Value<T> — it should disappear once the
            // consumers of PerceptionModule read it from the capability set instead.
            _publishModule(module);
            context.Kernel.RegisterModule(module);
        }
    }
}
