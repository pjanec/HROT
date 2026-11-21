using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="StorageGatewayModule"/> (CGF1-S0301 success conditions).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class StorageGatewayTests
{
    // ── CGF1-S0301 ────────────────────────────────────────────────────────

    /// <summary>
    /// Five manifest entries pointing to real local files; after <c>PullToNasAsync</c>
    /// all five files must exist in the NAS target directory and the operation must
    /// report success.  Also verifies that the module's declared parallelism cap is ≤ 8.
    /// </summary>
    [Fact]
    public async Task PullToNas_CopiesAllFiles()
    {
        var srcDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var nasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(nasDir);
        try
        {
            var manifests = Enumerable.Range(1, 5).Select(i =>
            {
                var srcFile = Path.Combine(srcDir, $"file_{i}.bin");
                File.WriteAllText(srcFile, $"content_{i}");
                return new FileManifestEntry
                {
                    SourceUnc    = srcFile,
                    RelativeDest = $"file_{i}.bin"
                };
            }).ToList();

            var gateway = new StorageGatewayModule();
            var result  = await gateway.PullToNasAsync(manifests, nasDir);

            Assert.Equal(5, result.SuccessCount);
            Assert.Equal(0, result.FailureCount);
            Assert.True(result.IsFullSuccess);

            foreach (var entry in manifests)
                Assert.True(File.Exists(Path.Combine(nasDir, entry.RelativeDest)),
                    $"Expected file not found: {entry.RelativeDest}");

            // Verify the parallelism cap is at most 8 (SMB Pull Gateway Pattern requirement).
            Assert.True(StorageGatewayModule.MaxParallelCopies <= 8,
                $"MaxParallelCopies should be ≤ 8 but was {StorageGatewayModule.MaxParallelCopies}");
        }
        finally
        {
            Directory.Delete(srcDir, recursive: true);
            Directory.Delete(nasDir, recursive: true);
        }
    }

    /// <summary>
    /// When one source file in the manifest does not exist, the operation should not
    /// throw.  It should report <c>FailureCount == 1</c> and <c>SuccessCount == 4</c>
    /// for the four valid files.
    /// </summary>
    [Fact]
    public async Task PullToNas_FailingFile_ReturnsPartialFailureResult()
    {
        var srcDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var nasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(nasDir);
        try
        {
            var manifests = new List<FileManifestEntry>();


            // Four valid source files.
            for (int i = 1; i <= 4; i++)
            {
                var srcFile = Path.Combine(srcDir, $"file_{i}.bin");
                File.WriteAllText(srcFile, $"content_{i}");
                manifests.Add(new FileManifestEntry
                {
                    SourceUnc    = srcFile,
                    RelativeDest = $"file_{i}.bin"
                });
            }

            // One non-existent source file.
            manifests.Add(new FileManifestEntry
            {
                SourceUnc    = Path.Combine(srcDir, "does_not_exist.bin"),
                RelativeDest = "does_not_exist.bin"
            });

            var gateway = new StorageGatewayModule();
            var result  = await gateway.PullToNasAsync(manifests, nasDir);

            Assert.Equal(4, result.SuccessCount);
            Assert.Equal(1, result.FailureCount);
            Assert.False(result.IsFullSuccess);
        }
        finally
        {
            Directory.Delete(srcDir, recursive: true);
            Directory.Delete(nasDir, recursive: true);
        }
    }

    // ── CGF1-S0306 A.1 — PushToNodesAsync parity tests ───────────────────

    /// <summary>
    /// One NAS source file pushed to three distinct local temp destinations;
    /// all three destination files must exist after the call and the result must
    /// report full success.
    /// </summary>
    [Fact]
    public async Task PushToNodes_CopiesFileToAllTargets()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var nasFile = Path.Combine(dir, "scenario.json");
        File.WriteAllText(nasFile, "{\"data\":\"test\"}");

        var destDirs = Enumerable.Range(1, 3).Select(_ =>
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).ToList();

        try
        {
            var targets = destDirs.Select((d, i) => new NodeDistributionTarget
            {
                NodeId          = (Fdp.Core.NodeId)(i + 1),
                DestinationPath = Path.Combine(d, "scenario.json")
            }).ToList();

            var gateway = new StorageGatewayModule();
            var result  = await gateway.PushToNodesAsync(nasFile, targets);

            Assert.Equal(3, result.SuccessCount);
            Assert.Equal(0, result.FailureCount);
            Assert.True(result.IsFullSuccess);

            foreach (var t in targets)
                Assert.True(File.Exists(t.DestinationPath),
                    $"Expected destination file not found: {t.DestinationPath}");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            foreach (var d in destDirs)
                if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
        }
    }

    /// <summary>
    /// When one destination path is invalid, <c>PushToNodesAsync</c> must not throw. It should report a
    /// partial failure with the successful copies counted and the failing one counted separately.
    ///
    /// <para>🔴 <b>FIXED 2026-09-18 (T-1, artifact-staging batch).</b> The invalid target was the UNC
    /// path <c>\\255.255.255.255\nonexistent\scenario.json</c>, with a comment claiming it "will fail on
    /// any OS". 📐 Measured: on Linux a backslash is an ORDINARY FILENAME CHARACTER, so that string is a
    /// legal relative path, the copy SUCCEEDS, and the assertion reads 3 where it expects 2.
    /// ⭐ Replaced with a destination whose parent is an existing FILE — invalid on Windows and Linux
    /// alike, for the same reason on both.</para>
    /// </summary>
    [Fact]
    public async Task PushToNodes_BadTarget_ReturnsPartialFailure()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var nasFile = Path.Combine(dir, "scenario.json");
        File.WriteAllText(nasFile, "{\"data\":\"test\"}");

        var goodDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        // A real FILE, used as the parent of the invalid destination below.
        var blockerFile = Path.Combine(dir, "blocker");
        File.WriteAllText(blockerFile, "not a directory");

        try
        {
            var targets = new List<NodeDistributionTarget>
            {
                new() { NodeId = (Fdp.Core.NodeId)(1), DestinationPath = Path.Combine(goodDir, "scenario.json") },
                new() { NodeId = (Fdp.Core.NodeId)(2), DestinationPath = Path.Combine(goodDir, "sub", "scenario.json") },
                // ⭐ Invalid on EVERY OS: `blocker` is a FILE, so creating a directory under it fails.
                new() { NodeId = (Fdp.Core.NodeId)(3), DestinationPath = Path.Combine(blockerFile, "sub", "scenario.json") },
            };

            var gateway = new StorageGatewayModule();
            var result  = await gateway.PushToNodesAsync(nasFile, targets);

            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(1, result.FailureCount);
            Assert.False(result.IsFullSuccess);
        }
        finally
        {
            if (Directory.Exists(dir))     Directory.Delete(dir,     recursive: true);
            if (Directory.Exists(goodDir)) Directory.Delete(goodDir, recursive: true);
        }
    }

    /// <summary>
    /// When the NAS source directory for a scenario exists but contains no files,
    /// <c>PrefetchScenarioAsync</c> must throw <see cref="InvalidOperationException"/>
    /// so the orchestrator treats the transition as a failure (CGF1 BATCH-15 A.2).
    /// </summary>
    [Fact]
    public async Task PrefetchScenarioAsync_EmptyDirectory_ThrowsInvalidOperation()
    {
        var nasDir     = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var scenarioId = "empty_scenario";
        // 🔴 FIXED 2026-09-18 (T-1, artifact-staging batch): this built `{nas}/{id}` and production
        //    looks in `{nas}/scenarios/{id}` (StorageGatewayModule.cs:238, via
        //    OrchestrationConstants.ScenariosDirectoryName). ⛔ So the call threw DirectoryNotFound
        //    BEFORE reaching the empty-directory guard, and this test has NEVER exercised the guard it
        //    is named for — it asserted the wrong exception and went red the moment anyone looked.
        // ⚠⚠ The consequence is bigger than one red: every test that reaches PrefetchScenarioAsync
        //    through this layout died at the same line, so the COPY LOOP and CheckTkbNameConsensus were
        //    untested too. That is what this batch extends.
        var scenarioDir = Path.Combine(nasDir, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
        Directory.CreateDirectory(scenarioDir);   // exists but contains no files

        try
        {
            var gateway = new StorageGatewayModule();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => gateway.PrefetchScenarioAsync(scenarioId, new List<NodeDistributionTarget>(), nasDir));

            Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(scenarioId, ex.Message);
        }
        finally
        {
            if (Directory.Exists(nasDir)) Directory.Delete(nasDir, recursive: true);
        }
    }

    // ── CGF1-S0505: Cancellation cleanup tests ───────────────────────────────

    /// <summary>
    /// When a <see cref="CancellationToken"/> is cancelled before or during
    /// <see cref="StorageGatewayModule.PullToNasAsync"/>, the call must:
    ///   1. Throw <see cref="OperationCanceledException"/>.
    ///   2. Delete any partially-written destination files.
    /// </summary>
    [Fact]
    public async Task PullToNasAsync_CancelsAndCleansPartialFiles()
    {
        var srcDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var nasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(nasDir);

        try
        {
            // Create 3 source files.
            var manifests = Enumerable.Range(1, 3).Select(i =>
            {
                var srcFile = Path.Combine(srcDir, $"cancel_{i}.bin");
                File.WriteAllText(srcFile, $"content_{i}");
                return new FileManifestEntry
                {
                    SourceUnc    = srcFile,
                    RelativeDest = $"cancel_{i}.bin",
                };
            }).ToList();

            // Cancel immediately so the operation is aborted before any file copies complete.
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var gateway = new StorageGatewayModule();

            // Must throw OperationCanceledException.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => gateway.PullToNasAsync(manifests, nasDir, cts.Token));

            // Any partially-written files must have been cleaned up.
            foreach (var entry in manifests)
            {
                var dest = Path.Combine(nasDir, entry.RelativeDest);
                Assert.False(File.Exists(dest),
                    $"Partial file should have been cleaned up: {dest}");
            }
        }
        finally
        {
            if (Directory.Exists(srcDir)) Directory.Delete(srcDir, recursive: true);
            if (Directory.Exists(nasDir)) Directory.Delete(nasDir, recursive: true);
        }
    }

    /// <summary>
    /// Parity test for <see cref="StorageGatewayModule.PushToNodesAsync"/>:
    /// when the <see cref="CancellationToken"/> is cancelled before the call,
    /// <see cref="OperationCanceledException"/> must propagate and any partially-written
    /// destination files must be deleted.
    /// </summary>
    [Fact]
    public async Task PushToNodesAsync_CancelsAndCleansPartialFiles()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(baseDir);
        var nasFile = Path.Combine(baseDir, "source.bin");
        File.WriteAllText(nasFile, "src content");

        var destDirs = Enumerable.Range(1, 3)
            .Select(_ => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()))
            .ToList();

        try
        {
            var targets = destDirs.Select((d, i) => new NodeDistributionTarget
            {
                NodeId          = (Fdp.Core.NodeId)(i + 1),
                DestinationPath = Path.Combine(d, "source.bin"),
            }).ToList();

            using var cts = new CancellationTokenSource();
            cts.Cancel();  // cancel before the call

            var gateway = new StorageGatewayModule();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => gateway.PushToNodesAsync(nasFile, targets, cts.Token));

            foreach (var t in targets)
                Assert.False(File.Exists(t.DestinationPath),
                    $"Partial destination file should have been cleaned up: {t.DestinationPath}");
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            foreach (var d in destDirs)
                if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
        }
    }

    /// <summary>
    /// ⭐ CE-4330 ② / ⑤ — the archive's counted copy: every byte is counted into the job's progress, the copy keeps the source's write time (the C1
    /// freshness skip compares it, as it did with <c>File.Copy</c>), and while the job GIVES WAY (a Live runs) it is held to its share of
    /// <see cref="StorageGatewayModule.GiveWayBytesPerSecond"/>.
    /// 🔴 Mutation: the give-way wait removed ⇒ the slow copy is as fast as the free one ⇒ red.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void CE4330_ACountedCopy_CountsItsBytes_KeepsTheWriteTime_AndGivesWayWhenAsked()
    {
        var root = Path.Combine(Path.GetTempPath(), "fdp_ce4330c_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var src = Path.Combine(root, "node_1.fdp");
            var bytes = new byte[3 * 1024 * 1024];
            new Random(7).NextBytes(bytes);
            File.WriteAllBytes(src, bytes);
            var stamp = new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(src, stamp);

            var free = new Fdp.Toolkit.Orchestration.StorageTransferProgress();
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            StorageGatewayModule.CopyCounted(src, Path.Combine(root, "a.part"), free, CancellationToken.None);
            var freeSeconds = t0.Elapsed.TotalSeconds;
            Assert.Equal(bytes.Length, free.BytesDone);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(root, "a.part")));
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, "a.part")));

            var slow = new Fdp.Toolkit.Orchestration.StorageTransferProgress { GiveWay = true };
            t0.Restart();
            StorageGatewayModule.CopyCounted(src, Path.Combine(root, "b.part"), slow, CancellationToken.None);
            // one thread's share is GiveWayBytesPerSecond / MaxParallelCopies: 3 MB at 2.5 MB/s ≈ 1.2 s (the first chunk is free)
            double floor = 2.0 * 1024 * 1024 / (StorageGatewayModule.GiveWayBytesPerSecond / StorageGatewayModule.MaxParallelCopies);
            Assert.True(t0.Elapsed.TotalSeconds >= floor * 0.9, $"giving way took {t0.Elapsed.TotalSeconds:F2} s (free: {freeSeconds:F2} s), expected ≥ {floor:F2} s");
            Assert.Equal(bytes.Length, slow.BytesDone);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// ⭐ CE-4330 / CE-4331 (§9) — a node with no recording of its own in the archive LISTENS: it is not pushed anything and is NOT a failure (the import
    /// now reads its failure count, so a listener counted as failed would refuse every AAR with a display station).
    /// 🔴 Mutation: the listener filter removed ⇒ node 2's missing file counts as a failure ⇒ red.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task CE4330_ANodeWithNoArchivedRecording_ListensAndIsNotAFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "fdp_ce4330l_" + Guid.NewGuid().ToString("N"));
        try
        {
            var ex = Guid.NewGuid().ToString();
            var nasEx = Path.Combine(root, "nas", OrchestrationConstants.ExercisesDirectoryName, ex);
            Directory.CreateDirectory(nasEx);
            File.WriteAllText(Path.Combine(nasEx, OrchestrationConstants.GetNodeRecordingFileName(1)), "rec1");
            var targets = new List<NodeDistributionTarget>();
            foreach (var n in new[] { 1, 2 })
                targets.Add(new NodeDistributionTarget
                {
                    NodeId = (Fdp.Core.NodeId)n,
                    DestinationPath = Path.Combine(root, "node" + n, ex, OrchestrationConstants.GetNodeRecordingFileName(n)),
                });
            var progress = new StorageTransferProgress();

            var result = await new StorageGatewayModule().PrefetchArchiveAsync(ex, targets, Path.Combine(root, "nas"), default, progress);

            Assert.Equal((1, 0), (result.SuccessCount, result.FailureCount));
            Assert.True(File.Exists(targets[0].DestinationPath));
            Assert.False(File.Exists(targets[1].DestinationPath));
            Assert.Equal((1, 1), (progress.FilesDone, progress.FilesTotal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// ⭐⭐ CE-4331 ② (§9a) — an import PLACES the recordings on the current cluster: each file goes to the node of its recorded NAME (and kind), copied
    /// under the CHOSEN node's own file name; a node of another kind listens; a cluster where nothing fits refuses and copies nothing.
    /// 🔴 Mutation: the push using the target's own recording name instead of the placed file ⇒ node 10 gets nothing / the wrong bytes ⇒ red.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task CE4331_AnImportPlacesEachRecordingOnTheNodeOfItsName_UnderThatNodesOwnFileName()
    {
        var root = Path.Combine(Path.GetTempPath(), "fdp_ce4331p_" + Guid.NewGuid().ToString("N"));
        try
        {
            var ex = Guid.NewGuid().ToString();
            var nasEx = Path.Combine(root, "nas", OrchestrationConstants.ExercisesDirectoryName, ex);
            Directory.CreateDirectory(nasEx);
            void Rec(int id, string name)
            {
                var f = Path.Combine(nasEx, OrchestrationConstants.GetNodeRecordingFileName(id));
                File.WriteAllText(f, "bytes-of-" + name);
                File.WriteAllText(f + ".meta.json", Fdp.Core.FlightRecorder.Metadata.MetadataSerializer.Serialize(
                    new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata { NodeName = name, Subsystem = "CGF", Roles = 1 }));
            }
            Rec(401, "cgf-a");
            Rec(402, "cgf-b");
            NodeDistributionTarget T(int n, string name, string sub, int roles) => new()
            {
                NodeId = (Fdp.Core.NodeId)n, NodeName = name, Subsystem = sub, Roles = roles,
                DestinationPath = Path.Combine(root, "node" + n, ex, OrchestrationConstants.GetNodeRecordingFileName(n)),
            };
            var targets = new List<NodeDistributionTarget> { T(10, "cgf-b", "CGF", 1), T(11, "cgf-a", "CGF", 1), T(12, "ig-1", "IG", 0) };

            var result = await new StorageGatewayModule().PrefetchArchiveAsync(ex, targets, Path.Combine(root, "nas"));

            Assert.Equal((2, 0), (result.SuccessCount, result.FailureCount));
            Assert.Equal("bytes-of-cgf-b", File.ReadAllText(targets[0].DestinationPath));   // node 10 plays cgf-b's file, under its own name node_10.fdp
            Assert.Equal("bytes-of-cgf-a", File.ReadAllText(targets[1].DestinationPath));
            Assert.Equal(new[] { 12 }, result.Placement!.Listeners);
            Assert.False(File.Exists(targets[2].DestinationPath));

            var none = await new StorageGatewayModule().PrefetchArchiveAsync(ex, new List<NodeDistributionTarget> { T(20, "ig-2", "IG", 0) }, Path.Combine(root, "nas"));
            Assert.True(none.Placement!.Refused);
            Assert.Equal((0, 0), (none.SuccessCount, none.FailureCount));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

/// <summary>
/// TKB-018 -- the scenario files' header consensus. ⛔ <c>R-4018</c>: a legacy <c>TkbName</c> in them is ignored.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class StorageGatewayTkbConsensusTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static void WriteJson(string path, string json) =>
        File.WriteAllText(path, json, new UTF8Encoding(false));

    private static string MakeScenarioDir(string nasBase, string scenarioId)
    {
        var dir = Path.Combine(nasBase, "scenarios", scenarioId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── Test 1 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrefetchScenario_SameTkbName_AllFiles_Succeeds()
    {
        var nasDir     = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var scenarioId = "same_tkb";
        var srcDir     = MakeScenarioDir(nasDir, scenarioId);

        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(destDir);

        try
        {
            WriteJson(Path.Combine(srcDir, "Hrot.SimHost.json"),
                "{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\",\"TkbName\":\"Alpha_v1\"},\"Entities\":{}}");
            WriteJson(Path.Combine(srcDir, "Hrot.CGF.json"),
                "{\"Header\":{\"SubsystemType\":\"Hrot.CGF\",\"TkbName\":\"Alpha_v1\"},\"Entities\":{}}");

            var gateway = new StorageGatewayModule();
            var targets = new List<NodeDistributionTarget>
            {
                new NodeDistributionTarget { NodeId = (Fdp.Core.NodeId)(1), DestinationPath = destDir },
            };

            var result = await gateway.PrefetchScenarioAsync(scenarioId, targets, nasDir);

            Assert.True(result.IsFullSuccess);
        }
        finally
        {
            if (Directory.Exists(nasDir))  Directory.Delete(nasDir,  recursive: true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    /// <summary>
    /// ⭐⭐ CE-4099 (AQ108 §4c A7) — a RECOVERY prefetch delivers the autosave AS the node's <c>scenario.json</c>; every other file of the folder goes as it
    /// is; the NAS folder is not touched. Then the shared staleness rule (<see cref="AutosaveFiles.Read"/>): fresh while <c>scenario.json</c> is the one it
    /// was based on, stale once it changed. 🔴 Red-proof (compiles): the gateway ignoring <c>scenarioFileFrom</c> ⇒ the saved text arrives ⇒ red.
    /// </summary>
    [Fact]
    public async Task CE4099_ARecoveryPrefetch_DeliversTheAutosaveAsScenarioJson_AndStalenessIsTheSavedFilesHash()
    {
        var nasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(destDir);
        try
        {
            var srcDir = MakeScenarioDir(nasDir, "rec");
            const string saved = "{\"Header\":{\"SubsystemType\":\"Hrot.Scenario\"},\"Entities\":{}}";
            const string edited = "{\"Header\":{\"SubsystemType\":\"Hrot.Scenario\"},\"Entities\":{\"aaaa0000-0000-4000-8000-000000000001\":{}}}";
            WriteJson(Path.Combine(srcDir, "scenario.json"), saved);
            WriteJson(Path.Combine(srcDir, "notes.json"), "{}");
            var autosaveDir = Path.Combine(srcDir, AutosaveFiles.Folder);
            Directory.CreateDirectory(autosaveDir);
            File.WriteAllText(Path.Combine(autosaveDir, AutosaveFiles.DataFile), edited);
            File.WriteAllText(Path.Combine(autosaveDir, AutosaveFiles.MetaFile),
                "{\"at\":\"2026-11-08T14:41:00.0000000Z\",\"savedHash\":\"" + Fdp.Toolkit.Scenario.ScenarioDescription.HashOf(saved) + "\"}");

            var status = AutosaveFiles.Read(nasDir, "rec", Fdp.Toolkit.Scenario.ScenarioDescription.HashOf);
            Assert.True(status.Exists && !status.Stale && status.Refusal is null);
            Assert.Equal(new DateTime(2026, 11, 8, 14, 41, 0, DateTimeKind.Utc), status.AtUtc);

            var targets = new List<NodeDistributionTarget> { new() { NodeId = (Fdp.Core.NodeId)(1), DestinationPath = destDir } };
            var result = await new StorageGatewayModule().PrefetchScenarioAsync("rec", targets, nasDir, scenarioFileFrom: status.DataPath);

            Assert.True(result.IsFullSuccess);
            Assert.Equal(edited, File.ReadAllText(Path.Combine(destDir, "scenario.json")));     // the autosave stands in
            Assert.True(File.Exists(Path.Combine(destDir, "notes.json")));                       // the rest of the folder as it is
            Assert.Equal(saved, File.ReadAllText(Path.Combine(srcDir, "scenario.json")));       // the NAS is untouched

            WriteJson(Path.Combine(srcDir, "scenario.json"), saved.Replace("Entities", "entities"));   // saved elsewhere since
            var stale = AutosaveFiles.Read(nasDir, "rec", Fdp.Toolkit.Scenario.ScenarioDescription.HashOf);
            Assert.True(stale.Stale);
            Assert.NotNull(stale.Refusal);
            Assert.NotNull(AutosaveFiles.Read(nasDir, "nothing-here", Fdp.Toolkit.Scenario.ScenarioDescription.HashOf).Refusal);
        }
        finally
        {
            if (Directory.Exists(nasDir)) Directory.Delete(nasDir, recursive: true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⛔⛔ <c>R-4018</c> — FLIPPED: a scenario never names a TKB, so two slices carrying DIFFERENT legacy
    /// <c>TkbName</c> values are not a conflict any more — the field is never read. Was: throws.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_ConflictingLegacyTkbNames_AreIgnored()
    {
        var nasDir     = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var scenarioId = "conflict_tkb";
        var srcDir     = MakeScenarioDir(nasDir, scenarioId);

        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(destDir);

        try
        {
            WriteJson(Path.Combine(srcDir, "Hrot.SimHost.json"),
                "{\"Header\":{\"TkbName\":\"Alpha_v1\"},\"Entities\":{}}");
            WriteJson(Path.Combine(srcDir, "Hrot.CGF.json"),
                "{\"Header\":{\"TkbName\":\"Beta_v1\"},\"Entities\":{}}");

            var gateway = new StorageGatewayModule();
            var targets = new List<NodeDistributionTarget>
            {
                new NodeDistributionTarget { NodeId = (Fdp.Core.NodeId)(1), DestinationPath = destDir },
            };

            var result = await gateway.PrefetchScenarioAsync(scenarioId, targets, nasDir);

            Assert.True(result.IsFullSuccess);
        }
        finally
        {
            if (Directory.Exists(nasDir))  Directory.Delete(nasDir,  recursive: true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrefetchScenario_NullTkbNames_AllFiles_Succeeds()
    {
        var nasDir     = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var scenarioId = "no_tkb";
        var srcDir     = MakeScenarioDir(nasDir, scenarioId);

        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(destDir);

        try
        {
            WriteJson(Path.Combine(srcDir, "Hrot.SimHost.json"),
                "{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\"},\"Entities\":{}}");
            WriteJson(Path.Combine(srcDir, "Hrot.CGF.json"),
                "{\"Header\":{\"SubsystemType\":\"Hrot.CGF\"},\"Entities\":{}}");

            var gateway = new StorageGatewayModule();
            var targets = new List<NodeDistributionTarget>
            {
                new NodeDistributionTarget { NodeId = (Fdp.Core.NodeId)(1), DestinationPath = destDir },
            };

            var result = await gateway.PrefetchScenarioAsync(scenarioId, targets, nasDir);

            Assert.True(result.IsFullSuccess);
        }
        finally
        {
            if (Directory.Exists(nasDir))  Directory.Delete(nasDir,  recursive: true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    // ── Test 4 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrefetchScenario_MixedNullAndNonNull_SameName_Succeeds()
    {
        var nasDir     = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var scenarioId = "mixed_tkb";
        var srcDir     = MakeScenarioDir(nasDir, scenarioId);

        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(destDir);

        try
        {
            WriteJson(Path.Combine(srcDir, "Hrot.SimHost.json"),
                "{\"Header\":{\"TkbName\":\"Alpha_v1\"},\"Entities\":{}}");
            WriteJson(Path.Combine(srcDir, "Hrot.CGF.json"),
                "{\"Header\":{\"SubsystemType\":\"Orchestrator\"},\"Entities\":{}}");

            var gateway = new StorageGatewayModule();
            var targets = new List<NodeDistributionTarget>
            {
                new NodeDistributionTarget { NodeId = (Fdp.Core.NodeId)(1), DestinationPath = destDir },
            };

            var result = await gateway.PrefetchScenarioAsync(scenarioId, targets, nasDir);

            Assert.True(result.IsFullSuccess);
        }
        finally
        {
            if (Directory.Exists(nasDir))  Directory.Delete(nasDir,  recursive: true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    // ── Test 5 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrefetchScenario_NonJsonFiles_AreIgnoredByConsensusCheck()
    {
        var nasDir     = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var scenarioId = "non_json_tkb";
        var srcDir     = MakeScenarioDir(nasDir, scenarioId);

        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(destDir);

        try
        {
            WriteJson(Path.Combine(srcDir, "Hrot.SimHost.json"),
                "{\"Header\":{\"TkbName\":\"Alpha_v1\"},\"Entities\":{}}");
            File.WriteAllBytes(Path.Combine(srcDir, "some.bin"), new byte[] { 0x01, 0x02, 0x03 });

            var gateway = new StorageGatewayModule();
            var targets = new List<NodeDistributionTarget>
            {
                new NodeDistributionTarget { NodeId = (Fdp.Core.NodeId)(1), DestinationPath = destDir },
            };

            var result = await gateway.PrefetchScenarioAsync(scenarioId, targets, nasDir);

            Assert.True(result.IsFullSuccess);
        }
        finally
        {
            if (Directory.Exists(nasDir))  Directory.Delete(nasDir,  recursive: true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    /// <summary>
    /// ⭐ CE-4271 — a scenario folder holding the committed still (<c>preview.jpg</c>, a JPEG, not a PNG) prefetches
    /// cleanly: the consensus check ignores it and the JPEG, byte for byte, lands in the node's staging.
    /// </summary>
    [Fact]
    public async Task CE4271_AScenarioFolderWithAPreviewJpg_PrefetchesCleanly_AndTheJpegLandsInTheNodesStaging()
    {
        var nasDir     = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var scenarioId = "with_still";
        var srcDir     = MakeScenarioDir(nasDir, scenarioId);
        var destDir    = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(destDir);
        try
        {
            WriteJson(Path.Combine(srcDir, "Hrot.SimHost.json"), "{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\"},\"Entities\":{}}");
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9 };
            File.WriteAllBytes(Path.Combine(srcDir, "preview.jpg"), jpeg);

            var targets = new List<NodeDistributionTarget> { new NodeDistributionTarget { NodeId = (Fdp.Core.NodeId)(1), DestinationPath = destDir } };
            var result  = await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nasDir);

            Assert.True(result.IsFullSuccess);
            var staged = Directory.GetFiles(destDir, "preview.jpg", SearchOption.AllDirectories);
            Assert.Single(staged);
            Assert.Equal(jpeg, File.ReadAllBytes(staged[0]));
        }
        finally
        {
            if (Directory.Exists(nasDir))  Directory.Delete(nasDir,  recursive: true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    // ── CE-280 — foreign-slice routing on load ──────────────────────────────────

    /// <summary>
    /// PrefetchScenario routes a <c>foreign/node_&lt;id&gt;.json</c> slice ONLY to the node whose id matches
    /// the filename — unlike the top-level <c>scenario.json</c>, which every node stages. This is the load
    /// side of the distributed save's foreign route (§4c/§6b T-C): ExCon's observer slice returns to ExCon,
    /// and no other node receives it.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_RoutesForeignSlice_ToOriginNodeOnly()
    {
        var nasDir     = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var scenarioId = "foreign_route";
        var srcDir     = MakeScenarioDir(nasDir, scenarioId);

        var destEcs     = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());   // node 100 (ECS)
        var destForeign = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());   // node 200 (ExCon)
        Directory.CreateDirectory(destEcs);
        Directory.CreateDirectory(destForeign);

        try
        {
            // Top-level canonical scenario (every node stages it) + a foreign slice for node 200 only.
            WriteJson(Path.Combine(srcDir, "scenario.json"),
                "{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\"},\"Entities\":{}}");
            var foreignDir = Path.Combine(srcDir, "foreign");
            Directory.CreateDirectory(foreignDir);
            WriteJson(Path.Combine(foreignDir, "node_200.json"),
                "{\"$meta\":{\"docType\":\"ExCon.Observer\"},\"Observer\":{}}");

            var gateway = new StorageGatewayModule();
            var targets = new List<NodeDistributionTarget>
            {
                new NodeDistributionTarget { NodeId = (Fdp.Core.NodeId)(100), DestinationPath = destEcs },
                new NodeDistributionTarget { NodeId = (Fdp.Core.NodeId)(200), DestinationPath = destForeign },
            };

            var result = await gateway.PrefetchScenarioAsync(scenarioId, targets, nasDir);
            Assert.True(result.IsFullSuccess);

            // Both nodes stage the canonical scenario.json.
            Assert.True(File.Exists(Path.Combine(destEcs, "scenario.json")));
            Assert.True(File.Exists(Path.Combine(destForeign, "scenario.json")));

            // Only node 200 receives its foreign slice; node 100 does not.
            Assert.True(File.Exists(Path.Combine(destForeign, "foreign", "node_200.json")));
            Assert.False(Directory.Exists(Path.Combine(destEcs, "foreign")));
        }
        finally
        {
            if (Directory.Exists(nasDir))       Directory.Delete(nasDir,       recursive: true);
            if (Directory.Exists(destEcs))      Directory.Delete(destEcs,      recursive: true);
            if (Directory.Exists(destForeign))  Directory.Delete(destForeign,  recursive: true);
        }
    }

    // ══ S2 — STAGING THE NAMED ARTIFACTS ══════════════════════════════════════════════════════════
    //
    // ⭐⭐⭐ These are the rails BP-550 never had. 📐 Before this batch nothing in the tree wrote a TKB
    //    zip or a ScenarioHeader.json into node staging, and — measured — no test ever reached
    //    PrefetchScenarioAsync's copy loop at all, because every fixture built the NAS layout without
    //    the `scenarios/` segment. Both halves are fixed above.
    // 📄 docs/DESIGN_Artifact_Staging.md §2, §4, §6.

    /// <summary>Builds a NAS with one scenario slice naming <paramref name="tkbName"/>/<paramref name="terrainName"/>.</summary>
    private static string MakeNas(string scenarioId, string? tkbName, string? terrainName, out string scenarioDir)
    {
        var nas = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        scenarioDir = Path.Combine(nas, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
        Directory.CreateDirectory(scenarioDir);

        var header = new List<string>();
        if (tkbName     != null) header.Add($"\"tkbName\":\"{tkbName}\"");
        if (terrainName != null) header.Add($"\"terrainName\":\"{terrainName}\"");
        File.WriteAllText(
            Path.Combine(scenarioDir, "Hrot.SimHost.json"),
            "{\"header\":{" + string.Join(",", header) + "},\"entities\":[]}");
        return nas;
    }

    private static string PublishTkb(string nas, string name, string content)
    {
        var dir = OrchestrationConstants.GetNasTkbRoot(nas);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, name + OrchestrationConstants.TkbArtifactExtension);
        File.WriteAllText(file, content);
        return file;
    }

    private static NodeDistributionTarget TargetIn(string root, int nodeId, string scenarioId) => new()
    {
        NodeId             = (Fdp.Core.NodeId)(nodeId),
        DestinationPath    = Path.Combine(
            OrchestrationConstants.GetNodeStagingRoot(root, nodeId),
            OrchestrationConstants.ScenariosDirectoryName, scenarioId),
        TkbDestinationPath = OrchestrationConstants.GetNodeTkbStagingRoot(root, nodeId),
    };

    /// <summary>
    /// ⭐⭐⭐ <c>R-4018</c> — the deployment's ONE TKB (the configured name) reaches EVERY target node on every prefetch,
    /// with the <see cref="StagedTkb"/> record naming it — whatever the scenario says. ⚠ The scenario here names a
    /// DIFFERENT legacy TKB, which must be ignored. (Was <c>S2b</c>: the scenario's own TKB, <c>BP-550</c>.)
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_SyncsTheConfiguredTkb_OnEveryNode_WhateverTheScenarioSays()
    {
        const string scenarioId = "s1";
        var nas  = MakeNas(scenarioId, "Legacy_v0", null, out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        PublishTkb(nas, "Alpha_v1", "zip-bytes");
        PublishTkb(nas, "Legacy_v0", "other-bytes");

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId), TargetIn(root, 2, scenarioId) };
            var result  = await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas, tkbName: "Alpha_v1");

            Assert.Equal(0, result.FailureCount);
            foreach (var t in targets)
            {
                Assert.True(File.Exists(Path.Combine(t.TkbDestinationPath, "Alpha_v1.zip")),
                    $"node {t.NodeId} did not receive the configured TKB");
                Assert.Equal("Alpha_v1", StagedTkb.Read(t.TkbDestinationPath));
                Assert.False(File.Exists(Path.Combine(t.TkbDestinationPath, "Legacy_v0.zip")),
                    "the scenario's legacy TkbName must never be read");
            }
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐⭐ <c>R-4018</c> — the DEFAULT deployment today: a configured name with NO master copy on the NAS is legal. Nothing
    /// fails, nothing is staged, and a record left by an earlier sync is REMOVED, so every node falls back to its built-in
    /// catalogue together.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_WithNoTkbMasterCopy_SucceedsAndClearsAStaleRecord()
    {
        const string scenarioId = "s1b";
        var nas  = MakeNas(scenarioId, null, null, out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId) };
            StagedTkb.Write(targets[0].TkbDestinationPath, "HrotDefault");   // an earlier sync's record

            var result = await new StorageGatewayModule().PrefetchScenarioAsync(
                scenarioId, targets, nas, tkbName: "HrotDefault");

            Assert.Equal(0, result.FailureCount);
            Assert.Null(StagedTkb.Read(targets[0].TkbDestinationPath));
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐⭐⭐ BP-557 (2026-10-03) — a scenario naming a TERRAIN leaves that terrain's whole FOLDER
    /// (definition + world + nested files) on EVERY target node, at <c>{node}/Terrain/&lt;name&gt;/</c> —
    /// where the node's <c>TerrainCatalog</c> looks first. 📄 docs/DESIGN_Terrain_World.md §7.3 W12.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_StagesTheNamedTerrainFolder_OnEveryNode_BP557()
    {
        const string scenarioId = "s-terrain";
        var nas  = MakeNas(scenarioId, null, "test-town", out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var src  = Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(nas), "test-town");
        Directory.CreateDirectory(Path.Combine(src, "roads"));
        File.WriteAllText(Path.Combine(src, Fdp.Toolkit.Terrain.TerrainCatalog.DefinitionFileName), "{}");
        File.WriteAllText(Path.Combine(src, "w.geojson"), "{}");
        File.WriteAllText(Path.Combine(src, "roads", "r.json"), "{}");

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId), TargetIn(root, 2, scenarioId) };
            await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas);

            foreach (var t in targets)
            {
                var nodeTerrain = Path.Combine(Path.GetDirectoryName(t.TkbDestinationPath)!,
                    Fdp.Toolkit.Terrain.TerrainCatalog.StagingDirectoryName, "test-town");
                Assert.True(File.Exists(Path.Combine(nodeTerrain, Fdp.Toolkit.Terrain.TerrainCatalog.DefinitionFileName)),
                    $"node {t.NodeId} did not receive the terrain definition");
                Assert.True(File.Exists(Path.Combine(nodeTerrain, "w.geojson")));
                Assert.True(File.Exists(Path.Combine(nodeTerrain, "roads", "r.json")));
            }
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>S2c</c> — the header the node reads its names out of now EXISTS, and in the shape the node
    /// actually parses: a FLAT object with PascalCase keys.
    ///
    /// <para>⚠ Asserting the SHAPE, not just existence, is what makes this non-vacuous: the scenario's own
    /// header block is nested under <c>header</c> and camelCase, and copying it verbatim would produce a
    /// file <c>ScenarioTerrainName</c> fails to read.</para>
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_WritesTheStagedHeader_InTheShapeTheNodeReads()
    {
        const string scenarioId = "s2";
        var nas  = MakeNas(scenarioId, "Alpha_v1", "basic-desert", out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        PublishTkb(nas, "Alpha_v1", "zip-bytes");

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId) };
            await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas, tkbName: "Alpha_v1");

            var headerPath = Path.Combine(targets[0].TkbDestinationPath, StorageGatewayModule.StagedScenarioHeaderFileName);
            Assert.True(File.Exists(headerPath), "the staged ScenarioHeader.json was not written");

            using var doc = JsonDocument.Parse(File.ReadAllText(headerPath));
            Assert.Equal("basic-desert", doc.RootElement.GetProperty("TerrainName").GetString());
            // ⛔ R-4018 — the SCENARIO header carries no TKB name; the TKB's name lives in its own sync record.
            Assert.False(doc.RootElement.TryGetProperty("TkbName", out _));
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐⭐ A scenario naming NO terrain, with no TKB sync asked for, stages nothing and is NOT a failure.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_WithNoNamedArtifacts_StagesNothingAndSucceeds()
    {
        const string scenarioId = "s3";
        var nas  = MakeNas(scenarioId, null, null, out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId) };
            var result  = await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas);

            Assert.Equal(0, result.FailureCount);
            Assert.False(Directory.Exists(targets[0].TkbDestinationPath),
                "nothing should be staged for a scenario that names no artifacts");
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐ T-6 (CE-3432) — a prefetch prunes the node's staged overlays: with 12 staged and the default N = 8, the current one (even if it is the OLDEST by use) plus the 8 most recently used others stay, the other 3 go, and the NAS copy is untouched.
    /// 🔴 Red-proof: drop the <c>OverlayStaging.Prune</c> call ⇒ all 13 stay ⇒ red; prune by name instead of use ⇒ the wrong 3 go ⇒ red; let it delete the current one ⇒ red.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_PrunesTheNodesStagedOverlays_KeepingTheCurrentAndTheNewestUsed_T6()
    {
        var nas = MakeNas("with-overlay", null, "test-town", out var scenarioDir);
        var ov = Path.Combine(scenarioDir, "terrain-overlay");
        Directory.CreateDirectory(ov);
        File.WriteAllText(Path.Combine(ov, "overlay.json"), "{\"format\":1,\"contentHash\":\"current\",\"base\":{\"name\":\"test-town\",\"contentHash\":\"b\"},\"scenario\":\"with-overlay\",\"world\":\"overlay.world.geojson\"}");
        File.WriteAllText(Path.Combine(ov, "overlay.world.geojson"), "{}");
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, "with-overlay") };
            var overlays = Path.Combine(Path.GetDirectoryName(targets[0].TkbDestinationPath)!, Fdp.Toolkit.Terrain.TerrainCatalog.StagingDirectoryName, "overlays");
            var now = DateTime.UtcNow;
            for (int i = 0; i < 12; i++)   // o00 is the oldest used, o11 the newest
            {
                var d = Path.Combine(overlays, $"o{i:00}"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "overlay.json"), "{}");
                Directory.SetLastWriteTimeUtc(d, now.AddHours(-100)); Directory.SetLastAccessTimeUtc(d, now.AddHours(-(50 - i)));
            }
            var current = Path.Combine(overlays, "current"); Directory.CreateDirectory(current); File.WriteAllText(Path.Combine(current, "overlay.json"), "{}");
            Directory.SetLastWriteTimeUtc(current, now.AddHours(-200)); Directory.SetLastAccessTimeUtc(current, now.AddHours(-200));   // the oldest of all — and in use

            var result = await new StorageGatewayModule().PrefetchScenarioAsync("with-overlay", targets, nas);
            Assert.Equal(0, result.FailureCount);

            var left = Directory.GetDirectories(overlays).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "current", "o04", "o05", "o06", "o07", "o08", "o09", "o10", "o11" }, left);   // the current one + the 8 newest used; o00…o03 gone
            Assert.True(File.Exists(Path.Combine(scenarioDir, "terrain-overlay", "overlay.json")), "the source (NAS) copy is untouched");
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐ O-4 (CE-4415, DESIGN_Terrain_Authoring §18a) — a scenario whose folder holds <c>terrain-overlay/</c> leaves the overlay on EVERY target at
    /// <c>{node}/Terrain/overlays/&lt;contentHash&gt;/</c> (same files, nested ones too), and the staged header carries <c>OverlayHash</c>; a scenario WITHOUT one stages
    /// no folder and writes no field — even after one that had it (the header is rewritten whole). 🔴 Red-proof: skip the overlay mirror ⇒ the folder is missing; drop the header field ⇒ the hash reads null.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_StagesTheTerrainOverlayByHash_AndNamesItInTheHeader_O4()
    {
        var nas  = MakeNas("with-overlay", null, "test-town", out var scenarioDir);
        Directory.CreateDirectory(Path.Combine(nas, OrchestrationConstants.ScenariosDirectoryName, "plain"));
        File.WriteAllText(Path.Combine(nas, OrchestrationConstants.ScenariosDirectoryName, "plain", "Hrot.SimHost.json"), "{\"header\":{\"terrainName\":\"test-town\"},\"entities\":[]}");
        var ov = Path.Combine(scenarioDir, "terrain-overlay");
        Directory.CreateDirectory(Path.Combine(ov, "buildings"));
        File.WriteAllText(Path.Combine(ov, "overlay.json"), "{\"format\":1,\"contentHash\":\"abc123\",\"base\":{\"name\":\"test-town\",\"contentHash\":\"b\"},\"scenario\":\"with-overlay\",\"world\":\"overlay.world.geojson\"}");
        File.WriteAllText(Path.Combine(ov, "overlay.world.geojson"), "{}");
        File.WriteAllText(Path.Combine(ov, "buildings", "h.building.json"), "{}");
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, "with-overlay"), TargetIn(root, 2, "with-overlay") };
            var gateway = new StorageGatewayModule();
            var result = await gateway.PrefetchScenarioAsync("with-overlay", targets, nas);
            Assert.Equal(0, result.FailureCount);
            foreach (var t in targets)
            {
                var nodeRoot = Path.GetDirectoryName(t.TkbDestinationPath)!;
                var dest = Path.Combine(nodeRoot, Fdp.Toolkit.Terrain.TerrainCatalog.StagingDirectoryName, "overlays", "abc123");
                foreach (var rel in new[] { "overlay.json", "overlay.world.geojson", Path.Combine("buildings", "h.building.json") })
                    Assert.Equal(File.ReadAllText(Path.Combine(ov, rel)), File.ReadAllText(Path.Combine(dest, rel)));
                Assert.Equal("abc123", Fdp.Toolkit.Terrain.ScenarioTerrainName.ReadOverlayHash(nodeRoot));
                Assert.Equal("test-town", Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(nodeRoot));
                Assert.Equal(dest, new Fdp.Toolkit.Terrain.TerrainCatalog(new[] { Path.Combine(nodeRoot, "Terrain") }).ResolveOverlay("abc123"));
            }

            // a scenario without an overlay, staged next on the same nodes: no field (a stale one must not survive); the earlier overlay folder is only content-addressed leftovers
            var plain = new List<NodeDistributionTarget> { TargetIn(root, 1, "plain") };
            await gateway.PrefetchScenarioAsync("plain", plain, nas);
            Assert.Null(Fdp.Toolkit.Terrain.ScenarioTerrainName.ReadOverlayHash(OrchestrationConstants.GetNodeStagingRoot(root, 1)));
            Assert.Equal("test-town", Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(OrchestrationConstants.GetNodeStagingRoot(root, 1)));
            var nas2 = MakeNas("bare", null, "test-town", out _);
            var root2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                await gateway.PrefetchScenarioAsync("bare", new List<NodeDistributionTarget> { TargetIn(root2, 1, "bare") }, nas2);
                Assert.False(Directory.Exists(Path.Combine(OrchestrationConstants.GetNodeStagingRoot(root2, 1), "Terrain", "overlays")));
            }
            finally { Cleanup(nas2, root2); }
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3075</c> — a scenario naming NO artifacts, staged after one that named some, must not leave the
    /// previous header behind: the node's terrain step falls back to it when the load message is silent.
    /// 📐 Measured live <c>2026-10-05</c>: <c>hill-attack-close</c> after <c>ua-posture</c> kept test-town on CGF.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_NamingNothing_AfterOneNamingATerrain_RemovesThePreviousHeader_CE3075()
    {
        var nas  = MakeNas("named", null, "test-town", out _);
        Directory.CreateDirectory(Path.Combine(nas, OrchestrationConstants.ScenariosDirectoryName, "bare"));
        File.WriteAllText(Path.Combine(nas, OrchestrationConstants.ScenariosDirectoryName, "bare", "Hrot.SimHost.json"),
            "{\"header\":{},\"entities\":[]}");
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            var gateway = new StorageGatewayModule();
            await gateway.PrefetchScenarioAsync("named", new List<NodeDistributionTarget> { TargetIn(root, 1, "named") }, nas);
            var nodeRoot = OrchestrationConstants.GetNodeStagingRoot(root, 1);
            Assert.Equal("test-town", Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(nodeRoot));

            var result = await gateway.PrefetchScenarioAsync("bare", new List<NodeDistributionTarget> { TargetIn(root, 1, "bare") }, nas);

            Assert.Equal(0, result.FailureCount);
            Assert.Null(Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(nodeRoot));   // ⛔ was: "test-town", inherited
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>S2d</c> — copying twice with no NAS change performs ZERO file writes the second time.
    /// ⚠ Asserted on the destination's own <c>LastWriteTimeUtc</c> being untouched, which is exactly the
    /// property the NODE's cache then keys on (§6). A success-count assertion would not prove it.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_Twice_WithNoNasChange_DoesNotRewriteTheArtifact()
    {
        const string scenarioId = "s4";
        var nas  = MakeNas(scenarioId, null, null, out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        PublishTkb(nas, "Alpha_v1", "zip-bytes");

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId) };
            var gateway = new StorageGatewayModule();

            await gateway.PrefetchScenarioAsync(scenarioId, targets, nas, tkbName: "Alpha_v1");
            var dest  = Path.Combine(targets[0].TkbDestinationPath, "Alpha_v1.zip");
            var first = File.GetLastWriteTimeUtc(dest);

            // ⭐ Make a rewrite DETECTABLE: stamp the destination with a distinct time. A copy would
            //   overwrite it with the source's time; a skip leaves it exactly as set.
            var sentinel = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(dest, sentinel);

            await gateway.PrefetchScenarioAsync(scenarioId, targets, nas, tkbName: "Alpha_v1");

            // ⚠ The stamp CHANGED the mtime, so IsAlreadyCurrent now differs and a copy is CORRECT.
            //   That is the point: the skip keys on the bytes' identity, not on "we did it once".
            Assert.NotEqual(sentinel, File.GetLastWriteTimeUtc(dest));
            Assert.Equal(first, File.GetLastWriteTimeUtc(dest));
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐⭐⭐ The measured property the whole scheme rests on: <c>File.Copy</c> PRESERVES the source's
    /// last-write time, which is what makes a timestamp a CLUSTER-WIDE identity rather than a local
    /// artefact. ⛔ If this ever stopped being true, both skips would silently mis-fire. 📄 design §6.
    /// </summary>
    [Fact]
    public async Task TheStagedArtifactInheritsTheNasArtifactsTimestamp()
    {
        const string scenarioId = "s5";
        var nas  = MakeNas(scenarioId, null, null, out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var src  = PublishTkb(nas, "Alpha_v1", "zip-bytes");
        var srcStamp = new DateTime(1999, 12, 31, 23, 59, 58, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(src, srcStamp);

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId) };
            await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas, tkbName: "Alpha_v1");

            var dest = Path.Combine(targets[0].TkbDestinationPath, "Alpha_v1.zip");
            Assert.Equal(srcStamp, File.GetLastWriteTimeUtc(dest));

            // ⇒ and therefore both sides agree, with nothing exchanged.
            Assert.True(StorageGatewayModule.IsAlreadyCurrent(src, dest));
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>⭐ <c>S2d</c>'s three cases stated directly on the predicate.</summary>
    [Fact]
    public void IsAlreadyCurrent_MatchesOnLengthAndMtime_AndNeverOnAMissingDestination()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var a = Path.Combine(dir, "a"); File.WriteAllText(a, "same-length");
            var b = Path.Combine(dir, "b"); File.WriteAllText(b, "same-length");
            var c = Path.Combine(dir, "c"); File.WriteAllText(c, "different length entirely");

            File.SetLastWriteTimeUtc(b, File.GetLastWriteTimeUtc(a));
            Assert.True(StorageGatewayModule.IsAlreadyCurrent(a, b));       // length + mtime match

            File.SetLastWriteTimeUtc(c, File.GetLastWriteTimeUtc(a));
            Assert.False(StorageGatewayModule.IsAlreadyCurrent(a, c));      // mtime matches, LENGTH differs

            File.SetLastWriteTimeUtc(b, File.GetLastWriteTimeUtc(a).AddSeconds(1));
            Assert.False(StorageGatewayModule.IsAlreadyCurrent(a, b));      // length matches, MTIME differs

            Assert.False(StorageGatewayModule.IsAlreadyCurrent(a, Path.Combine(dir, "nope")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// ⭐⭐ <c>S2a</c> — disagreeing slices still fail loud, now on the TERRAIN name (the only name a scenario
    /// carries). ⛔ <c>R-4018</c>: was the TKB name, which is no longer read.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_WithDisagreeingTerrainNames_StillFailsLoud()
    {
        const string scenarioId = "s6";
        var nas = MakeNas(scenarioId, null, "test-town", out var scenarioDir);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(Path.Combine(scenarioDir, "Hrot.CGF.json"),
            "{\"header\":{\"terrainName\":\"basic-desert\"},\"entities\":[]}");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new StorageGatewayModule().PrefetchScenarioAsync(
                    scenarioId, new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId) }, nas));
            Assert.Contains("consensus", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(nas, root); }
    }

    // ── Asset management increment A (CE-3019) — docs/DESIGN_Asset_Management.md §2, §3, §8 ──────────────────

    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Put(string root, string rel, string text)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    /// <summary>⭐ A2 — the walker reaches every depth and keeps the subfolder structure (a 1-level walker fails this).</summary>
    [Fact]
    public void AssetManifest_ScansATreeFourLevelsDeep_KeepingTheStructure_A2()
    {
        var root = NewDir();
        try
        {
            Put(root, "top.json", "t");
            Put(root, "a/one.json", "1");
            Put(root, "a/b/two.json", "22");
            Put(root, "a/b/c/three.json", "333");
            var m = Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(root);
            Assert.Equal(new[] { "a/b/c/three.json", "a/b/two.json", "a/one.json", "top.json" },
                m.Entries.Select(e => e.RelativePath).ToArray());
            Assert.True(m.TryGet("a/b/c/three.json", out var deep));
            Assert.Equal(3, deep.Length);
            Assert.Equal(0, Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(Path.Combine(root, "missing")).Count);
        }
        finally { Cleanup(root); }
    }

    /// <summary>⭐ A1 — the diff returns ADDED, CHANGED and REMOVED; the removed set is what makes "mirror" testable.</summary>
    [Fact]
    public void AssetManifest_Diff_ReturnsAddedChangedAndRemoved_A1()
    {
        var nas = NewDir(); var node = NewDir();
        try
        {
            Put(nas, "same.json", "s");     Put(node, "same.json", "s");
            File.SetLastWriteTimeUtc(Path.Combine(node, "same.json"), File.GetLastWriteTimeUtc(Path.Combine(nas, "same.json")));
            Put(nas, "sub/changed.json", "new-bytes"); Put(node, "sub/changed.json", "old");
            Put(nas, "sub/added.json", "a");
            Put(node, "gone/removed.json", "r");

            var diff = Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(nas)
                .Diff(Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(node));

            Assert.Equal(new[] { "sub/added.json" }, diff.Added.Select(e => e.RelativePath));
            Assert.Equal(new[] { "sub/changed.json" }, diff.Changed.Select(e => e.RelativePath));
            Assert.Equal(new[] { "gone/removed.json" }, diff.Removed.Select(e => e.RelativePath));
            Assert.False(diff.IsEmpty);
        }
        finally { Cleanup(nas, node); }
    }

    /// <summary>
    /// ⭐⭐ A3 — the manifest's freshness IS the built skip: a <c>File.Copy</c> compares equal under BOTH, a modified
    /// file unequal under BOTH. ⚠ The archive arm is NOT railed here: ZIP's 2-second DOS timestamps make it unequal
    /// until increment B restores each member's mtime (<c>B4</c>, <c>CE-3020</c>) — homed there, not forgotten.
    /// </summary>
    [Fact]
    public void AssetManifest_Freshness_AgreesWithTheBuiltSkip_A3()
    {
        var nas = NewDir(); var node = NewDir();
        try
        {
            Put(nas, "deep/x/asset.json", "payload");
            var src = Path.Combine(nas, "deep", "x", "asset.json");
            var dst = Path.Combine(node, "deep", "x", "asset.json");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst);

            var diff = Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(nas)
                .Diff(Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(node));
            Assert.True(diff.IsEmpty);
            Assert.True(StorageGatewayModule.IsAlreadyCurrent(src, dst));

            File.WriteAllText(dst, "payloaD");                          // same length, new mtime
            File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src).AddSeconds(5));
            diff = Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(nas)
                .Diff(Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(node));
            Assert.Equal(new[] { "deep/x/asset.json" }, diff.Changed.Select(e => e.RelativePath));
            Assert.False(StorageGatewayModule.IsAlreadyCurrent(src, dst));
        }
        finally { Cleanup(nas, node); }
    }

    // ── Asset management increment B, the tree engine (CE-3020) — docs/DESIGN_Asset_Management.md §2, §4, §7.2, §7.3b ──

    /// <summary>⭐ B3 — one huge file + 3 000 small ones ⇒ exactly two transfers; the big file is never archived.</summary>
    [Fact]
    public void Partitioner_OneHugeFileAndThreeThousandSmall_IsTwoTransfers_B3()
    {
        var entries = new List<Fdp.Toolkit.Orchestration.Assets.AssetManifestEntry>
            { new("terrain/height.raw", 100L * 1024 * 1024 * 1024, DateTime.UtcNow) };
        for (int i = 0; i < 3000; i++) entries.Add(new($"bt/n{i}.json", 400, DateTime.UtcNow));

        var (standalone, archived) = new Fdp.Toolkit.Orchestration.Assets.TransportPartitioner().Partition(entries);
        Assert.Equal(new[] { "terrain/height.raw" }, standalone.Select(e => e.RelativePath));
        Assert.Equal(3000, archived.Count);
        Assert.True(new Fdp.Toolkit.Orchestration.Assets.TransportPartitioner().IsStandalone(new("x/tex.PNG", 10, DateTime.UtcNow)));
    }

    /// <summary>
    /// ⭐⭐⭐ B4 + A3's archive arm — after a sync the node tree MIRRORS the NAS tree (structure, bytes, mtimes, a removed
    /// file deleted), and a SECOND sync writes nothing. ⛔ Without the mtime restore the archived files would compare
    /// unequal (ZIP's 2-second timestamps) and the second sync would rewrite them all.
    /// </summary>
    [Fact]
    public void TreeSync_Mirrors_AndTheSecondSyncWritesNothing_B4()
    {
        var nas = NewDir(); var node = NewDir();
        try
        {
            Put(nas, "a.json", "top");
            Put(nas, "x/y/z/deep.json", "deep");
            Put(nas, "x/tex.png", "pretend-compressed");                 // standalone by extension
            File.SetLastWriteTimeUtc(Path.Combine(nas, "x", "y", "z", "deep.json"),
                new DateTime(2026, 9, 19, 11, 22, 33, 456, DateTimeKind.Utc));   // an odd second + millis: ZIP would lose both
            Put(node, "stale/old.json", "gone on NAS");

            var sync = new Fdp.Toolkit.Orchestration.Assets.AssetTreeSync();
            var first = sync.Sync(nas, node);
            Assert.Equal(1, first.CopiedStandalone);
            Assert.Equal(2, first.Unpacked);
            Assert.Equal(1, first.Deleted);                               // W4 answered: mirror deletes

            Assert.True(Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(nas)
                .Diff(Fdp.Toolkit.Orchestration.Assets.AssetManifest.Scan(node)).IsEmpty);
            Assert.Equal("deep", File.ReadAllText(Path.Combine(node, "x", "y", "z", "deep.json")));

            var second = sync.Sync(nas, node);
            Assert.Equal(0, second.Writes);
        }
        finally { Cleanup(nas, node); }
    }

    /// <summary>
    /// ⭐⭐ B2 clause ③ at tree level (design §7.3b) — ADD-ONLY never destroys authored work: a file the author has and
    /// NAS lacks SURVIVES (rail ① LOSS), a NEW NAS file arrives, and a CHANGED one does NOT overwrite the local copy (rail ③).
    /// </summary>
    [Fact]
    public void TreeSync_AddOnly_KeepsAuthoredWork_AddsNewFiles_AndNeverOverwrites_B2()
    {
        var nas = NewDir(); var author = NewDir();
        try
        {
            Put(author, "mine/unpublished.json", "local work");
            Put(author, "shared/tree.json", "author's edit");
            Put(nas, "shared/tree.json", "someone else's version");
            Put(nas, "theirs/new.json", "a new asset");

            var r = new Fdp.Toolkit.Orchestration.Assets.AssetTreeSync()
                .Sync(nas, author, Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.AddOnly);

            Assert.Equal("local work", File.ReadAllText(Path.Combine(author, "mine", "unpublished.json")));
            Assert.Equal("author's edit", File.ReadAllText(Path.Combine(author, "shared", "tree.json")));
            Assert.Equal("a new asset", File.ReadAllText(Path.Combine(author, "theirs", "new.json")));
            Assert.Equal(0, r.Deleted);
            Assert.Equal(1, r.HeldBackChanged);
            Assert.Equal(1, r.HeldBackRemoved);
        }
        finally { Cleanup(nas, author); }
    }

    // ── Asset management B4a / B6 (CE-3020) — the sync INSIDE the prefetch saga ─────────────────────────────

    /// <summary>⭐ The saga reads each node's advertised tokens: a rooted kind it NEEDS is mirrored, one it AUTHORS is
    /// add-only, a kind with no root is not sent, and the load-part kinds never appear here.</summary>
    [Fact]
    public void AssetSyncsFor_MapsTheAdvertisedTokens_B4a()
    {
        var syncs = AssetPrefetchProcessManager.AssetSyncsFor(new[]
        {
            "fdp.role.brain", "hrot.asset.needs.tkb", "hrot.asset.needs.scenario",
            "hrot.asset.root.blueprint=/n/Blueprints", "hrot.asset.needs.blueprint",
            "hrot.asset.root.btree=/n/BTrees", "hrot.asset.authors.btree",
            "hrot.asset.needs.hsm",                                     // no root ⇒ nothing to write to
        }).OrderBy(a => a.Kind).ToList();

        Assert.Equal(2, syncs.Count);
        Assert.Equal(new AssetSyncTarget("blueprint", "/n/Blueprints", Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.Mirror), syncs[0]);
        Assert.Equal(new AssetSyncTarget("btree", "/n/BTrees", Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.AddOnly), syncs[1]);
        Assert.Empty(AssetPrefetchProcessManager.AssetSyncsFor(null));
    }

    /// <summary>
    /// ⭐⭐ B4a — a prefetch delivers <c>{nas}/assets/&lt;kind&gt;</c> into each node's own root: a runtime brain gets the MIRROR,
    /// an authoring brain gets only the files it lacks and keeps its edit (§7.3b ③). An unpublished kind is not a failure.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_SyncsBehaviourAssets_MirrorForRuntime_AddOnlyForAuthors_B4a()
    {
        const string scenarioId = "s-assets";
        var nas  = MakeNas(scenarioId, null, null, out _);
        var root = NewDir();
        Put(Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(nas, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder), "ai/patrol.bp.json", "nas patrol");
        Put(Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(nas, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder), "ai/sub/guard.bp.json", "nas guard");
        var runtime = Path.Combine(root, "runtime", "Blueprints");
        var author  = Path.Combine(root, "author", "Blueprints");
        Put(author, "ai/patrol.bp.json", "author's unpublished edit");

        try
        {
            var targets = new List<NodeDistributionTarget>
            {
                TargetIn(root, 1, scenarioId) with { AssetSyncs = new[] {
                    new AssetSyncTarget("blueprint", runtime, Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.Mirror),
                    new AssetSyncTarget("hsm", Path.Combine(root, "runtime", "HSMs"), Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.Mirror) } },
                TargetIn(root, 2, scenarioId) with { AssetSyncs = new[] {
                    new AssetSyncTarget("blueprint", author, Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.AddOnly) } },
            };
            var result = await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas);

            Assert.Equal(0, result.FailureCount);                                              // unpublished hsm is legal
            Assert.Equal("nas patrol", File.ReadAllText(Path.Combine(runtime, "ai", "patrol.bp.json")));
            Assert.Equal("nas guard", File.ReadAllText(Path.Combine(runtime, "ai", "sub", "guard.bp.json")));
            Assert.Equal("author's unpublished edit", File.ReadAllText(Path.Combine(author, "ai", "patrol.bp.json")));
            Assert.Equal("nas guard", File.ReadAllText(Path.Combine(author, "ai", "sub", "guard.bp.json")));
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐ B6 — a sync that cannot write counts as a gateway FAILURE, which is exactly what the saga already turns into
    /// "drop the parked transition, fan out nothing" (<c>ClusterMasterPrefetchTests.A_failed_distribution_fails_the_request_and_fans_out_nothing</c>).
    /// ⛔ No second failure route.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_AnAssetSyncThatCannotWrite_IsAGatewayFailure_B6()
    {
        const string scenarioId = "s-assets-fail";
        var nas  = MakeNas(scenarioId, null, null, out _);
        var root = NewDir();
        Put(Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(nas, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder), "a.bp.json", "x");
        var blocked = Path.Combine(root, "not-a-dir");
        File.WriteAllText(blocked, "a FILE where the root should be");
        try
        {
            var targets = new List<NodeDistributionTarget>
            {
                TargetIn(root, 1, scenarioId) with { AssetSyncs = new[] {
                    new AssetSyncTarget("blueprint", blocked, Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.Mirror) } },
            };
            var result = await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas);
            Assert.Equal(1, result.FailureCount);
        }
        finally { Cleanup(nas, root); }
    }

    // ── Asset management increment C (CE-3021) — docs/DESIGN_Asset_Management.md §5, §7.3c, §10 D6 ─────────────

    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static void PutAt(string root, string rel, string text, DateTime mtime)
    {
        Put(root, rel, text);
        File.SetLastWriteTimeUtc(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)), mtime);
    }

    private static AssetSyncService ServiceFor(string nas, int nodeId, string kind, string root)
        => new(new StorageGatewayModule(), nas, () => new Dictionary<int, string[]>
        {
            [nodeId] = new[] { $"hrot.asset.root.{kind}={root}", $"hrot.asset.authors.{kind}" },
        });

    /// <summary>
    /// ⭐⭐ C1 + §10 D6 — a publish sends the author's NEW and NEWER files only: a NAS file newer than the author's copy is
    /// not rolled back, another author's NAS file is not deleted, and a second publish transfers nothing.
    /// </summary>
    [Fact]
    public async Task Publish_SendsNewAndNewerOnly_NeverRollsBackOrDeletes_C1()
    {
        var nas = NewDir(); var author = NewDir();
        var nasBp = Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(nas, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder);
        try
        {
            PutAt(author, "mine/new.bp.json", "brand new", T0);
            PutAt(author, "shared/edited.bp.json", "author newer", T0.AddHours(2));
            PutAt(nasBp,  "shared/edited.bp.json", "nas older", T0.AddHours(1));
            PutAt(author, "shared/stale.bp.json", "author older", T0);
            PutAt(nasBp,  "shared/stale.bp.json", "nas newer", T0.AddHours(3));
            PutAt(nasBp,  "theirs/other.bp.json", "another author's", T0);

            var svc = ServiceFor(nas, 7, "blueprint", author);
            var r = await svc.PublishToNasAsync("blueprint", 7);
            Assert.Equal(0, r.FailureCount);

            Assert.Equal("brand new", File.ReadAllText(Path.Combine(nasBp, "mine", "new.bp.json")));
            Assert.Equal("author newer", File.ReadAllText(Path.Combine(nasBp, "shared", "edited.bp.json")));
            Assert.Equal("nas newer", File.ReadAllText(Path.Combine(nasBp, "shared", "stale.bp.json")));       // not rolled back
            Assert.Equal("another author's", File.ReadAllText(Path.Combine(nasBp, "theirs", "other.bp.json"))); // not deleted
            Assert.Equal(T0.AddHours(2), File.GetLastWriteTimeUtc(Path.Combine(nasBp, "shared", "edited.bp.json")));

            Assert.Empty(svc.PreviewRefresh("blueprint", 7).ToReplace.Where(e => e.RelativePath != "shared/stale.bp.json"));
            Assert.Throws<InvalidOperationException>(() => ServiceFor(nas, 7, "btree", author).PreviewRefresh("blueprint", 7));
        }
        finally { Cleanup(nas, author); }
    }

    /// <summary>
    /// ⭐⭐ C5, railed as the PAIR the plan asks for — a NAS-newer file does NOT reach an author on a load (add-only, B2) and
    /// DOES on an explicit refresh, which names it first; the author's own newer edit survives both.
    /// </summary>
    [Fact]
    public async Task Refresh_IsTheOnlyRouteAnUpdateReachesAnAuthor_AndNamesWhatItReplaces_C5()
    {
        var nas = NewDir(); var author = NewDir();
        var nasBp = Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(nas, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder);
        try
        {
            PutAt(nasBp,  "a.bp.json", "nas newer", T0.AddHours(3));
            PutAt(author, "a.bp.json", "author older", T0);
            PutAt(nasBp,  "b.bp.json", "nas older", T0);
            PutAt(author, "b.bp.json", "author newer edit", T0.AddHours(5));

            // On a load: add-only — nothing changes.
            new Fdp.Toolkit.Orchestration.Assets.AssetTreeSync().Sync(nasBp, author, Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.AddOnly);
            Assert.Equal("author older", File.ReadAllText(Path.Combine(author, "a.bp.json")));

            var svc = ServiceFor(nas, 3, "blueprint", author);
            var plan = svc.PreviewRefresh("blueprint", 3);
            Assert.Equal(new[] { "a.bp.json" }, plan.ToReplace.Select(e => e.RelativePath));
            Assert.Equal(new[] { "b.bp.json" }, plan.DestinationNewer.Select(e => e.RelativePath));

            await svc.RefreshFromNasAsync("blueprint", 3);
            Assert.Equal("nas newer", File.ReadAllText(Path.Combine(author, "a.bp.json")));
            Assert.Equal("author newer edit", File.ReadAllText(Path.Combine(author, "b.bp.json")));
        }
        finally { Cleanup(nas, author); }
    }

    /// <summary>
    /// ⭐⭐ C2/C3 — the four arms: AHEAD warns, BEHIND warns (and the probe moved nothing), IN SYNC is silent, and an OFFLINE
    /// author — absent from the active roster — produces NO warning: the silence is a stated limit, not proof (design §5).
    /// </summary>
    [Fact]
    public void Probe_AheadAndBehindWarn_InSyncAndOfflineAreSilent_C3()
    {
        var nas = NewDir(); var ahead = NewDir(); var behind = NewDir(); var same = NewDir();
        var nasBp = Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(nas, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder);
        try
        {
            PutAt(nasBp, "x.bp.json", "n", T0.AddHours(1));
            PutAt(ahead, "x.bp.json", "a", T0.AddHours(2));
            PutAt(behind, "x.bp.json", "b", T0);
            PutAt(same, "x.bp.json", "n", T0.AddHours(1));

            string[] Author(string root) => new[] { $"hrot.asset.root.blueprint={root}", "hrot.asset.authors.blueprint" };
            var findings = AssetPrefetchProcessManager.ProbeOnLoad(nas, new Dictionary<int, string[]>
            {
                [1] = Author(ahead), [2] = Author(behind), [3] = Author(same),
                [4] = new[] { "hrot.asset.root.blueprint=/x", "hrot.asset.needs.blueprint" },   // receives, does not author
            });

            Assert.Equal(new[] { (1, AssetStaleness.Ahead), (2, AssetStaleness.Behind) },
                findings.OrderBy(f => f.NodeId).Select(f => (f.NodeId.Value, f.State)));
            Assert.Equal("b", File.ReadAllText(Path.Combine(behind, "x.bp.json")));                // ⛔ a probe never transfers
            Assert.Empty(AssetPrefetchProcessManager.ProbeOnLoad(nas, new Dictionary<int, string[]>()));   // the offline author
        }
        finally { Cleanup(nas, ahead, behind, same); }
    }

    /// <summary>⭐ C4 — publish is a cluster op, reachable any time: rejected loudly without a service, Success with one.</summary>
    [Fact(Timeout = 10_000)]
    public async Task PublishAssets_IsAClusterOp_RejectedWithoutAService_SucceedsWithOne_C4()
    {
        var nas = NewDir(); var author = NewDir();
        try
        {
            PutAt(author, "p.bp.json", "x", T0);
            var bus = new Fdp.Core.FdpEventBus();
            using var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
            Guid Send()
            {
                var id = Guid.NewGuid();
                master.HandleClusterOpRequest(new Hrot.NED.Descriptors.Orchestration.ClusterOpRequest
                {
                    RequestId = id,
                    OperationType = Hrot.NED.Descriptors.Orchestration.ClusterOpType.PublishAssets,
                    PayloadJson = "{\"Kind\":\"blueprint\",\"NodeId\":7}",
                });
                return id;
            }

            var rejected = Send();
            master.Tick();                 // injected requests drain on the master's tick
            bus.SwapBuffers();
            Assert.Contains(bus.ReadManaged<Fdp.Toolkit.Orchestration.ClusterOpCompletedEvent>(),
                e => e.RequestId == rejected && e.StatusCode == Fdp.Toolkit.Orchestration.OrchestrationStatusCode.Rejected);

            master.AssetSync = ServiceFor(nas, 7, "blueprint", author);
            var ok = Send();
            master.Tick();
            var deadline = DateTime.UtcNow.AddSeconds(8);
            bool done = false;
            while (!done && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
                bus.SwapBuffers();
                done = bus.ReadManaged<Fdp.Toolkit.Orchestration.ClusterOpCompletedEvent>()
                    .Any(e => e.RequestId == ok && e.StatusCode == Fdp.Toolkit.Orchestration.OrchestrationStatusCode.Success);
            }
            Assert.True(done, "PublishAssets never reported Success");
            Assert.True(File.Exists(Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(nas, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder), "p.bp.json")));
        }
        finally { Cleanup(nas, author); }
    }

    /// <summary>⭐ CE-3021 — the REMOTE path: an <c>AssetOpIntent</c> on the master's bus is drained by ClusterMaster like an
    /// injected request (rejected loudly here, since this master composes no service).</summary>
    [Fact]
    public void AssetOpIntent_OnTheBus_IsDrainedByTheMaster_C4()
    {
        var bus = new Fdp.Core.FdpEventBus();
        using var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
        var id = Guid.NewGuid();
        bus.PublishManaged(new Fdp.Toolkit.Orchestration.AssetOpIntent { RequestId = id, Kind = "blueprint", NodeId = (Fdp.Core.NodeId)(1) });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        Assert.Contains(bus.ReadManaged<Fdp.Toolkit.Orchestration.ClusterOpCompletedEvent>(),
            e => e.RequestId == id && e.StatusCode == Fdp.Toolkit.Orchestration.OrchestrationStatusCode.Rejected);
    }

    private static void Cleanup(params string[] dirs)
    {
        foreach (var d in dirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { }
    }

    /// <summary>⭐ Macros S3a ⑦ (SR-80 / SR-148) — the whole <c>{nas}/macros</c> tree reaches EVERY node's root (beside its TKB directory, where
    /// <c>FileMacroLibrary.ForNode</c> reads); two nodes sharing one root copy it once; no macros tree on the NAS stages nothing.</summary>
    [Fact]
    public void StageMacros_MirrorsTheWholeTree_ToEveryNodeRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "macros-stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nas = Path.Combine(root, "nas");
            var file = Path.Combine(ContentLayout.Macros(nas), "forces", "fire-team", ContentLayout.ScenarioFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "{\"header\": {\"Kind\": \"macro\"}, \"entities\": {}}");

            NodeDistributionTarget Node(int id, string nodeDir) => new()
            {
                NodeId = (Fdp.Core.NodeId)id,
                DestinationPath = Path.Combine(root, nodeDir, "scenarios", "s"),
                TkbDestinationPath = Path.Combine(root, nodeDir, OrchestrationConstants.TkbDirectoryName),
            };
            var targets = new List<NodeDistributionTarget> { Node(1, "node-1"), Node(2, "node-2"), Node(3, "node-2") };

            int ok = 0, failed = 0;
            StorageGatewayModule.StageMacros(targets, nas, ref ok, ref failed);

            Assert.Equal((2, 0), (ok, failed));   // node-2's root once
            foreach (var n in new[] { "node-1", "node-2" })
            {
                var lib = Fdp.Toolkit.Scenario.FileMacroLibrary.ForNode(Path.Combine(root, n));
                Assert.True(lib.TryRead("forces/fire-team", out var json, out var why), why);
                Assert.Contains("macro", json);
            }

            int ok2 = 0, failed2 = 0;
            StorageGatewayModule.StageMacros(targets, Path.Combine(root, "empty-nas"), ref ok2, ref failed2);
            Assert.Equal((0, 0), (ok2, failed2));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
