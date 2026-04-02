using System;
using System.Collections.Generic;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using Hrot.Orchestrator.Translators;
using NedClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpClusterState  = FDP.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator.Integration.Tests;

/// <summary>
/// End-to-end translator round-trip tests (CMC-S017).
/// Verifies that a DDS <see cref="ClusterOpRequest"/> flows through the
/// <see cref="ClusterOpMasterTranslator"/> + <see cref="ClusterMaster"/> (bus mode)
/// + <see cref="ClusterSlave"/> + <see cref="NodeOpMasterTranslator"/> pipeline
/// and produces a DDS <see cref="ClusterOpStatus"/> with
/// <see cref="OrchestrationStatusCode.Success"/>.
///
/// Uses DDS domain 19 — reserved for translator round-trip integration tests.
/// Real CycloneDDS in-process loopback is used; no fake stubs are needed because
/// <see cref="DdsReader{T}"/> / <see cref="DdsWriter{T}"/> are sealed.
/// </summary>
[Collection("CqrsIntegrationTests")]
public sealed class TranslatorRoundTripTests : IDisposable
{
    private const int TestDomain = 19;   // reserved — no conflicts with domain 17 (unit) or 18 (harness)

    private readonly DdsParticipant _participant;

    public TranslatorRoundTripTests()
    {
        _participant = new DdsParticipant(TestDomain);
    }

    public void Dispose() => _participant.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Advances one logical frame: swap bus, tick translators, tick master, tick slave.
    /// Translators must tick BEFORE ClusterMaster so ingress (DDS→bus) is processed first.
    /// </summary>
    private static void Frame(FdpEventBus bus,
        ClusterOpMasterTranslator clusterOpXlator,
        NodeOpMasterTranslator    nodeOpXlator,
        ClusterMaster master, ClusterSlave slave)
    {
        bus.SwapBuffers();
        clusterOpXlator.Tick();   // DDS ClusterOpRequest → bus TransitionStateIntent; bus ClusterOpCompletedEvent → DDS ClusterOpStatus
        nodeOpXlator.Tick();      // bus ExecuteNodeOpIntent → DDS NodeOpCommand; DDS NodeOpStatus → bus NodeOpCompletedEvent
        master.Tick();
        slave.Tick();
    }

    // ── CMC-S017 Test 7: Full DDS→bus→bus→DDS round-trip ─────────────────────

    /// <summary>
    /// A DDS <see cref="ClusterOpRequest"/> with <c>OperationType = TransitionState</c>
    /// flows through:
    /// <list type="number">
    ///   <item><see cref="ClusterOpMasterTranslator"/> (DDS→bus <see cref="TransitionStateIntent"/>)</item>
    ///   <item><see cref="ClusterMaster"/> (bus fan-out → <see cref="ExecuteNodeOpIntent"/>)</item>
    ///   <item><see cref="ClusterSlave"/> (stub handler → ACK via <see cref="NodeOpCompletedEvent"/>)</item>
    ///   <item><see cref="ClusterMaster"/> (ACK → <see cref="ClusterOpCompletedEvent"/>)</item>
    ///   <item><see cref="ClusterOpMasterTranslator"/> (bus→DDS <see cref="ClusterOpStatus"/>)</item>
    /// </list>
    /// and produces a DDS <see cref="ClusterOpStatus"/> with
    /// <see cref="OrchestrationStatusCode.Success"/>.
    /// </summary>
    [Fact(Timeout = 20_000)]
    public void ClusterOpRequest_ThroughTranslators_ProducesClusterOpStatus()
    {
        // ── DDS readers/writers ───────────────────────────────────────────────
        using var requestWriter = new DdsWriter<ClusterOpRequest>(_participant);
        using var requestReader = new DdsReader<ClusterOpRequest>(_participant);
        using var statusWriter  = new DdsWriter<ClusterOpStatus>(_participant);
        using var statusReader  = new DdsReader<ClusterOpStatus>(_participant);
        using var nodeOpWriter  = new DdsWriter<NodeOpCommand>(_participant);
        using var nodeOpReader  = new DdsReader<NodeOpStatus>(_participant);
        using var hbWriter      = new DdsWriter<NodeHeartbeat>(_participant);

        // Wait for DDS publication/subscription matching (local loopback).
        Thread.Sleep(500);

        // ── Domain components ─────────────────────────────────────────────────
        var bus = new FdpEventBus();
        var master       = new ClusterMaster(bus, NoMandatoryConfig());
        using var slave  = new ClusterSlave(1, "SimHost", bus);
        slave.RegisterHandler(new StubAllOpsHandler(1));

        var clusterOpXlator = new ClusterOpMasterTranslator(requestReader, statusWriter, bus);
        var nodeOpXlator    = new NodeOpMasterTranslator(
            _ => nodeOpWriter,   // factory: always return the same writer for nodeId 1
            nodeOpReader,
            bus);

        // ── Register node via heartbeat (DDS publish → bus bridge not used here;
        //    we publish directly to bus so the master sees the node immediately) ──
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            LocalStateId  = (int)FDP.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
            SubsystemName = "SimHost",
        });
        Frame(bus, clusterOpXlator, nodeOpXlator, master, slave);

        // ── Write ClusterOpRequest to DDS ─────────────────────────────────────
        var requestId = Guid.NewGuid();
        requestWriter.Write(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = NedClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"{(int)FdpClusterState.LoadingLive}\"}}",
        });

        // Wait for DDS local-loopback delivery.
        Thread.Sleep(300);

        // ── Tick until we receive a DDS ClusterOpStatus ───────────────────────
        ClusterOpStatus? result = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline && result is null)
        {
            Frame(bus, clusterOpXlator, nodeOpXlator, master, slave);

            using var scope = statusReader.Take();
            foreach (var sample in scope)
            {
                if (sample.IsValid)
                {
                    result = sample.Data;
                    break;
                }
            }
        }

        Assert.NotNull(result);
        Assert.Equal(OrchestrationStatusCode.Success, result!.Value.StatusCode);
    }
}
