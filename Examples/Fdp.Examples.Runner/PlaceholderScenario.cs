using Fdp.Examples.Common;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using Fdp.ModuleHost_Core;

namespace Fdp.Examples.Runner
{
    /// <summary>
    /// Minimal scenario used to exercise the runner plumbing (NLog setup, CLI, log file creation)
    /// without requiring any real toolkit modules. Succeeds at tick 1.
    /// </summary>
    internal sealed class PlaceholderScenario : IScenario
    {
        public string ScenarioName => "placeholder";

        public void Configure(EntityRepository world, ModuleHostKernel kernel) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            FdpLog<PlaceholderScenario>.Info("[placeholder] Phase 1 PASSED tick={0}", currentTick);
            return currentTick >= 1;
        }

        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }
}
