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
/// Unit tests for <see cref="DiagnosticsDumpProcessManager"/>.
/// </summary>
public sealed class DiagnosticsDumpProcessManagerTests
{
    // ── SC1: Success — files pulled to NAS, success event published ─────────

    [Fact(Timeout = 10_000)]
    public async Task Success_PullsToNasAndPublishesSuccessEvent()
    {
        string? srcDir = null;
        string? nasDir = null;

        try
        {
            srcDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            nasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(nasDir);

            var diagFile = Path.Combine(srcDir, "events.json");
            await File.WriteAllTextAsync(diagFile, "{}");

            var bus        = new FdpEventBus();
            var gateway    = new StorageGatewayModule();
            var aggregator = new DiagnosticsConsensusAggregator();
            var manager    = new DiagnosticsDumpProcessManager(bus, gateway, nasDir, aggregator);

            var requestId = Guid.NewGuid();

            // Register the pending request via intent.
            bus.PublishManaged(new ExecuteDiagnosticDumpIntent
            {
                RequestId   = requestId,
                PayloadJson = "{}",
            });
            bus.SwapBuffers();
            manager.Tick(); // Reads intent → adds to pending set.
            bus.SwapBuffers();

            // Seed the aggregator with a full manifest.
            var fullEntry = new FileManifestEntry { SourceUnc = diagFile, RelativeDest = "events.json" };
            var responses = new Dictionary<int, Dictionary<NodeOpType, string>>
            {
                [1] = new() { [NodeOpType.CollectDiagnostics] =
                    System.Text.Json.JsonSerializer.Serialize(new[] { fullEntry }) },
            };
            aggregator.Aggregate(responses);

            // Publish success event with stripped manifest.
            var strippedEntry = new FileManifestEntry { RelativeDest = "events.json" };
            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId     = requestId,
                StatusCode    = OrchestrationStatusCode.Success,
                ResultPayload = new List<FileManifestEntry> { strippedEntry },
            });
            bus.SwapBuffers();
            manager.Tick();

            // Wait for async NAS pull.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline &&
                   !File.Exists(Path.Combine(nasDir, "events.json")))
            {
                await Task.Delay(100);
            }

            Assert.True(File.Exists(Path.Combine(nasDir, "events.json")),
                "Diagnostic file should be pulled to NAS on success");
        }
        finally
        {
            if (srcDir != null && Directory.Exists(srcDir)) Directory.Delete(srcDir, recursive: true);
            if (nasDir != null && Directory.Exists(nasDir)) Directory.Delete(nasDir, recursive: true);
        }
    }

    // ── SC2: NAS pull failure → failure event published ──────────────────────

    [Fact(Timeout = 10_000)]
    public async Task PullFailure_PublishesFailureEvent()
    {
        var bus        = new FdpEventBus();
        var gateway    = new StorageGatewayModule();
        var aggregator = new DiagnosticsConsensusAggregator();

        // Use a real temp NAS dir so the pull path is valid but the source file doesn't exist.
        var nasDir = Path.Combine(Path.GetTempPath(), "DdPm_SC2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(nasDir);

        try
        {
            var manager = new DiagnosticsDumpProcessManager(bus, gateway, nasDir, aggregator);

            var requestId = Guid.NewGuid();

            bus.PublishManaged(new ExecuteDiagnosticDumpIntent { RequestId = requestId, PayloadJson = "{}" });
            bus.SwapBuffers();
            manager.Tick();
            bus.SwapBuffers();

            // Seed aggregator with a non-existent LOCAL source file (fails fast, no UNC timeout).
            var missingFile = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N") + ".json");
            var fakeEntry = new FileManifestEntry
            {
                SourceUnc    = missingFile,
                RelativeDest = "events.json",
            };
            var responses = new Dictionary<int, Dictionary<NodeOpType, string>>
            {
                [1] = new() { [NodeOpType.CollectDiagnostics] =
                    System.Text.Json.JsonSerializer.Serialize(new[] { fakeEntry }) },
            };
            aggregator.Aggregate(responses);

            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId     = requestId,
                StatusCode    = OrchestrationStatusCode.Success,
                ResultPayload = new List<FileManifestEntry> { new FileManifestEntry { RelativeDest = "events.json" } },
            });
            bus.SwapBuffers();
            manager.Tick();

            // Poll for the failure event (NAS pull fails because source file doesn't exist).
            ClusterOpCompletedEvent? failureEvent = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
                bus.SwapBuffers();
                foreach (var ev in bus.ReadManaged<ClusterOpCompletedEvent>())
                {
                    if (ev.RequestId == requestId && ev.StatusCode == OrchestrationStatusCode.Failure)
                    {
                        failureEvent = ev;
                        break;
                    }
                }
                if (failureEvent != null) break;
            }

            Assert.NotNull(failureEvent);
        }
        finally
        {
            if (Directory.Exists(nasDir)) Directory.Delete(nasDir, recursive: true);
        }
    }

    // ── SC3: Abort/rejection — PullToNasAsync NOT called, failure event published

    [Fact]
    public void Abort_PublishesFailureEvent_WithoutNasPull()
    {
        var bus        = new FdpEventBus();
        var gateway    = new StorageGatewayModule();
        var aggregator = new DiagnosticsConsensusAggregator();
        var manager    = new DiagnosticsDumpProcessManager(bus, gateway, @"C:\Fake\Nas", aggregator);

        var requestId = Guid.NewGuid();

        // Register the intent.
        bus.PublishManaged(new ExecuteDiagnosticDumpIntent { RequestId = requestId, PayloadJson = "{}" });
        bus.SwapBuffers();
        manager.Tick();
        bus.SwapBuffers();

        // Publish rejection (abort) event.
        bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId  = requestId,
            StatusCode = OrchestrationStatusCode.Rejected,
        });
        bus.SwapBuffers();
        manager.Tick();

        // Aggregator full manifest should NOT have been taken (pull not attempted).
        // If the aggregator is empty, TakeFullManifest returns null — which is fine.
        // But no NAS file should exist and the manager should publish Failure on the bus.
        bus.SwapBuffers();
        ClusterOpCompletedEvent? failureEvent = null;
        foreach (var ev in bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.RequestId == requestId && ev.StatusCode == OrchestrationStatusCode.Failure)
            {
                failureEvent = ev;
                break;
            }
        }

        Assert.NotNull(failureEvent);
    }

    // ── SC4: Unknown request IDs are ignored ─────────────────────────────────

    [Fact]
    public void UnknownRequestId_IsIgnored()
    {
        var bus        = new FdpEventBus();
        var gateway    = new StorageGatewayModule();
        var aggregator = new DiagnosticsConsensusAggregator();
        var manager    = new DiagnosticsDumpProcessManager(bus, gateway, @"C:\Fake\Nas", aggregator);

        // Publish a success event for an ID that was never registered.
        bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId     = Guid.NewGuid(),
            StatusCode    = OrchestrationStatusCode.Success,
            ResultPayload = new List<FileManifestEntry>(),
        });
        bus.SwapBuffers();

        // Should not throw, and should not publish any additional events.
        manager.Tick();
        bus.SwapBuffers();

        int eventCount = 0;
        foreach (var _ in bus.ReadManaged<ClusterOpCompletedEvent>())
            eventCount++;

        Assert.Equal(0, eventCount);
    }
}
