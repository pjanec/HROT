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
}
