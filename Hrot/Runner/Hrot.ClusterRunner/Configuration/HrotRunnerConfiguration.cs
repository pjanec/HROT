using CommandLine;
using Newtonsoft.Json;

namespace Hrot.ClusterRunner.Configuration
{
    /// <summary>
    /// Hrot-specific runner configuration extending the generic base.
    ///
    /// <para>Adds <c>--mode</c> (which subsystems to host), <c>--config</c>
    /// (JSON override file), and the validation/parsing logic that is specific
    /// to Hrot's subsystem selection concept.</para>
    /// </summary>
    public class HrotRunnerConfiguration : Fdp.Toolkit.Runner.RunnerConfiguration
    {
        // -- Hrot-specific CLI options ----------------------------------------

        /// <summary>Mode string supplied via --mode.  Examples: all, simhost, ig, ios, orchestrator, cgf, ci, simhost,ig, orchestrator,cgf</summary>
        [Option('m', "mode", Required = true, HelpText = "all|simhost|ig|ios|orchestrator|cgf|ci|simhost,ig|orchestrator,cgf")]
        public string ModeString { get; set; } = string.Empty;

        /// <summary>Scenario name forwarded to <see cref="ScenarioSubsystem"/> when <c>--mode ci</c>.</summary>
        [Option('s', "scenario", Required = false, HelpText = "Scenario name for --mode ci (e.g. MinimalCI_01)")]
        public string ScenarioName { get; set; } = string.Empty;

        /// <summary>Network protocol: <c>ned</c> (default) or <c>bdc</c>.</summary>
        [Option("network", Default = "ned", HelpText = "Network protocol: ned (default) or bdc")]
        public string NetworkProtocol { get; set; } = "ned";

        /// <summary>Directory for NLog file target output. Defaults to <c>AppContext.BaseDirectory\logs</c>.</summary>
        [Option("log-dir", Required = false, HelpText = "Directory for log file output. Defaults to <AppBase>\\logs.")]
        public string LogDirectory { get; set; } = string.Empty;

        /// <summary>Optional JSON config file that overrides CLI defaults.</summary>
        [Option('c', "config", HelpText = "JSON config file path")]
        public string ConfigFile { get; set; } = string.Empty;

        /// <summary>
        /// Relative path segments to the AI Behaviors project file used for hot-reloading BTrees.
        /// When relative, the system traverses parent directories from the CWD looking for this path.
        /// Defaults to the standard workspace layout.
        /// Can be overridden via JSON config file.
        /// </summary>
        public string[] AiBehaviorsProjectPath { get; set; } = new[] { "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj" };

        // -- Parsed values ---------------------------------------------------

        /// <summary>Parsed set of requested subsystem names. Set by <see cref="Validate"/>.</summary>
        public HashSet<string> RequestedSubsystems { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        // -- Validation ------------------------------------------------------

        /// <summary>
        /// Parses <see cref="ModeString"/> into <see cref="RequestedSubsystems"/>,
        /// <see cref="Fdp.Toolkit.Runner.RunnerConfiguration.WaitForString"/> into
        /// <see cref="Fdp.Toolkit.Runner.RunnerConfiguration.WaitForPeers"/>,
        /// and enforces logical constraints.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the mode string is invalid or peer-wait rules are violated.
        /// </exception>
        public void Validate()
        {
            // Expand "all" and "demo" shorthands before splitting
            string expandedMode = ModeString.Trim().ToLowerInvariant();
            if (expandedMode == "all" || expandedMode == "demo")
                expandedMode = "orchestrator,simhost,ig,excon,cgf";

            // Parse mode string -> RequestedSubsystems
            RequestedSubsystems.Clear();
            foreach (var name in expandedMode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // "ios" is a legacy alias for "excon"
                var normalized = name == "ios" ? "excon" : name;
                var validNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "simhost", "ig", "excon", "orchestrator", "cgf", "ci", "editor", "stridemock" };
                if (!validNames.Contains(normalized))
                    throw new InvalidOperationException(
                        $"Invalid mode: '{ModeString}'. Use: all, simhost, ig, ios, orchestrator, or comma-separated combination.");
                RequestedSubsystems.Add(normalized);
            }
            if (RequestedSubsystems.Count == 0)
                throw new InvalidOperationException(
                    $"Invalid mode: '{ModeString}'. Use: all, simhost, ig, ios, orchestrator, or comma-separated combination.");

            // Parse wait-for list -> WaitForPeers
            if (!string.IsNullOrWhiteSpace(WaitForString))
            {
                WaitForPeers = new HashSet<string>(
                    WaitForString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim().ToLowerInvariant()));

                // Validate each peer name
                var validPeers = new HashSet<string> { "simhost", "ig", "ios" };
                foreach (var peer in WaitForPeers)
                {
                    if (!validPeers.Contains(peer))
                        throw new InvalidOperationException(
                            $"Invalid wait-for peer: '{peer}'. Valid values: simhost, ig, ios.");
                }
            }

            // Validate network protocol.
            if (!string.Equals(NetworkProtocol, "ned", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(NetworkProtocol, "bdc", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Unknown --network value: '{NetworkProtocol}'. Use 'ned' or 'bdc'.");

            // CI mode is always standalone (no peer synchronisation required).
            if (RequestedSubsystems.Contains("ci")) return;

            // Editor mode is always standalone - must not be combined with distributed flags.
            if (RequestedSubsystems.Contains("editor") &&
                (RequestedSubsystems.Contains("ig") || RequestedSubsystems.Contains("excon") ||
                 RequestedSubsystems.Contains("orchestrator") || RequestedSubsystems.Contains("cgf")))
            {
                throw new InvalidOperationException(
                    "Editor must not be combined with distributed flags (IG, ExCon, Orchestrator, CGF).");
            }

            // Editor mode is always standalone (no peer synchronisation required).
            if (RequestedSubsystems.Contains("editor")) return;

            // When launching a single subsystem that must synchronise with others,
            // --wait-for must be supplied (unless --no-wait suppresses synchronisation).
            bool isAll = RequestedSubsystems.Contains("orchestrator") &&
                         RequestedSubsystems.Contains("simhost") &&
                         RequestedSubsystems.Contains("ig") &&
                         RequestedSubsystems.Contains("excon") &&
                         RequestedSubsystems.Contains("cgf");
            bool isOrchestratorOnly = RequestedSubsystems.Count == 1 && RequestedSubsystems.Contains("orchestrator");
            if (!NoWait && WaitForPeers.Count == 0 && !isAll && !isOrchestratorOnly)
                throw new InvalidOperationException(
                    "--wait-for required when launching separate subsystems without --no-wait.");
        }

        // -- JSON config merge -----------------------------------------------

        /// <summary>
        /// Merges non-default JSON values over CLI defaults.
        /// </summary>
        /// <param name="path">Path to the JSON file.</param>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        public void MergeFromJsonFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Config file not found: {path}");

            var json = File.ReadAllText(path);
            var overrides = JsonConvert.DeserializeObject<HrotRunnerConfiguration>(json);
            if (overrides is null) return;

            // Merge non-empty / non-default values
            if (!string.IsNullOrEmpty(overrides.ModeString))
                ModeString = overrides.ModeString;
            if (overrides.DomainId != 0)
                DomainId = overrides.DomainId;
            if (overrides.Headless)
                Headless = overrides.Headless;
            if (overrides.NoWait)
                NoWait = overrides.NoWait;
            if (!string.IsNullOrEmpty(overrides.WaitForString))
                WaitForString = overrides.WaitForString;
            if (!string.IsNullOrEmpty(overrides.ConfigFile))
                ConfigFile = overrides.ConfigFile;
            if (!string.IsNullOrEmpty(overrides.NetworkProtocol) &&
                !string.Equals(overrides.NetworkProtocol, "ned", StringComparison.OrdinalIgnoreCase))
                NetworkProtocol = overrides.NetworkProtocol;
        }
    }
}