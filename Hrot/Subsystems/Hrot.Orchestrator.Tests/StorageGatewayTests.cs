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
                NodeId          = i + 1,
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
                new() { NodeId = 1, DestinationPath = Path.Combine(goodDir, "scenario.json") },
                new() { NodeId = 2, DestinationPath = Path.Combine(goodDir, "sub", "scenario.json") },
                // ⭐ Invalid on EVERY OS: `blocker` is a FILE, so creating a directory under it fails.
                new() { NodeId = 3, DestinationPath = Path.Combine(blockerFile, "sub", "scenario.json") },
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
                NodeId          = i + 1,
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
}

/// <summary>
/// TKB-018 -- Tests for <see cref="StorageGatewayModule"/> TkbName consensus check.
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
                new NodeDistributionTarget { NodeId = 1, DestinationPath = destDir },
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

    // ── Test 2 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrefetchScenario_ConflictingTkbNames_ThrowsInvalidOperationException()
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
                new NodeDistributionTarget { NodeId = 1, DestinationPath = destDir },
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => gateway.PrefetchScenarioAsync(scenarioId, targets, nasDir));
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
                new NodeDistributionTarget { NodeId = 1, DestinationPath = destDir },
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
                new NodeDistributionTarget { NodeId = 1, DestinationPath = destDir },
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
                new NodeDistributionTarget { NodeId = 1, DestinationPath = destDir },
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
                new NodeDistributionTarget { NodeId = 100, DestinationPath = destEcs },
                new NodeDistributionTarget { NodeId = 200, DestinationPath = destForeign },
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
        NodeId             = nodeId,
        DestinationPath    = Path.Combine(
            OrchestrationConstants.GetNodeStagingRoot(root, nodeId),
            OrchestrationConstants.ScenariosDirectoryName, scenarioId),
        TkbDestinationPath = OrchestrationConstants.GetNodeTkbStagingRoot(root, nodeId),
    };

    /// <summary>
    /// ⭐⭐⭐ <c>S2b</c> — a scenario whose header names a TKB leaves that zip on EVERY target node.
    /// 🔴 This is the literal content of <c>BP-550</c>: before this batch, nothing wrote it anywhere.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_StagesTheNamedTkbZip_OnEveryNode()
    {
        const string scenarioId = "s1";
        var nas  = MakeNas(scenarioId, "Alpha_v1", null, out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        PublishTkb(nas, "Alpha_v1", "zip-bytes");

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId), TargetIn(root, 2, scenarioId) };
            await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas);

            foreach (var t in targets)
                Assert.True(File.Exists(Path.Combine(t.TkbDestinationPath, "Alpha_v1.zip")),
                    $"node {t.NodeId} did not receive the named TKB artifact");
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>S2c</c> — the header the node reads its names out of now EXISTS, and in the shape the node
    /// actually parses: a FLAT object with PascalCase keys.
    ///
    /// <para>⚠ Asserting the SHAPE, not just existence, is what makes this non-vacuous: the scenario's own
    /// header block is nested under <c>header</c> and camelCase, and copying it verbatim would produce a
    /// file <c>TkbLoadClusterStateHandler</c> and <c>ScenarioTerrainName</c> both fail to read.</para>
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
            await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas);

            var headerPath = Path.Combine(targets[0].TkbDestinationPath, StorageGatewayModule.StagedScenarioHeaderFileName);
            Assert.True(File.Exists(headerPath), "the staged ScenarioHeader.json was not written");

            using var doc = JsonDocument.Parse(File.ReadAllText(headerPath));
            Assert.Equal("Alpha_v1",     doc.RootElement.GetProperty("TkbName").GetString());
            Assert.Equal("basic-desert", doc.RootElement.GetProperty("TerrainName").GetString());
        }
        finally { Cleanup(nas, root); }
    }

    /// <summary>
    /// ⭐⭐ A scenario naming NO TKB stages nothing and is NOT a failure — the <c>NedTkbCatalog</c>
    /// fallback is a legal path. ⛔ One of the dispatch's three named traps.
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
    /// ⭐⭐⭐ <c>S2d</c> — copying twice with no NAS change performs ZERO file writes the second time.
    /// ⚠ Asserted on the destination's own <c>LastWriteTimeUtc</c> being untouched, which is exactly the
    /// property the NODE's cache then keys on (§6). A success-count assertion would not prove it.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_Twice_WithNoNasChange_DoesNotRewriteTheArtifact()
    {
        const string scenarioId = "s4";
        var nas  = MakeNas(scenarioId, "Alpha_v1", null, out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        PublishTkb(nas, "Alpha_v1", "zip-bytes");

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId) };
            var gateway = new StorageGatewayModule();

            await gateway.PrefetchScenarioAsync(scenarioId, targets, nas);
            var dest  = Path.Combine(targets[0].TkbDestinationPath, "Alpha_v1.zip");
            var first = File.GetLastWriteTimeUtc(dest);

            // ⭐ Make a rewrite DETECTABLE: stamp the destination with a distinct time. A copy would
            //   overwrite it with the source's time; a skip leaves it exactly as set.
            var sentinel = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(dest, sentinel);

            await gateway.PrefetchScenarioAsync(scenarioId, targets, nas);

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
        var nas  = MakeNas(scenarioId, "Alpha_v1", null, out _);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var src  = PublishTkb(nas, "Alpha_v1", "zip-bytes");
        var srcStamp = new DateTime(1999, 12, 31, 23, 59, 58, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(src, srcStamp);

        try
        {
            var targets = new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId) };
            await new StorageGatewayModule().PrefetchScenarioAsync(scenarioId, targets, nas);

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
    /// ⭐⭐ <c>S2a</c>'s existing behaviour is UNCHANGED — disagreeing slices still fail loud. ⛔ The task
    /// was "return the value it already computes", so this rail must stay green untouched.
    /// </summary>
    [Fact]
    public async Task PrefetchScenario_WithDisagreeingTkbNames_StillFailsLoud()
    {
        const string scenarioId = "s6";
        var nas = MakeNas(scenarioId, "Alpha_v1", null, out var scenarioDir);
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(Path.Combine(scenarioDir, "Hrot.CGF.json"),
            "{\"header\":{\"tkbName\":\"Beta_v2\"},\"entities\":[]}");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new StorageGatewayModule().PrefetchScenarioAsync(
                    scenarioId, new List<NodeDistributionTarget> { TargetIn(root, 1, scenarioId) }, nas));
            Assert.Contains("consensus", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(nas, root); }
    }

    private static void Cleanup(params string[] dirs)
    {
        foreach (var d in dirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { }
    }
}
