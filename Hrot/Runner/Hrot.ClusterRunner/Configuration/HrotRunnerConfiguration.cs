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
        [Option('m', "mode", Required = true, HelpText = "all|simhost|ig|ios|orchestrator|cgf|traffic|ci|editor|migrate|replaybrowser or comma-separated combination")]
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
        /// ⭐⭐ Batch 103 (103a) — restore the shipped default layout on every run. 🔒 <b>Defaults
        /// ON</b> per the user's ruling, while the layout is still evolving.
        /// ⚠ Destructive by design; the runner logs it every run so it is discoverable.
        /// ⭐ <c>--reset-layout=false</c> keeps your own arrangement.
        ///
        /// <para>⛔⛔ <b>The type is <c>bool?</c> ON PURPOSE, and it is the whole fix.</b> 📐 Measured
        /// against <c>CommandLineParser 2.9.1</c>, <c>2026-08-21</c>, with a plain <c>bool</c>:
        /// <list type="bullet">
        ///   <item>a <c>--no-</c>-prefixed spelling ⇒ <b><c>UnknownOptionError</c></b> — the runner
        ///   refuses to start. ⚠ And that was the flag the startup LOG told the user to use.</item>
        ///   <item><c>--reset-layout=false</c> ⇒ parses fine and the value stays <b><c>true</c></b>
        ///   — ⛔ a plain <c>bool</c> is a SWITCH, so its <c>=false</c> is discarded. ⚠ That was the
        ///   syntax the <c>HelpText</c> documented.</item>
        /// </list>
        /// ⇒ ⛔⛔ <b>there was NO working way to turn a DESTRUCTIVE default off</b>, and both documented
        /// escape hatches failed in different directions — one loudly, one silently.
        /// ⭐ <c>bool?</c> makes the option take a VALUE, so <c>--reset-layout=false</c> and
        /// <c>--reset-layout false</c> both land. 📌 Railed by
        /// <c>TheLayoutResetCanActuallyBeTurnedOffTests</c> — ⛔ a claim about a parser belongs in a
        /// test, not in a comment.</para>
        /// </summary>
        [Option("reset-layout", Default = true,
                HelpText = "Restore the shipped default window layout on start (default: true). "
                         + "Pass --reset-layout=false to keep your own arrangement.")]
        public bool? ResetLayout { get; set; }

        /// <summary>
        /// Relative path segments to the AI Behaviors project file used for hot-reloading BTrees.
        /// When relative, the system traverses parent directories from the CWD looking for this path.
        /// Defaults to the standard workspace layout.
        /// Can be overridden via JSON config file.
        /// </summary>
        public string[] AiBehaviorsProjectPath { get; set; } = new[] { "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj" };

        /// <summary>
        /// ⭐⭐⭐ <b>Ruling 67 — the configured authoring root, for a DEPLOYED node where there is no
        /// source tree to walk up to.</b>
        ///
        /// <para>🔒 <b>User, <c>2026-08-14</c>:</b> *"we need a <b>config file provided asset path</b> for
        /// the CGF as well as the Editor (<b>same shared code</b>), with <b>fallback to the repo source</b>
        /// as of now."* ⇒ empty means *"unset"*, and resolution falls through to
        /// <see cref="AiBehaviorsProjectPath"/>'s walk-up and then the output directory — ⭐ so a dev box
        /// behaves exactly as before.</para>
        ///
        /// <para>⚠ <b>A value that names a missing directory THROWS at startup</b>
        /// *(<c>AssetRoots.Configure</c>)* — the ruling's own call: silently falling through *"would
        /// reintroduce 'it worked on the dev box'."*</para>
        /// </summary>
        [Option("asset-root", Required = false,
                HelpText = "Absolute path to the authoring asset root (Assets/ and Recipes/ live under "
                         + "it). Ruling 67: required on a deployed node, where there is no source tree "
                         + "to discover. Unset = fall back to the source walk-up, then the output dir.")]
        public string AssetRoot { get; set; } = string.Empty;

        /// <summary>Target schema version for --mode migrate. -1 means current registered version.</summary>
        [Option("target-version", Required = false, Default = -1, HelpText = "Target schema version (-1 = current) for --mode migrate")]
        public int TargetVersion { get; set; } = -1;

        /// <summary>Input directory for --mode migrate. Defaults to current working directory.</summary>
        [Option("input-dir", Required = false, HelpText = "Directory to migrate (for --mode migrate). Defaults to current directory.")]
        public string InputDirectory { get; set; } = string.Empty;

        /// <summary>
        /// ⭐ CE-3218 — keep only the newest N exercise recordings per <c>exercises</c> directory at start-up (≤ 0 keeps all).
        /// Unset: <c>FDP_KEEP_EXERCISES</c>, else 20 on a scratch staging root (the temp dir), else keep all
        /// (<see cref="Fdp.Toolkit.Orchestration.ExerciseRetention"/>).
        /// </summary>
        [Option("keep-exercises", Required = false,
                HelpText = "Keep only the newest N exercise recordings at start-up (<= 0 keeps all). Default: FDP_KEEP_EXERCISES, "
                         + "else 20 when the staging root is the temp directory, else keep all.")]
        public int? KeepExercises { get; set; }

        /// <summary>⭐ <c>CE-1249</c> (BV-16, R-1027) — <c>off</c> attaches no brain journal to any unit (default <c>on</c>; read at attach,
        /// never toggled at runtime).</summary>
        [Option("brain-journal", Required = false, Default = "on",
                HelpText = "on (default) | off — the per-unit brain journal (last 24 decisions with causes, recorded)")]
        public string BrainJournal { get; set; } = "on";

        /// <summary>When true, --mode migrate reports what would be done without writing any files.</summary>
        [Option("dry-run", Required = false, Default = false, HelpText = "Report what would be done without writing files")]
        public bool DryRun { get; set; }

        /// <summary>
        /// ⭐ CE-4042 (docs/DESIGN_Scenario_Regression.md) — verify these scenarios (comma-separated, or <c>all</c> = every one with a
        /// <c>checks.json</c>) in the editor's stepped preview, each in a FRESH process, then exit 0 (all pass) or 1. With <c>--mode editor</c>.
        /// </summary>
        [Option("verify", Required = false,
                HelpText = "With --mode editor: verify these scenarios (a,b,c or all) — each judged by its checks.json and compared with its "
                         + "golden.json, each in a fresh process; exit 0 when every one passes, else 1")]
        public string Verify { get; set; } = string.Empty;

        /// <summary>⭐ CE-4042 — re-record each verified scenario's golden (refused for a failing run).</summary>
        [Option("verify-capture", Required = false, Default = false, HelpText = "With --verify: re-record each scenario's golden.json")]
        public bool VerifyCapture { get; set; }

        /// <summary>⭐ CE-4045 H1a (docs/DESIGN_Scenario_Regression.md §10-1, R-4085) — run each scenario LIVE (it records) and require its own
        /// recording, played back by the product's player, to hold the same world at every snapshot.</summary>
        [Option("verify-replay", Required = false, Default = false,
                HelpText = "With --verify: run each scenario LIVE and compare it, snapshot by snapshot, with its own recording played back "
                         + "(components the recording's manifest does not carry are listed apart, never failed)")]
        public bool VerifyReplay { get; set; }

        /// <summary>⭐ CE-4042 — the nightly's output folder: a log and a report per scenario, <c>summary.json</c> and <c>junit.xml</c>.</summary>
        [Option("verify-out", Required = false,
                HelpText = "With --verify: write each scenario's log and report, summary.json and junit.xml (for CI) to this folder")]
        public string VerifyOut { get; set; } = string.Empty;

        /// <summary>
        /// ⭐ <c>CE-3299</c> B5 — R1, the restore-equivalence oracle (docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1k): each scenario runs
        /// twice, each in a fresh process — straight, writing a checkpoint at this many seconds; then restored from it — and the two must
        /// hold the same full component state from then on. 0 = off.
        /// </summary>
        [Option("verify-restore-at", Required = false, Default = 0d,
                HelpText = "With --verify: R1 — checkpoint each scenario at this sim time (s), restore it in a second fresh process, and require "
                         + "the same full component state from then on; exit 0 when every one matches")]
        public double VerifyRestoreAt { get; set; }

        /// <summary>⭐ <c>CE-3299</c> B5 — R1's snapshot grid (s; default 1). Smaller to run a difference down step by step (1/60 = every step).</summary>
        [Option("verify-restore-every", Required = false, Default = 1d,
                HelpText = "With --verify-restore-at: snapshot every this many seconds (default 1; 0.0166667 = every step, to run a difference down)")]
        public double VerifyRestoreEvery { get; set; } = 1d;

        /// <summary>⭐ CE-4042 — a child's wall-clock limit in minutes (a hung run fails, never waits forever). 📐 <c>uc-urban-battle</c> (200 s of
        /// sim, ~40 men) steps ~9× slower than real time: 1751 s once, past 1800 s the next ⇒ the default is 60.</summary>
        [Option("verify-timeout", Required = false, Default = 60,
                HelpText = "With --verify: the wall-clock limit per scenario, in minutes (default 60); a scenario past it fails")]
        public int VerifyTimeout { get; set; } = 60;

        /// <summary>⭐ CE-4042 — write the verify report (JSON) here.</summary>
        [Option("verify-report", Required = false, HelpText = "With --verify: write the report (JSON) to this file")]
        public string VerifyReport { get; set; } = string.Empty;

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
                // ST-015: "stridemock" is gone with the mock subsystem. Dropping it from this set is
                // what makes `--mode stridemock` throw again instead of composing a subsystem that no
                // longer exists.
                // ⭐ HN-030: "dump-api" prints the debug-API manifest and exits — the artefact
                //   tools/ai-debug-mcp generates its tool catalog from. It composes NO subsystem (see
                //   Program.cs), which is why it sits beside "ci" and "migrate" rather than in the roster.
                var validNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "simhost", "ig", "excon", "orchestrator", "cgf", "ci", "editor",
                      "replaybrowser", "migrate", "dump-api",
                      "traffic" };   // ⭐ AQ93 N1 — the SUMO traffic node; NOT in "all" (the editor serves traffic embedded)
                if (!validNames.Contains(normalized))
                    throw new InvalidOperationException(
                        $"Invalid mode: '{ModeString}'. Use: all, simhost, ig, ios, orchestrator, editor, cgf, traffic, ci, migrate, dump-api, replaybrowser, or comma-separated combination.");
                RequestedSubsystems.Add(normalized);
            }
            if (RequestedSubsystems.Count == 0)
                throw new InvalidOperationException(
                    $"Invalid mode: '{ModeString}'. Use: all, simhost, ig, ios, orchestrator, editor, cgf, traffic, ci, migrate, dump-api, replaybrowser, or comma-separated combination.");

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

            // Migrate mode is standalone: no peer synchronisation or subsystem-combination logic.
            if (RequestedSubsystems.Contains("migrate")) return;

            // Editor mode is always standalone - must not be combined with distributed flags.
            if (RequestedSubsystems.Contains("editor") &&
                (RequestedSubsystems.Contains("ig") || RequestedSubsystems.Contains("excon") ||
                 RequestedSubsystems.Contains("orchestrator") || RequestedSubsystems.Contains("cgf")))
            {
                throw new InvalidOperationException(
                    "Editor must not be combined with distributed flags (IG, ExCon, Orchestrator, CGF).");
            }

            // ReplayBrowser mode is always standalone - must not be combined with distributed flags.
            if (RequestedSubsystems.Contains("replaybrowser") && RequestedSubsystems.Count > 1)
            {
                throw new InvalidOperationException(
                    "ReplayBrowser must run in isolation and cannot be combined with other subsystems.");
            }

            // Editor and ReplayBrowser modes are always standalone (no peer synchronisation required).
            if (RequestedSubsystems.Contains("editor") || RequestedSubsystems.Contains("replaybrowser")) return;

            // ⭐ HN-030: dump-api composes no subsystem at all — it prints the API manifest and exits — so
            //   there is nothing to synchronise with and no --no-wait to demand. ⛔ Without this it fell into
            //   the single-subsystem peer-wait rule below and refused to run.
            if (RequestedSubsystems.Contains("dump-api")) return;

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
