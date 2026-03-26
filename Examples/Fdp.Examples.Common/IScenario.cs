using Fdp.Kernel;
using ModuleHost.Core;
using FDP.Toolkit.Vis2D;

namespace Fdp.Examples.Common
{
    /// <summary>
    /// Contract for all CI-testable scenario scripts.
    /// Implementations must be deterministic and must not reference Raylib or wall-clock time.
    /// </summary>
    public interface IScenario
    {
        /// <summary>Unique scenario key used by the CLI --scenario flag.</summary>
        string ScenarioName { get; }

        /// <summary>
        /// Called once. Register toolkits and spawn entities here.
        /// The world and kernel are fully configured before EvaluateTick is called.
        /// </summary>
        void Configure(EntityRepository world, ModuleHostKernel kernel);

        /// <summary>
        /// Called every tick AFTER the time controller is stepped but BEFORE kernel.Update().
        /// May inject events or mutate state to simulate external stimuli.
        /// Returns <c>true</c> when the scenario's success condition is met (CI pass).
        /// Throws <see cref="ScenarioFailureException"/> with a diagnostic message on any failure.
        /// </summary>
        bool EvaluateTick(uint currentTick, EntityRepository world);

        /// <summary>
        /// Optional: register 2D visualizers on the MapCanvas for human observation.
        /// Called only when --attach-vis2d is set. Must be a no-op otherwise.
        /// </summary>
        void ConfigureVisuals(MapCanvas? canvas, EntityRepository world);

        /// <summary>
        /// Called by <see cref="ScenarioSubsystem"/> during teardown, after the kernel
        /// has been disposed but <em>before</em> the world is disposed.
        /// Actual order: <c>_kernel.Dispose()</c> → <c>OnShutdown()</c> → <c>_world.Dispose()</c>.
        /// Override to release any unmanaged resources allocated in <see cref="Configure"/>
        /// (e.g. DDS participants, <c>PhysicsToolkitModule</c> NativeArrays) while the
        /// world singleton is still intact.  The default implementation is a no-op.
        /// </summary>
        void OnShutdown() { }
    }
}
