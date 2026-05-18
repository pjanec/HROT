namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// A focused unit of logic that operates on components (Stateless).
    /// Systems execute in a deterministic order based on declared dependencies.
    /// Differs from Fdp.Core.ComponentSystem by signature (receives View).
    /// </summary>
    public interface IEcsModuleSystem
    {
        /// <summary>
        /// Execute system logic.
        /// Called by scheduler in dependency order.
        /// </summary>
        /// <param name="view">Read-only simulation view</param>
        /// <param name="deltaTime">Time since last execution (seconds)</param>
        void Execute(ISimulationView view, float deltaTime);
    }
}
