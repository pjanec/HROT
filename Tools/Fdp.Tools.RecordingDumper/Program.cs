using System;
using System.IO;
using CommandLine;
using Fdp.Toolkit.ReplayBrowser;

namespace Fdp.Tools.RecordingDumper
{
    /// <summary>
    /// Entry point for fdp-recording-dumper.
    /// Exit codes: 0=success, 1=argument error, 2=file-not-found, 3=runtime error.
    /// </summary>
    internal static class Program
    {
        public static int Main(string[] args)
            => RunMain(args, Console.Out, Console.Error);

        /// <summary>
        /// Testable entry point: accepts injectable stdout/stderr to avoid real process exit.
        /// </summary>
        internal static int RunMain(string[] args, TextWriter stdout, TextWriter stderr)
        {
            var result = Parser.Default.ParseArguments<DumperOptions>(args);
            return result.MapResult(
                opts => Execute(opts, stdout, stderr),
                _ =>
                {
                    // CommandLine already printed usage to stderr.
                    return 1;
                });
        }

        private static int Execute(DumperOptions opts, TextWriter stdout, TextWriter stderr)
        {
            // --- Validate mutual exclusion of frame-based and time-based windowing ---
            bool hasFrameWindow = opts.StartFrame.HasValue || opts.EndFrame.HasValue;
            bool hasTimeWindow = opts.StartTimeSec.HasValue || opts.EndTimeSec.HasValue;
            if (hasFrameWindow && hasTimeWindow)
            {
                stderr.WriteLine("Error: --start-frame/--end-frame and --start-time/--end-time are mutually exclusive.");
                return 1;
            }

            // --- Validate input file ---
            if (!File.Exists(opts.Input))
            {
                stderr.WriteLine($"Error: Input file not found: {opts.Input}");
                return 2;
            }

            // --- Map options to JsonExportOptions ---
            var exportOpts = new JsonExportOptions
            {
                IncludeEvents = !opts.NoEvents,
                IncludeEntities = !opts.NoEntities,
                Minified = opts.Minified,
                FormatMode = opts.Changelog ? ExportFormatMode.Changelog : ExportFormatMode.AbsoluteState,
                EpsilonTolerance = opts.Epsilon,
            };

            if (hasFrameWindow)
            {
                exportOpts.WindowMode = ExportWindowMode.ByFrame;
                exportOpts.StartFrame = opts.StartFrame ?? 0;
                exportOpts.EndFrame = opts.EndFrame ?? int.MaxValue;
            }
            else if (hasTimeWindow)
            {
                exportOpts.WindowMode = ExportWindowMode.ByTime;
                exportOpts.StartTimeSec = opts.StartTimeSec ?? 0f;
                exportOpts.EndTimeSec = opts.EndTimeSec ?? float.PositiveInfinity;
            }

            if (opts.EntityIndex.HasValue)
            {
                exportOpts.FilterByEntityIndex = true;
                exportOpts.TargetEntityIndex = opts.EntityIndex.Value;
            }

            // --- Run export ---
            try
            {
                var svc = new RecordingExportService();
                svc.ExportToJson(opts.Input, opts.Output, exportOpts);
                stdout.WriteLine($"Exported: {opts.Output}");
                return 0;
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"Error during export: {ex.Message}");
                stderr.WriteLine(ex.StackTrace);
                return 3;
            }
        }
    }
}
