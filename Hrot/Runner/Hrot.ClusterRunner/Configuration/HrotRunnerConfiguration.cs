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

        /// <summary>Mode string supplied via --mode.  Examples: all, server, player, simhost, ig, orchestrator, cgf, traffic, ci, simhost,ig, orchestrator,cgf</summary>
        [Option('m', "mode", Required = true, HelpText = "all (every node in one process)|server (orchestrator,simhost,cgf,traffic)|player (a client joining a server; the IG node)|orchestrator|simhost|cgf|traffic|ig|ci|editor|migrate|replaybrowser or comma-separated combination")]
        public string ModeString { get; set; } = string.Empty;

        /// <summary>Scenario name forwarded to <see cref="ScenarioSubsystem"/> when <c>--mode ci</c>.</summary>
        [Option('s', "scenario", Required = false, HelpText = "Scenario to load at startup, on any host with the cluster master (editor, all, server, orchestrator); for --mode ci the CI scenario (e.g. MinimalCI_01)")]
        public string ScenarioName { get; set; } = string.Empty;

        /// <summary>⭐ <c>CE-3354</c> ⑥ — what the startup load (<c>--scenario</c>) opens: <c>edit</c> or <c>live</c>; empty = the default (<see cref="StartupLoadsLive"/>).</summary>
        [Option("load", Required = false, HelpText = "With --scenario: edit | live. Default: edit when this process hosts the editor, else live (loaded paused).")]
        public string LoadTarget { get; set; } = string.Empty;

        /// <summary>⭐ <c>CE-3354</c> ⑥ — load-and-run: the live load runs once every node's load succeeded (implies <c>--load live</c>).</summary>
        [Option("play", Required = false, Default = false, HelpText = "With --scenario: load live and RUN when every node has loaded (implies --load live).")]
        public bool Play { get; set; }

        /// <summary>⭐ <c>CE-3354</c> ⑥ — node NAMES whose scenario load also waits for their built 3-D map (the cluster's map-in-load setting). Absent = not given; given with no name = this process' own nodes.</summary>
        public IEnumerable<string>? MapInLoad { get; set; }

        /// <summary>The raw <c>--map-in-load</c> value: comma-separated names, or <see cref="OwnNodes"/> (what <see cref="NormaliseArgs"/> makes of a bare <c>--map-in-load</c>). ⚠ The command-line parser has no optional-value option, hence the sentinel.</summary>
        [Option("map-in-load", Required = false, HelpText = "These nodes' loads wait for their 3-D map (comma-separated node names); with no names: this process' own nodes.")]
        public string? MapInLoadText { get; set; }

        /// <summary>The sentinel a bare <c>--map-in-load</c> becomes: this process' own nodes.</summary>
        public const string OwnNodes = "*";

        /// <summary>⭐ <c>CE-3354</c> ⑥ — <c>--map-in-load</c> with no names (last, or followed by another option) becomes <c>--map-in-load=*</c>, because the parser cannot take an option's value as optional. Call before parsing.</summary>
        public static string[] NormaliseArgs(string[] args)
        {
            var result = new List<string>(args.Length);
            for (int i = 0; i < args.Length; i++)
            {
                bool bare = args[i] == "--map-in-load" && (i + 1 >= args.Length || args[i + 1].StartsWith('-'));
                result.Add(bare ? "--map-in-load=" + OwnNodes : args[i]);
            }
            return result.ToArray();
        }

        /// <summary>⭐ CE-3354 ⑥ — this process hosts the cluster master (the editor's embedded core, or the orchestrator — which <c>all</c> / <c>server</c> include).</summary>
        public bool HostsMaster => RequestedSubsystems.Contains("editor") || RequestedSubsystems.Contains("orchestrator");

        /// <summary>⭐ CE-3354 ⑥ — a startup load was asked for (<c>--scenario</c> outside <c>--mode ci</c>, which keeps its own meaning).</summary>
        public bool HasStartupLoad => !string.IsNullOrWhiteSpace(ScenarioName) && !RequestedSubsystems.Contains("ci");

        /// <summary>⭐ CE-3354 ⑥ — the startup load goes live: <c>--play</c>, or <c>--load live</c>, or no <c>--load</c> and no editor in the process. Else it opens for editing.</summary>
        public bool StartupLoadsLive => Play || string.Equals(LoadTarget?.Trim(), "live", StringComparison.OrdinalIgnoreCase)
                                        || (string.IsNullOrWhiteSpace(LoadTarget) && !RequestedSubsystems.Contains("editor"));

        /// <summary>⭐ <c>CE-3354</c> ② — this process' ONE node's unique name (a process with one node); several nodes in one process use <see cref="NodeNames"/>.</summary>
        [Option("node-name", Required = false, HelpText = "The unique name of this process' node (default <subsystem>-<nodeId>). Several nodes in one process: use --node-names.")]
        public string NodeName { get; set; } = string.Empty;

        /// <summary>⭐ <c>CE-3354</c> ② — <c>Sub=name,Sub=name</c>: node names keyed by subsystem (several nodes in one process).</summary>
        [Option("node-names", Required = false, HelpText = "Node names per subsystem for several nodes in one process: IG=film-cam,SimHost=sim-1 (default <subsystem>-<nodeId>).")]
        public string NodeNames { get; set; } = string.Empty;

        private Dictionary<string, string> _nodeNameMap = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>⭐ <c>CE-3354</c> ② — the node name configured for <paramref name="subsystemName"/> (<c>--node-name</c> for the process' one node, else the
        /// <c>--node-names</c> entry, case-insensitive; <c>player</c> = <c>IG</c>), or <see langword="null"/> ⇒ the default <c>&lt;subsystem&gt;-&lt;nodeId&gt;</c>.
        /// Valid after <see cref="Validate"/>.</summary>
        public string? NodeNameFor(string subsystemName)
        {
            if (!string.IsNullOrWhiteSpace(NodeName)) return NodeName.Trim();
            if (string.IsNullOrEmpty(subsystemName)) return null;
            string key = subsystemName.Equals("player", StringComparison.OrdinalIgnoreCase) ? "ig" : subsystemName;
            return _nodeNameMap.TryGetValue(key, out var n) ? n : null;
        }

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
        /// as of now."* ⇒ empty means *"unset"*; ⭐ CE-4089 (R-4097): resolution then falls
        /// through to the NAS root (<c>ContentLayout.NasRoot</c>), which a dev start seeds from the repository — no source walk-up, no exe folder.
        /// <see cref="AiBehaviorsProjectPath"/> remains only to find the project a hot-reload rebuild builds.</para>
        ///
        /// <para>⚠ <b>A value that names a missing directory THROWS at startup</b>
        /// *(<c>AssetRoots.Configure</c>)* — the ruling's own call: silently falling through *"would
        /// reintroduce 'it worked on the dev box'."*</para>
        /// </summary>
        [Option("asset-root", Required = false,
                HelpText = "Absolute path to the content root (assets/ and recipes/ live under "
                         + "it). Ruling 67. Unset = the NAS root (a dev start seeds it from the repository's "
                         + "scenarios/, assets/ and recipes/).")]
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

        /// <summary>⭐ CE-4045 H3 (docs/DESIGN_Scenario_Regression.md §10-7, RH-7) — with the replay check (implied): after H1b / H2 the replay is
        /// sought to this run time, BRANCHED live there and run on; it must go on as the straight live run did. 0 = off.</summary>
        [Option("verify-branch-at", Required = false, Default = 0d,
                HelpText = "With --verify: H3 — after the replay check, branch the replay LIVE at this run time (s), run on, and require the "
                         + "branch to equal the straight live run from there (implies --verify-replay)")]
        public double VerifyBranchAt { get; set; }

        /// <summary>⭐ CE-4045 H5 (docs/DESIGN_Scenario_Regression.md §10-3, R-4086) — each scenario runs LIVE twice, each in a fresh process:
        /// straight, and with a During Action Review at this sim time (every earlier second looked back at, then back to live); the review
        /// must show the live run, the return must give the world back, and the run after it must be the straight run. 0 = off.</summary>
        [Option("verify-review-at", Required = false, Default = 0d,
                HelpText = "With --verify: H5 — review each scenario's LIVE run at this sim time (s) and require the review to show the live "
                         + "run, the return to give the world back and the rest of the run to equal a straight run; exit 0 when all hold")]
        public double VerifyReviewAt { get; set; }

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
        /// The fine-grained nodes, in start order (the orchestrator first: the id allocator needs it serving before SimHost
        /// bootstraps). Each is a mode of its own; the shorthands below only combine them.
        /// </summary>
        private static readonly string[] AllNodes = { "orchestrator", "simhost", "ig", "cgf", "traffic" };

        /// <summary>The server side of a networked game — everything except the node a player brings along (ig).</summary>
        private static readonly string[] ServerNodes = { "orchestrator", "simhost", "cgf", "traffic" };

        /// <summary>
        /// Whole-string shorthands: <c>all</c> / <c>demo</c> = every fine-grained node in one process; <c>server</c> = the
        /// orchestrator, SimHost, CGF and traffic nodes, which players (<c>--mode player</c>) join.
        /// </summary>
        private static readonly Dictionary<string, string[]> RosterShorthands = new(StringComparer.Ordinal)
        {
            ["all"]    = AllNodes,
            ["demo"]   = AllNodes,
            ["server"] = ServerNodes,
        };

        private const string ModeList =
            "Use: all, server, player, orchestrator, simhost, cgf, traffic, ig, editor, ci, migrate, dump-api, replaybrowser, or comma-separated combination.";

        /// <summary>
        /// ⭐ <c>CE-3354</c> ⑥ — the startup load's flags, refused clearly where they cannot hold: no master in the process (a lone <c>simhost</c> / <c>cgf</c> / <c>ig</c> / <c>traffic</c>, <c>player</c>) · <c>--load</c> / <c>--play</c>
        /// without <c>--scenario</c> · an unknown <c>--load</c> · <c>--play</c> with <c>--load edit</c> (a run needs a live load). <c>--map-in-load</c> alone is fine (it sets the cluster setting) but needs a master too.
        /// <c>--mode ci</c> keeps <c>--scenario</c>'s old meaning and takes none of these.
        /// </summary>
        private void ValidateStartupLoad()
        {
            if (MapInLoad == null && MapInLoadText != null)   // the parsed text: names, or the sentinel = this process' own nodes (an empty list)
                MapInLoad = MapInLoadText.Trim() == OwnNodes
                    ? Array.Empty<string>()
                    : MapInLoadText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool ci = RequestedSubsystems.Contains("ci");
            bool other = RequestedSubsystems.Contains("migrate") || RequestedSubsystems.Contains("dump-api");
            bool scenario = !string.IsNullOrWhiteSpace(ScenarioName);
            string load = LoadTarget?.Trim() ?? string.Empty;
            if (ci || other)
            {
                if (load.Length > 0 || Play || MapInLoad != null)
                    throw new InvalidOperationException("--load, --play and --map-in-load apply to a cluster with a master, not to --mode ci / migrate / dump-api.");
                return;
            }
            if (load.Length > 0 && !load.Equals("edit", StringComparison.OrdinalIgnoreCase) && !load.Equals("live", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"--load '{LoadTarget}' is not edit | live.");
            if ((load.Length > 0 || Play) && !scenario)
                throw new InvalidOperationException("--load / --play load a scenario: name it with --scenario <name>.");
            if (Play && load.Equals("edit", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("--play runs a live load; it cannot be combined with --load edit.");
            if ((scenario || MapInLoad != null) && !HostsMaster)
                throw new InvalidOperationException(
                    $"--scenario / --map-in-load are sent to the cluster master, and this process ({string.Join(", ", RequestedSubsystems)}) hosts none. "
                    + "Run them where the master is (--mode editor | all | server | orchestrator), or load the scenario over HTTP / from the operator's panel.");
        }

        /// <summary>⭐ <c>CE-3354</c> ② — <c>--node-name</c> names the process' ONE node (several ⇒ ambiguous, refused); <c>--node-names</c> keys must be hosted subsystems
        /// and no two nodes may take one name.</summary>
        private void ValidateNodeNames()
        {
            _nodeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nodeSubsystems = RequestedSubsystems.Where(x => x is not ("ci" or "migrate" or "dump-api")).ToList();
            if (!string.IsNullOrWhiteSpace(NodeName))
            {
                if (nodeSubsystems.Count > 1)
                    throw new InvalidOperationException(
                        $"--node-name is ambiguous: this process hosts {nodeSubsystems.Count} nodes ({string.Join(", ", nodeSubsystems)}). "
                        + "Name them with --node-names Sub=name,Sub=name.");
                CheckNodeName(NodeName.Trim());
            }
            if (string.IsNullOrWhiteSpace(NodeNames)) return;
            var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(NodeName)) used[NodeName.Trim()] = "--node-name";
            foreach (var pair in NodeNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0 || eq == pair.Length - 1)
                    throw new InvalidOperationException($"--node-names: '{pair}' is not Subsystem=name (e.g. IG=film-cam,SimHost=sim-1).");
                string sub = pair[..eq].Trim().ToLowerInvariant(), name = pair[(eq + 1)..].Trim();
                if (sub == "player") sub = "ig";
                CheckNodeName(name);
                if (!nodeSubsystems.Contains(sub, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"--node-names: '{pair[..eq].Trim()}' is not a node of this process (it hosts: {string.Join(", ", nodeSubsystems)}).");
                if (_nodeNameMap.ContainsKey(sub))
                    throw new InvalidOperationException($"--node-names: '{pair[..eq].Trim()}' is named twice.");
                if (used.TryGetValue(name, out var other))
                    throw new InvalidOperationException($"--node-names: the name '{name}' is given to both {other} and {pair[..eq].Trim()} — a node name is unique.");
                used[name] = pair[..eq].Trim();
                _nodeNameMap[sub] = name;
            }
        }

        private static void CheckNodeName(string name)
        {
            if (name.Length == 0 || name.IndexOfAny(new[] { ',', '=', '"' }) >= 0 || name.Any(char.IsControl))
                throw new InvalidOperationException($"Invalid node name '{name}': non-empty, without commas, '=' or quotes.");
        }

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
            // Expand the roster shorthands ("all", "demo", "server") before splitting.
            string expandedMode = ModeString.Trim().ToLowerInvariant();
            if (RosterShorthands.TryGetValue(expandedMode, out var roster))
                expandedMode = string.Join(",", roster);

            // Parse mode string -> RequestedSubsystems
            RequestedSubsystems.Clear();
            foreach (var name in expandedMode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // "player" is the user-facing name of the IG node (the internal key stays "ig").
                // ⛔ R-3067 / AQ109 X2 — the ExCon node and its legacy "ios" alias are gone: both are now an Invalid mode (thrown below), never silently dropped.
                var normalized = name switch { "player" => "ig", _ => name };
                // ST-015: "stridemock" is gone with the mock subsystem. Dropping it from this set is
                // what makes `--mode stridemock` throw again instead of composing a subsystem that no
                // longer exists.
                // ⭐ HN-030: "dump-api" prints the debug-API manifest and exits — the artefact
                //   tools/ai-debug-mcp generates its tool catalog from. It composes NO subsystem (see
                //   Program.cs), which is why it sits beside "ci" and "migrate" rather than in the roster.
                var validNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "simhost", "ig", "orchestrator", "cgf", "ci", "editor",
                      "replaybrowser", "migrate", "dump-api",
                      "traffic" };   // ⭐ AQ93 N1 — the SUMO traffic node; part of "all" and "server" since 2026-10-28 (AQ93 §12.2)
                if (!validNames.Contains(normalized))
                    throw new InvalidOperationException(
                        $"Invalid mode: '{ModeString}'. {ModeList}");
                RequestedSubsystems.Add(normalized);
            }
            if (RequestedSubsystems.Count == 0)
                throw new InvalidOperationException(
                    $"Invalid mode: '{ModeString}'. {ModeList}");

            ValidateNodeNames();
            ValidateStartupLoad();

            // Parse wait-for list -> WaitForPeers   ("player" is the user-facing name of "ig", normalised as the mode token is)
            if (!string.IsNullOrWhiteSpace(WaitForString))
            {
                WaitForPeers = new HashSet<string>(
                    WaitForString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim().ToLowerInvariant())
                                 .Select(s => s == "player" ? "ig" : s));

                // Validate each peer name
                var validPeers = new HashSet<string> { "simhost", "ig" };
                foreach (var peer in WaitForPeers)
                {
                    if (!validPeers.Contains(peer))
                        throw new InvalidOperationException(
                            $"Invalid wait-for peer: '{peer}'. Valid values: simhost, ig, player.");
                }
            }

            // CI mode is always standalone (no peer synchronisation required).
            if (RequestedSubsystems.Contains("ci")) return;

            // Migrate mode is standalone: no peer synchronisation or subsystem-combination logic.
            if (RequestedSubsystems.Contains("migrate")) return;

            // Editor mode is always standalone - must not be combined with distributed flags.
            if (RequestedSubsystems.Contains("editor") &&
                (RequestedSubsystems.Contains("ig") ||
                 RequestedSubsystems.Contains("orchestrator") || RequestedSubsystems.Contains("cgf")))
            {
                throw new InvalidOperationException(
                    "Editor must not be combined with distributed flags (IG, Orchestrator, CGF).");
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
            // A launch that hosts the master AND the simulation core (orchestrator + simhost + cgf — "all", "server" and every
            // superset of them) waits for nobody: players join it later, and it must never block on them.
            bool hostsTheServerCore = RequestedSubsystems.Contains("orchestrator") &&
                                      RequestedSubsystems.Contains("simhost") &&
                                      RequestedSubsystems.Contains("cgf");
            bool isOrchestratorOnly = RequestedSubsystems.Count == 1 && RequestedSubsystems.Contains("orchestrator");
            if (!NoWait && WaitForPeers.Count == 0 && !hostsTheServerCore && !isOrchestratorOnly)
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
        }
    }
}
