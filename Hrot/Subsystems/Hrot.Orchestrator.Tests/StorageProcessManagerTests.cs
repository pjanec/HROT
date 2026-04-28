using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for <see cref="StorageProcessManager"/> (DEBT-01 from BATCH-01 review).
/// Verifies that the orchestrator manifest entry prepending (via GlobalContextManifestReadyEvent)
/// works correctly and that the manager only initiates NAS pulls when valid payloads are received.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class StorageProcessManagerTests
{
    /// <summary>
    /// SC1 -- GlobalContextManifestReadyEvent entry is prepended to manifest.
    /// Both the node file AND the orchestrator entry are pulled to NAS.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task ProcessManager_OrchestratorEntry_IsPrepended_ToManifest()
    {
        string? tempSourceDir = null;
        string? tempNasDir    = null;

        try
        {
            // Setup: create temp dirs and files.
            tempSourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            tempNasDir    = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempSourceDir);
            Directory.CreateDirectory(tempNasDir);

            var nodeFile         = Path.Combine(tempSourceDir, "NodeData.bin");
            var orchestratorFile = Path.Combine(tempSourceDir, "Orchestrator.json");
            await File.WriteAllTextAsync(nodeFile, "node-content");
            await File.WriteAllTextAsync(orchestratorFile, "orchestrator-content");

            var bus     = new FdpEventBus();
            var gateway = new StorageGatewayModule();

            var nodeEntry = new FileManifestEntry
            {
                SourceUnc     = nodeFile,
                RelativeDest  = "NodeData.bin",
            };

            var orchestratorEntry = new FileManifestEntry
            {
                SourceUnc    = orchestratorFile,
                RelativeDest = "Orchestrator.json",
            };

            var manager = new StorageProcessManager(
                bus,
                gateway,
                tempNasDir);

            // Publish GlobalContextManifestReadyEvent so the orchestrator entry is queued.
            bus.PublishManaged(new GlobalContextManifestReadyEvent { Entry = orchestratorEntry });
            bus.SwapBuffers();
            manager.Tick();
            bus.SwapBuffers();

            // Publish ClusterOpCompletedEvent with node manifest entry.
            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId     = Guid.NewGuid(),
                StatusCode    = OrchestrationStatusCode.Success,
                ResultPayload = new List<FileManifestEntry> { nodeEntry },
            });
            bus.SwapBuffers();
            manager.Tick();

            // Wait for async pull to complete (up to 3 seconds).
            var maxWait = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < maxWait)
            {
                if (File.Exists(Path.Combine(tempNasDir, "NodeData.bin")) &&
                    File.Exists(Path.Combine(tempNasDir, "Orchestrator.json")))
                {
                    break;
                }
                await Task.Delay(100);
            }

            // Assert: both files exist in NAS dir.
            Assert.True(File.Exists(Path.Combine(tempNasDir, "NodeData.bin")),
                "Node file should be pulled to NAS");
            Assert.True(File.Exists(Path.Combine(tempNasDir, "Orchestrator.json")),
                "Orchestrator shim entry should be pulled to NAS");
        }
        finally
        {
            if (tempSourceDir != null && Directory.Exists(tempSourceDir))
                Directory.Delete(tempSourceDir, recursive: true);
            if (tempNasDir != null && Directory.Exists(tempNasDir))
                Directory.Delete(tempNasDir, recursive: true);
        }
    }

    /// <summary>
    /// SC2 -- Null payload: no NAS pull.
    /// When ResultPayload is null, no files should be created in NAS dir.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ProcessManager_NullPayload_NoNasPull()
    {
        string? tempNasDir = null;

        try
        {
            tempNasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempNasDir);

            var bus     = new FdpEventBus();
            var gateway = new StorageGatewayModule();
            var manager = new StorageProcessManager(bus, gateway, tempNasDir);

            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId     = Guid.NewGuid(),
                StatusCode    = OrchestrationStatusCode.Success,
                ResultPayload = null,
            });
            bus.SwapBuffers();
            manager.Tick();

            // Assert: NAS dir is empty.
            var files = Directory.GetFiles(tempNasDir);
            Assert.Empty(files);
        }
        finally
        {
            if (tempNasDir != null && Directory.Exists(tempNasDir))
                Directory.Delete(tempNasDir, recursive: true);
        }
    }

    /// <summary>
    /// SC3 -- Empty manifest: no NAS pull.
    /// When ResultPayload is an empty list, no files should be created.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ProcessManager_EmptyManifest_NoNasPull()
    {
        string? tempNasDir = null;

        try
        {
            tempNasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempNasDir);

            var bus     = new FdpEventBus();
            var gateway = new StorageGatewayModule();
            var manager = new StorageProcessManager(bus, gateway, tempNasDir);

            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId     = Guid.NewGuid(),
                StatusCode    = OrchestrationStatusCode.Success,
                ResultPayload = new List<FileManifestEntry>(),
            });
            bus.SwapBuffers();
            manager.Tick();

            // Assert: NAS dir is empty.
            var files = Directory.GetFiles(tempNasDir);
            Assert.Empty(files);
        }
        finally
        {
            if (tempNasDir != null && Directory.Exists(tempNasDir))
                Directory.Delete(tempNasDir, recursive: true);
        }
    }
}
