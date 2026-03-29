namespace Bagira.Runner.Configuration
{
    /// <summary>
    /// Bitfield indicating which subsystems the Runner should host.
    /// Supports single-mode, combined modes, and the "all" shorthand.
    /// </summary>
    [Flags]
    public enum RunMode
    {
        /// <summary>No subsystem selected (invalid configuration).</summary>
        None = 0,

        /// <summary>Run the SimHost subsystem.</summary>
        SimHost = 1 << 0,  // 1

        /// <summary>Run the IG (Image Generator) subsystem.</summary>
        IG = 1 << 1,       // 2

        /// <summary>Run the IOS subsystem.</summary>
        IOS = 1 << 2,      // 4

        /// <summary>Run the distributed orchestrator (DrillMaster) only.</summary>
        Orchestrator = 1 << 3, // 8

        /// <summary>Run the CGF (Computer Generated Forces) subsystem.</summary>
        CGF = 1 << 4,          // 16

        /// <summary>Run a headless deterministic CI scenario and exit with the scenario result code.</summary>
        CI = 1 << 5,           // 32

        /// <summary>Run all three subsystems in one aggregated process.</summary>
        All = SimHost | IG | IOS
    }
}
