using System;
using Bagira.Runner.Configuration;
using Bagira.Runner.Services;
using CommandLine;

namespace Bagira.IOS.Standalone
{
    /// <summary>
    /// Thin standalone wrapper for the IOS (Interactive Operations Station).
    ///
    /// <para>Delegates all IOS logic to <see cref="IosSubsystem"/> via
    /// <see cref="SubsystemOrchestrator"/>; this executable only handles
    /// CLI parsing, window ownership, and the render loop.</para>
    ///
    /// <para>Usage:
    /// <c>Bagira.IOS.Standalone [--domain 0] [--node 10] [--headless]</c></para>
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default
                .ParseArguments<IosStandaloneCli>(args)
                .MapResult(Run, _ => 1);
        }

        static int Run(IosStandaloneCli opts)
        {
            Console.Title = "Bagira.IOS.Standalone";
            Console.WriteLine($"[IOS] Starting standalone — domain={opts.DomainId} headless={opts.Headless}");

            var runnerConfig = new RunnerConfiguration
            {
                ModeString = "ios",
                DomainId   = opts.DomainId,
                Headless   = opts.Headless,
                NoWait     = true   // no DDS waiting-room for standalone
            };

            try
            {
                runnerConfig.Validate();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[IOS] Configuration error: {ex.Message}");
                return 1;
            }

            var orchestrator = new SubsystemOrchestrator(runnerConfig);

            try
            {
                orchestrator.Initialize();
                orchestrator.Run();     // blocks until window is closed (or Stop is called)
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[IOS] Fatal error: {ex.Message}");
                return 1;
            }
            finally
            {
                orchestrator.Shutdown();
            }

            Console.WriteLine("[IOS] Exited.");
            return 0;
        }
    }

    /// <summary>CLI options for the IOS standalone launcher.</summary>
    class IosStandaloneCli
    {
        /// <summary>DDS domain ID (default 0).</summary>
        [Option('d', "domain", Default = 0, HelpText = "DDS domain ID.")]
        public int DomainId { get; set; }

        /// <summary>Run without Raylib window or ImGui (for automated testing).</summary>
        [Option("headless", Default = false, HelpText = "Run without UI (headless mode).")]
        public bool Headless { get; set; }
    }
}
