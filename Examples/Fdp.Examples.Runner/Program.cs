using CommandLine;
using Fdp.Examples.Common;
using FDP.Framework.Runner;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Fdp.Examples.Runner
{
    /// <summary>
    /// Entry point for the <c>fdp-demo-runner</c> CLI executable.
    /// Parses arguments, configures NLog, creates the scenario, and runs the orchestrator.
    /// </summary>
    internal static class Program
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public static int Main(string[] args)
            => RunMain(args, Console.Out, code => Environment.Exit(code));

        /// <summary>
        /// Testable entry point — identical to <see cref="Main"/> but accepts injectable
        /// <paramref name="stdout"/> and <paramref name="exitCallback"/> so tests can capture
        /// output and exit codes without calling <see cref="Environment.Exit"/>.
        /// </summary>
        internal static int RunMain(
            string[] args,
            TextWriter stdout,
            Action<int> exitCallback)
        {
            int capturedCode = -1;

            var result = CommandLine.Parser.Default.ParseArguments<DemoRunnerOptions>(args);

            return result.MapResult(
                opts => Execute(opts, stdout, code =>
                {
                    capturedCode = code;
                    exitCallback(code);
                }),
                _ =>
                {
                    // CommandLine already printed usage; return non-zero.
                    exitCallback(1);
                    return 1;
                });
        }

        // ── Core execution ────────────────────────────────────────────────────

        private static int Execute(
            DemoRunnerOptions opts,
            TextWriter stdout,
            Action<int> exitCallback)
        {
            // 1. Build scenario (validates name early — exits non-zero on unknown).
            IScenario scenario;
            try
            {
                scenario = ScenarioRegistry.Create(opts.Scenario);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"[RUNNER] Error: {ex.Message}");
                exitCallback(1);
                return 1;
            }

            // 2. Configure NLog programmatically (no external NLog.config needed for CI).
            string logPath = ConfigureNLog(opts.Scenario);
            stdout.WriteLine($"[RUNNER] Log: {logPath}");
            stdout.Flush();

            // 3. Build RunnerOptions from CLI flags.
            //    Default to headless + deterministic when vis2d is not requested.
            bool headless = !opts.AttachVis2d;
            bool deterministic = opts.Deterministic || headless; // deterministic implied by headless

            var runnerOptions = new RunnerOptions
            {
                Headless          = headless,
                DomainId          = opts.DomainId,
                Deterministic     = deterministic,
                FixedDeltaSeconds = opts.FixedDeltaSeconds
            };

            // 4. Wire subsystem and orchestrator.
            int capturedCode = -1;
            var sub = new ScenarioSubsystem(
                scenario,
                opts.MaxTicks,
                code =>
                {
                    capturedCode = code;
                    exitCallback(code);
                },
                opts.FixedDeltaSeconds);

            var orch = new SubsystemOrchestrator(new[] { sub }, runnerOptions);
            sub.AttachOrchestrator(orch);

            try
            {
                orch.Initialize();
                orch.Run();
            }
            finally
            {
                orch.Shutdown();
                LogManager.Flush();
                LogManager.Shutdown();
            }

            return capturedCode;
        }

        // ── NLog setup ────────────────────────────────────────────────────────

        /// <summary>
        /// Configures NLog with a file target (Trace+) and console target (Info+).
        /// Returns the resolved log file path so the caller can print it to stdout.
        /// </summary>
        private static string ConfigureNLog(string scenarioName)
        {
            // Build a deterministic, human-readable timestamp suffix.
            var now = DateTime.Now;
            string timestamp = now.ToString("yyyyMMdd-HHmmss");
            string logDir    = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            string logFile   = Path.Combine(logDir, $"demo-{scenarioName}-{timestamp}.log");

            var config = new LoggingConfiguration();

            // ── File target: Trace and above ──────────────────────────────────
            var fileTarget = new FileTarget("logfile")
            {
                FileName    = logFile,
                Layout      = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}",
                KeepFileOpen = true,
                AutoFlush   = true
            };

            // ── Console target: Info and above ────────────────────────────────
            var consoleTarget = new ConsoleTarget("console")
            {
                Layout = "${level:uppercase=true} | ${logger:shortName=true} | ${message}"
            };

            config.AddRule(LogLevel.Trace, LogLevel.Fatal, fileTarget);
            config.AddRule(LogLevel.Info,  LogLevel.Fatal, consoleTarget);

            LogManager.Configuration = config;

            return logFile;
        }
    }
}
