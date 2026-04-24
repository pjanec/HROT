namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// Provides a human-readable profile name for an ECS module system.
    ///
    /// <para>
    /// Implement this interface on adapter types that wrap legacy systems so the
    /// <c>SystemScheduler</c> can resolve a clean display name (e.g.
    /// <c>"CarKinematicsSystem"</c>) instead of the generic adapter type name
    /// (e.g. <c>"ComponentSystemAdapter`1"</c>) in the Architecture Diagnostics
    /// Window.
    /// </para>
    /// </summary>
    public interface IProfiledSystem
    {
        /// <summary>Gets the display name used for profiling and diagnostics.</summary>
        string ProfileName { get; }
    }
}
