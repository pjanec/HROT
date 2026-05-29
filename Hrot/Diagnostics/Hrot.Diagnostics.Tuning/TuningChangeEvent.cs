namespace Hrot.Diagnostics.Tuning
{
    // Records a tuning change for replay honesty (Design section 5.4).
    // Not wired to FlightRecorder in Slice 1; the field layout is stable.
    public readonly struct TuningChangeEvent
    {
        public readonly TuningKey Key;
        public readonly float     OldValue;
        public readonly float     NewValue;
        public readonly ulong     WallTick;   // frame counter at apply time
        // OperatorId placeholder for Slice 2 access control.
    }
}
