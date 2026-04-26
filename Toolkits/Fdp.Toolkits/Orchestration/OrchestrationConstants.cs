namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Global orchestration constants defining cluster-wide deployment conventions.
    /// </summary>
    public static class OrchestrationConstants
    {
        /// <summary>
        /// Default root directory for scenario staging, checkpoints, and archives.
        /// </summary>
        public const string DefaultStagingDirectory = @"C:\FDP_Temp";

        public static string GetNodeRecordingFileName(int nodeId)
            => $"node_{nodeId}.fdp";
    }
}
