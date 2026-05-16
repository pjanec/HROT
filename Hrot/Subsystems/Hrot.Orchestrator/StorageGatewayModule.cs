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
        CheckTkbNameConsensus(files);

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

                    // Skip if source and destination are the exact same file
                    if (string.Equals(srcFile, destPath, StringComparison.OrdinalIgnoreCase))
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

        return new GatewayResult { SuccessCount = success, FailureCount = failure };
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
        return Directory.GetDirectories(root)
            .Where(d => Directory.GetFiles(d, "*.json").Length > 0)
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

        var result = new List<ExerciseInventoryItem>();
        foreach (var d in Directory.GetDirectories(root))
        {
            if (Directory.GetFiles(d, "*.fdp").Length == 0) continue;
            if (!Guid.TryParse(Path.GetFileName(d), out var exerciseId)) continue;

            var startTime = Directory.GetCreationTimeUtc(d);
            result.Add(new ExerciseInventoryItem(exerciseId, startTime, TimeSpan.Zero));
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

        var result = new List<ExerciseInventoryItem>();
        foreach (var d in Directory.GetDirectories(nasRoot))
        {
            if (Directory.GetFiles(d, "*.fdp").Length == 0) continue;
            if (!Guid.TryParse(Path.GetFileName(d), out var exerciseId)) continue;

            DateTime startTime = Directory.GetCreationTimeUtc(d);
            TimeSpan duration = TimeSpan.Zero;
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
                }
                catch
                {
                }
            }

            var metaFiles = Directory.GetFiles(d, "*.meta.json");
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

            result.Add(new ExerciseInventoryItem(exerciseId, startTime, duration));
        }
        return result;
    }

    /// <summary>
    /// Writes a <c>scenario_manifest.json</c> file to
    /// <c>&lt;nasBasePath&gt;\scenario_manifest.json</c> listing the
    /// <see cref="FileManifestEntry.RelativeDest"/> of every entry in
    /// <paramref name="manifests"/>.
    /// </summary>
    public async Task WriteScenarioManifestAsync(
        IReadOnlyList<FileManifestEntry> manifests,
        string nasBasePath)
    {
        if (manifests == null)                      throw new ArgumentNullException(nameof(manifests));
        if (string.IsNullOrWhiteSpace(nasBasePath)) throw new ArgumentNullException(nameof(nasBasePath));

        var names = manifests.Select(m => m.RelativeDest).ToArray();
        var json  = System.Text.Json.JsonSerializer.Serialize(
            new { files = names },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var manifestPath = Path.Combine(nasBasePath, "scenario_manifest.json");
        Directory.CreateDirectory(nasBasePath);
        await File.WriteAllTextAsync(manifestPath, json).ConfigureAwait(false);
    }

    // ── TkbName consensus helpers ──────────────────────────────────────────────

    /// <summary>
    /// Reads the <c>TkbName</c> field from the <c>Header</c> section of each JSON file
    /// using a forward-only <see cref="System.Text.Json.Utf8JsonReader"/> (no DOM allocation).
    /// Throws <see cref="InvalidOperationException"/> if any two non-empty TkbName values disagree.
    /// </summary>
    private static void CheckTkbNameConsensus(string[] files)
    {
        string? agreedTkbName   = null;
        string? agreedSourceFile = null;

        foreach (var file in files)
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            string? tkbName = PeekTkbNameFromFile(file);
            if (string.IsNullOrEmpty(tkbName))
                continue;

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
    }

    /// <summary>
    /// Reads <c>Header.TkbName</c> from a JSON file using a forward-only
    /// <see cref="System.Text.Json.Utf8JsonReader"/>. Returns null when the field
    /// is absent or the file cannot be read.
    /// </summary>
    private static string? PeekTkbNameFromFile(string filePath)
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
                        else if (inHeader && (propName == "TkbName" || propName == "tkbName"))
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
