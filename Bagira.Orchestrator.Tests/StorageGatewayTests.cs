using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Bagira.Orchestrator.Tests;

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
    /// When one destination path is invalid (e.g. a path on a non-existent drive),
    /// <c>PushToNodesAsync</c> must not throw.  It should report a partial failure
    /// with the successful copies counted and the failing one counted separately.
    /// </summary>
    [Fact]
    public async Task PushToNodes_BadTarget_ReturnsPartialFailure()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var nasFile = Path.Combine(dir, "scenario.json");
        File.WriteAllText(nasFile, "{\"data\":\"test\"}");

        var goodDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            var targets = new List<NodeDistributionTarget>
            {
                new() { NodeId = 1, DestinationPath = Path.Combine(goodDir, "scenario.json") },
                new() { NodeId = 2, DestinationPath = Path.Combine(goodDir, "sub", "scenario.json") },
                // Invalid: a "path" whose parent directory creation will fail on any OS.
                new() { NodeId = 3, DestinationPath = "\\\\255.255.255.255\\nonexistent\\scenario.json" },
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
        var scenarioDir = Path.Combine(nasDir, scenarioId);
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
}
