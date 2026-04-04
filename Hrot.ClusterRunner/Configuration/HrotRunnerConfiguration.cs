using CommandLine;
using Newtonsoft.Json;

namespace Hrot.ClusterRunner.Configuration
{
    /// <summary>
    /// Hrot-specific runner configuration extending the generic base.
    ///
    /// <para>Adds <c>--mode</c> (which subsystems to host), <c>--config</c>
    /// (JSON override file), and the validation/parsing logic that is specific
    /// to Hrot's <see cref="RunMode"/> concept.</para>
    /// </summary>
    public class HrotRunnerConfiguration : FDP.Framework.Runner.RunnerConfiguration
    {
        // ── Hrot-specific CLI options ───────────────────────────────────────

        /// <summary>Mode string supplied via --mode.  Examples: all, simhost, ig, ios, orchestrator, cgf, ci, simhost,ig, orchestrator,cgf</summary>
        [Option('m', "mode", Required = true, HelpText = "all|simhost|ig|ios|orchestrator|cgf|ci|simhost,ig|orchestrator,cgf")]
        public string ModeString { get; set; } = string.Empty;

        /// <summary>Scenario name forwarded to <see cref="ScenarioSubsystem"/> when <c>--mode ci</c>.</summary>
        [Option('s', "scenario", Required = false, HelpText = "Scenario name for --mode ci (e.g. MinimalCI_01)")]
        public string ScenarioName { get; set; } = string.Empty;

        /// <summary>Optional JSON config file that overrides CLI defaults.</summary>
        [Option('c', "config", HelpText = "JSON config file path")]
        public string ConfigFile { get; set; } = string.Empty;

        // ── Parsed values ─────────────────────────────────────────────────────

        /// <summary>Parsed subsystem flags. Set by <see cref="Validate"/>.</summary>
        public RunMode ParsedMode { get; set; }

        // ── Validation ────────────────────────────────────────────────────────

        /// <summary>
        /// Parses <see cref="ModeString"/> into <see cref="ParsedMode"/>,
        /// <see cref="FDP.Framework.Runner.RunnerConfiguration.WaitForString"/> into
        /// <see cref="FDP.Framework.Runner.RunnerConfiguration.WaitForPeers"/>,
        /// and enforces logical constraints.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the mode string is invalid or peer-wait rules are violated.
        /// </exception>
        public void Validate()
        {
            // Parse mode string → ParsedMode
            ParsedMode = ParseModeString(ModeString);
            if (ParsedMode == RunMode.None)
                throw new InvalidOperationException(
                    $"Invalid mode: '{ModeString}'. Use: all, simhost, ig, ios, orchestrator, or comma-separated combination.");

            // Parse wait-for list → WaitForPeers
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

            // CI mode is always standalone (no peer synchronisation required).
            if (ParsedMode == RunMode.CI) return;

            // Editor mode is always standalone — must not be combined with distributed flags.
            if (ParsedMode.HasFlag(RunMode.Editor) &&
                (ParsedMode & (RunMode.IG | RunMode.ExCon | RunMode.Orchestrator | RunMode.CGF)) != 0)
            {
                throw new InvalidOperationException(
                    "RunMode.Editor must not be combined with distributed flags (IG, ExCon, Orchestrator, CGF).");
            }

            // Editor mode is always standalone (no peer synchronisation required).
            if (ParsedMode == RunMode.Editor) return;

            // When launching a single subsystem that must synchronise with others,
            // --wait-for must be supplied (unless --no-wait suppresses synchronisation).
            if (!NoWait && WaitForPeers.Count == 0 && ParsedMode != RunMode.All && ParsedMode != RunMode.Orchestrator)
                throw new InvalidOperationException(
                    "--wait-for required when launching separate subsystems without --no-wait.");
        }

        // ── JSON config merge ─────────────────────────────────────────────────

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
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static RunMode ParseModeString(string str)
        {
            var lower = str.ToLowerInvariant().Trim();

            if (lower == "all")          return RunMode.All;
            if (lower == "simhost")      return RunMode.SimHost;
            if (lower == "ig")           return RunMode.IG;
            if (lower == "excon")        return RunMode.ExCon;
            if (lower == "ios")          return RunMode.ExCon;
            if (lower == "orchestrator") return RunMode.Orchestrator;
            if (lower == "cgf")          return RunMode.CGF;
            if (lower == "ci")           return RunMode.CI;
            if (lower == "editor")       return RunMode.Editor;
            if (lower == "demo")         return RunMode.Demo;

            // Comma-separated combination (e.g. "simhost,ig" or "orchestrator,cgf")
            RunMode result = RunMode.None;
            foreach (var part in lower.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.Trim())
                {
                    case "simhost":      result |= RunMode.SimHost;      break;
                    case "ig":           result |= RunMode.IG;           break;
                    case "excon":          result |= RunMode.ExCon;      break;
                    case "ios":          result |= RunMode.ExCon;        break;
                    case "orchestrator": result |= RunMode.Orchestrator; break;
                    case "cgf":          result |= RunMode.CGF;          break;
                    case "editor":       result |= RunMode.Editor;       break;
                    default:             return RunMode.None; // Any invalid token → reject entire string
                }
            }

            return result;
        }
    }
}
