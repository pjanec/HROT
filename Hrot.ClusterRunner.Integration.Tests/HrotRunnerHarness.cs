using System;
using System.Threading;
using Hrot.ClusterRunner.Configuration;
using Hrot.ClusterRunner.Services;

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

    public HrotRunnerHarness()
    {
        DomainId = Interlocked.Increment(ref _domainCounter);

        OrchestratorSvc = new OrchestratorSubsystem();
        SimHost = new SimHostSubsystem();
        Ig = new IgSubsystem();
        ExCon = new ExConSubsystem();

        var options = new RunnerOptions { Headless = true, DomainId = DomainId };
        Orchestrator = new SubsystemOrchestrator(new ISubsystem[]
        {
            OrchestratorSvc,   // must be first: starts DdsIdAllocatorServer before SimHost
            SimHost,
            Ig,
            ExCon
        }, options);

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

