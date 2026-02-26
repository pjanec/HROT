using CommandLine;
using Newtonsoft.Json;

namespace Bagira.Runner.Configuration
{
    /// <summary>
    /// Runtime configuration for the Runner process.
    /// CLI options are parsed via <c>CommandLineParser</c>; a JSON config file
    /// can override any CLI default via <see cref="MergeFromJsonFile"/>.
    /// Call <see cref="Validate"/> after parsing to hydrate <see cref="ParsedMode"/>
    /// and <see cref="WaitForPeers"/>.
    /// </summary>
    public class RunnerConfiguration
    {
        // ── CLI options ───────────────────────────────────────────────────────

        /// <summary>Mode string supplied via --mode.  Examples: all, simhost, ig, ios, simhost,ig</summary>
        [Option('m', "mode", Required = true, HelpText = "all|simhost|ig|ios|simhost,ig")]
        public string ModeString { get; set; } = string.Empty;

        /// <summary>DDS domain ID (default 0).</summary>
        [Option('d', "domain", Default = 0, HelpText = "DDS domain ID")]
        public int DomainId { get; set; }

        /// <summary>Run without UI (no Raylib window, no ImGui).</summary>
        [Option("headless", Default = false, HelpText = "Run without UI")]
        public bool Headless { get; set; }

        /// <summary>Skip waiting-room synchronisation and start immediately.</summary>
        [Option("no-wait", Default = false, HelpText = "Skip waiting room sync")]
        public bool NoWait { get; set; }

        /// <summary>Comma-separated list of subsystem names to wait for: simhost,ig,ios</summary>
        [Option("wait-for", HelpText = "simhost,ig,ios (comma-separated)")]
        public string WaitForString { get; set; } = string.Empty;

        /// <summary>Optional JSON config file that overrides CLI defaults.</summary>
        [Option('c', "config", HelpText = "JSON config file path")]
        public string ConfigFile { get; set; } = string.Empty;

        // ── Parsed values (populated by Validate) ────────────────────────────

        /// <summary>Parsed subsystem flags. Set by <see cref="Validate"/>.</summary>
        public RunMode ParsedMode { get; set; }

        /// <summary>Lower-case subsystem names to wait for. Set by <see cref="Validate"/>.</summary>
        public HashSet<string> WaitForPeers { get; set; } = new();

        // ── Validation ────────────────────────────────────────────────────────

        /// <summary>
        /// Parses <see cref="ModeString"/> into <see cref="ParsedMode"/>,
        /// <see cref="WaitForString"/> into <see cref="WaitForPeers"/>,
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
                    $"Invalid mode: '{ModeString}'. Use: all, simhost, ig, ios, or comma-separated combination.");

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

            // When launching a single subsystem that must synchronise with others,
            // --wait-for must be supplied (unless --no-wait suppresses synchronisation).
            if (!NoWait && WaitForPeers.Count == 0 && ParsedMode != RunMode.All)
                throw new InvalidOperationException(
                    "--wait-for required when launching separate subsystems without --no-wait.");
        }

        // ── JSON config merge ─────────────────────────────────────────────────

        /// <summary>
        /// Merges non-default JSON values over CLI defaults.
        /// Numeric overrides only apply when the JSON value differs from the C# default.
        /// </summary>
        /// <param name="path">Path to the JSON file.</param>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        public void MergeFromJsonFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Config file not found: {path}");

            var json = File.ReadAllText(path);
            var overrides = JsonConvert.DeserializeObject<RunnerConfiguration>(json);
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

        /// <summary>
        /// Converts a mode string ("all", "simhost", "ig", "ios", or comma-separated)
        /// to the corresponding <see cref="RunMode"/> flags.
        /// Returns <see cref="RunMode.None"/> for any unrecognised token.
        /// </summary>
        private static RunMode ParseModeString(string str)
        {
            var lower = str.ToLowerInvariant().Trim();

            if (lower == "all")     return RunMode.All;
            if (lower == "simhost") return RunMode.SimHost;
            if (lower == "ig")      return RunMode.IG;
            if (lower == "ios")     return RunMode.IOS;

            // Comma-separated combination (e.g. "simhost,ig")
            RunMode result = RunMode.None;
            foreach (var part in lower.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.Trim())
                {
                    case "simhost": result |= RunMode.SimHost; break;
                    case "ig":      result |= RunMode.IG;      break;
                    case "ios":     result |= RunMode.IOS;     break;
                    default:        return RunMode.None; // Any invalid token → reject entire string
                }
            }
            return result;
        }
    }
}
