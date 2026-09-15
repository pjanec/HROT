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
/// Verifies that the manager only initiates NAS pulls when valid payloads are received.
/// CE-278: the SC1 orchestrator-entry-prepend test (the SaveScenario=2 pull path via
/// GlobalContextManifestReadyEvent) is retired with the op.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class StorageProcessManagerTests
{
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
