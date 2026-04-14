using System;
using System.Threading;
using Fdp.Engine.Runner;
using Fdp.Kernel;
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

    private static int _domainCounter = DomainIdBase - 1;

    public int DomainId { get; }
    public SubsystemOrchestrator Orchestrator { get; }
    public OrchestratorSubsystem OrchestratorSvc { get; }
    public SimHostSubsystem SimHost { get; }
    public IgSubsystem Ig { get; }
    public ExConSubsystem ExCon { get; }
    public CgfSubsystem? Cgf { get; private set; }

    public HrotRunnerHarness()
    {
        DomainId = Interlocked.Increment(ref _domainCounter);

        var factory = new NedNetworkFactory(
            participant:  null,
            entityMap:    new FDP.Toolkit.Replication.Services.NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.MuscleGround | NodeRole.Perception);

        OrchestratorSvc = new OrchestratorSubsystem();
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

        var factory = new NedNetworkFactory(
            participant:  null,
            entityMap:    new FDP.Toolkit.Replication.Services.NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.MuscleGround | NodeRole.Perception);

        OrchestratorSvc = new OrchestratorSubsystem();
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
            Cgf = new CgfSubsystem(factory);  // factory (participant=null) triggers participant creation inside CgfSubsystem
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
    }
}

