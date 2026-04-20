namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// Application-level ECS component IDs for FDP.Toolkit.Perception components
    /// that fall outside the 0-79 globally-allocated range.
    /// Follows the per-toolkit registry pattern established in Phase 5.
    ///
    /// <para>IDs 172-173 are in the 160-199 application-level descriptor block.</para>
    /// </summary>
    public static class PerceptionApplicationComponentIds
    {
        /// <summary><c>SensorContactList</c> — Muscle-tier raw physical track list with hysteresis state.</summary>
        public const int SensorContactList  = 172;

        /// <summary><c>ActiveSensorTracks</c> — Brain-tier cognitive buffer of currently acquired sensor tracks.</summary>
        public const int ActiveSensorTracks = 173;
    }
}
