using System;
using System.Threading;
using Bagira.Runner.Configuration;
using Bagira.Runner.Models;
using Bagira.Runner.Services;
using CommandLine;

namespace Bagira.SimHost.Standalone
{
    /// <summary>
    /// Thin standalone wrapper for the SimHost simulation kernel.
    ///
    /// <para>Delegates all simulation logic to <see cref="SimHostSubsystem"/>;
    /// this executable only handles CLI parsing and graceful shutdown.</para>
    ///
    /// <para>Usage: <c>Bagira.SimHost.Standalone [--domain 0]</c></para>
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default
                .ParseArguments<SimHostStandaloneCli>(args)
                .MapResult(Run, _ => 1);
        }

        static int Run(SimHostStandaloneCli opts)
        {
            Console.Title = "Bagira.SimHost.Standalone";
            Console.WriteLine($"[SimHost] Starting standalone — domain={opts.DomainId}");

            var config = new SubsystemConfig
            {
                DomainId      = opts.DomainId,
                Headless      = true,   // SimHost has no UI; always headless
                SubsystemName = "SimHost"
            };

            var subsystem = new SimHostSubsystem();

            try
            {
                subsystem.Initialize(config);
                subsystem.Start();

                Console.WriteLine("[SimHost] Running. Press Ctrl+C to exit.");

                var exitEvent = new ManualResetEventSlim(false);
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    exitEvent.Set();
                };

                exitEvent.Wait();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SimHost] Fatal error: {ex.Message}");
                return 1;
            }
            finally
            {
                subsystem.Stop();
                subsystem.Shutdown();
            }

            Console.WriteLine("[SimHost] Exited.");
            return 0;
        }
    }

    /// <summary>CLI options for the SimHost standalone launcher.</summary>
    class SimHostStandaloneCli
    {
        /// <summary>DDS domain ID (default 0).</summary>
        [Option('d', "domain", Default = 0, HelpText = "DDS domain ID.")]
        public int DomainId { get; set; }
    }
}
