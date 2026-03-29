using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bagira.Orchestrator;

/// <summary>
/// Identifies a file to be pulled from a node's local storage to the central NAS.
/// </summary>
public sealed record FileManifestEntry
{
    /// <summary>
    /// UNC or fully-qualified source path of the file on the originating node
    /// (e.g. <c>\\NODE01\c$\FDP_Temp\checkpoint_a.fdp</c>).
    /// </summary>
    public string SourceUnc { get; init; } = string.Empty;

    /// <summary>
    /// Relative destination path under the NAS base directory into which the file
    /// should be written (e.g. <c>drills\2026-03-29\checkpoint_a.fdp</c>).
    /// </summary>
    public string RelativeDest { get; init; } = string.Empty;
}

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
/// SMB Pull Gateway co-located with the <see cref="DrillMaster"/>.
///
/// <para>Owns all bulk file movement (Scenarios, Checkpoints, Archive Export/Import)
/// using the <em>SMB Pull Gateway Pattern</em>: the gateway opens one outbound SMB
/// connection to the central NAS and pulls files from leaf nodes via parallel outbound
/// reads (<see cref="PullToNasAsync"/>), rather than having all nodes push
/// simultaneously.  This avoids the ≈20-connection inbound SMB limit on Windows
/// client SKUs.</para>
///
/// <para>The maximum outbound parallelism is capped at <see cref="MaxParallelCopies"/>
/// to prevent disk or NIC saturation under large drills.</para>
///
/// <para><b>Integration point:</b> <see cref="DrillMaster"/> calls
/// <see cref="Bagira.Orchestrator.DrillMaster.FanOutSerializeLocal"/> to command all
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
        IReadOnlyList<FileManifestEntry> manifests, string nasBasePath)
    {
        if (manifests == null)  throw new ArgumentNullException(nameof(manifests));
        if (string.IsNullOrWhiteSpace(nasBasePath)) throw new ArgumentNullException(nameof(nasBasePath));

        int successCount = 0;
        int failureCount = 0;

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies };

        await Task.Run(() =>
        {
            Parallel.ForEach(manifests, options, entry =>
            {
                try
                {
                    var destPath = Path.Combine(nasBasePath, entry.RelativeDest);
                    var destDir  = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(entry.SourceUnc, destPath, overwrite: true);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref failureCount);
                }
            });
        }).ConfigureAwait(false);

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
        string nasSourcePath, IReadOnlyList<NodeDistributionTarget> targets)
    {
        if (string.IsNullOrWhiteSpace(nasSourcePath)) throw new ArgumentNullException(nameof(nasSourcePath));
        if (targets == null) throw new ArgumentNullException(nameof(targets));

        int successCount = 0;
        int failureCount = 0;

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies };

        await Task.Run(() =>
        {
            Parallel.ForEach(targets, options, target =>
            {
                try
                {
                    var destDir = Path.GetDirectoryName(target.DestinationPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(nasSourcePath, target.DestinationPath, overwrite: true);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref failureCount);
                }
            });
        }).ConfigureAwait(false);

        return new GatewayResult { SuccessCount = successCount, FailureCount = failureCount };
    }
}
