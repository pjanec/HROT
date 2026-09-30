using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost.Modules
{
    /// <summary>
    /// SoD (Separation-of-Duties) module that drives the Phase 1 stub
    /// <see cref="EqsSolverSystem"/> at 10 Hz on a background thread.
    ///
    /// <para>Registered on the Muscle node so the solver can query entities with
    /// <c>EqsSensor</c> and emit <c>EqsResultEvent</c> for <c>EqsResultUpdateSystem</c>
    /// to consume on the Brain side.</para>
    ///
    /// <para><c>AreaQuerySolverSystem</c> continues to run inside
    /// <see cref="CognitiveSpatialModule"/> unchanged; this module no longer delegates
    /// to it.</para>
    ///
    /// <para>⭐ Templates come from the <see cref="IEqsTemplateRegistry"/> world singleton, which the
    /// registering capability installs with <see cref="EqsTemplateRegistry.InstallDefault"/> (every
    /// <c>[EqsTemplate]</c> class in the loaded assemblies). ⛔ An earlier version of this comment said
    /// <c>AiHotReloadCoordinator</c> picked templates up on assembly load — it has no EQS code; there is
    /// no EQS hot reload (<c>CE-465</c>, design §16).</para>
    /// </summary>
    public sealed class EqsModule : IEcsModule, IDisposable
    {
        /// <inheritdoc/>
        public string Name => "Eqs";

        /// <inheritdoc/>
        /// <remarks>Runs asynchronously at 10 Hz against a SoD snapshot.</remarks>
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);

        private readonly EqsSolverSystem _solver = new();

        // Captured on first Tick so Dispose can release the native EqsResultPool ring buffer.
        private EntityRepository? _repo;

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime)
        {
            if (view is EntityRepository repo)
            {
                // Keep a handle so Dispose can free the Allocator.Persistent result pool.
                _repo = repo;

                // Lazy-init global solver state.
                if (!repo.HasSingleton<EqsSolverGlobalState>())
                    repo.SetSingletonUnmanaged(new EqsSolverGlobalState
                    {
                        MaxAccurateRaycastsPerSolverTick = 2048,
                        AccurateRaysSubmittedThisTick    = 0,
                    });

                // Reset per-tick ray budget at the start of each EqsModule.Tick before the solver runs.
                ref var gs = ref repo.GetSingletonUnmanaged<EqsSolverGlobalState>();
                gs.AccurateRaysSubmittedThisTick = 0;
            }
            _solver.Execute(view, deltaTime);
        }

        /// <summary>
        /// Releases the <see cref="EqsResultPool"/> native ring buffer.
        /// </summary>
        /// <remarks>
        /// <para>⚠ <b>This module is not the pool's owner, and this free is a stop-gap.</b> The pool is
        /// allocated by <c>NavigationSolverComponentRegistry.RegisterAll</c> (with a guarded lazy fallback
        /// in <see cref="EqsSolverSystem"/>), together with three sibling persistent arrays —
        /// <c>PathfindingBatchData</c>, <c>AreaQueryBatchData</c> and <c>EqsTargetPool</c> — that no
        /// production code frees at all. <c>NavigationSolverComponentRegistry.DisposeAll</c> is the
        /// symmetric counterpart; it has no host caller yet because no host exposes a world-teardown hook.
        /// Giving those four pools a single owner with a lifetime is <c>B4</c>'s job (see
        /// <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1o).</para>
        ///
        /// <para>Freeing through a <c>ref</c> clears the stored handle's <c>IsCreated</c> flag, so a later
        /// <c>DisposeAll</c> on the same world is a no-op rather than a double free.</para>
        ///
        /// <para>The kernel disposes modules implementing <see cref="IDisposable"/> on teardown (see
        /// <c>ModuleHostKernel</c>).</para>
        /// </remarks>
        public void Dispose()
        {
            if (_repo is null) return;

            if (_repo.HasSingleton<EqsResultPool>())
            {
                ref var pool = ref _repo.GetSingletonUnmanaged<EqsResultPool>();
                if (pool.Results.IsCreated)
                    pool.Results.Dispose();
            }

            _repo = null;
        }
    }
}
