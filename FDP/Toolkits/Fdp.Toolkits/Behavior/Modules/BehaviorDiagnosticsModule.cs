using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Fdp.Toolkit.Behavior.Modules
{
    /// <summary>
    /// Hosts behavior diagnostics systems outside pause-gated logic packs.
    /// </summary>
    public sealed class BehaviorDiagnosticsModule : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "BehaviorDiagnostics";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new DebugStatePatchSystem());
            registry.RegisterSystem(new TraceBufferLifecycleSystem());
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime) { }
    }
}
