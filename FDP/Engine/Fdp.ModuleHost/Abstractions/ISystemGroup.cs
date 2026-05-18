using System.Collections.Generic;

namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// A group of related systems for hierarchical organization and profiling.
    /// </summary>
    public interface ISystemGroup : IEcsModuleSystem
    {
        /// <summary>
        /// Name of this system group (for profiling/debugging).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// When false, inner systems are not executed.
        /// Defaults to true for groups that do not implement explicit toggling.
        /// </summary>
        bool Enabled => true;

        /// <summary>
        /// Systems contained in this group.
        /// </summary>
        IReadOnlyList<IEcsModuleSystem> GetSystems();
    }
}
