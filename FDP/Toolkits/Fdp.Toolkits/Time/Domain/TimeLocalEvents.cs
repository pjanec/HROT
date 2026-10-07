namespace Fdp.Toolkit.Time.Domain
{
    public struct AdvanceFrameIntent
    {
        public long   FrameID;
        public float  FixedDelta;
        public double TargetSimTime;   // 0 = use FixedDelta; >0 = snap sim time to this value
    }

    public struct FrameStepCompletedEvent
    {
        public long FrameID;
        public int  NodeID;
    }

    // Bus-driven time control intents (HEXAG2-S010/S011).
    public struct PauseTimeIntent    { }
    public struct ResumeTimeIntent   { }
    public struct StepTimeIntent     { public float DeltaSeconds; }
    public struct SetTimeScaleIntent { public float TimeScale; }

    /// <summary>
    /// ⭐ Q86 §4-C (R-215) — the ONE time discontinuity, as a request: the master jumps to <see cref="Position"/> and
    /// pauses (<c>MasterSyncController.SnapAndPause</c>, slave roster kept). Read BEFORE pause/resume in the same frame,
    /// so "snap, then resume" (a load that runs) lands in that order.
    /// </summary>
    public struct SnapTimeIntent     { public Fdp.Core.GlobalTime Position; }

    /// <summary>
    /// Published by ClusterMaster when the participating slave node set changes.
    /// MasterSyncController drains this to keep _expectedSlaves current without a direct
    /// coupling between ClusterMaster and MasterSyncController (HEXAG2-S011).
    /// </summary>
    public struct SlaveNodeSetUpdatedEvent
    {
        public System.Collections.Generic.IReadOnlySet<int> SlaveNodeIds;
    }
}
