using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Network.Cyclone.Egress;
using Fdp.Network.Cyclone.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Fdp.Network.Cyclone.Tests;

/// <summary>
/// ⭐ CE-3427 N-1 — the state egress OFF the main thread (docs/DESIGN_Egress_Change_Detection.md §7): a marked translator's scan runs on a
/// replica synced at the end of <c>Export</c>, while the next frame runs, and is joined before any translator is touched again. One rail
/// per hazard of §7's table; the real <see cref="SystemScheduler"/>, egress and cleanup systems — only the translators are test doubles.
/// </summary>
public sealed class TheOffFrameEgressTests
{
    [ComponentId(461)]   // a test-local id (GlobalComponentIds: 461 is left free in production for test structs)
    public struct OffFramePos { public int V; }

    /// <summary>A shared, thread-safe log of what went "on the wire", in order.</summary>
    private sealed class Wire
    {
        private readonly List<string> _lines = new();
        public void Add(string s) { lock (_lines) _lines.Add(s); }
        public List<string> Lines { get { lock (_lines) return _lines.ToList(); } }
    }

    /// <summary>A STATE translator: one sample per owned entity per scan, "state id@tick=value".</summary>
    private sealed class StateTranslator : IOffFrameEgress
    {
        private readonly Wire _wire;
        public int ScanDelayMs;
        public int OffFrameScans, ScansOnAnotherThread;
        public int MainThread = Environment.CurrentManagedThreadId;
        public StateTranslator(Wire wire) => _wire = wire;
        public string TopicName => "state";
        public long DescriptorOrdinal => 901;
        public TranslatorDirection Direction => TranslatorDirection.Egress;
        public long ReceivedSampleCount => 0;
        public long SentSampleCount { get; private set; }
        public Func<bool>? IsReplayRestoring { get; set; }
        public int DiscardIngress() => 0;
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) => _wire.Add($"dispose {networkEntityId}");
        public void ScanAndPublish(ISimulationView view) { BeginFrame(view); ScanState(view, offFrame: false); }
        public void BeginFrame(ISimulationView live) { }
        public void ScanState(ISimulationView view, bool offFrame)
        {
            if (ScanDelayMs > 0) Thread.Sleep(ScanDelayMs);
            if (offFrame) { OffFrameScans++; if (Environment.CurrentManagedThreadId != MainThread) ScansOnAnotherThread++; }
            foreach (var e in view.Query().With<OffFramePos>().With<NetworkIdentity>().Build())
            {
                _wire.Add($"state {view.GetComponentRO<NetworkIdentity>(e).Value}@{view.Tick}={view.GetComponentRO<OffFramePos>(e).V}");
                SentSampleCount++;
            }
        }
    }

    /// <summary>An EVENT translator: stays on the frame — "event@tick".</summary>
    private sealed class EventTranslator : INetworkTranslator
    {
        private readonly Wire _wire;
        public EventTranslator(Wire wire) => _wire = wire;
        public string TopicName => "event";
        public TranslatorDirection Direction => TranslatorDirection.Egress;
        public long ReceivedSampleCount => 0;
        public long SentSampleCount => 0;
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public Action? DuringExport;   // runs in the middle of Export, before the state translator's slot
        public void ScanAndPublish(ISimulationView view) { _wire.Add($"event@{view.Tick}"); DuringExport?.Invoke(); }
    }

    private sealed class Rig : IDisposable
    {
        public readonly EntityRepository World = new();
        public readonly Wire Wire = new();
        public readonly StateTranslator State;
        public EventTranslator Events = null!;
        public readonly SystemScheduler Scheduler = new();
        public readonly EgressPipelineLaunchSystem Launch;
        public readonly List<Entity> Units = new();
        private const int LocalNode = 1;

        public readonly List<StateTranslator> Extra = new();

        public Rig(int units, bool cleanupFirst = false, int workers = 1, int extraStates = 0)
        {
            Launch = new EgressPipelineLaunchSystem(workers);
            State = new StateTranslator(Wire);
            for (int i = 0; i < extraStates; i++) Extra.Add(new StateTranslator(new Wire()));
            World.RegisterComponent<OffFramePos>();
            World.RegisterComponent<NetworkIdentity>();
            World.RegisterComponent<NetworkAuthority>();
            World.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>();
            for (int i = 0; i < units; i++)
            {
                var e = World.CreateEntity();
                World.AddComponent(e, new OffFramePos { V = 0 });
                World.AddComponent(e, new NetworkIdentity(100 + i));
                World.AddComponent(e, new NetworkAuthority(LocalNode, LocalNode));
                Units.Add(e);
            }
            // ⭐ the launch system is registered FIRST: [UpdateLastInPhase] must still put it after every egress and cleanup system
            Scheduler.RegisterSystem(Launch);
            Events = new EventTranslator(Wire);
            // ⭐ TWO egress systems, as production has six: the events' and the state's — a switch can land between them
            var egressEvents = new CycloneEgressSystem(new INetworkTranslator[] { Events });
            var states = new List<INetworkTranslator> { State };
            states.AddRange(Extra);
            var egressState = new CycloneEgressSystem(states.ToArray());
            var cleanup = new CycloneNetworkCleanupSystem(new IDescriptorTranslator[] { State });
            if (cleanupFirst) Scheduler.RegisterSystem(cleanup);
            Scheduler.RegisterSystem(egressEvents);
            Scheduler.RegisterSystem(egressState);
            if (!cleanupFirst) Scheduler.RegisterSystem(cleanup);
            Scheduler.BuildExecutionOrders();
        }

        public EgressPipeline Pipeline => Launch.Pipeline!;

        /// <summary>One frame: advance, move every unit (value = tick × 10 + its index), then the Export phase.</summary>
        public void Frame(Action? beforeExport = null)
        {
            World.Tick();
            World.Bus.SwapBuffers();   // as the kernel: last frame's events go, this frame's become readable
            for (int i = 0; i < Units.Count; i++)
                if (World.IsAlive(Units[i])) World.GetComponentRW<OffFramePos>(Units[i]).V = (int)World.SimulationTick * 10 + i;
            beforeExport?.Invoke();
            Scheduler.ExecutePhase(SystemPhase.Export, World, 0.016f);
        }

        /// <summary>As production retracts an entity: TearDown and a <c>DestructionOrder</c> this frame.</summary>
        public void Destroy(Entity e)
        {
            World.SetLifecycleState(e, EntityLifecycle.TearDown);
            World.Bus.Publish(new Fdp.Toolkit.Lifecycle.Events.DestructionOrder { Entity = e });
            World.Bus.SwapBuffers();
        }

        public void Dispose() { Launch.Pipeline?.Dispose(); World.Dispose(); }
    }

    private static List<string> States(IEnumerable<string> lines) => lines.Where(l => l.StartsWith("state ")).ToList();

    [Fact]
    public void Off_the_frame_every_state_sample_is_the_on_frame_sample_and_events_are_not_delayed()
    {
        List<string> onFrame, offFrame;
        using (var on = new Rig(units: 3))
        {
            for (int f = 0; f < 6; f++) on.Frame();
            onFrame = on.Wire.Lines;
            Assert.Equal(0, on.State.OffFrameScans);
        }
        using (var off = new Rig(units: 3))
        {
            off.Frame();                                              // the launch system creates the world's pipeline
            off.Pipeline.RequestedMode = EgressMode.OffFrame;          // …and the switch takes effect at the next join
            for (int f = 1; f < 6; f++) off.Frame();
            off.Pipeline.Join();                                      // the last scan
            offFrame = off.Wire.Lines;
            Assert.Equal(5, off.State.OffFrameScans);
            Assert.Equal(5, off.State.ScansOnAnotherThread);          // ⭐ the scan ran OFF the main thread
        }

        // ⭐⭐ the same samples, tick by tick and value by value: a replica of frame k scanned after frame k's Export is frame k's state
        Assert.Equal(States(onFrame), States(offFrame));

        // ⭐ order: frame k's event goes out in frame k, BEFORE frame k's state (one frame late), and frame k's state before frame k+1's event
        for (int tick = 3; tick <= 6; tick++)
        {
            int ev = offFrame.IndexOf($"event@{tick}");
            int st = offFrame.FindIndex(l => l.StartsWith("state ") && l.Contains($"@{tick}="));
            int next = offFrame.IndexOf($"event@{tick + 1}");
            Assert.True(ev >= 0 && st > ev, $"frame {tick}: the event precedes the state");
            if (next >= 0) Assert.True(st < next, $"frame {tick}: its state precedes frame {tick + 1}'s event");
        }
    }

    [Fact]
    public void The_launch_runs_last_in_Export_though_it_was_registered_first()
    {
        using var rig = new Rig(units: 1);
        var export = rig.Scheduler.GetAllSystems().ToList();
        Assert.IsType<EgressPipelineLaunchSystem>(export[^1]);
    }

    [Fact]
    public void A_destroyed_entity_s_last_state_goes_out_before_its_delete_and_nothing_after()
    {
        // ⭐ the cleanup runs BEFORE the egress system in Export and the scan is slow: only the cleanup's own join keeps the order
        using var rig = new Rig(units: 2, cleanupFirst: true);
        rig.State.ScanDelayMs = 30;
        rig.Frame();
        rig.Pipeline.RequestedMode = EgressMode.OffFrame;
        for (int f = 0; f < 3; f++) rig.Frame();
        rig.Frame(() => rig.Destroy(rig.Units[0]));      // frame 5: unit 100 is destroyed
        for (int f = 0; f < 3; f++) rig.Frame();
        rig.Pipeline.Join();

        var lines = rig.Wire.Lines;
        int dispose = lines.IndexOf("dispose 100");
        Assert.True(dispose > 0, "the cleanup deleted it");
        int lastState = lines.FindLastIndex(l => l.StartsWith("state 100@"));
        Assert.True(lastState >= 0 && lastState < dispose, "its last state went out before the delete");
        Assert.Equal("state 100@5=50", lines[lastState]);             // tick 5: the last frame it lived through
        Assert.Contains("state 101@8=81", lines);                     // the other unit keeps publishing
    }

    [Fact]
    public void A_held_egress_launches_no_scan_and_a_released_one_does()
    {
        using var rig = new Rig(units: 1);
        rig.Frame();
        rig.Pipeline.RequestedMode = EgressMode.OffFrame;
        rig.Frame();
        EgressHold.Hold(rig.World, "replay seek");
        rig.Frame(); rig.Frame();
        rig.Pipeline.Join();
        int held = rig.State.OffFrameScans;
        Assert.Equal(1, held);                                       // only the frame before the hold
        EgressHold.Release(rig.World);
        rig.Frame();
        rig.Pipeline.Join();
        Assert.Equal(2, rig.State.OffFrameScans);
    }

    [Fact]
    public void Off_by_default_nothing_changes_the_scans_run_in_their_slot_on_the_main_thread()
    {
        if (Environment.GetEnvironmentVariable("FDP_EGRESS_OFFFRAME") == "1") return;   // the default is the environment's
        using var rig = new Rig(units: 2);
        for (int f = 0; f < 4; f++) rig.Frame();
        Assert.Equal(EgressMode.OnFrame, rig.Pipeline.Mode);
        Assert.Equal(0, rig.State.OffFrameScans);
        Assert.Equal(0, rig.Pipeline.Scans);
        // ⭐ every frame's state right after its own event, exactly as with no pipeline at all
        var lines = rig.Wire.Lines;
        for (int tick = 2; tick <= 5; tick++)
            Assert.Equal($"state 100@{tick}={tick * 10}", lines[lines.IndexOf($"event@{tick}") + 1]);
    }

    [Fact]
    public void Switching_every_few_frames_loses_and_repeats_nothing()
    {
        using var rig = new Rig(units: 2);
        rig.Frame();
        for (int f = 0; f < 40; f++)
        {
            if (f % 4 == 0) rig.Pipeline.RequestedMode = rig.Pipeline.RequestedMode == EgressMode.OnFrame ? EgressMode.OffFrame : EgressMode.OnFrame;
            rig.Frame();
        }
        rig.Pipeline.Join();
        var states = States(rig.Wire.Lines);
        // ⭐ every frame's state exactly once per unit, whichever mode scanned it
        for (int tick = 2; tick <= 41; tick++)
            for (int u = 0; u < 2; u++)
                Assert.Single(states, $"state {100 + u}@{tick}={tick * 10 + u}");
        Assert.True(rig.State.OffFrameScans >= 15);
    }

    /// <summary>⭐ The switch (an HTTP request, any thread) changes nothing until the next join: a request that lands in the MIDDLE of
    /// Export — after a state translator's system decided, before the launch — must not make the launch scan what the system already
    /// scanned, nor skip what it skipped. ✅ Red-proof (run): the egress systems reading the REQUESTED mode instead of the joined one ⇒ a
    /// frame scanned twice or not at all ⇒ red.</summary>
    [Fact]
    public void A_switch_requested_in_the_middle_of_Export_waits_for_the_next_join()
    {
        using var rig = new Rig(units: 1);
        rig.Frame();
        rig.Events.DuringExport = () => rig.Pipeline.RequestedMode =
            rig.Pipeline.RequestedMode == EgressMode.OnFrame ? EgressMode.OffFrame : EgressMode.OnFrame;
        for (int f = 0; f < 12; f++) rig.Frame();
        rig.Pipeline.Join();
        var states = States(rig.Wire.Lines);
        for (int tick = 2; tick <= 14; tick++)
            Assert.Single(states, $"state 100@{tick}={tick * 10}");
        Assert.True(rig.State.OffFrameScans >= 5);
    }

    /// <summary>⭐ Stress: 2 000 frames, the switch flipping at random moments (between frames and mid-Export), six state translators fanned over
    /// two pool workers — no fault, no deadlock, and every frame's state exactly once.</summary>
    [Fact]
    public void Stress_two_thousand_frames_of_random_switching_over_the_worker_pool_lose_and_repeat_nothing()
    {
        using var rig = new Rig(units: 4, workers: 2, extraStates: 5);
        var random = new Random(3427);
        rig.Frame();
        rig.Events.DuringExport = () => { if (random.Next(7) == 0) rig.Pipeline.RequestedMode = random.Next(2) == 0 ? EgressMode.OnFrame : EgressMode.OffFrame; };
        for (int f = 0; f < 2000; f++)
        {
            if (random.Next(5) == 0) rig.Pipeline.RequestedMode = random.Next(2) == 0 ? EgressMode.OnFrame : EgressMode.OffFrame;
            rig.Frame();
        }
        rig.Pipeline.Join();
        Assert.Equal(0, rig.Pipeline.Faults);
        var counts = new Dictionary<string, int>();
        foreach (var l in States(rig.Wire.Lines)) counts[l] = counts.TryGetValue(l, out int n) ? n + 1 : 1;
        for (int tick = 2; tick <= 2001; tick++)
            for (int u = 0; u < 4; u++)
                Assert.Equal(1, counts.TryGetValue($"state {100 + u}@{tick}={tick * 10 + u}", out int c) ? c : 0);
        Assert.True(rig.State.OffFrameScans > 300, $"off-frame scans: {rig.State.OffFrameScans}");
        foreach (var x in rig.Extra) Assert.Equal(rig.State.OffFrameScans, x.OffFrameScans);   // every translator of every off-frame scan
    }

    /// <summary>⭐ A host that goes away without disposing its pipeline does not leak its world: the scan thread and the shared worker pool
    /// hold no strong reference to the pipeline (which holds the live world and a replica). 📐 Before: the integration suite ran out of
    /// memory with FDP_EGRESS_OFFFRAME=1. ✅ Red-proof (run): the thread's loop capturing the pipeline ⇒ the world stays alive ⇒ red.</summary>
    [Fact]
    public void A_world_left_without_disposing_its_pipeline_is_collected()
    {
        var weak = RunAWorldOffTheFrameAndDropIt();
        for (int i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Thread.Sleep(20);
        }
        Assert.False(weak.IsAlive, "the world is still reachable after its host dropped it");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RunAWorldOffTheFrameAndDropIt()
    {
        var rig = new Rig(units: 2, workers: 2, extraStates: 2);
        rig.Frame();
        rig.Pipeline.RequestedMode = EgressMode.OffFrame;
        for (int f = 0; f < 10; f++) rig.Frame();
        rig.Pipeline.Join();
        Assert.True(rig.State.OffFrameScans > 0);
        return new WeakReference(rig.World);   // ⛔ the rig (and its pipeline) is NOT disposed — as a host that just went away
    }

    [Fact]
    public void A_scan_that_throws_puts_the_pipeline_back_on_the_frame()
    {
        using var rig = new Rig(units: 1);
        rig.Frame();
        rig.Pipeline.RequestedMode = EgressMode.OffFrame;
        rig.Frame();
        var thrower = new ThrowingOnce();
        rig.Pipeline.Register(thrower, null);
        rig.Frame();                   // launch: the thrower throws on the scan thread
        rig.Frame();                   // join: back on the frame
        Assert.Equal(1, rig.Pipeline.Faults);
        Assert.Equal(EgressMode.OnFrame, rig.Pipeline.Mode);
    }

    private sealed class ThrowingOnce : IOffFrameEgress
    {
        public string TopicName => "thrower";
        public long DescriptorOrdinal => 902;
        public TranslatorDirection Direction => TranslatorDirection.Egress;
        public long ReceivedSampleCount => 0;
        public long SentSampleCount => 0;
        public Func<bool>? IsReplayRestoring { get; set; }
        public int DiscardIngress() => 0;
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void BeginFrame(ISimulationView live) { }
        public void ScanState(ISimulationView view, bool offFrame) => throw new InvalidOperationException("boom");
    }
}
