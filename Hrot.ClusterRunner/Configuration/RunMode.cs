namespace Hrot.ClusterRunner.Configuration
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

        /// <summary>Run the ExCon subsystem.</summary>
        ExCon = 1 << 2,      // 4

        /// <summary>Run the distributed orchestrator (ClusterMaster) only.</summary>
        Orchestrator = 1 << 3, // 8

        /// <summary>Run the CGF (Computer Generated Forces) subsystem.</summary>
        CGF = 1 << 4,          // 16

        /// <summary>Run a headless deterministic CI scenario and exit with the scenario result code.</summary>
        CI = 1 << 5,           // 32

        /// <summary>Run the standalone HROT Editor (offline, no DDS participant).</summary>
        Editor = 1 << 6,       // 64

        /// <summary>Run all subsystems in one aggregated process (alias for All, human-readable name).</summary>
        Demo = Orchestrator | SimHost | IG | ExCon | CGF,

        /// <summary>Run all subsystems (Orchestrator + SimHost + IG + ExCon + CGF) in one aggregated process.</summary>
        All = Demo
    }
}
