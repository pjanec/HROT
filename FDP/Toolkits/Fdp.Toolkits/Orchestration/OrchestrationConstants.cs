using System;
using System.IO;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Global orchestration constants defining cluster-wide deployment conventions.
    /// </summary>
    public static class OrchestrationConstants
    {
        /// <summary>
        /// Resolves the default root directory for scenario staging, checkpoints, and archives.
        /// Honors the <c>FDP_STAGING_ROOT</c> environment variable when set, otherwise falls
        /// back to a platform-appropriate temp directory (cross-platform; not a fixed Windows path).
        /// </summary>
        public static string ResolveStagingRoot() =>
            Environment.GetEnvironmentVariable("FDP_STAGING_ROOT")
            ?? "C:\\FDP_Temp";
        public const string ScenariosDirectoryName = "scenarios";
        public const string ExercisesDirectoryName = "exercises";
        public const string EpisodesDirectoryName = "episodes";

        public static string GetNodeRecordingFileName(int nodeId)
            => $"node_{nodeId}.fdp";

        public static string GetNodeDirectoryName(int nodeId)
            => $"node-{nodeId}";

        public static string GetNodeStagingRoot(int nodeId)
            => GetNodeStagingRoot(ResolveStagingRoot(), nodeId);

        public static string GetNodeStagingRoot(string stagingRoot, int nodeId)
            => Path.Combine(stagingRoot, "nodes", GetNodeDirectoryName(nodeId));

        public static string GetNodeScenariosRoot(int nodeId)
            => Path.Combine(GetNodeStagingRoot(nodeId), ScenariosDirectoryName);

        public static string GetNodeScenariosRoot(string stagingRoot, int nodeId)
            => Path.Combine(GetNodeStagingRoot(stagingRoot, nodeId), ScenariosDirectoryName);

        public static string GetNodeExercisesRoot(int nodeId)
            => Path.Combine(GetNodeStagingRoot(nodeId), ExercisesDirectoryName);

        public static string GetNodeExercisesRoot(string stagingRoot, int nodeId)
            => Path.Combine(GetNodeStagingRoot(stagingRoot, nodeId), ExercisesDirectoryName);

        public static string GetEpisodesRoot(string stagingRoot)
            => Path.Combine(stagingRoot, EpisodesDirectoryName);

        public static string GetEpisodeRecordingFileName(Guid episodeId, int nodeId)
            => $"{episodeId}_node{nodeId}.fdp";

        public static string GetEpisodeRecordingFilePath(string stagingRoot, Guid episodeId, int nodeId)
            => Path.Combine(GetEpisodesRoot(stagingRoot), GetEpisodeRecordingFileName(episodeId, nodeId));
    }
}
