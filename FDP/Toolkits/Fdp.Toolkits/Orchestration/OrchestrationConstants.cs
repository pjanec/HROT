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
            ?? (OperatingSystem.IsWindows()
                ? "C:\\FDP_Temp"
                : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FDP_Temp"));
        public const string ScenariosDirectoryName = "scenarios";
        public const string ExercisesDirectoryName = "exercises";
        public const string EpisodesDirectoryName = "episodes";

        /// <summary>
        /// Name of the cluster-wide SHARED directory — the NAS stand-in on a single box.
        /// <para>
        /// ⭐⭐⭐ This is where AUTHORED scenarios live, for every host. ⛔ Not to be confused with
        /// <see cref="GetNodeScenariosRoot(int)"/>, which is one node's STAGING copy: the orchestrator
        /// stages shared → node, so a node's own directory is empty until something is staged into it
        /// and is never the list the operator picks from.
        /// </para>
        /// <para>
        /// 📐 <b>Measured <c>2026-08-27</c></b> — this member exists because the two hosts disagreed:
        /// <c>EditorBootstrap.ScenariosRoot</c> resolved <c>shared/scenarios</c> (3 scenarios) while
        /// <c>CgfSubsystem</c> resolved <c>nodes/node-N/scenarios</c> (a directory that did not exist),
        /// so <c>--mode all</c> — which composes CGF and NOT the editor — showed an empty picker.
        /// ⇒ the path SHAPE now has one definition, reachable from every assembly that needs it
        /// (<c>Fdp.Toolkits</c>), instead of a literal <c>"shared"</c> in <c>Hrot.Orchestrator</c> that
        /// <c>Hrot.CGF</c> cannot reference.
        /// </para>
        /// </summary>
        public const string SharedDirectoryName = "shared";

        /// <summary>The cluster-wide shared (NAS) root. See <see cref="SharedDirectoryName"/>.</summary>
        public static string GetSharedRoot()
            => GetSharedRoot(ResolveStagingRoot());

        /// <inheritdoc cref="GetSharedRoot()"/>
        public static string GetSharedRoot(string stagingRoot)
            => Path.Combine(stagingRoot, SharedDirectoryName);

        /// <summary>
        /// The root the operator's scenario list comes from on EVERY host — <c>{shared}/scenarios</c>.
        /// </summary>
        public static string GetSharedScenariosRoot()
            => GetSharedScenariosRoot(ResolveStagingRoot());

        /// <inheritdoc cref="GetSharedScenariosRoot()"/>
        public static string GetSharedScenariosRoot(string stagingRoot)
            => Path.Combine(GetSharedRoot(stagingRoot), ScenariosDirectoryName);

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
