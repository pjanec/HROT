using Fdp.Core;
using Fdp.Toolkit.Physics.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Physics.Modules
{
    /// <summary>
    /// Wraps <see cref="RaycastSolverSystem"/> and <see cref="HitResolutionSystem"/> into a
    /// reusable module that can be installed on any node role requiring synchronous raycast
    /// query resolution.
    ///
    /// <para><b>Execution model:</b> <see cref="ExecutionPolicy.Synchronous"/> — both systems
    /// run on the main simulation thread in the <c>InputSystemGroup</c>.</para>
    ///
    /// <para><b>Registration:</b> Exposes <see cref="InputSystems"/> — an
    /// <c>IReadOnlyList&lt;IEcsModuleSystem&gt;</c> containing <see cref="RaycastSolverSystem"/>
    /// and <see cref="HitResolutionSystem"/> — for wiring into a <see cref="SystemGroup"/> or
    /// the modern kernel.  The <see cref="IEcsModule"/> overload
    /// <see cref="RegisterSystems(ISystemRegistry)"/> is a no-op and is provided only for
    /// API compliance.</para>
    /// </summary>
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class PhysicsQueryModule : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "PhysicsQuery";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly RaycastSolverSystem _raycastSolver = new();
        private readonly HitResolutionSystem  _hitResolution = new();
        // ⭐ CE-3293 S2 — what Tick runs: the systems themselves until a kernel registers this module, then the scheduler's
        //   wrappers, so every tick passes the one gate (DESIGN_Paused_And_Stepped_Execution.md rule 3).
        private IEcsModuleSystem _raycastRun;
        private IEcsModuleSystem _hitRun;

        public PhysicsQueryModule()
        {
            _raycastRun = _raycastSolver;
            _hitRun     = _hitResolution;
        }

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new RaycastResultMaterializationSystem());
            _raycastRun = registry.RegisterManualSystem(_raycastSolver);
            _hitRun     = registry.RegisterManualSystem(_hitResolution);
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float dt)
        {
            _raycastRun.Execute(view, dt);
            _hitRun.Execute(view, dt);
        }
    }
}
