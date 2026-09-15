using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation.EngineBacked;
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
/// <c>EqsModule</c>, then the navigation module, then <c>AreaQueryResultMaterializationSystem</c> and
/// <c>CognitiveSpatialModule</c> — perception concerns interleaved <i>around</i> navigation. Collapsing
/// them into one contiguous <c>Perception</c> capability would move the navigation module later in the
/// list. Whether that interleaving is meaningful or merely historical is <b>not measured</b>, so this
/// split preserves it exactly rather than guessing. ⇒ <b>a follow-up should establish whether the two
/// halves can be merged</b>; until then, the shape here is the honest one.</para>
/// </remarks>
internal static class SimHostCapabilities
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
            => context.Kernel.RegisterModule(new EqsModule());
    }

    /// <summary>On-demand pathfinding, backed by the engine's navmesh and road graph.</summary>
    internal sealed class NavigationSolver : INodeCapability
    {
        private readonly EngineBackedNavigationModule _module;

        internal NavigationSolver(EngineBackedNavigationModule module) => _module = module;

        public string Key => CapabilityKeys.NavigationSolver;

        /// <summary>The pool this capability WRITES resolved routes into, by handle.</summary>
        /// <remarks>
        /// The same pool <see cref="MuscleGround"/> reads them back from — two pools here mean routes
        /// that resolve and vehicles that never follow them (<c>CE-180</c>). Declaring the need is what
        /// makes the node allocate exactly one and hand it to both.
        /// </remarks>
        public IReadOnlyList<string> Needs { get; } = new[] { ResourceKeys.TrajectoryPool };

        public void Register(HrotNodeContext context, NodeBootValues values)
            => context.Kernel.RegisterModule(_module);
    }

    /// <summary>Perception's spatial half: area-query materialisation and the cognitive grid systems.</summary>
    internal sealed class PerceptionSpatial : INodeCapability
    {
        private readonly Action<CognitiveSpatialModule> _publishModule;

        internal PerceptionSpatial(Action<CognitiveSpatialModule> publishModule)
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

            // The host still exposes this module publicly (diagnostics read it), so hand it back.
            // ⚠ Migration boundary, like NodeBootPlan.Value<T> — it should disappear once the
            // consumers of PerceptionModule read it from the capability set instead.
            _publishModule(module);
            context.Kernel.RegisterModule(module);
        }
    }
}
