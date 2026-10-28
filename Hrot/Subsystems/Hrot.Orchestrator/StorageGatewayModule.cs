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
using Fdp.Toolkit.Orchestration.Transfer;
using Hrot.Network.Orchestration;
using Fdp.Core.Serialization;

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

    /// <summary>
    /// ⭐⭐ CE-3020 — the behaviour-asset trees this node receives, from its advertised tokens
    /// (docs/DESIGN_Asset_Management.md §4, §7.3a/§7.3b): kind, the node's root for it, and MIRROR or ADD-ONLY.
    /// Empty = none (every non-Brain node, and any node that advertised none).
    /// </summary>
    public IReadOnlyList<AssetSyncTarget> AssetSyncs { get; init; } = Array.Empty<AssetSyncTarget>();
}

/// <summary>One behaviour-asset tree a node receives: <paramref name="Kind"/> from <c>{nas}/assets/&lt;kind&gt;</c> into
/// <paramref name="DestinationRoot"/>, by <paramref name="Mode"/>.</summary>
public sealed record AssetSyncTarget(string Kind, string DestinationRoot, Fdp.Toolkit.Orchestration.Assets.AssetSyncMode Mode);

/// <summary>
/// ⭐⭐ <c>S2a</c> — the artifact names the staged scenario slices AGREE on.
///
/// <para>🔴 <see langword="null"/>-legal: no terrain name means the scenario simply has no terrain. ⛔ Not a
/// failure — only DISAGREEMENT between slices is.</para>
///
/// <para>⛔⛔ <b>No TKB name — <c>R-4018</c>.</b> A scenario never names a TKB: there is one TKB per deployment,
/// named by <c>ClusterConfiguration.TkbName</c> and staged by <see cref="StorageGatewayModule"/>'s TKB sync
/// whatever the scenario says. An older file's <c>Header.TkbName</c> is never read.
/// 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §0.</para>
/// </summary>
public readonly record struct StagedArtifactNames(string? TerrainName)
{
    /// <summary>True when the scenario names at least one artifact worth staging.</summary>
    public bool Any => !string.IsNullOrEmpty(TerrainName);
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

    private readonly IFileTransferProvider _provider;

    /// <param name="provider">⭐ <c>P6</c> (Q91 §4-E2) — the transport every PUSH site (NAS → node) goes through.
    /// <see langword="null"/> = the share transport (<see cref="NodeShareProvider"/>), today's copy. ⛔ The PULL sites
    /// (node → NAS) do not use it.</param>
    public StorageGatewayModule(IFileTransferProvider? provider = null)
        => _provider = provider ?? DefaultProvider;

    /// <summary>The transport a push uses when none is given — the share (co-located: the staging root itself).</summary>
    public static readonly IFileTransferProvider DefaultProvider = new NodeShareProvider();

    /// <summary>The transport this gateway's pushes go through.</summary>
    public IFileTransferProvider Provider => _provider;

    /// <summary>
    /// ⭐ <c>P6</c> — one push through the provider, awaited on the caller's worker. The target's root is the directory
    /// the file lands in and the item is its name, so one item means the same file on every target of the request.
    /// ⛔ Cancellation propagates as <see cref="OperationCanceledException"/> (the gateway's contract); the provider has
    /// already removed any partial file.
    /// </summary>
    private static async Task<TransferStatus> DeliverAsync(
        IFileTransferProvider provider, IReadOnlyList<TransferTarget> targets, IReadOnlyList<TransferItem> items,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var transfer = provider.Start(new TransferRequest(targets, items));
        using (ct.Register(transfer.Cancel))
            await transfer.Completion.ConfigureAwait(false);
        var status = transfer.Status;
        if (status.Code == OrchestrationStatusCode.Cancelled) throw new OperationCanceledException(ct);
        if (status.FilesFailed > 0)
            FdpLog<StorageGatewayModule>.Error("[Gateway] {0} transfer: {1}", provider.Transport, status.Message);
        return status;
    }

    /// <summary>⭐ The push of ONE file into one directory per target (<c>{dir}/{fileName}</c>).</summary>
    private static Task<TransferStatus> DeliverFileAsync(
        IFileTransferProvider provider, string sourcePath, string fileName,
        IEnumerable<(int NodeId, string Directory)> targets, CancellationToken ct = default)
        => DeliverAsync(provider,
            targets.Select(t => new TransferTarget(t.NodeId, t.Directory)).ToList(),
            new[] { new TransferItem(sourcePath, fileName) }, ct);

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

                        // ⭐ C1 — an entry that carries its (length, mtime) and whose NAS copy already matches is skipped
                        //   by the ONE freshness rule (IsAlreadyCurrent's) — nothing re-transfers.
                        if (entry.LastWriteUtc is DateTime mtime && File.Exists(destPath))
                        {
                            var d = new FileInfo(destPath);
                            if (d.Length == entry.Length && d.LastWriteTimeUtc == mtime)
                            {
                                Interlocked.Increment(ref successCount);
                                return;
                            }
                        }

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

        // ⭐ P6 — through the provider: one request per destination file name (a target may rename the file).
        int successCount = 0, failureCount = 0;
        var pushes = targets
            .GroupBy(t => Path.GetFileName(t.DestinationPath))
            .Select(g => DeliverFileAsync(_provider, nasSourcePath, g.Key,
                g.Select(t => (t.NodeId, Path.GetDirectoryName(t.DestinationPath) ?? string.Empty)), ct))
            .ToList();
        foreach (var status in await Task.WhenAll(pushes).ConfigureAwait(false))
        {
            successCount += status.FilesDone + status.FilesSkipped;
            failureCount += status.FilesFailed;
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
    /// <param name="tkbName">⭐ <c>R-4018</c> — the deployment's ONE TKB (<c>ClusterConfiguration.TkbName</c>), synced from
    /// <c>{nas}/tkb/&lt;name&gt;.zip</c> whatever the scenario says. ⚠ <see langword="null"/> skips the TKB sync — tests only;
    /// the production saga always passes the configured name.</param>
    /// <param name="ct">⭐ P8 H5 — the operator's cancel STOPS the copy (the transfer removes its partial files).</param>
    public Task<GatewayResult> PrefetchScenarioAsync(
        string scenarioId,
        IReadOnlyList<NodeDistributionTarget> targets,
        string nasBasePath,
        string? tkbName = null,
        CancellationToken ct = default)
        // ⭐ R-4001 — ALL of it on a worker, the NAS directory scan included: the saga calls this from the frame thread,
        //   and a transfer that finishes at once would otherwise resume the rest of the chain there.
        => Task.Run(() => PrefetchScenarioCoreAsync(scenarioId, targets, nasBasePath, tkbName, ct), ct);

    private async Task<GatewayResult> PrefetchScenarioCoreAsync(
        string scenarioId,
        IReadOnlyList<NodeDistributionTarget> targets,
        string nasBasePath,
        string? tkbName,
        CancellationToken ct)
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

        // Sanity gate -- TerrainName must agree across all scenario files. ⛔ R-4018: no TKB name is read from them.
        // ⭐ S2a — it RETURNS what it computed, instead of discarding it.
        var artifactNames = CheckTerrainNameConsensus(files);

        int success = 0, failure = 0;

		// eliminate duplicate destination paths (if multiple nodes share the same staging directory) to avoid redundant copies
		var distinctTargets = targets.DistinctBy(t => t.DestinationPath).ToList();

        // ⭐ P6 — every slice to every target through the provider, ONE request per file (bundled: every target in it).
        //   A target already holding the file (same length + mtime — including the NAS file itself) is skipped.
        foreach (var srcFile in files)
        {
            var status = await DeliverFileAsync(_provider, srcFile, Path.GetFileName(srcFile),
                distinctTargets.Select(t => (t.NodeId, t.DestinationPath)), ct).ConfigureAwait(false);
            success += status.FilesDone + status.FilesSkipped;
            failure += status.FilesFailed;
        }

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
                var owners = distinctTargets.Where(t => t.NodeId == originNodeId).ToList();
                if (owners.Count == 0) continue;
                var status = await DeliverFileAsync(_provider, foreignFile, Path.GetFileName(foreignFile),
                    owners.Select(t => (t.NodeId, Path.Combine(t.DestinationPath, "foreign"))), ct).ConfigureAwait(false);
                success += status.FilesDone + status.FilesSkipped;
                failure += status.FilesFailed;
            }
        }

        // ⭐⭐⭐ S2c — stage the scenario's named terrain beside the scenario slices.
        ct.ThrowIfCancellationRequested();
        StageNamedArtifacts(artifactNames, distinctTargets, nasBasePath, ref success, ref failure);

        // ⭐⭐⭐ R-4018 — the deployment's ONE TKB, before every scenario load: a freshness check that copies only what changed.
        if (!string.IsNullOrWhiteSpace(tkbName))
            SyncTkb(tkbName!, distinctTargets, nasBasePath, ref success, ref failure, _provider, ct);

        // ⭐⭐ CE-3020 — the behaviour-asset trees each node needs or authors, INSIDE the prefetch saga (B4a): this task
        //   is what the parked transition waits on, so a sync failure fails the request the same way (B6).
        ct.ThrowIfCancellationRequested();
        SyncBehaviourAssets(targets, nasBasePath, ref success, ref failure);

        return new GatewayResult { SuccessCount = success, FailureCount = failure };
    }

    /// <summary>
    /// ⭐⭐⭐ <c>S2c</c> — stage the scenario's named terrain folder, and write the header that names it into
    /// every target's staging directory.
    ///
    /// <para>⚠⚠ <b>The header written here is a DIFFERENT SHAPE from the scenario's own header block,
    /// and that is deliberate.</b> 📐 The node-side reader takes a FLAT object with PascalCase keys —
    /// <c>ScenarioTerrainName.cs</c> uses <c>ValueTextEquals("TerrainName")</c> with no nesting — whereas a
    /// scenario file carries <c>header: {{ terrainName }}</c>, nested and camelCase.</para>
    ///
    /// <para>⛔⛔ <b><c>R-4018</c> — the TKB is NOT staged here any more</b>: a scenario no longer names one.
    /// <see cref="SyncTkb"/> stages the deployment's one TKB on every prefetch, whatever the scenario says.</para>
    ///
    /// <para>⚠ A scenario naming NO terrain stages nothing and is <b>not</b> a failure.</para>
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
        if (!names.Any)
        {
            // ⭐⭐ CE-3075 — staging nothing must not leave the PREVIOUS scenario's header behind: both node-side
            //   readers fall back to it when the load message is silent, so a scenario naming no terrain
            //   loaded after one that named some INHERITED those names on CGF (measured live 2026-10-05:
            //   hill-attack-close after ua-posture kept test-town resident on CGF only). Remove it; stage nothing.
            foreach (var target in targets)
            {
                if (string.IsNullOrEmpty(target.TkbDestinationPath)) continue;
                var stale = Path.Combine(target.TkbDestinationPath, StagedScenarioHeaderFileName);
                try { if (File.Exists(stale)) File.Delete(stale); }
                catch (Exception ex)
                {
                    FdpLog<StorageGatewayModule>.Error(
                        "[Gateway] PrefetchScenario: could not remove the previous scenario's header '{0}': {1}", stale, ex.Message);
                    Interlocked.Increment(ref failure);
                }
            }
            return;
        }

        var headerJson = BuildStagedHeaderJson(names);

        // ⭐⭐ BP-557 (2026-10-03) — the named TERRAIN, a FOLDER {nas}/terrain/<name>/ (definition + world +
        //   roads), staged to {node}/Terrain/<name>/. 📄 docs/DESIGN_Terrain_World.md §7.3 W12.
        //   ⚠ Not on the NAS is NOT an error here: every node's TerrainCatalog also searches the shared root
        //   and the terrains shipped with the build, and the node fails loudly itself if none has it.
        string? terrainSource = null;
        if (!string.IsNullOrEmpty(names.TerrainName))
        {
            var candidate = Path.Combine(nasBasePath, Fdp.Toolkit.Terrain.TerrainCatalog.SharedDirectoryName,
                names.TerrainName);
            if (File.Exists(Path.Combine(candidate, Fdp.Toolkit.Terrain.TerrainCatalog.DefinitionFileName)))
                terrainSource = candidate;
            else
                FdpLog<StorageGatewayModule>.Info(
                    "[Gateway] PrefetchScenario: terrain '{0}' is not published under '{1}'; nodes resolve it "
                  + "from their shared root or the terrains shipped with the build.",
                    names.TerrainName, candidate);
        }

        foreach (var target in targets)
        {
            if (string.IsNullOrEmpty(target.TkbDestinationPath)) continue;

            try
            {
                if (terrainSource != null)
                {
                    var nodeRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(target.TkbDestinationPath))!;
                    var terrainDest = Path.Combine(nodeRoot, Fdp.Toolkit.Terrain.TerrainCatalog.StagingDirectoryName,
                        names.TerrainName!);
                    // ⭐ D2 — the terrain folder travels by the asset sync (mirror), not a second copier.
                    var r = new Fdp.Toolkit.Orchestration.Assets.AssetTreeSync().Sync(terrainSource, terrainDest);
                    Interlocked.Add(ref success, Math.Max(1, r.Writes));
                }

                Directory.CreateDirectory(target.TkbDestinationPath);

                // S2c — the header the node reads its names out of.
                File.WriteAllText(
                    Path.Combine(target.TkbDestinationPath, StagedScenarioHeaderFileName), headerJson);
                Interlocked.Increment(ref success);
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

    /// <summary>
    /// ⭐⭐⭐ <c>R-4018</c> — sync the deployment's ONE TKB from its NAS master copy (<c>{nas}/tkb/&lt;name&gt;.zip</c>) into
    /// every target's TKB staging directory, and leave the <see cref="StagedTkb"/> record naming it. Runs before every
    /// scenario load as a FRESHNESS CHECK: a node already holding the same bytes is left untouched (<c>S2d</c>), which is
    /// what lets the node's own (name, length, mtime) cache skip the reload too.
    ///
    /// <para>⭐ <b>No master copy is legal</b> — the default deployment today. The stale record is removed so the node
    /// keeps its built-in catalogue, and nothing counts as a failure. 🔒 User, <c>2026-10-15</c>: <i>"go ahead with
    /// the fallback"</i>. 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §0.</para>
    /// </summary>
    public static void SyncTkb(
        string tkbName,
        IReadOnlyList<NodeDistributionTarget> targets,
        string nasBasePath,
        ref int success,
        ref int failure,
        IFileTransferProvider? provider = null,
        CancellationToken ct = default)
    {
        var source = Path.Combine(nasBasePath, OrchestrationConstants.NasTkbDirectoryName,
            tkbName + OrchestrationConstants.TkbArtifactExtension);
        var staged = targets.Where(t => !string.IsNullOrEmpty(t.TkbDestinationPath)).ToList();

        if (!File.Exists(source))
        {
            FdpLog<StorageGatewayModule>.Info(
                "[Gateway] TKB '{0}' has no master copy at '{1}' — nodes keep their built-in catalogue.", tkbName, source);
            foreach (var target in staged)
            {
                try { StagedTkb.Clear(target.TkbDestinationPath); }
                catch (Exception ex)
                {
                    FdpLog<StorageGatewayModule>.Error(
                        "[Gateway] TKB record of '{0}' could not be cleared: {1}", target.TkbDestinationPath, ex.Message);
                    failure++;
                }
            }
            return;
        }
        if (staged.Count == 0) return;

        // ⭐ P6 — ONE request to every target through the provider; a node already holding the same bytes is skipped
        //   (S2d: an unchanged archive is never rewritten — the provider's freshness rule is IsAlreadyCurrent's).
        var status = DeliverFileAsync(provider ?? DefaultProvider, source, Path.GetFileName(source),
            staged.Select(t => (t.NodeId, t.TkbDestinationPath)), ct).GetAwaiter().GetResult();

        foreach (var target in staged)
        {
            if (status.FailedNodeIds.Contains(target.NodeId))
            {
                FdpLog<StorageGatewayModule>.Error(
                    "[Gateway] TKB sync of '{0}' → '{1}' failed: {2}", tkbName, target.TkbDestinationPath, status.Message);
                failure++;
                continue;
            }
            try
            {
                StagedTkb.Write(target.TkbDestinationPath, tkbName);
                success++;
            }
            catch (Exception ex)
            {
                FdpLog<StorageGatewayModule>.Error(
                    "[Gateway] TKB record → '{0}' failed: {1}", target.TkbDestinationPath, ex.Message);
                failure++;
            }
        }
    }

    /// <summary>
    /// ⭐⭐ CE-3020 — sync every behaviour-asset tree each target receives (docs/DESIGN_Asset_Management.md §4, B4/B4a):
    /// <c>{nas}/assets/&lt;kind&gt;</c> → the node's root for the kind, MIRROR or ADD-ONLY (§7.3b ③). Targets sharing one root
    /// (several nodes on one machine) sync it once. ⚠ A kind with no NAS tree is skipped and logged, never a failure —
    /// "nobody published K" is legal (§7.3b, the external-tool case). A sync that throws counts as a FAILURE, which the
    /// saga turns into the L8 failure path (B6 — no second failure route).
    /// </summary>
    private static void SyncBehaviourAssets(
        IReadOnlyList<NodeDistributionTarget> targets, string nasBasePath, ref int success, ref int failure)
    {
        var done = new HashSet<string>(StringComparer.Ordinal);
        var sync = new Fdp.Toolkit.Orchestration.Assets.AssetTreeSync();
        foreach (var target in targets)
        {
            foreach (var a in target.AssetSyncs)
            {
                var source = OrchestrationConstants.GetNasAssetRoot(nasBasePath, a.Kind);
                if (!done.Add(a.Kind + "|" + Path.GetFullPath(a.DestinationRoot))) continue;
                if (!Directory.Exists(source))
                {
                    FdpLog<StorageGatewayModule>.Info(
                        "[Gateway] Asset sync: no '{0}' tree published at '{1}' — nothing to send to node {2}.",
                        a.Kind, source, target.NodeId);
                    continue;
                }
                try
                {
                    var r = sync.Sync(source, a.DestinationRoot, a.Mode);
                    Interlocked.Increment(ref success);
                    FdpLog<StorageGatewayModule>.Info(
                        $"[Gateway] Asset sync {a.Kind} → node {target.NodeId} ({a.Mode}): {r.CopiedStandalone} copied, "
                      + $"{r.Unpacked} unpacked, {r.Deleted} deleted, {r.HeldBackChanged} changed held back (author).");
                }
                catch (Exception ex)
                {
                    FdpLog<StorageGatewayModule>.Error(
                        "[Gateway] Asset sync {0} → node {1} at '{2}' FAILED: {3}", a.Kind, target.NodeId, a.DestinationRoot, ex.Message);
                    Interlocked.Increment(ref failure);
                }
            }
        }
    }

    /// <summary>The header file the node-side terrain reader opens. ⛔ One definition of the name.</summary>
    public const string StagedScenarioHeaderFileName = "ScenarioHeader.json";

    /// <summary>
    /// ⭐ The FLAT, PascalCase header the node reads. ⚠ See <see cref="StageNamedArtifacts"/> for why this
    /// is not the scenario's own header block. ⭐ Null names are omitted rather than written as
    /// <c>null</c>, so an absent name and a null-valued one look identical to the readers.
    /// </summary>
    public static string BuildStagedHeaderJson(StagedArtifactNames names)
    {
        var fields = new List<string>(1);
        if (!string.IsNullOrEmpty(names.TerrainName))
            fields.Add($"\"TerrainName\":{JsonSerializer.Serialize(names.TerrainName, FdpJsonOptionsRegistry.DefaultRelaxed)}");
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

        // ⭐ P6 — through the provider: each node gets ITS OWN recording (and the schema manifest beside it, so replay
        //   validation succeeds), one request per node.
        var pushes = targets.Select(target =>
        {
            var srcPath  = Path.Combine(sourceDir, OrchestrationConstants.GetNodeRecordingFileName(target.NodeId));
            var destName = Path.GetFileName(target.DestinationPath);
            var items = new List<TransferItem> { new(srcPath, destName) };
            if (File.Exists(srcPath + ".meta.json")) items.Add(new TransferItem(srcPath + ".meta.json", destName + ".meta.json"));
            return DeliverAsync(_provider,
                new[] { new TransferTarget(target.NodeId, Path.GetDirectoryName(target.DestinationPath) ?? string.Empty) },
                items, ct);
        }).ToList();

        int successCount = 0, failureCount = 0;
        foreach (var status in await Task.WhenAll(pushes).ConfigureAwait(false))
        {
            if (status.FilesFailed > 0) failureCount++;
            else successCount++;
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
            var (parent, branchTicks) = ExerciseBranch.Of(ReadSidecars(d));   // ⭐ CE-4097 — the recording's own sidecar
            result.Add(new ExerciseInventoryItem(exerciseId, startTime, TimeSpan.Zero, null, parent, branchTicks));
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

            // ⭐ CE-4097 (AQ108 D5) — every node's sidecar: the duration of the first that has one (as before), and the branch's parent
            //   and point through ExerciseBranch — the recording's metadata is the ONE source, the inventory keeps no second record.
            var metas = ReadSidecars(d);
            foreach (var meta in metas)
            {
                if (meta.Duration > TimeSpan.Zero)
                {
                    duration = meta.Duration;
                    break;
                }
            }
            var (parent, branchTicks) = ExerciseBranch.Of(metas);

            result.Add(new ExerciseInventoryItem(exerciseId, startTime, duration, scenarioId, parent, branchTicks));
        }
        return result;
    }

    /// <summary>The recording sidecars (<c>*.meta.json</c>) in an exercise directory, in name order; an unreadable one is skipped.</summary>
    private static List<RecordingMetadata> ReadSidecars(string exerciseDir)
    {
        var list = new List<RecordingMetadata>();
        // Case-insensitive match: *.meta.json files may have drifted extension casing
        // when authored on Windows; PlatformDefault would silently miss them on Linux.
        foreach (var metaPath in Directory.GetFiles(exerciseDir, "*.meta.json",
                     new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<RecordingMetadata>(File.ReadAllText(metaPath),
                    Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
                if (meta != null) list.Add(meta);
            }
            catch
            {
            }
        }
        return list;
    }

    // CE-278: WriteScenarioManifestAsync (scenario_manifest.json) retired — it was written only by the
    // SaveScenario=2 pull path and had zero readers (in-repo or external NAS tools, confirmed).

    // ── TerrainName consensus helpers ──────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <c>L1</c> — <b>the shared content names of a scenario as it sits on the NAS</b>, for the
    /// orchestrator to put on the message that opens the <c>Loading*</c> phase.
    ///
    /// <para>⭐⭐ Deliberately the SAME consensus reader the staging path uses
    /// (<see cref="CheckTerrainNameConsensus"/>), not a second peek: the names the nodes are TOLD and the names
    /// the gateway STAGES must be one answer. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §4.2.</para>
    ///
    /// <para>⚠ A missing directory, an unreadable file or a scenario naming nothing all return an EMPTY
    /// result — that is legal and silent (§4.2). ⛔ A disagreement between two slices still THROWS, exactly
    /// as it does on the staging path; the caller decides whether that fails the transition.</para>
    /// </summary>
    /// <param name="scenarioDirectory">
    /// The scenario's directory on the NAS — <c>{nasBasePath}/scenarios/{scenarioId}</c>, the same
    /// directory <see cref="PrefetchScenarioAsync"/> copies from.
    /// </param>
    public static StagedArtifactNames ReadScenarioContentNames(string scenarioDirectory)
    {
        if (string.IsNullOrWhiteSpace(scenarioDirectory) || !Directory.Exists(scenarioDirectory))
            return default;

        return CheckTerrainNameConsensus(Directory.GetFiles(scenarioDirectory, "*.json"));
    }

    /// <summary>
    /// ⭐ CE-3119 — copies a scenario's files (the flat files of its directory, the same set
    /// <see cref="PrefetchScenarioAsync"/> stages) from <paramref name="sourceDirectory"/> into
    /// <paramref name="destinationDirectory"/>, creating it. Used to snapshot the scenario an exercise runs and to
    /// file that copy with the exercise's recordings (<c>docs/designs/mgmt-1/DESIGN.md</c> §13.3a).
    /// </summary>
    /// <returns>The number of files copied; <c>0</c> when the source does not exist or is empty.</returns>
    public static int CopyScenarioFiles(string sourceDirectory, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory)) return 0;
        if (string.IsNullOrWhiteSpace(destinationDirectory)) throw new ArgumentNullException(nameof(destinationDirectory));

        var files = Directory.GetFiles(sourceDirectory);
        if (files.Length == 0) return 0;

        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in files)
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
        return files.Length;
    }

    /// <summary>
    /// Reads <c>Header.TerrainName</c> from each JSON file (forward-only, no DOM) and throws
    /// <see cref="InvalidOperationException"/> if two non-empty values disagree. ⛔ <c>R-4018</c>: a file's
    /// <c>Header.TkbName</c> is never read — a scenario does not name a TKB.
    /// </summary>
    private static StagedArtifactNames CheckTerrainNameConsensus(string[] files)
    {
        string? agreedTerrainName = null;
        string? terrainSourceFile = null;

        foreach (var file in files)
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

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

        return new StagedArtifactNames(agreedTerrainName);
    }

    /// <summary>
    /// Reads one string field of <c>Header</c> from a JSON file using a forward-only
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
