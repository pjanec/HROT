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

    // Stub intents for future bus-driven time control (wired in HEXAG2-S010/S011).
    public struct PauseTimeIntent    { }
    public struct ResumeTimeIntent   { }
    public struct StepTimeIntent     { public float DeltaSeconds; }
    public struct SetTimeScaleIntent { public float TimeScale; }
}
