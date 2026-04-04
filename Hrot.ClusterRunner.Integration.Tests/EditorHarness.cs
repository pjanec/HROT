using System;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Time.Controllers;
using Hrot.CGF;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.SimHost;
using ModuleHost.Core;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Offline (no DDS) test harness for editor integration tests.
/// Instantiates <see cref="ModuleHostKernel"/> with the three local packs:
/// <see cref="SimHostCoreLogicPack"/>, <see cref="CgfLogicPack"/>,
/// and <see cref="ScenarioEditorModule"/>.
///
/// <para>No CycloneDDS domain is allocated.</para>
/// </summary>
public sealed class EditorHarness : IDisposable
{
    private const int PumpSleepMs = 5;

    private SteppingTimeController? _stepping;

    public EntityRepository  Repo   { get; }
    public FdpEventBus        Bus    { get; }
    public ModuleHostKernel   Kernel { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public EditorHarness()
    {
        Repo   = new EntityRepository();
        Bus    = Repo.Bus;

        var accumulator = new EventAccumulator();
        Kernel = new ModuleHostKernel(Repo, accumulator);

        // Stepping time controller — offline, no DDS sync.
        var stepping = new SteppingTimeController(new GlobalTime { TimeScale = 1.0f });
        _stepping = stepping;
        Kernel.SetTimeController(stepping);

        var entityMap        = new NetworkEntityMap();
        var doctrineRegistry = new DoctrineRegistry();
        var clusterSlave     = new ClusterSlave(0, "EditorHarness");

        Kernel.RegisterModule(new SimHostCoreLogicPack(entityMap));
        Kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, entityMap));
        Kernel.RegisterModule(new ScenarioEditorModule());

        Kernel.Initialize();
    }

    // ── Pump API ──────────────────────────────────────────────────────────────

    /// <summary>Advances <paramref name="frames"/> simulation frames.</summary>
    public void PumpFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            _stepping?.Step(PumpSleepMs / 1000f);
            Kernel.Update();
        }
    }

    /// <summary>
    /// Pumps frames until <paramref name="condition"/> returns <c>true</c>
    /// or <paramref name="timeoutMs"/> milliseconds have elapsed.
    /// </summary>
    public bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        if (condition()) return true;

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            _stepping?.Step(PumpSleepMs / 1000f);
            Kernel.Update();
            if (condition()) return true;
        }

        return false;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        Kernel.Dispose();
        Repo.Dispose();
    }
}
