namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// Registry for system registration and scheduling.
    /// </summary>
    public interface ISystemRegistry
    {
        /// <summary>
        /// Register a system for execution.
        /// System's phase and dependencies are determined by attributes.
        /// </summary>
        void RegisterSystem<T>(T system) where T : IEcsModuleSystem;

        /// <summary>
        /// Registers a system in the Manual phase for diagnostics tracking.
        /// Returns a profiled wrapper. The module must tick the wrapper manually
        /// so execution time is recorded in the kernel's profiling UI.
        /// </summary>
        IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem;
    }
}
