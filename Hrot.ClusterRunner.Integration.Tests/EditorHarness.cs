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

        // Standalone time controller — no network sync.
        var timeCtrl = TimeControllerFactory.Create(
            Bus,
            new TimeControllerConfig { Role = TimeRole.Standalone });
        Kernel.SetTimeController(timeCtrl);

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
            Kernel.Update(PumpSleepMs / 1000f);
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
            Kernel.Update(PumpSleepMs / 1000f);
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
