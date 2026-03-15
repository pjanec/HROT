using Fdp.Kernel;
using FDP.Toolkit.Physics.Systems;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Physics.Modules
{
    /// <summary>
    /// Wraps <see cref="RaycastSolverSystem"/> and <see cref="HitResolutionSystem"/> into a
    /// reusable module that can be installed on any node role requiring synchronous raycast
    /// query resolution.
    ///
    /// <para><b>Execution model:</b> <see cref="ExecutionPolicy.Synchronous"/> — both systems
    /// run on the main simulation thread in the <c>InputSystemGroup</c>.</para>
    ///
    /// <para><b>Registration:</b> Because both systems extend <see cref="ComponentSystem"/>
    /// (not <see cref="IModuleSystem"/>), they must be registered into a <see cref="SystemGroup"/>
    /// via <see cref="RegisterSystems(SystemGroup)"/>.  The <see cref="IModule"/> overload
    /// <see cref="RegisterSystems(ISystemRegistry)"/> is a no-op and is provided only for
    /// API compliance.</para>
    /// </summary>
    public sealed class PhysicsQueryModule : IModule
    {
        /// <inheritdoc/>
        public string Name => "PhysicsQuery";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly RaycastSolverSystem  _raycastSolver  = new();
        private readonly HitResolutionSystem  _hitResolution  = new();

        /// <summary>
        /// Registers <see cref="RaycastSolverSystem"/> and <see cref="HitResolutionSystem"/>
        /// into the supplied <see cref="SystemGroup"/>.
        /// </summary>
        /// <param name="inputGroup">
        /// The input-phase system group (both systems carry
        /// <c>[UpdateInGroup(typeof(InputSystemGroup))]</c>).
        /// </param>
        public void RegisterSystems(SystemGroup inputGroup)
        {
            inputGroup.AddSystem(_raycastSolver);
            inputGroup.AddSystem(_hitResolution);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// No-op — both systems are <see cref="ComponentSystem"/> subclasses and cannot be
        /// registered via <see cref="ISystemRegistry"/>.  Use
        /// <see cref="RegisterSystems(SystemGroup)"/> instead.
        /// </remarks>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float dt) { }
    }
}
