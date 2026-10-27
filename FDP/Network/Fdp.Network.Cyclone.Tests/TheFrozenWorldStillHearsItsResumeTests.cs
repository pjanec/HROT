using System.Threading.Tasks;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Modules;
using Xunit;

namespace Fdp.Network.Cyclone.Tests;

/// <summary>
/// ⭐⭐⭐ <b>cgf==editor slice 4 item ③ (<c>DQ30-C</c>) — while a debugger holds a node's world frozen,
/// WORLD-STATE ingress stops and CONTROL-PLANE ingress keeps polling.</b>
///
/// <para>📄 <c>docs/UX/Design_Question_30_Debug_Pause_Resume.md</c> §C *(the categorization, and why it
/// is required rather than a nicety)* · <c>docs/UX/UX_Feature_Cgf_Brain_Diagnostics.md</c> §4.</para>
///
/// <para>⛔⛔ <b>The second assertion is the one that matters most</b>, and it is not symmetry for its
/// own sake: <c>DQ30-A</c>'s deadlock is a node that freezes and then cannot hear the command that
/// un-freezes it. ⇒ a rail asserting only *"world state stopped"* would pass on a build that had
/// bricked every frozen node.</para>
/// </summary>
public sealed class TheFrozenWorldStillHearsItsResumeTests
{
    /// <summary>A translator that counts ingress polls and declares its own class.</summary>
    private sealed class SpyTranslator : INetworkTranslator
    {
        private readonly TranslatorClass _category;

        public SpyTranslator(string topic, TranslatorClass category)
        {
            TopicName = topic;
            _category = category;
        }

        public int Polls { get; private set; }

        public string TopicName { get; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        public TranslatorClass Category => _category;
        public long ReceivedSampleCount => Polls;
        public long SentSampleCount => 0;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) => Polls++;
        public void ScanAndPublish(ISimulationView view) { }
    }

    /// <summary>
    /// ⚠ A translator that says NOTHING about its class — the shape every one of the existing
    /// implementations has, since <c>Category</c> arrived as a default interface member.
    /// </summary>
    private sealed class UnmarkedTranslator : INetworkTranslator
    {
        public int Polls { get; private set; }

        public string TopicName => "unmarked";
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        public long ReceivedSampleCount => Polls;
        public long SentSampleCount => 0;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) => Polls++;
        public void ScanAndPublish(ISimulationView view) { }
    }

    /// <summary>
    /// ⭐ Reuses the suite's existing <c>DummySimulationView</c> rather than adding a second one —
    /// these rails never touch the view, only the poll count.
    /// </summary>
    private static ISimulationView View()
        => new Fdp.Network.Cyclone.Tests.Translators.DummySimulationView();

    // ── the split ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Frozen: the world stops, the control plane does not.</b>
    /// ⚠ Both translators sit in the SAME ingress system on purpose — 📐 measured, CGF's auxiliary pack
    /// mixes classes in one system, which is exactly why the category is per-translator and not
    /// per-system.
    /// </summary>
    [Fact]
    public void WhileFrozenWorldStateStopsAndTheControlPlaneKeepsPolling()
    {
        var world   = new SpyTranslator("replication", TranslatorClass.WorldState);
        var control = new SpyTranslator("time-mode",   TranslatorClass.ControlPlane);

        bool frozen = false;
        var system = new CycloneNetworkIngressSystem(new INetworkTranslator[] { world, control })
        {
            IsWorldStateFrozen = () => frozen,
        };

        system.Execute(View(), 1f / 60f);
        Assert.Equal(1, world.Polls);
        Assert.Equal(1, control.Polls);

        frozen = true;
        system.Execute(View(), 1f / 60f);
        system.Execute(View(), 1f / 60f);

        Assert.Equal(1, world.Polls);       // ⛔ stopped — a frozen snapshot stays coherent
        Assert.Equal(3, control.Polls);     // ⭐ still polling — this is how the resume arrives

        frozen = false;
        system.Execute(View(), 1f / 60f);
        Assert.Equal(2, world.Polls);
    }

    /// <summary>
    /// 🔒 <b>The fail-safe default: an UNMARKED translator counts as world state and stops.</b>
    ///
    /// <para>⭐ The direction is chosen, not incidental: a miscategorised control-plane translator
    /// fails LOUDLY and immediately (*"resume does not work"*), while the opposite default would leak
    /// live world data into a frozen snapshot SILENTLY.</para>
    /// </summary>
    [Fact]
    public void AnUnmarkedTranslatorIsTreatedAsWorldState()
    {
        var unmarked = new UnmarkedTranslator();

        Assert.Equal(TranslatorClass.WorldState, ((INetworkTranslator)unmarked).Category);

        var system = new CycloneNetworkIngressSystem(new INetworkTranslator[] { unmarked })
        {
            IsWorldStateFrozen = () => true,
        };

        system.Execute(View(), 1f / 60f);

        Assert.Equal(0, unmarked.Polls);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A node with no debugger is unchanged BY CONSTRUCTION.</b> 📐 The gate is constructed at
    /// 12 production sites across 6 assemblies, most inside helpers shared with SimHost and IG — so
    /// the un-set case is not an edge case, it is the overwhelming majority, and it must poll
    /// everything exactly as before.
    /// </summary>
    [Fact]
    public void WithNoGateSetEveryTranslatorIsPolledAsBefore()
    {
        var world   = new SpyTranslator("replication", TranslatorClass.WorldState);
        var control = new SpyTranslator("time-mode",   TranslatorClass.ControlPlane);

        var system = new CycloneNetworkIngressSystem(new INetworkTranslator[] { world, control });

        system.Execute(View(), 1f / 60f);
        system.Execute(View(), 1f / 60f);

        Assert.Equal(2, world.Polls);
        Assert.Equal(2, control.Polls);
    }

    /// <summary>
    /// ⚠ <b>The decision is taken once per <c>Execute</c>, not once per translator.</b> A gate that
    /// flipped mid-phase would run half a frame's world-state translators against one answer and half
    /// against the other, producing exactly the incoherent snapshot the freeze exists to prevent.
    /// </summary>
    [Fact]
    public void TheGateIsAskedOncePerExecuteNotOncePerTranslator()
    {
        int asked = 0;

        var system = new CycloneNetworkIngressSystem(new INetworkTranslator[]
        {
            new SpyTranslator("a", TranslatorClass.WorldState),
            new SpyTranslator("b", TranslatorClass.WorldState),
            new SpyTranslator("c", TranslatorClass.ControlPlane),
        })
        {
            IsWorldStateFrozen = () => { asked++; return true; },
        };

        system.Execute(View(), 1f / 60f);

        Assert.Equal(1, asked);
    }

    // ── R-4007 (Q91 §4-M) — the JOIN gate: state left in DDS, events DISCARDED, control plane polled ──────────

    private sealed class EventSpy : INetworkTranslator, IDiscardableIngress
    {
        public int Polls { get; private set; }
        public int Discards { get; private set; }
        public string TopicName => "weapon-fire";
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        public TranslatorClass Category => TranslatorClass.WorldEvent;
        public long ReceivedSampleCount => Polls;
        public long SentSampleCount => 0;
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) => Polls++;
        public void ScanAndPublish(ISimulationView view) { }
        public int DiscardIngress() { Discards++; return 0; }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>R-4007</c> — a node frozen for a JOIN: world state is not polled (DDS keeps the latest per instance), world
    /// EVENTS are drained and thrown away (no stale burst when the gate opens), the control plane keeps polling.
    /// ⭐ And the debugger freeze HOLDS events instead — the two freezes differ only there.
    /// </summary>
    [Fact]
    public void R4007_AJoinDiscardsEventsAndLeavesState_TheDebuggerHoldsBoth()
    {
        var state   = new SpyTranslator("replication", TranslatorClass.WorldState);
        var control = new SpyTranslator("time-mode",   TranslatorClass.ControlPlane);
        var events  = new EventSpy();
        bool joining = true, debugger = false;
        var system = new CycloneNetworkIngressSystem(new INetworkTranslator[] { state, events, control })
        {
            IsJoinFrozen       = () => joining,
            IsWorldStateFrozen = () => debugger,
        };

        system.Execute(View(), 1f / 60f);
        Assert.Equal((0, 0, 1, 1), (state.Polls, events.Polls, events.Discards, control.Polls));

        joining = false; debugger = true;
        system.Execute(View(), 1f / 60f);
        Assert.Equal((0, 0, 1, 2), (state.Polls, events.Polls, events.Discards, control.Polls));   // ⭐ held, not discarded

        debugger = false;
        system.Execute(View(), 1f / 60f);
        Assert.Equal((1, 1, 1, 3), (state.Polls, events.Polls, events.Discards, control.Polls));
    }

    /// <summary>⭐ <c>R-4007</c> — the gate is wired onto EVERY ingress system among the scheduled systems, and nothing else.</summary>
    [Fact]
    public void R4007_WireJoinGate_ReachesEveryIngressSystem()
    {
        var a = new CycloneNetworkIngressSystem(new INetworkTranslator[0]);
        var b = new CycloneNetworkIngressSystem(new INetworkTranslator[0]);
        int wired = CycloneNetworkIngressSystem.WireJoinGate(new object[] { a, "not a system", b }, () => true);
        Assert.Equal(2, wired);
        Assert.True(a.IsJoinFrozen!() && b.IsJoinFrozen!());
    }

    // ── CE-3329 W5 (DESIGN_Replay_Time_Axis_And_Review §12u) — the exit of a replay / review DISCARDS what the readers hold ──────

    /// <summary>A world-STATE translator: its reader a private field on a base class, emptied through the shared
    /// <c>DdsReaderDiscard.DiscardAll</c>.</summary>
    private abstract class ReaderOwningBase
    {
        private readonly CycloneDDS.Runtime.DdsReader<Fdp.Network.Cyclone.Topics.EntityMasterTopic> _reader;
        protected ReaderOwningBase(CycloneDDS.Runtime.DdsParticipant p, string topic)
            => _reader = new CycloneDDS.Runtime.DdsReader<Fdp.Network.Cyclone.Topics.EntityMasterTopic>(p, topic);
        protected int TakeAll()
        {
            int n = 0;
            while (true) { using var loan = _reader.Take(); if (loan.Count == 0) return n; n += loan.Count; }
        }
        public int Peek() { using var loan = _reader.Read(); return loan.Count; }
        protected int Discard() => Fdp.Toolkit.Replication.Services.DdsReaderDiscard.DiscardAll(_reader);
    }

    private sealed class StateTranslator : ReaderOwningBase, INetworkTranslator, IDiscardableIngress
    {
        public int DiscardIngress() => Discard();
        public StateTranslator(CycloneDDS.Runtime.DdsParticipant p, string topic) : base(p, topic) => TopicName = topic;
        public int Applied { get; private set; }
        public string TopicName { get; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        public long ReceivedSampleCount => Applied;
        public long SentSampleCount => 0;
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) => Applied += TakeAll();
        public void ScanAndPublish(ISimulationView view) { }
    }

    /// <summary>
    /// ⭐⭐⭐ CE-3329 W5 — what the readers kept through a replay (the newest state per instance, the entry's disposes) is DROPPED when
    /// the exit is released, before the first poll; nothing is dropped while the exit is still held; the control plane is never
    /// touched; what arrives AFTER the release is applied as usual.
    /// 🔴 Without the drain the three replay-era samples land on the world the node just put back.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task CE3329_W5_TheReleasedExitDropsTheReplayEraState_ThenIngressResumes()
    {
        using var participant = new CycloneDDS.Runtime.DdsParticipant(0);
        string topic = "ce3329-w5-" + System.Guid.NewGuid().ToString("N");
        var state   = new StateTranslator(participant, topic);
        var events  = new EventSpy();
        var control = new SpyTranslator("time-mode", TranslatorClass.ControlPlane);
        bool held = true;   // the controller's gate: restoring, then held from the teardown until the release
        var system = new CycloneNetworkIngressSystem(new INetworkTranslator[] { state, events, control }) { IsReplayRestoring = () => held };

        using var writer = new CycloneDDS.Runtime.DdsWriter<Fdp.Network.Cyclone.Topics.EntityMasterTopic>(participant, topic);
        for (long id = 1; id <= 3; id++) writer.Write(new Fdp.Network.Cyclone.Topics.EntityMasterTopic { EntityId = id });
        await WaitFor(() => state.Peek() == 3);

        system.DiscardAtReplayExit();          // ⚠ asked while still held: nothing may be drained yet
        system.Execute(View(), 1f / 60f);
        Assert.Equal((0, 0, 3), (system.ExitDiscards, state.Applied, state.Peek()));

        held = false;                          // the release
        system.Execute(View(), 1f / 60f);
        Assert.Equal(1, system.ExitDiscards);
        Assert.Equal(3, system.LastExitDiscardedSamples);
        Assert.Equal(0, state.Applied);        // ⭐ dropped, never applied
        Assert.Equal(2, events.Discards);      // the event translator discards itself (once at the hold, once at the release)
        Assert.Equal(2, control.Polls);        // the control plane polled through it all

        writer.Write(new Fdp.Network.Cyclone.Topics.EntityMasterTopic { EntityId = 4 });
        await WaitFor(() => state.Peek() == 1);
        system.Execute(View(), 1f / 60f);
        Assert.Equal((1, 1), (state.Applied, system.ExitDiscards));   // ⭐ the owners' republish is taken in; the drain ran once
    }

    private static async Task WaitFor(System.Func<bool> condition)
    {
        var started = System.DateTime.UtcNow;
        while (!condition())
        {
            if (System.DateTime.UtcNow - started > System.TimeSpan.FromSeconds(10)) throw new System.TimeoutException("DDS loopback");
            await Task.Delay(20);
        }
    }
}
