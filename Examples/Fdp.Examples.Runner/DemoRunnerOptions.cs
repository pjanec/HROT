using CommandLine;
using Fdp.Toolkit.Runner;

namespace Fdp.Examples.Runner
{
    /// <summary>
    /// CLI options for the demo runner, extending <see cref="RunnerConfiguration"/> with
    /// demo-specific flags.
    /// </summary>
    public class DemoRunnerOptions : RunnerConfiguration
    {
        /// <summary>Name of the scenario to execute (e.g. "autodrive", "sensorGrid").</summary>
        [Option("scenario", Required = true, HelpText = "Scenario name (e.g. autodrive, sensorGrid)")]
        public string Scenario { get; set; } = string.Empty;

        /// <summary>Maximum number of ticks before the runner declares a timeout (exit 2).</summary>
        [Option("max-ticks", Default = 500, HelpText = "Tick budget before timeout")]
        public int MaxTicks { get; set; } = 500;

        /// <summary>When <c>true</c>, a Raylib Vis2D window is opened for human observation.</summary>
        [Option("attach-vis2d", Default = false, HelpText = "Spawn Raylib window with 2D map")]
        public bool AttachVis2d { get; set; }
    }
}
