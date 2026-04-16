using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Replication.Services;
using Fdp.Core;
using Hrot.CGF;
using Hrot.ClusterRunner.Services;
using Hrot.Common;
using Hrot.ExCon;
using Hrot.IG;
using Hrot.Map.Common;
using Hrot.Network.NED.Factory;
using Hrot.Orchestrator;
using Hrot.SimHost;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Domain-isolated orchestration harness for Runner integration tests.
///
/// <para>
/// <see cref="OrchestratorSubsystem"/> is the FIRST subsystem in the list so that
/// <see cref="ClusterMaster"/> (and its embedded <c>DdsIdAllocatorServer</c>) are running
/// before <see cref="SimHostSubsystem"/> initialises. This ensures that
/// <c>SimHostApp.EnsureIdAllocatorRouting</c> finds a publication match immediately
/// instead of waiting up to 30 s before throwing.
/// </para>
/// </summary>
public sealed class HrotRunnerHarness : IDisposable
{
    private const int DomainIdBase = 100;
    // Warmup is intentionally short: OrchestratorSubsystem (first in the list) starts
    // DdsIdAllocatorServer before SimHostSubsystem calls EnsureIdAllocatorRouting, so the
    // ID-allocator match is near-instant. CycloneDDS loopback SPDP/SEDP discovery for the
    // remaining application topics completes within ~200 ms on typical hardware.
    private const int WarmupFrames = 20;    // 20 × 5 ms = 100 ms of pumped frames
    private const int PumpSleepMs = 5;
    /// <summary>
    /// Extra wall-clock sleep AFTER warmup frames, allowing DDS SPDP/SEDP discovery to complete
    /// for any topic whose reader/writer pair was not yet matched by the last warmup frame.
    /// 200 ms is sufficient for loopback CycloneDDS discovery on all topics used by the harness.
    /// </summary>
    private const int PostWarmupSettleMs = 200;
    /// <summary>
    /// Extra frames pumped after the standard warmup when CGF is present.
    /// <c>ClusterSlave</c> fires the first <c>NodeHeartbeat</c> after 1 s of real time.
    /// <c>BrainMuscleOwnershipStrategy</c> needs at least one heartbeat from SimHost before it
    /// can delegate WorldPos authority to a MuscleGround node on entity creation.
    /// 220 frames × 5 ms sleep = 1 100 ms, which is safely longer than 1 s.
    /// </summary>
    private const int CgfHeartbeatWarmupFrames = 220;

    private static int _domainCounter = DomainIdBase - 1;

    public int DomainId { get; }
    public SubsystemOrchestrator Orchestrator { get; }
    public OrchestratorSubsystem OrchestratorSvc { get; }
    public SimHostSubsystem SimHost { get; }
    public IgSubsystem Ig { get; }
    public ExConSubsystem ExCon { get; }
    public CgfSubsystem? Cgf { get; private set; }

    // Shared DDS participant owned by the harness; disposed after Orchestrator.Shutdown().
    private readonly DdsParticipant _participant;

    public HrotRunnerHarness()
    {
        DomainId = Interlocked.Increment(ref _domainCounter);

        // Create a single shared DDS participant for the harness domain.
        // All subsystems share this participant so the composition root (this harness)
        // owns the DDS lifecycle, matching the hexagonal architecture requirement.
        _participant = HrotEnvironment.CreateParticipant(DomainId);
        var factory = new NedNetworkFactory(
            participant:  _participant,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.MuscleGround | NodeRole.Perception);

        OrchestratorSvc = new OrchestratorSubsystem(factory);  // HEXAG2-S009: factory required
        SimHost = new SimHostSubsystem(factory);
        Ig = new IgSubsystem(factory);
        ExCon = new ExConSubsystem(factory);
        Cgf = new CgfSubsystem(factory);  // CGF processes CreateEntityRequest and sends ACKs

        var options = new RunnerOptions { Headless = true, DomainId = DomainId };
        Orchestrator = new SubsystemOrchestrator(new ISubsystem[]
        {
            OrchestratorSvc,   // must be first: starts DdsIdAllocatorServer before SimHost
            SimHost,
            Ig,
            ExCon,
            Cgf,
        }, options);

        Orchestrator.Initialize();
        Warmup();
    }

    /// <summary>
    /// Creates a harness with a specific set of subsystem names and domain ID (for shared-domain tests).
    /// Typically used alongside <see cref="CgfHarness(int)"/> for IT-4 tests.
    /// <para>Subsystem names are comma-separated and case-insensitive: simhost, ig, excon, cgf.</para>
    /// </summary>
    public HrotRunnerHarness(string modes, int domainId)
    {
        DomainId = domainId;

        var requested = new System.Collections.Generic.HashSet<string>(
            modes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

        // Create a single shared DDS participant for the harness domain.
        _participant = HrotEnvironment.CreateParticipant(domainId);
        var factory = new NedNetworkFactory(
            participant:  _participant,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.MuscleGround | NodeRole.Perception);

        OrchestratorSvc = new OrchestratorSubsystem(factory);  // HEXAG2-S009: factory required
        SimHost         = new SimHostSubsystem(factory);
        Ig              = new IgSubsystem(factory);
        ExCon           = new ExConSubsystem(factory);

        // Always include Orchestrator; conditionally include other subsystems.
        var subsystems = new System.Collections.Generic.List<ISubsystem> { OrchestratorSvc };
        if (requested.Contains("simhost")) subsystems.Add(SimHost);
        if (requested.Contains("ig"))      subsystems.Add(Ig);
        if (requested.Contains("excon"))   subsystems.Add(ExCon);
        if (requested.Contains("cgf"))
        {
            Cgf = new CgfSubsystem(factory);
            subsystems.Add(Cgf);
        }

        var options = new RunnerOptions { Headless = true, DomainId = domainId };
        Orchestrator = new SubsystemOrchestrator(subsystems, options);

        Orchestrator.Initialize();
        Warmup();
    }

    public void PumpFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            Orchestrator.RunFrames(1);
        }
    }

    public bool PumpUntil(Func<bool> condition, int timeoutFrames = 300)
    {
        if (condition()) return true;

        for (int i = 0; i < timeoutFrames; i++)
        {
            Orchestrator.RunFrames(1);
            Thread.Sleep(PumpSleepMs);

            if (condition()) return true;
        }

        return false;
    }

    public void Dispose()
    {
        Orchestrator.Shutdown();
        // Dispose the shared participant after all DDS readers/writers owned by the
        // subsystems have been torn down inside Shutdown().
        _participant.Dispose();
    }

    private void Warmup()
    {
        for (int i = 0; i < WarmupFrames; i++)
        {
            Orchestrator.RunFrames(1);
            Thread.Sleep(PumpSleepMs);
        }

        // Extra settle time: give CycloneDDS SPDP/SEDP discovery time to complete for all
        // topics (EntityMaster, GeoSpatial, CreateEntityRequest/Ack, MissionControlRequest/Ack,
        // etc.) even when the process starts cold (no DDS participant has run before).
        Thread.Sleep(PostWarmupSettleMs);

        // When CGF is present, BrainMuscleOwnershipStrategy must know about SimHost before any
        // entity is created via CreateEntityRequest. SimHost's ClusterSlave fires its first
        // NodeHeartbeat after 1 s of real time. Pump CgfHeartbeatWarmupFrames extra frames
        // (>1100 ms) so the heartbeat is received and the cluster cache is populated before
        // any test action is taken.
        if (Cgf != null)
        {
            for (int i = 0; i < CgfHeartbeatWarmupFrames; i++)
            {
                Orchestrator.RunFrames(1);
                Thread.Sleep(PumpSleepMs);
            }
        }
    }
}

