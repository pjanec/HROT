using System;
using System.Threading;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Configuration;
using Bagira.Runner.Services;

namespace Bagira.Runner.Integration.Tests;

/// <summary>
/// Domain-isolated orchestration harness for Runner integration tests.
///
/// <para>
/// The ID-allocator server is owned by <see cref="SimHostSubsystem"/> and runs on its
/// own background thread (started inside <see cref="SimHostSubsystem.Initialize"/>).
/// The harness no longer needs a separate server — removing the duplicate avoids the
/// ID-range collision that occurred when two servers both started their counter at 1.
/// </para>
/// </summary>
public sealed class BagiraRunnerHarness : IDisposable
{
    private const int DomainIdBase = 100;
    private const int WarmupFrames = 200;   // 1 s of simulation ticks to give CycloneDDS time to match
    private const int PumpSleepMs = 5;
    /// <summary>
    /// Extra wall-clock sleep AFTER warmup frames, allowing DDS SPDP/SEDP discovery to complete
    /// for any topic whose reader/writer pair was not yet matched by the last warmup frame.
    /// 1 s is sufficient for loopback CycloneDDS discovery on all topics used by the harness.
    /// </summary>
    private const int PostWarmupSettleMs = 1000;

    private static int _domainCounter = DomainIdBase - 1;

    public int DomainId { get; }
    public SubsystemOrchestrator Orchestrator { get; }
    public SimHostSubsystem SimHost { get; }
    public IgSubsystem Ig { get; }
    public IosSubsystem Ios { get; }

    public BagiraRunnerHarness()
    {
        DomainId = Interlocked.Increment(ref _domainCounter);

        var config = new RunnerConfiguration
        {
            Headless = true,
            DomainId = DomainId,
            ModeString = "all"
        };
        config.Validate();

        SimHost = new SimHostSubsystem();
        Ig = new IgSubsystem();
        Ios = new IosSubsystem();

        Orchestrator = new SubsystemOrchestrator(config, new ISubsystem[]
        {
            SimHost,
            Ig,
            Ios
        });

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
