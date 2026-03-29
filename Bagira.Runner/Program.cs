using CommandLine;
using Bagira.Map.Common;
using Bagira.Runner.Configuration;
using Bagira.Runner.Services;
using CycloneDDS.Runtime;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Bagira.Runner;

/// <summary>
/// Entry point for the Bagira Runner process.
///
/// <para>The Runner can host one or more subsystems in a single process
/// (<c>--mode all</c> or <c>--mode simhost,ig</c>) or launch a single
/// subsystem in standalone mode (<c>--mode simhost</c>).
/// When running in standalone mode, the Waiting Room protocol
/// synchronises startup with peer processes unless <c>--no-wait</c> is supplied.
/// </para>
///
/// <para><b>CLI arguments:</b>
/// <list type="table">
///   <item><term>-m / --mode</term><description>all | simhost | ig | ios | comma-separated</description></item>
///   <item><term>-d / --domain</term><description>DDS domain ID (default: 0)</description></item>
///   <item><term>--headless</term><description>Run without UI</description></item>
///   <item><term>--no-wait</term><description>Skip waiting room synchronisation</description></item>
///   <item><term>--wait-for</term><description>Comma-separated peers to wait for</description></item>
///   <item><term>-c / --config</term><description>JSON config file path</description></item>
/// </list>
/// </para>
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        // Enable NLog Console Output globally for FdpLog<T>
        var logConfig = new LoggingConfiguration();
        var logConsole = new ColoredConsoleTarget("logConsole")
        {
            Layout = "${time} | ${level:uppercase=true:padding=-5} | ${logger:shortName=true} | ${message}${exception:format=tostring}"
        };
        logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, logConsole);
        LogManager.Configuration = logConfig;

        // Parse CLI args
        BagiraRunnerConfiguration? config = null;
        var parseResult = Parser.Default.ParseArguments<BagiraRunnerConfiguration>(args);

        parseResult.WithParsed(c => config = c)
                   .WithNotParsed(_ => Environment.Exit(1));

        if (config is null)
            return 1;

        // Merge optional JSON config file
        if (!string.IsNullOrEmpty(config.ConfigFile))
        {
            try
            {
                config.MergeFromJsonFile(config.ConfigFile);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"[Runner] ERROR: {ex.Message}");
                return 1;
            }
        }

        // Validate (parses mode/wait-for strings)
        try
        {
            config.Validate();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[Runner] Configuration error: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"[Runner] Starting – mode={config.ParsedMode}, domain={config.DomainId}, headless={config.Headless}");

        // ── Waiting Room synchronisation ──────────────────────────────────────
        if (config.WaitForPeers.Any())
        {
            string subsystemName = config.ParsedMode == RunMode.All ? "all"
                : config.ParsedMode == RunMode.Orchestrator ? "orchestrator"
                : config.ParsedMode.HasFlag(RunMode.SimHost) ? "simhost"
                : config.ParsedMode.HasFlag(RunMode.IG)      ? "ig"
                : "ios";

            using var wrcParticipant = BagiraEnvironment.CreateParticipant(config.DomainId);
            using var coordinator   = new WaitingRoomCoordinator(
                wrcParticipant, Environment.ProcessId, subsystemName, config.WaitForPeers);
            try
            {
                coordinator.WaitForPeers();
            }
            catch (TimeoutException ex)
            {
                Console.Error.WriteLine($"[Runner] Waiting room timeout: {ex.Message}");
                return 1;
            }
        }

        // ── CI mode: headless deterministic scenario run ──────────────────
        if (config.ParsedMode == RunMode.CI)
        {
            if (string.IsNullOrWhiteSpace(config.ScenarioName))
            {
                Console.Error.WriteLine("[Runner] --scenario is required for --mode ci.");
                return 1;
            }

            Console.WriteLine($"[Runner] CI mode – scenario={config.ScenarioName}");

            var ciSub       = new CiSubsystem(config.ScenarioName);
            var ciOptions   = new RunnerOptions
            {
                Headless          = true,
                Deterministic     = true,
                FixedDeltaSeconds = 1.0f / 60.0f,
                DomainId          = config.DomainId
            };
            var ciOrchestrator = new SubsystemOrchestrator(new[] { (ISubsystem)ciSub }, ciOptions);
            ciSub.AttachOrchestrator(ciOrchestrator);

            ciOrchestrator.Initialize();
            ciOrchestrator.Run();
            ciOrchestrator.Shutdown();

            // ScenarioSubsystem calls Environment.Exit(code) before reaching here.
            // This return is a safety fallback.
            return 0;
        }

        // ── Build subsystems from mode ────────────────────────────────────────
        var subsystems = new List<ISubsystem>();
        if (config.ParsedMode.HasFlag(RunMode.Orchestrator)) subsystems.Add(new OrchestratorSubsystem());
        if (config.ParsedMode.HasFlag(RunMode.SimHost)) subsystems.Add(new SimHostSubsystem());
        if (config.ParsedMode.HasFlag(RunMode.IG))      subsystems.Add(new IgSubsystem());
        if (config.ParsedMode.HasFlag(RunMode.IOS))     subsystems.Add(new IosSubsystem());
        if (config.ParsedMode.HasFlag(RunMode.CGF))     subsystems.Add(new CgfSubsystem());

        var options = new RunnerOptions { Headless = config.Headless, DomainId = config.DomainId, NodeId = config.NodeId };

        // ── Create + run orchestrator ─────────────────────────────────────────
        var orchestrator = new SubsystemOrchestrator(subsystems, options);
        try
        {
            orchestrator.Initialize();
            orchestrator.Run();
        }
        finally
        {
            orchestrator.Shutdown();
        }

        Console.WriteLine("[Runner] Shutdown complete.");
        return 0;
    }
}

