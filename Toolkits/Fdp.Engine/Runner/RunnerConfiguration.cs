using CommandLine;

namespace Fdp.Engine.Runner
{
    /// <summary>
    /// Base runtime configuration shared by all FDP runner processes.
    ///
    /// <para>Contains only domain-agnostic flags.
    /// </summary>
    public class RunnerConfiguration
    {
        // ── Generic CLI options ───────────────────────────────────────────────

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

        /// <summary>Optional path to a JSON test script (used in headless mode).</summary>
        [Option("script", HelpText = "Path to headless test script JSON")]
        public string TestScriptPath { get; set; } = string.Empty;

        /// <summary>Base node ID for deterministic multi-instance offsetting (default 0 = use legacy constants).</summary>
        [Option('n', "node-id", Default = 0, HelpText = "Base node ID for multi-instance offsetting (0 = legacy)")]
        public int NodeId { get; set; }

        /// <summary>Force fixed-step time instead of wall-clock dt (CI mode).</summary>
        [Option("deterministic", Default = false, HelpText = "Force fixed-step time (CI mode)")]
        public bool Deterministic { get; set; }

        /// <summary>Fixed simulation delta in seconds used when Deterministic is true. Default = 1/60 s.</summary>
        [Option("fixed-dt", Default = 0.016667f, HelpText = "Fixed delta in seconds (default 60 Hz)")]
        public float FixedDeltaSeconds { get; set; } = 1.0f / 60.0f;

        // ── Parsed values ─────────────────────────────────────────────────────

        /// <summary>Lower-case subsystem names to wait for. Populated by <c>Validate()</c>.</summary>
        public HashSet<string> WaitForPeers { get; set; } = new();
    }
}
