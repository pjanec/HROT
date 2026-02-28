using System;
using System.Threading;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Configuration;
using Bagira.Runner.Services;
using CycloneDDS.Runtime;
using ModuleHost.Network.Cyclone.Services;

namespace Bagira.Runner.Integration.Tests;

/// <summary>
/// Domain-isolated orchestration harness for Runner integration tests.
/// </summary>
public sealed class BagiraRunnerHarness : IDisposable
{
    private const int DomainIdBase = 100;
    private const int WarmupFrames = 60;
    private const int PumpSleepMs = 5;

    private static int _domainCounter = DomainIdBase - 1;

    public int DomainId { get; }
    public SubsystemOrchestrator Orchestrator { get; }
    public SimHostSubsystem SimHost { get; }
    public IgSubsystem Ig { get; }
    public IosSubsystem Ios { get; }

    private readonly DdsParticipant _idAllocatorParticipant;
    private readonly DdsIdAllocatorServer _idAllocatorServer;
    private readonly CancellationTokenSource _idServerCts = new();
    private readonly Thread _idServerThread;

    public BagiraRunnerHarness()
    {
        DomainId = Interlocked.Increment(ref _domainCounter);

        _idAllocatorParticipant = new DdsParticipant((uint)DomainId);
        _idAllocatorServer = new DdsIdAllocatorServer(_idAllocatorParticipant);
        _idServerThread = new Thread(RunIdAllocatorServer)
        {
            IsBackground = true,
            Name = "DDS-IdAllocatorServer"
        };
        _idServerThread.Start();

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
            _idAllocatorServer.ProcessRequests();
            Orchestrator.RunFrames(1);
            _idAllocatorServer.ProcessRequests();
        }
    }

    public bool PumpUntil(Func<bool> condition, int timeoutFrames = 300)
    {
        if (condition()) return true;

        for (int i = 0; i < timeoutFrames; i++)
        {
            _idAllocatorServer.ProcessRequests();
            Orchestrator.RunFrames(1);
            _idAllocatorServer.ProcessRequests();
            Thread.Sleep(PumpSleepMs);

            if (condition()) return true;
        }

        return false;
    }

    public void Dispose()
    {
        Orchestrator.Shutdown();
        _idServerCts.Cancel();
        _idServerThread.Join(TimeSpan.FromSeconds(2));
        _idAllocatorServer.Dispose();
        _idAllocatorParticipant.Dispose();
    }

    private void RunIdAllocatorServer()
    {
        while (!_idServerCts.IsCancellationRequested)
        {
            _idAllocatorServer.ProcessRequests();
            Thread.Sleep(1);
        }
    }

    private void Warmup()
    {
        for (int i = 0; i < WarmupFrames; i++)
        {
            _idAllocatorServer.ProcessRequests();
            Orchestrator.RunFrames(1);
            _idAllocatorServer.ProcessRequests();
            Thread.Sleep(PumpSleepMs);
        }
    }
}
