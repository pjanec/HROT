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
    public sealed class PhysicsQueryModule : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "PhysicsQuery";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly RaycastSolverSystem _raycastSolver = new();
        private readonly HitResolutionSystem  _hitResolution = new();

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new RaycastResultMaterializationSystem());
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float dt)
        {
            _raycastSolver.Execute(view, dt);
            _hitResolution.Execute(view, dt);
        }
    }
}
