namespace Fdp.Toolkit.Runner
{
    /// <summary>
    /// ⭐ N-13 (CE-4475; docs/DESIGN_Threaded_Work_Inventory.md §7 U-2, F-21) — a subsystem whose simulation runs SEPARATELY from its presentation
    /// (the threaded node: the sim ticks world S and publishes the staging, the presentation pulls it into world A). When the process hosts
    /// several kernels, they all run on the HOST's one sim worker, in sequence (<see cref="SubsystemOrchestrator.StartSimWorker"/>): the subsystem
    /// then starts no thread of its own and its sim step is called by that worker; its <see cref="ISubsystem.Update"/> stays the presentation.
    /// </summary>
    public interface ISimSteppedSubsystem
    {
        /// <summary>Called once, before the host's worker starts: run the sim step only when <see cref="SimStepOnHostWorker"/> is called.</summary>
        void UseHostSimWorker();

        /// <summary>One sim step on the host's worker thread (its own pending time); false when there was nothing to do.</summary>
        bool SimStepOnHostWorker();
    }
}
