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
    /// ⭐ CE-4210 MA-4 — a macro's slices, merged: the node wrote its slice beside the macro (<c>macros/x/.slices</c>); the merge writes
    /// <c>macros/x/scenario.json</c> with the macro header kept, and cleans THAT <c>.slices</c> up. Nothing appears under <c>scenarios/</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4210_AMacrosSlices_AreMerged_BesideTheMacro_WithItsHeader_AndCleanedUp()
    {
        string nas = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            string slicesDir = Path.Combine(nas, "macros", "x", ".slices");
            Directory.CreateDirectory(slicesDir);
            var dom = new System.Text.Json.Nodes.JsonObject
            {
                ["Header"] = new System.Text.Json.Nodes.JsonObject { ["Kind"] = "macro", ["Anchor"] = new System.Text.Json.Nodes.JsonObject { ["Position"] = new System.Text.Json.Nodes.JsonArray(1, 2, 0), ["Heading"] = 45.0 } },
                ["Entities"] = new System.Text.Json.Nodes.JsonObject(),
            };
            Fdp.Core.Serialization.Migrations.JsonEnvelope.Write(dom, new Fdp.Core.Serialization.Migrations.DocumentMeta("Hrot.Scenario", 2));
            File.WriteAllText(Path.Combine(slicesDir, "node_1.json"), dom.ToJsonString());

            var manager = new StorageProcessManager(new FdpEventBus(), new StorageGatewayModule(), nas);
            manager.MergeScenarioSlices("macros/x", new List<FileManifestEntry>
            {
                new() { RelativeDest = Path.Combine("macros", "x", ".slices", "node_1.json"), DocType = "Hrot.Scenario" },
            });

            var merged = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(nas, "macros", "x", "scenario.json")))!.AsObject();
            var header = Fdp.Toolkit.Scenario.ScenarioHeaderJson.Read(merged)!;
            Assert.Equal("macro", header.Kind);
            Assert.Equal(45f, header.Anchor!.Value.Heading.Degrees);
            Assert.False(Directory.Exists(slicesDir), "the staging slices beside the macro are cleaned up");
            Assert.False(Directory.Exists(Path.Combine(nas, "scenarios")), "nothing under scenarios/");
        }
        finally { if (Directory.Exists(nas)) Directory.Delete(nas, recursive: true); }
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
