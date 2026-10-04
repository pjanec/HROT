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
    /// <para>⭐ The area query is an EQS template (<c>EntitiesOfForceInArea</c>) answered here; the old
    /// <c>AreaQuerySolverSystem</c> pipeline was retired on 2026-10-01 (EQS design §18).</para>
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
        /// <remarks>Runs asynchronously at 10 Hz against a SoD snapshot. ⭐ CE-3038 — the 400 ms timeout moved here with
        /// vision (it was <c>CognitiveSpatialModule</c>'s, CE-3032): the breaker is for HANGS; the deterministic work budget
        /// (<see cref="EqsSolverSystem.BudgetUnits"/>) is what bounds a tick.</remarks>
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10).WithTimeout(400);

        private readonly EqsSolverSystem _solver = new();

        // ⭐ CE-3038 — the perception grid lives with the module that READS it (the visual template, the area query).
        //   🔴 It used to be rebuilt on CognitiveSpatialModule's thread, so this module could not touch it.
        private readonly Fdp.Toolkit.Perception.Modules.PerceptionGridProvider? _ownedGrid;
        private readonly Fdp.Toolkit.Perception.Systems.LocalGridBuilderSystem _gridBuilder;

        // Captured on first Tick so Dispose can release the native EqsResultPool ring buffer.
        private EntityRepository? _repo;

        /// <summary>A module with its own perception grid at its composition-time placement (no terrain to follow).</summary>
        public EqsModule() : this(null, null) { }

        /// <param name="grid">The perception grid's owner; null ⇒ this module allocates (and frees) its own.</param>
        /// <param name="terrainSource">⭐ CE-3018 — the live terrain the grid rebases to; null ⇒ fixed placement.</param>
        public EqsModule(Fdp.Toolkit.Perception.Modules.PerceptionGridProvider? grid,
                         Func<Fdp.Toolkit.Terrain.TerrainWorld?>? terrainSource)
        {
            _ownedGrid   = grid is null ? new Fdp.Toolkit.Perception.Modules.PerceptionGridProvider() : null;
            PerceptionGrid = (grid ?? _ownedGrid!).Grid;
            _gridBuilder = new Fdp.Toolkit.Perception.Systems.LocalGridBuilderSystem(PerceptionGrid, terrainSource);
            FollowsTerrain = terrainSource != null;
        }

        /// <summary>
        /// ⭐⭐ The module as EVERY terrain host composes it (SimHost, Stride, the editor's in-process muscle): a perception
        /// grid that follows the resident terrain (CE-3018) and the VISUAL template over it with 3-D sight through that
        /// terrain (docs/DESIGN_Terrain_World.md §4.3; docs/DESIGN_Sensors_And_Doctrine.md §5.5). ⭐ One factory, so a
        /// host cannot wire the sight test and forget the grid — both read the same live source.
        /// </summary>
        public static EqsModule ForTerrainHost(EntityRepository world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var module = new EqsModule(null, Fdp.Toolkit.Terrain.TerrainWorldSource.Live(world));
            var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(world);
            Fdp.Toolkit.Perception.Sensors.VisualPerception.Register(registry, module.PerceptionGrid,
                Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.ForLiveWorld(world));
            return module;
        }

        /// <summary>The perception grid this module rebuilds each tick and the visual template reads.</summary>
        public CarKinem.Spatial.SpatialHashGrid PerceptionGrid { get; }

        /// <summary>True when the perception grid follows the resident terrain — what a composition rail asserts.</summary>
        public bool FollowsTerrain { get; }

        /// <summary>The solver (diagnostics / rails).</summary>
        public EqsSolverSystem Solver => _solver;

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
            if (deltaTime > 0f) _gridBuilder.Execute(view, deltaTime);   // the grid first: the visual template reads it
            _solver.Execute(view, deltaTime);
        }

        /// <summary>
        /// Releases the <see cref="EqsResultPool"/> native ring buffer.
        /// </summary>
        /// <remarks>
        /// <para>⚠ <b>This module is not the pool's owner, and this free is a stop-gap.</b> The pool is
        /// allocated by <c>NavigationSolverComponentRegistry.RegisterAll</c> (with a guarded lazy fallback
        /// in <see cref="EqsSolverSystem"/>), together with a sibling persistent array —
        /// <c>PathfindingBatchData</c> — that no production code frees at all *(two more,
        /// <c>AreaQueryBatchData</c> and <c>EqsTargetPool</c>, went with the AreaQuery pipeline)*. <c>NavigationSolverComponentRegistry.DisposeAll</c> is the
        /// symmetric counterpart; it has no host caller yet because no host exposes a world-teardown hook.
        /// Giving those pools a single owner with a lifetime is <c>B4</c>'s job (see
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
            _ownedGrid?.Dispose();
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
