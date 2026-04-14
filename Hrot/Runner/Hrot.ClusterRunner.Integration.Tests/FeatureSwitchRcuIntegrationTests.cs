using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Editor;
using Hrot.Map.Common;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.NED.Messages;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Network.Interfaces;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>PACK2-R005 Part B — IT-3: Feature Switch RCU integration tests.</summary>
[Collection("EditorOfflineTests")]
public sealed class FeatureSwitchRcuIntegrationTests
{
    private const int    PumpMs        = 5_000;
    private const int    SwitchMs      = 30_000;  // larger timeout for RCU async ops under parallel load
    private const long   TestTkbType   = 1L;
    private const long   TestNetworkId = 99L;

    // ── Spy types ────────────────────────────────────────────────────────────

    private sealed class RecordingDdsWriter : IDdsWriter<CreateEntityRequest>
    {
        public int CallCount { get; private set; }
        public void Write(CreateEntityRequest _) => CallCount++;
        public void DisposeInstance(CreateEntityRequest _) { }
    }

    /// <summary>
    /// Minimal IEcsModule whose Tick calls PollIngress on the translator so that
    /// SpawnEntityCommand events are consumed and forwarded to the recording writer.
    /// Uses Tick() (runs after SwapBuffers) so events are visible in the same frame.
    /// </summary>
    private sealed class SpyEgressPack : IEcsModule
    {
        private readonly SpawnEntityCommandEgressTranslator _translator;

        public string Name => "SpyEgressPack";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public SpyEgressPack(SpawnEntityCommandEgressTranslator translator)
            => _translator = translator;

        // RegisterSystems is intentionally left as the default no-op (interface default impl).

        public void Tick(ISimulationView view, float deltaTime)
            => _translator.PollIngress(view.GetCommandBuffer(), view);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SwitchExternalAndWait(EditorHarness harness)
    {
        var t = harness.Editor.SwitchToExternalAsync();
        // Use a manual loop with Thread.Sleep to yield CPU to the background drain tasks
        // scheduled by ModuleHostKernel's RCU drain mechanism. Without sleeping, the spin
        // loop starves the thread pool when the full test suite runs in parallel.
        var deadline = DateTime.UtcNow.AddMilliseconds(SwitchMs);
        while (!t.IsCompleted && DateTime.UtcNow < deadline)
        {
            harness.PumpFrames(1);
            System.Threading.Thread.Sleep(1);
        }
        if (!t.IsCompleted || t.IsFaulted)
            throw t.Exception ?? (Exception)new TimeoutException("SwitchToExternalAsync timed out");
    }

    private static void SwitchInternalAndWait(EditorHarness harness)
    {
        var t = harness.Editor.SwitchToInternalAsync();
        var deadline = DateTime.UtcNow.AddMilliseconds(SwitchMs);
        while (!t.IsCompleted && DateTime.UtcNow < deadline)
        {
            harness.PumpFrames(1);
            System.Threading.Thread.Sleep(1);
        }
        if (!t.IsCompleted || t.IsFaulted)
            throw t.Exception ?? (Exception)new TimeoutException("SwitchToInternalAsync timed out");
    }

    // ── IT-3a ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SwitchToExternal_EjectsLogicPacks_SpawnNoLongerLocal()
    {
        using var harness = new EditorHarness();

        // Pre-condition: spawn works in Internal mode
        harness.Bus.PublishManaged(new SpawnEntityCommand
            { TkbType = TestTkbType, NetworkId = TestNetworkId, OwnerNodeId = 0, InitType = ReliableInitType.None });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpMs));

        // Reset
        harness.Editor.NewScenario();
        harness.PumpFrames(2);

        // Switch to External
        SwitchExternalAndWait(harness);
        Assert.Equal(SimHostMode.External, harness.Editor.CurrentMode);

        // Spawn command should NOT create an entity (SimHostCoreLogicPack is ejected)
        harness.Bus.PublishManaged(new SpawnEntityCommand
            { TkbType = TestTkbType, NetworkId = TestNetworkId + 1, OwnerNodeId = 0, InitType = ReliableInitType.None });
        harness.PumpFrames(5);

        Assert.Equal(0, harness.Repo.EntityCount);
    }

    // ── IT-3b ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SwitchToInternal_RestoresLogicPacks_SpawnWorksAgain()
    {
        using var harness = new EditorHarness();

        SwitchExternalAndWait(harness);
        SwitchInternalAndWait(harness);

        Assert.Equal(SimHostMode.Internal, harness.Editor.CurrentMode);

        // Spawn should work again
        harness.Bus.PublishManaged(new SpawnEntityCommand
            { TkbType = TestTkbType, NetworkId = TestNetworkId, OwnerNodeId = 0, InitType = ReliableInitType.None });

        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpMs),
            "After restoring Internal mode, spawn should create an entity");
    }

    // ── IT-3c ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RapidToggle_NoRaceCondition()
    {
        using var harness = new EditorHarness();

        for (int i = 0; i < 5; i++)
        {
            SwitchExternalAndWait(harness);
            Assert.Equal(SimHostMode.External, harness.Editor.CurrentMode);

            SwitchInternalAndWait(harness);
            Assert.Equal(SimHostMode.Internal, harness.Editor.CurrentMode);
        }

        // After 5 round-trips, spawn should still work
        harness.Bus.PublishManaged(new SpawnEntityCommand
            { TkbType = TestTkbType, NetworkId = TestNetworkId, OwnerNodeId = 0, InitType = ReliableInitType.None });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpMs));
    }

    // ── IT-3d (DDS spy) ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that in External mode, SpawnEntityCommand is forwarded to the
    /// DDS egress layer (mock writer). Uses the internal SpawnEntityCommandEgressTranslator
    /// constructor (accessible via InternalsVisibleTo on Hrot.Map.Common).
    /// </summary>
    [Fact]
    public void SwitchToExternal_SpawnCommand_ReachesDdsWriter()
    {
        var spy   = new RecordingDdsWriter();
        var geoTx = HrotEnvironment.CreateGeoTransform();

        using var harness = new EditorHarness();

        // Build an offline translator spy using the internal testable constructor
        var translator = new SpawnEntityCommandEgressTranslator(spy, harness.Bus, geoTx);
        var spyPack    = new SpyEgressPack(translator);

        // Provide the spy pack so SwitchToExternalAsync installs it
        harness.SetTranslatorPacks(new List<IEcsModule> { spyPack });

        SwitchExternalAndWait(harness);

        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TestTkbType,
            NetworkId   = TestNetworkId,
            OwnerNodeId = 0,
            InitType    = ReliableInitType.None,
        });
        harness.PumpFrames(3);

        Assert.Equal(1, spy.CallCount);
    }
}
