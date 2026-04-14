using System;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Orchestrator
{
    /// <summary>
    /// Composite <see cref="IEcsModule"/> that wraps a <see cref="ClusterSlave"/> —
    /// the cluster-orchestration state machine — as an installable module for use by
    /// the HROT Editor composition root and the Feature Switch (Phase 5 / PACK2-C001).
    ///
    /// <para><b>Purpose:</b> Encapsulates the orchestration tick loop
    /// (<see cref="ClusterSlave.Tick"/>) as a first-class ECS module so that it can be
    /// installed, hot-plugged, and torn down alongside other
    /// <see cref="IEcsModule"/> instances through <c>ModuleHostKernel</c>.</para>
    ///
    /// <para><b>Construction:</b> Build the wrapped <see cref="ClusterSlave"/> with all
    /// desired <see cref="FDP.Toolkit.Orchestration.Handlers.IClusterStateHandler"/>
    /// registrations via <see cref="Hrot.SimHost.NodeBootstrapper.BuildOrchestration"/>
    /// (or a custom bootstrap method), then pass the result to this constructor.</para>
    ///
    /// <para><b>Registration pattern:</b> Because <see cref="FDP.Toolkit.Orchestration.ClusterSlave"/>
    /// is not an ECS system, <see cref="IEcsModule.RegisterSystems(ISystemRegistry)"/> is
    /// a no-op. All work is performed in <see cref="Tick(ISimulationView, float)"/>
    /// which delegates to <see cref="ClusterSlave.Tick"/>.</para>
    /// </summary>
    public sealed class OrchestrationLogicPack : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "OrchestrationLogicPack";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly ClusterSlave _clusterSlave;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the orchestration logic pack around the supplied
        /// <see cref="ClusterSlave"/>.
        /// </summary>
        /// <param name="clusterSlave">
        /// A fully configured <c>ClusterSlave</c> with all desired
        /// <c>IClusterStateHandler</c> registrations applied. The caller (typically
        /// <c>NodeBootstrapper.BuildOrchestration</c>) is responsible for constructing
        /// and wiring all handlers before passing it here.
        /// </param>
        public OrchestrationLogicPack(ClusterSlave clusterSlave)
        {
            _clusterSlave = clusterSlave ?? throw new ArgumentNullException(nameof(clusterSlave));
        }

        // ── IEcsModule ────────────────────────────────────────────────────────

        /// <summary>
        /// No-op — <see cref="ClusterSlave"/> is not an ECS system; all orchestration
        /// work is performed in <see cref="Tick(ISimulationView, float)"/>.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <summary>
        /// Advances one cluster-orchestration frame by delegating to
        /// <see cref="ClusterSlave.Tick"/>.
        /// </summary>
        public void Tick(ISimulationView view, float deltaTime)
            => _clusterSlave.Tick();
    }
}
