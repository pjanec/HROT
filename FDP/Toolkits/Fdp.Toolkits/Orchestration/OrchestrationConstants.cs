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

        // ══ S1a — THE TKB STAGING DIRECTORY ═══════════════════════════════════════════════════════
        //
        // ⭐⭐⭐ The artifact directory a node reads its TKB zip and its scenario header from, and the
        //    one the orchestrator's prefetch writes them into. ⛔ Until 2026-09-18 the literal "TKB"
        //    was built BY HAND in three places and written by NOBODY — see BP-550 and
        //    DESIGN_Artifact_Staging.md §2.

        /// <summary>
        /// Name of the per-node TKB artifact directory. ⛔ This is the ONLY place the string is built.
        /// </summary>
        public const string TkbDirectoryName = "TKB";

        /// <summary>
        /// Name of the NAS directory TKB artifacts are PUBLISHED to, beside <c>scenarios/</c>.
        /// ⚠ Lower-case, unlike the per-node <see cref="TkbDirectoryName"/>: it sits beside
        /// <c>scenarios</c>/<c>exercises</c>/<c>episodes</c> and follows their casing, while the per-node
        /// directory keeps the upper-case name the node-side readers have always used. ⛔ Two constants
        /// rather than one, so neither side has to know about the other's convention.
        /// </summary>
        public const string NasTkbDirectoryName = "tkb";

        /// <summary>Extension of a published TKB artifact. ⛔ One definition.</summary>
        public const string TkbArtifactExtension = ".zip";

        /// <summary>The NAS directory TKB artifacts are published to — <c>{nas}/tkb</c>.</summary>
        public static string GetNasTkbRoot(string nasBasePath)
            => Path.Combine(nasBasePath, NasTkbDirectoryName);

        /// <summary>
        /// ⭐⭐⭐ <b>The node's TKB staging directory, from a root that is ALREADY per-node.</b>
        ///
        /// <para>🔒 <b><c>V1</c> settled by measurement, <c>2026-09-18</c> — and the lean flipped.</b>
        /// 📐 Every production host passes a per-node root as its <c>localTempRoot</c>:
        /// <c>SimHostApp.cs:362</c> (<c>Combine(base, "nodes", $"node-{id}")</c>),
        /// <c>CgfSubsystem.cs:612</c> (identical), <c>OrchestratorSubsystem.cs:137</c>
        /// (<c>GetNodeStagingRoot(orchestratorNodeId)</c>). ⇒ the handler's "bare" root already IS
        /// <c>{base}/nodes/node-N</c>, so it needs no node id — which is exactly the condition the plan
        /// named as what would flip its lean.</para>
        ///
        /// <para>⛔ <b>And the precedent the plan cited argues the other way.</b> 📐
        /// <c>ReferenceArchiveHandler(localTempRoot, nodeId)</c> uses the id to build a FILE NAME —
        /// <c>node_{id}.fdp</c>, under the shared <c>exercises/</c> directory
        /// (<c>ReferenceArchiveHandler.cs:73-74</c>) — not to make its root per-node. It is a precedent
        /// for node-discriminated FILENAMES in a shared directory, the opposite shape.</para>
        /// </summary>
        /// <param name="nodeStagingRoot">
        /// A root that is already this node's own — i.e. <see cref="GetNodeStagingRoot(string,int)"/>,
        /// or the <c>localTempRoot</c> every host bootstrap already computes.
        /// </param>
        public static string GetTkbStagingRoot(string nodeStagingRoot)
            => Path.Combine(nodeStagingRoot, TkbDirectoryName);

        /// <summary>
        /// ⭐⭐ The same directory, addressed the way the ORCHESTRATOR addresses it: from the shared base
        /// root plus a node id. ⛔ Deliberately defined by COMPOSING the two existing helpers rather than
        /// re-assembling the path, so the two sides cannot drift — a rail asserts the composition.
        /// </summary>
        public static string GetNodeTkbStagingRoot(string stagingRoot, int nodeId)
            => GetTkbStagingRoot(GetNodeStagingRoot(stagingRoot, nodeId));

        /// <inheritdoc cref="GetNodeTkbStagingRoot(string,int)"/>
        public static string GetNodeTkbStagingRoot(int nodeId)
            => GetNodeTkbStagingRoot(ResolveStagingRoot(), nodeId);

        public static string GetEpisodesRoot(string stagingRoot)
            => Path.Combine(stagingRoot, EpisodesDirectoryName);

        /// <summary>
        /// ⭐⭐ The full path of ONE node's exercise recording: <c>{root}/exercises/{exerciseId}/node_N.fdp</c>.
        ///
        /// <para>📌 <b>Why this exists</b> (added <c>2026-09-01</c>): the EPISODE side already had
        /// <see cref="GetEpisodeRecordingFilePath"/>, but the EXERCISE side did not, so the same three-segment
        /// shape was hand-composed at 8 call sites — and a test that hand-composed it <b>wrong</b> (omitting
        /// the <c>exercises/</c> segment) failed as <i>"recording file not found"</i>, which reads as a broken
        /// recorder and is not one. ⇒ ⭐ the two halves of the recording layout now have ONE definition each,
        /// and asserting on a recording means calling the same function production called.</para>
        ///
        /// <para>⚠ Six other sites still compose this shape by hand
        /// (<c>ReferenceArchiveHandler</c> ×2, <c>GlobalContextClusterOpHandler</c> ×2,
        /// <c>StorageGatewayModule</c>, <c>ClusterMaster</c>, <c>DebugApiService</c>,
        /// <c>RecordReplayWiringTests</c>) — they are correct today and are left alone; adopting them is a
        /// separate, wider change that crosses lanes.</para>
        /// </summary>
        public static string GetExerciseRecordingFilePath(string stagingRoot, Guid exerciseId, int nodeId)
            => Path.Combine(
                stagingRoot,
                ExercisesDirectoryName,
                exerciseId.ToString(),
                GetNodeRecordingFileName(nodeId));

        public static string GetEpisodeRecordingFileName(Guid episodeId, int nodeId)
            => $"{episodeId}_node{nodeId}.fdp";

        public static string GetEpisodeRecordingFilePath(string stagingRoot, Guid episodeId, int nodeId)
            => Path.Combine(GetEpisodesRoot(stagingRoot), GetEpisodeRecordingFileName(episodeId, nodeId));
    }
}
