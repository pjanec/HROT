using CommandLine;

namespace FDP.Framework.Runner
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

        // ── Parsed values ─────────────────────────────────────────────────────

        /// <summary>Lower-case subsystem names to wait for. Populated by <c>Validate()</c>.</summary>
        public HashSet<string> WaitForPeers { get; set; } = new();
    }
}
