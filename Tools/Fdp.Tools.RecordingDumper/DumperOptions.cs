using CommandLine;
using Fdp.Toolkit.ReplayBrowser;

namespace Fdp.Tools.RecordingDumper
{
    /// <summary>
    /// Command-line options for fdp-recording-dumper.
    /// Maps directly to JsonExportOptions fields.
    /// </summary>
    internal sealed class DumperOptions
    {
        [Option('i', "input", Required = true, HelpText = "Path to the input .fdp recording file.")]
        public string Input { get; set; } = string.Empty;

        [Option('o', "output", Required = true, HelpText = "Path to the output .json file.")]
        public string Output { get; set; } = string.Empty;

        [Option('s', "start-frame", Required = false, HelpText = "First frame to export (ByFrame windowing).")]
        public int? StartFrame { get; set; }

        [Option('e', "end-frame", Required = false, HelpText = "Last frame to export inclusive (ByFrame windowing).")]
        public int? EndFrame { get; set; }

        [Option('t', "start-time", Required = false, HelpText = "Start time in seconds (ByTime windowing).")]
        public float? StartTimeSec { get; set; }

        [Option('u', "end-time", Required = false, HelpText = "End time in seconds (ByTime windowing).")]
        public float? EndTimeSec { get; set; }

        [Option("entity-id", Required = false, HelpText = "Export only entities with this ECS index.")]
        public int? EntityIndex { get; set; }

        [Option("no-events", Required = false, Default = false, HelpText = "Omit the Events block from output.")]
        public bool NoEvents { get; set; }

        [Option("no-entities", Required = false, Default = false, HelpText = "Omit the Entities block from output.")]
        public bool NoEntities { get; set; }

        [Option("minified", Required = false, Default = false, HelpText = "Write minified (non-indented) JSON.")]
        public bool Minified { get; set; }

        [Option("changelog", Required = false, Default = false, HelpText = "Use Changelog export mode.")]
        public bool Changelog { get; set; }

        [Option("epsilon", Required = false, Default = 0.001, HelpText = "Epsilon tolerance for changelog diff.")]
        public double Epsilon { get; set; }
    }
}
