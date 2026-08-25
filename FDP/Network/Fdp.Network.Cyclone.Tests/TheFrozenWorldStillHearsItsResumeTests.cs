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
}
