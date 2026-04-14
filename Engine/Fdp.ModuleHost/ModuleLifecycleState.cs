namespace Fdp.ModuleHost
{
    /// <summary>
    /// Lifecycle state of a module managed by the <see cref="ModuleHostKernel"/>.
    /// Used to track the progression from async background loading through live execution
    /// to safe, deterministic teardown.
    /// </summary>
    public enum ModuleLifecycleState
    {
        /// <summary>
        /// Module is being compiled and provisioned in the background.
        /// Its systems are not yet part of the active execution topology.
        /// </summary>
        Loading,

        /// <summary>
        /// Module is live. It is part of the active execution topology and
        /// will be dispatched on every applicable frame.
        /// </summary>
        Ready,

        /// <summary>
        /// Module has been atomically unhooked from the active topology via RCU swap.
        /// It will no longer receive new ticks, but may still be mid-execution on a background thread.
        /// The main thread's harvest loop continues to manage this entry until all in-flight
        /// tasks complete and leased views are released.
        /// </summary>
        Draining,

        /// <summary>
        /// Module has been fully drained. All background tasks have completed, all leased views
        /// have been returned, and the final <see cref="System.IDisposable.Dispose"/> has been
        /// dispatched to a background worker. The module is no longer referenced by the kernel.
        /// </summary>
        Disposed
    }
}
