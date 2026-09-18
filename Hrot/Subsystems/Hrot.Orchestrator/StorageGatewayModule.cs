using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Identifies a push target on a remote simulation node.
/// </summary>
public sealed record NodeDistributionTarget
{
    /// <summary>Roster identifier of the target node.</summary>
    public int NodeId { get; init; }

    /// <summary>
    /// Full UNC or local destination path for the pushed file on the target node
    /// (e.g. <c>\\NODE01\c$\FDP_Temp\scenario.json</c>).
    /// </summary>
    public string DestinationPath { get; init; } = string.Empty;

    /// <summary>
    /// ⭐⭐⭐ <c>S2b</c>/<c>S2c</c> — this node's TKB ARTIFACT directory, which is a SIBLING of the
    /// scenario destination rather than a child of it
    /// (<c>{base}/nodes/node-N/TKB</c> vs <c>{base}/nodes/node-N/scenarios/{id}</c>).
    ///
    /// <para>⚠ Carried on the target rather than derived inside the gateway because the gateway is handed
    /// destinations, not a staging root plus node ids — deriving it there would mean parsing the node
    /// segment back out of a path. 📄 <c>OrchestrationConstants.GetNodeTkbStagingRoot</c> builds it.</para>
    ///
    /// <para>⛔ Empty means "this target stages no artifacts" — legal, and the gateway skips it silently
    /// rather than failing, so an un-migrated caller degrades to the previous behaviour.</para>
    /// </summary>
    public string TkbDestinationPath { get; init; } = string.Empty;
}

/// <summary>
/// ⭐⭐ <c>S2a</c> — the artifact names the staged scenario slices AGREE on.
///
/// <para>🔴 Both are <see langword="null"/>-legal and mean different things when absent: no TKB name means
/// the node uses <c>NedTkbCatalog.RegisterAll()</c> (a supported path), and no terrain name means the
/// scenario simply has no terrain. ⛔ Neither is a failure — only DISAGREEMENT between slices is.</para>
/// </summary>
public readonly record struct StagedArtifactNames(string? TkbName, string? TerrainName)
{
    /// <summary>True when the scenario names at least one artifact worth staging.</summary>
    public bool Any => !string.IsNullOrEmpty(TkbName) || !string.IsNullOrEmpty(TerrainName);
}

/// <summary>
/// Reports the outcome of a bulk file-transfer operation performed by
/// <see cref="StorageGatewayModule"/>.
/// </summary>
public sealed class GatewayResult
{
    /// <summary>Number of files transferred successfully.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of files that failed to transfer.</summary>
    public int FailureCount { get; init; }

    /// <summary><c>true</c> when every file in the batch succeeded.</summary>
    public bool IsFullSuccess => FailureCount == 0;
}

/// <summary>
/// SMB Pull Gateway co-located with the <see cref="ClusterMaster"/>.
///
/// <para>Owns all bulk file movement (Scenarios, Checkpoints, Archive Export/Import)
/// using the <em>SMB Pull Gateway Pattern</em>: the gateway opens one outbound SMB
/// connection to the central NAS and pulls files from leaf nodes via parallel outbound
/// reads (<see cref="PullToNasAsync"/>), rather than having all nodes push
/// simultaneously.  This avoids the ≈20-connection inbound SMB limit on Windows
/// client SKUs.</para>
///
/// <para>The maximum outbound parallelism is capped at <see cref="MaxParallelCopies"/>
/// to prevent disk or NIC saturation under large exercises.</para>
///
/// <para><b>Integration point:</b> <see cref="ClusterMaster"/> calls
/// <see cref="Hrot.Orchestrator.ClusterMaster.FanOutSerializeLocal"/> to command all
/// nodes to write local snapshots; after all <c>NodeOpStatus(Success)</c> ACKs arrive
/// with <c>ResultJson</c> containing serialized <see cref="FileManifestEntry"/> lists,
/// the collected manifests are passed to <see cref="PullToNasAsync"/> to move files to
/// the NAS in a single coordinated pass.</para>
/// </summary>
public sealed class StorageGatewayModule
{
    /// <summary>
    /// Maximum number of concurrent file-copy operations used by
    /// <see cref="PullToNasAsync"/> and <see cref="PushToNodesAsync"/>.
    /// </summary>
    public const int MaxParallelCopies = 8;

    /// <summary>
    /// Comparison to use for filesystem path equality: case-insensitive on Windows
    /// (NTFS default), case-sensitive (Ordinal) elsewhere, since case-differing paths
    /// are distinct files on Linux.
    /// </summary>
    private static StringComparison PlatformPathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Pulls all files described by <paramref name="manifests"/> from their source
    /// paths and copies them into <paramref name="nasBasePath"/>, preserving the
    /// relative destination expressed in each <see cref="FileManifestEntry.RelativeDest"/>.
    ///
    /// <para>Copies run in parallel with at most <see cref="MaxParallelCopies"/>
    /// concurrent operations.  Per-file errors are caught and counted; the method
    /// completes once every file—successful or failed—has been processed.</para>
    /// </summary>
    /// <param name="manifests">Ordered list of files to pull from node storage.</param>
    /// <param name="nasBasePath">
    /// Root directory on the NAS (local or UNC path) under which each
    /// <see cref="FileManifestEntry.RelativeDest"/> is resolved.
    /// Intermediate directories are created automatically.
    /// </param>
    /// <returns>
    /// A <see cref="GatewayResult"/> reporting per-file success and failure counts.
    /// </returns>
    public async Task<GatewayResult> PullToNasAsync(
        IReadOnlyList<FileManifestEntry> manifests,
        string nasBasePath,
        CancellationToken ct = default)
    {
        if (manifests == null)  throw new ArgumentNullException(nameof(manifests));
        if (string.IsNullOrWhiteSpace(nasBasePath)) throw new ArgumentNullException(nameof(nasBasePath));

        int successCount = 0;
        int failureCount = 0;

        var opts    = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies, CancellationToken = ct };
        var partial = new ConcurrentBag<string>();

        try
        {
            await Task.Run(() =>
            {
                Parallel.ForEach(manifests, opts, entry =>
                {
                    try
                    {
                        var destPath = Path.Combine(nasBasePath, entry.RelativeDest);
                        var destDir  = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);

                        partial.Add(destPath);
                        File.Copy(entry.SourceUnc, destPath, overwrite: true);
                        partial.TryTake(out _);   // remove on success — only tracked while in-flight
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                });
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Delete partially-written NAS files to keep storage consistent.
            foreach (var f in partial)
                try { File.Delete(f); } catch { /* best-effort */ }
            throw;
        }

        return new GatewayResult { SuccessCount = successCount, FailureCount = failureCount };
    }

    /// <summary>
    /// Pushes a file from <paramref name="nasSourcePath"/> to each target node described
    /// by <paramref name="targets"/> using parallel outbound copies.
    ///
    /// <para>Each <see cref="NodeDistributionTarget.DestinationPath"/> is the fully
    /// qualified UNC or local path (e.g. <c>\\NODE01\c$\FDP_Temp\scenario.json</c>)
    /// where the file should land.  Intermediate directories are created automatically.
    /// </para>
    /// </summary>
    /// <param name="nasSourcePath">Fully-qualified path of the source file on the NAS.</param>
    /// <param name="targets">
    /// Push targets; one copy operation is performed per entry.
    /// </param>
    /// <returns>
    /// A <see cref="GatewayResult"/> reporting per-target success and failure counts.
    /// </returns>
    public async Task<GatewayResult> PushToNodesAsync(
        string nasSourcePath,
        IReadOnlyList<NodeDistributionTarget> targets,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nasSourcePath)) throw new ArgumentNullException(nameof(nasSourcePath));
        if (targets == null) throw new ArgumentNullException(nameof(targets));

        int successCount = 0;
        int failureCount = 0;

        var opts    = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies, CancellationToken = ct };
        var partial = new ConcurrentBag<string>();

        try
        {
            await Task.Run(() =>
            {
                Parallel.ForEach(targets, opts, target =>
                {
                    try
                    {
                        var destDir = Path.GetDirectoryName(target.DestinationPath);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);

                        partial.Add(target.DestinationPath);
                        File.Copy(nasSourcePath, target.DestinationPath, overwrite: true);
                        partial.TryTake(out _);   // remove on success — only tracked while in-flight
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                });
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Delete partially-written destination files to keep storage consistent.
            foreach (var f in partial)
                try { File.Delete(f); } catch { /* best-effort */ }
            throw;
        }

        return new GatewayResult { SuccessCount = successCount, FailureCount = failureCount };
    }

    /// <summary>
    /// Copies all scenario files for <paramref name="scenarioId"/> from the NAS
    /// (<c>&lt;nasBasePath&gt;\scenarios\&lt;scenarioId&gt;\</c>) to each target node's
    /// local staging directory (<c>C:\FDP_Temp\&lt;scenarioId&gt;\</c>) by pushing
    /// every file in the source directory to every <see cref="NodeDistributionTarget"/>.
    ///
    /// <para>Any files that do not exist locally are silently skipped.  Per-file errors are
    /// counted but do not abort the operation.</para>
    /// </summary>
    /// <param name="scenarioId">Logical scenario identifier (directory name under the NAS scenarios folder).</param>
    /// <param name="targets">Target nodes; each entry's <see cref="NodeDistributionTarget.DestinationPath"/>
    /// should be the fully-qualified destination <em>directory</em> on the target node.</param>
    /// <param name="nasBasePath">NAS root under which <c>scenarios\&lt;scenarioId&gt;</c> is resolved.</param>
    public async Task<GatewayResult> PrefetchScenarioAsync(
        string scenarioId,
        IReadOnlyList<NodeDistributionTarget> targets,
        string nasBasePath)
    {
        if (string.IsNullOrWhiteSpace(scenarioId)) throw new ArgumentNullException(nameof(scenarioId));
        if (targets == null)                        throw new ArgumentNullException(nameof(targets));
        if (string.IsNullOrWhiteSpace(nasBasePath)) throw new ArgumentNullException(nameof(nasBasePath));

        var sourceDir = Path.Combine(nasBasePath, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException(
                $"[Gateway] PrefetchScenario: NAS source directory '{sourceDir}' does not exist. " +
                $"Ensure scenario '{scenarioId}' is staged to the NAS before issuing a prefetch transition.");

        var files   = Directory.GetFiles(sourceDir);

        // An empty scenario directory is a mis-configuration: fail fast so the
        // orchestrator publishes SysOpStatus.Failure rather than fanning out
        // PrefetchFiles that would result in no staged content (CGF1 BATCH-15 A.2).
        if (files.Length == 0)
            throw new InvalidOperationException(
                $"[Gateway] PrefetchScenario: NAS source directory '{sourceDir}' is empty. " +
                $"Ensure scenario '{scenarioId}' contains at least one file before prefetching.");

        // NEW: sanity gate -- TkbName must agree across all scenario files.
        // ⭐ S2a — it now RETURNS what it already computed, instead of discarding it.
        var artifactNames = CheckTkbNameConsensus(files);

        int success = 0, failure = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies };

		// eliminate duplicate destination paths (if multiple nodes share the same staging directory) to avoid redundant copies
		var distinctTargets = targets.DistinctBy(t => t.DestinationPath).ToList();

        // For each (file, target) pair: push the file to the target node's staging dir.
        var pairs = new List<(string sourceFile, NodeDistributionTarget target)>(files.Length * targets.Count);
        foreach (var file in files)
            foreach (var target in distinctTargets)
                pairs.Add((file, target));

        await Task.Run(() =>
        {
            Parallel.ForEach(pairs, options, pair =>
            {
                var (srcFile, tgt) = pair;
                try
                {
                    var destPath = Path.Combine(tgt.DestinationPath, Path.GetFileName(srcFile));

                    // Skip if source and destination are the exact same file. Path equality
                    // is case-insensitive only on Windows; on Linux, case-differing paths
                    // are distinct files.
                    if (string.Equals(srcFile, destPath, PlatformPathComparison))
                    {
                        Interlocked.Increment(ref success);
                        return;
                    }

                    var destDir  = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Copy(srcFile, destPath, overwrite: true);
                    Interlocked.Increment(ref success);
                }
                catch (Exception ex)
                {
                    FdpLog<StorageGatewayModule>.Error(
                        "[Gateway] PrefetchScenario: failed to copy '{0}' → '{1}': {2}",
                        Path.GetFileName(srcFile), tgt.DestinationPath, ex.Message);
                    Interlocked.Increment(ref failure);
                }
            });
        }).ConfigureAwait(false);

        // ⭐⭐⭐ CE-280 — route the format-incompatible foreign slices back to THEIR origin nodes only.
        //   A distributed save keeps each foreign slice under <name>/foreign/node_<id>.json (§4c). On load
        //   each such slice belongs to exactly ONE node (the id in its filename), so unlike the top-level
        //   scenario.json (which every node stages) it is delivered only to the target whose NodeId matches.
        //   That node's own load handler (e.g. ExConScenarioLoadHandler) reads it from <staging>/foreign/.
        //   Defensive: no foreign/ dir ⇒ nothing routed; a slice with no matching active target is skipped.
        var foreignDir = Path.Combine(sourceDir, "foreign");
        if (Directory.Exists(foreignDir))
        {
            foreach (var foreignFile in Directory.GetFiles(foreignDir, "node_*.json"))
            {
                if (!TryParseForeignNodeId(Path.GetFileName(foreignFile), out var originNodeId))
                    continue;
                foreach (var tgt in distinctTargets)
                {
                    if (tgt.NodeId != originNodeId) continue;
                    try
                    {
                        var destPath = Path.Combine(tgt.DestinationPath, "foreign", Path.GetFileName(foreignFile));
                        if (string.Equals(foreignFile, destPath, PlatformPathComparison))
                        {
                            Interlocked.Increment(ref success);
                            continue;
                        }
                        var destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);
                        File.Copy(foreignFile, destPath, overwrite: true);
                        Interlocked.Increment(ref success);
                    }
                    catch (Exception ex)
                    {
                        FdpLog<StorageGatewayModule>.Error(
                            "[Gateway] PrefetchScenario: failed to route foreign '{0}' → node {1}: {2}",
                            Path.GetFileName(foreignFile), originNodeId, ex.Message);
                        Interlocked.Increment(ref failure);
                    }
                }
            }
        }

        // ⭐⭐⭐ S2b + S2c — stage the named ARTIFACTS beside the scenario slices.
        StageNamedArtifacts(artifactNames, distinctTargets, nasBasePath, ref success, ref failure);

        return new GatewayResult { SuccessCount = success, FailureCount = failure };
    }

    /// <summary>
    /// ⭐⭐⭐ <c>S2b</c> + <c>S2c</c> — copy the named TKB zip, and write the header that names it, into
    /// every target's TKB directory.
    ///
    /// <para>🔴 <b>Why <c>S2c</c> is the half that matters.</b> 📐 Measured <c>2026-09-18</c>:
    /// <c>ScenarioHeader.json</c> had <b>zero production writers</b>, so
    /// <c>TkbLoadClusterStateHandler.ExtractTkbNameFromLocalScenario</c> returned <c>null</c> on every
    /// real load and the handler fell through to <c>NedTkbCatalog.RegisterAll()</c>. ⇒ the TKB loader had
    /// <b>never resolved a name in production</b> — which is why the missing zip (<c>BP-550</c>) went
    /// unnoticed: <b>the name was never read, so the zip was never wanted.</b></para>
    ///
    /// <para>⚠⚠ <b>The header written here is a DIFFERENT SHAPE from the scenario's own header block,
    /// and that is deliberate.</b> 📐 Both node-side readers take a FLAT object with PascalCase keys —
    /// <c>TkbLoadClusterStateHandler.cs:152</c> and <c>ScenarioTerrainName.cs:42</c> both use
    /// <c>ValueTextEquals("TkbName")</c>/<c>("TerrainName")</c> with no nesting — whereas a scenario file
    /// carries <c>header: {{ tkbName, terrainName }}</c>, nested and camelCase. ⛔ Copying the scenario's
    /// block verbatim would produce a file neither reader can parse.</para>
    ///
    /// <para>⚠ A scenario naming NO artifacts stages nothing and is <b>not</b> a failure — the
    /// <c>NedTkbCatalog</c> fallback is a legal path.</para>
    ///
    /// 📄 docs/DESIGN_Artifact_Staging.md §2, §4.
    /// </summary>
    private static void StageNamedArtifacts(
        StagedArtifactNames names,
        IReadOnlyList<NodeDistributionTarget> targets,
        string nasBasePath,
        ref int success,
        ref int failure)
    {
        if (!names.Any) return;

        var headerJson = BuildStagedHeaderJson(names);

        string? tkbSource = null;
        if (!string.IsNullOrEmpty(names.TkbName))
        {
            tkbSource = Path.Combine(
                nasBasePath,
                OrchestrationConstants.NasTkbDirectoryName,
                names.TkbName + OrchestrationConstants.TkbArtifactExtension);

            if (!File.Exists(tkbSource))
            {
                // 🔴🔴 A NAMED-but-UNPUBLISHED artifact is LOGGED LOUDLY and deliberately NOT counted as a
                //    gateway failure. ⚠ This was a real decision, not an oversight — the first draft
                //    counted it, and three PRE-EXISTING rails went red
                //    (PrefetchScenario_SameTkbName_AllFiles_Succeeds and two siblings), which is what
                //    surfaced the question.
                //
                //    📐 Those rails encode the EXISTING contract: a scenario may name a TKB that is not
                //    published, and prefetch still reports IsFullSuccess. ⛔ Changing that would change
                //    the orchestrator's TRANSITION OUTCOME for a condition this design never said should
                //    block a transition — far beyond "copy the zip".
                //
                //    ⭐⭐ And the loud failure already has a designed home: the node's own
                //    TkbLoadClusterStateHandler throws FileNotFoundException naming the exact path when
                //    it cannot find the zip its header told it to load. ⇒ IsFullSuccess keeps meaning
                //    "every transfer I attempted succeeded", and nothing is swallowed — the error names
                //    the missing NAS path, which is the actionable half.
                //    📄 docs/DESIGN_Artifact_Staging.md §4a (folded back by this batch).
                FdpLog<StorageGatewayModule>.Error(
                    "[Gateway] PrefetchScenario: scenario names TKB '{0}' but '{1}' does not exist on the "
                  + "NAS. Nodes will fail to load it. Publish the artifact to {2}.",
                    names.TkbName, tkbSource, OrchestrationConstants.GetNasTkbRoot(nasBasePath));
                tkbSource = null;
            }
        }

        foreach (var target in targets)
        {
            if (string.IsNullOrEmpty(target.TkbDestinationPath)) continue;

            try
            {
                Directory.CreateDirectory(target.TkbDestinationPath);

                // S2c — the header the node reads its names out of.
                File.WriteAllText(
                    Path.Combine(target.TkbDestinationPath, StagedScenarioHeaderFileName), headerJson);
                Interlocked.Increment(ref success);

                // S2b — the artifact itself.
                if (tkbSource != null)
                {
                    var dest = Path.Combine(target.TkbDestinationPath, Path.GetFileName(tkbSource));
                    if (IsAlreadyCurrent(tkbSource, dest))
                    {
                        // ⭐ S2d — the node already holds these bytes; leaving the file UNTOUCHED is what
                        //   makes the node's own (name, length, mtime) cache fire too. §6.
                        Interlocked.Increment(ref success);
                    }
                    else
                    {
                        File.Copy(tkbSource, dest, overwrite: true);
                        Interlocked.Increment(ref success);
                    }
                }
            }
            catch (Exception ex)
            {
                FdpLog<StorageGatewayModule>.Error(
                    "[Gateway] PrefetchScenario: failed to stage artifacts → '{0}': {1}",
                    target.TkbDestinationPath, ex.Message);
                Interlocked.Increment(ref failure);
            }
        }
    }

    /// <summary>The header file both node-side readers open. ⛔ One definition of the name.</summary>
    public const string StagedScenarioHeaderFileName = "ScenarioHeader.json";

    /// <summary>
    /// ⭐ The FLAT, PascalCase header the node reads. ⚠ See <see cref="StageNamedArtifacts"/> for why this
    /// is not the scenario's own header block. ⭐ Null names are omitted rather than written as
    /// <c>null</c>, so an absent name and a null-valued one look identical to the readers.
    /// </summary>
    public static string BuildStagedHeaderJson(StagedArtifactNames names)
    {
        var fields = new List<string>(2);
        if (!string.IsNullOrEmpty(names.TkbName))
            fields.Add($"\"TkbName\":{JsonSerializer.Serialize(names.TkbName)}");
        if (!string.IsNullOrEmpty(names.TerrainName))
            fields.Add($"\"TerrainName\":{JsonSerializer.Serialize(names.TerrainName)}");
        return "{" + string.Join(",", fields) + "}";
    }

    /// <summary>
    /// ⭐⭐⭐ <c>S2d</c> — the orchestrator's skip: does the destination already hold these exact bytes?
    ///
    /// <para>🔒 <b>Ruled by the user <c>2026-09-18</c>: the key is <c>(length, lastWriteTimeUtc)</c> — no
    /// hash, no sidecar.</b> 📐 And the scheme rests on a measured property: <c>File.Copy</c> PRESERVES the
    /// source's last-write time, so the mtime a node sees is the NAS artifact's OWN. ⇒ ⭐⭐ a timestamp is a
    /// CLUSTER-WIDE identity, and both sides can evaluate literally the same predicate without exchanging
    /// anything.</para>
    ///
    /// <para>⚠ <b>The residual risk is accepted, not overlooked:</b> <c>(length, mtime)</c> is not a
    /// content identity. A rebuilt-but-identical zip copies needlessly (wasteful, correct); a different
    /// zip with the same length AND the same 100-ns mtime would wrongly skip (vanishingly unlikely for
    /// tool-built artifacts, ⚠ but silent). 📄 design §6 records the tradeoff.</para>
    /// </summary>
    public static bool IsAlreadyCurrent(string sourceFile, string destFile)
    {
        var src = new FileInfo(sourceFile);
        var dst = new FileInfo(destFile);
        if (!src.Exists || !dst.Exists) return false;          // ⭐ a missing destination always copies
        return src.Length == dst.Length
            && src.LastWriteTimeUtc == dst.LastWriteTimeUtc;
    }

    /// <summary>
    /// Parses the origin node id from a foreign slice filename of the form <c>node_&lt;id&gt;.json</c>.
    /// </summary>
    internal static bool TryParseForeignNodeId(string fileName, out int nodeId)
    {
        nodeId = 0;
        if (string.IsNullOrEmpty(fileName)) return false;
        var stem = Path.GetFileNameWithoutExtension(fileName);   // node_<id>
        const string prefix = "node_";
        if (!stem.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return int.TryParse(stem.AsSpan(prefix.Length), out nodeId);
    }

    /// <summary>
    /// Fetches per-node <c>.fdp</c> archives from <paramref name="nasBasePath"/>
    /// and delivers each to the node-specific <see cref="NodeDistributionTarget.DestinationPath"/>.
    /// Each node receives <c>&lt;nasBasePath&gt;/&lt;exerciseId&gt;/node_&lt;nodeId&gt;.fdp</c>.
    /// </summary>
    public async Task<GatewayResult> PrefetchArchiveAsync(
        string exerciseId,
        IReadOnlyList<NodeDistributionTarget> targets,
        string nasBasePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exerciseId))     throw new ArgumentNullException(nameof(exerciseId));
        if (targets == null)                         throw new ArgumentNullException(nameof(targets));
        if (string.IsNullOrWhiteSpace(nasBasePath)) throw new ArgumentNullException(nameof(nasBasePath));

        var sourceDir = Path.Combine(nasBasePath, OrchestrationConstants.ExercisesDirectoryName, exerciseId);
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException(
                $"[Gateway] PrefetchArchive: NAS source directory '{sourceDir}' does not exist. " +
                $"Ensure exercise '{exerciseId}' is archived to the NAS before issuing a replay prefetch.");

        int successCount = 0;
        int failureCount = 0;

        var opts    = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies, CancellationToken = ct };
        var partial = new ConcurrentBag<string>();

        try
        {
            await Task.Run(() =>
            {
                Parallel.ForEach(targets, opts, target =>
                {
                    try
                    {
                        var fileName = OrchestrationConstants.GetNodeRecordingFileName(target.NodeId);
                        var srcPath = Path.Combine(sourceDir, fileName);
                        var destDir = Path.GetDirectoryName(target.DestinationPath);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);

                        partial.Add(target.DestinationPath);
                        File.Copy(srcPath, target.DestinationPath, overwrite: true);
                        partial.TryTake(out _);   // remove on success — only tracked while in-flight

                        // Pull the companion schema manifest file so replay validation succeeds.
                        var metaSrcPath = srcPath + ".meta.json";
                        var metaDestPath = target.DestinationPath + ".meta.json";
                        if (File.Exists(metaSrcPath))
                        {
                            partial.Add(metaDestPath);
                            File.Copy(metaSrcPath, metaDestPath, overwrite: true);
                            partial.TryTake(out _);   // remove on success — only tracked while in-flight
                        }

                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                });
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Delete partially-written destination files to keep storage consistent.
            foreach (var f in partial)
                try { File.Delete(f); } catch { /* best-effort */ }
            throw;
        }

        return new GatewayResult { SuccessCount = successCount, FailureCount = failureCount };
    }

    /// <summary>
    /// Returns the names of subdirectories under <paramref name="root"/> that
    /// contain at least one <c>*.json</c> file. Represents locally available scenarios.
    /// Returns an empty list if <paramref name="root"/> does not exist.
    /// </summary>
    public IReadOnlyList<string> ScanLocalScenarios(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        // Case-insensitive match: scenario *.json files may have drifted extension casing
        // when authored on Windows; PlatformDefault would silently miss them on Linux.
        var jsonMatch = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        return Directory.GetDirectories(root)
            .Where(d => Directory.GetFiles(d, "*.json", jsonMatch).Length > 0)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Select(n => n!)
            .ToList();
    }

    /// <summary>
    /// Returns the names of subdirectories under <paramref name="root"/> that
    /// contain at least one <c>*.fdp</c> file. Represents locally recorded exercises.
    /// Returns an empty list if <paramref name="root"/> does not exist.
    /// </summary>
    public IReadOnlyList<ExerciseInventoryItem> ScanLocalExercises(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<ExerciseInventoryItem>();

        // Case-insensitive match: *.fdp files may have drifted extension casing when
        // authored on Windows; PlatformDefault would silently miss them on Linux.
        var fdpMatch = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };

        var result = new List<ExerciseInventoryItem>();
        foreach (var d in Directory.GetDirectories(root))
        {
            if (Directory.GetFiles(d, "*.fdp", fdpMatch).Length == 0) continue;
            if (!Guid.TryParse(Path.GetFileName(d), out var exerciseId)) continue;

            var startTime = Directory.GetCreationTimeUtc(d);
            result.Add(new ExerciseInventoryItem(exerciseId, startTime, TimeSpan.Zero, null));
        }
        return result;
    }

    /// <summary>
    /// Returns the names of subdirectories under <paramref name="nasRoot"/> that
    /// contain at least one <c>*.fdp</c> file. Represents exercises archived to NAS.
    /// Returns an empty list if <paramref name="nasRoot"/> does not exist.
    /// </summary>
    public IReadOnlyList<ExerciseInventoryItem> ScanNasExercises(string nasRoot)
    {
        if (!Directory.Exists(nasRoot)) return Array.Empty<ExerciseInventoryItem>();

        // Case-insensitive match: *.fdp files may have drifted extension casing when
        // authored on Windows; PlatformDefault would silently miss them on Linux.
        var fdpMatch = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };

        var result = new List<ExerciseInventoryItem>();
        foreach (var d in Directory.GetDirectories(nasRoot))
        {
            if (Directory.GetFiles(d, "*.fdp", fdpMatch).Length == 0) continue;
            if (!Guid.TryParse(Path.GetFileName(d), out var exerciseId)) continue;

            DateTime startTime = Directory.GetCreationTimeUtc(d);
            TimeSpan duration = TimeSpan.Zero;
            string? scenarioId = null;
            var ctxPath = Path.Combine(d, "Orchestrator.json");
            if (File.Exists(ctxPath))
            {
                try
                {
                    var json = File.ReadAllText(ctxPath);
                    var dto = JsonSerializer.Deserialize<GlobalContextDto>(json,
                        Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
                    if (dto != null && dto.StartWallTicks > 0)
                        startTime = new DateTime(dto.StartWallTicks, DateTimeKind.Utc);
                    if (dto != null && dto.ScenarioTimeSeconds > 0)
                        duration = TimeSpan.FromSeconds(dto.ScenarioTimeSeconds);
                    if (dto != null && !string.IsNullOrEmpty(dto.ScenarioId))
                        scenarioId = dto.ScenarioId;
                }
                catch
                {
                }
            }

            // Case-insensitive match: *.meta.json files may have drifted extension casing
            // when authored on Windows; PlatformDefault would silently miss them on Linux.
            var metaFiles = Directory.GetFiles(d, "*.meta.json",
                new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });
            foreach (var metaPath in metaFiles)
            {
                try
                {
                    var metaJson = File.ReadAllText(metaPath);
                    var meta = JsonSerializer.Deserialize<RecordingMetadata>(metaJson,
                        Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
                    if (meta != null && meta.Duration > TimeSpan.Zero)
                    {
                        duration = meta.Duration;
                        break;
                    }
                }
                catch
                {
                }
            }

            result.Add(new ExerciseInventoryItem(exerciseId, startTime, duration, scenarioId));
        }
        return result;
    }

    // CE-278: WriteScenarioManifestAsync (scenario_manifest.json) retired — it was written only by the
    // SaveScenario=2 pull path and had zero readers (in-repo or external NAS tools, confirmed).

    // ── TkbName consensus helpers ──────────────────────────────────────────────

    /// <summary>
    /// Reads the <c>TkbName</c> field from the <c>Header</c> section of each JSON file
    /// using a forward-only <see cref="System.Text.Json.Utf8JsonReader"/> (no DOM allocation).
    /// Throws <see cref="InvalidOperationException"/> if any two non-empty TkbName values disagree.
    /// </summary>
    private static StagedArtifactNames CheckTkbNameConsensus(string[] files)
    {
        string? agreedTkbName     = null;
        string? agreedSourceFile  = null;
        string? agreedTerrainName = null;
        string? terrainSourceFile = null;

        foreach (var file in files)
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            string? tkbName = PeekHeaderStringFromFile(file, "TkbName", "tkbName");
            if (!string.IsNullOrEmpty(tkbName))
            {
                if (agreedTkbName == null)
                {
                    agreedTkbName    = tkbName;
                    agreedSourceFile = file;
                }
                else if (!string.Equals(agreedTkbName, tkbName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"[Gateway] TkbName consensus check failed: " +
                        $"'{agreedTkbName}' (from '{Path.GetFileName(agreedSourceFile)}') " +
                        $"conflicts with '{tkbName}' (from '{Path.GetFileName(file)}').");
                }
            }

            // ⭐⭐ S2c/S4 — the TERRAIN name rides the SAME staged header file, so it must reach the same
            //    consensus and be written beside TkbName. 🔴 The plan named only the TKB name; measured,
            //    ScenarioTerrainName.Read reads `TerrainName` out of that very file, so writing only
            //    TkbName would leave S4 ("terrain inherits it with no new code") IMPOSSIBLE.
            string? terrainName = PeekHeaderStringFromFile(file, "TerrainName", "terrainName");
            if (!string.IsNullOrEmpty(terrainName))
            {
                if (agreedTerrainName == null)
                {
                    agreedTerrainName = terrainName;
                    terrainSourceFile = file;
                }
                else if (!string.Equals(agreedTerrainName, terrainName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"[Gateway] TerrainName consensus check failed: " +
                        $"'{agreedTerrainName}' (from '{Path.GetFileName(terrainSourceFile)}') " +
                        $"conflicts with '{terrainName}' (from '{Path.GetFileName(file)}').");
                }
            }
        }

        return new StagedArtifactNames(agreedTkbName, agreedTerrainName);
    }

    /// <summary>
    /// Reads <c>Header.TkbName</c> from a JSON file using a forward-only
    /// <see cref="System.Text.Json.Utf8JsonReader"/>. Returns null when the field
    /// is absent or the file cannot be read.
    /// </summary>
    private static string? PeekHeaderStringFromFile(string filePath, string pascalName, string camelName)
    {
        try
        {
            var bytes  = File.ReadAllBytes(filePath);
            var reader = new System.Text.Json.Utf8JsonReader(bytes,
                new System.Text.Json.JsonReaderOptions { AllowTrailingCommas = true });

            bool inHeader = false;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case System.Text.Json.JsonTokenType.PropertyName:
                        var propName = reader.GetString();
                        if (!inHeader && (propName == "Header" || propName == "header"))
                        {
                            inHeader = true;
                        }
                        else if (inHeader && (propName == pascalName || propName == camelName))
                        {
                            reader.Read();
                            return reader.TokenType == System.Text.Json.JsonTokenType.String
                                ? reader.GetString() : null;
                        }
                        break;
                    case System.Text.Json.JsonTokenType.StartObject:
                    case System.Text.Json.JsonTokenType.EndObject:
                        // Once we exit the header object, stop searching.
                        if (inHeader && reader.TokenType == System.Text.Json.JsonTokenType.EndObject)
                            return null;
                        break;
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
