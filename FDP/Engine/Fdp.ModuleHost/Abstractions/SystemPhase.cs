namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// Execution phases for systems within the simulation loop.
    /// </summary>
    public enum SystemPhase
    {
        /// <summary>
        /// Input phase: Hardware input, early processing (Main Thread).
        /// </summary>
        Input = 1,
        
        /// <summary>
        /// BeforeSync phase: Pre-sync preparation (Main Thread).
        /// </summary>
        BeforeSync = 2,
        
        // [SYNC A -> B] - Kernel Operation
        
        /// <summary>
        /// Simulation phase: Main logic - modules (Background Threads).
        /// </summary>
        Simulation = 10,
        
        // [PLAYBACK COMMANDS] - Kernel Operation
        
        /// <summary>
        /// PostSimulation phase: Transform sync, interpolation (Main Thread).
        /// </summary>
        PostSimulation = 20,
        
        /// <summary>
        /// Export phase: Network send, recording (Main Thread).
        /// </summary>
        Export = 40,

        /// <summary>
        /// Explicitly excluded from the kernel's automatic phase execution.
        /// Systems in this phase are registered for diagnostics and profiling
        /// but must be manually ticked by their owning module.
        /// </summary>
        Manual = 255
    }
}
