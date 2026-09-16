using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Modules;
using Hrot.Common.Infrastructure;

namespace Hrot.CGF;

/// <summary>
/// CGF's role-selected capabilities — the third host onto the capability axis.
/// </summary>
/// <remarks>
/// <para><b><c>B4b</c> step 2, host (c).</b> SimHost went first (<c>SimHostCapabilities</c>), IG second
/// (<c>IgCapabilities</c>, which found the seam's missing third hook). 📄
/// <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1x.</para>
///
/// <para>⛔⛔ <b>WHAT THIS IS NOT.</b> CGF does <i>not</i> adopt
/// <see cref="SharedApplicationBootstrapper"/> here, and that is a design position rather than an
/// omission: §4.1j's phase table marks node-bootstrap adoption <i>"optional, LAST"</i> because it is
/// the only phase touching orchestration, participant and time authority — <i>"what §3.1 says not to
/// move blindly."</i> ⇒ this host takes the CAPABILITY axis only, which is orthogonal to the
/// bootstrapper and needs nothing from it. <c>CgfSubsystem</c> keeps its own inline ECS root and calls
/// these hooks itself.</para>
///
/// <para>⚠ <b>Order is behaviour.</b> <c>ModuleHostKernel.RegisterModule</c> appends to a plain list
/// the frame loop walks in sequence, so the module sequence and the two system lists below reproduce
/// the host's previous hand-written block exactly. A source rail pins that the root no longer builds
/// the groups from the pack directly.</para>
/// </remarks>
internal static class CgfCapabilities
{
    /// <summary>
    /// The Brain tier: mission control, cognitive runtime, action dispatch and the Brain-side
    /// standalone systems, as assembled by <see cref="CgfLogicPack"/>.
    /// </summary>
    /// <remarks>
    /// <para>⭐ <b>This capability contributes on TWO hooks, and both halves are load-bearing.</b>
    /// <see cref="ProvideModules"/> yields the two whole modules the kernel registers; then
    /// <see cref="PopulateSystems"/> hands out the pack's phase-typed lists, which the root wraps in
    /// its togglable groups. Splitting them across hooks is not decoration — the modules must be
    /// registered <i>before</i> the groups are built, which is the order the previous inline block
    /// had and which the hook sequence now encodes.</para>
    ///
    /// <para>⭐⭐ <b>The pack is INJECTED, not constructed here.</b> <c>CgfLogicPack</c> needs the
    /// behaviour registry, the network entity map, the scenario source and the mapper registry — all
    /// values the root has and this class would otherwise have to be handed anyway. Following the
    /// interface's own rule (<i>the variation point is the INSTANCE, not a factory</i>) keeps the
    /// capability a declaration of WHAT the Brain contributes rather than a second place that decides
    /// HOW the pack is built. ⚠ It also keeps <c>ScenarioSource</c> reachable at the root, which the
    /// load handlers still read.</para>
    /// </remarks>
    internal sealed class Brain : INodeCapability
    {
        private readonly CgfLogicPack _logicPack;

        internal Brain(CgfLogicPack logicPack)
        {
            _logicPack = logicPack ?? throw new ArgumentNullException(nameof(logicPack));
        }

        /// <inheritdoc/>
        public string Key => CapabilityKeys.Brain;

        /// <summary>
        /// Nothing shared. The Brain allocates no one-per-world resource.
        /// </summary>
        /// <remarks>
        /// ⭐ Measured, not assumed: <c>CgfLogicPack</c>'s constructor takes a behaviour registry, an
        /// entity map, a scenario source and a mapper registry — no pool, no grid, no raycast batch.
        /// Kinematics and pathfinding, which own those resources, live on the Muscle
        /// (<c>ResourceKeys.TrajectoryPool</c> is declared by <c>MuscleGround</c>). ⛔ An empty list
        /// here is therefore the correct answer, and <c>CE-199</c>'s cross-check makes it loud the
        /// moment it stops being true.
        /// </remarks>
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        /// <inheritdoc/>
        /// <remarks>
        /// ⚠ Sequence is verbatim from the block this replaces: <c>BehaviorDiagnosticsModule</c> then
        /// the pack.
        /// </remarks>
        public IEnumerable<IEcsModule> ProvideModules()
        {
            yield return new BehaviorDiagnosticsModule();
            yield return _logicPack;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// ⛔ <b>Nothing is added to <paramref name="postSimulation"/>, and that is checked at the
        /// root.</b> CGF builds only an input group and a simulation group; a capability quietly
        /// contributing a post-simulation system would have its systems silently dropped. The root
        /// refuses rather than discards — the S2b habit of making an omission loud.
        /// </remarks>
        public void PopulateSystems(
            HrotNodeContext context,
            List<IEcsModuleSystem> input,
            List<IEcsModuleSystem> simulation,
            List<IEcsModuleSystem> postSimulation)
        {
            foreach (IEcsModuleSystem system in _logicPack.InputSystems)      input.Add(system);
            foreach (IEcsModuleSystem system in _logicPack.SimulationSystems) simulation.Add(system);
        }
    }
}
