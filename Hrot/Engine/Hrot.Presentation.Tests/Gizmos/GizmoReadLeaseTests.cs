using System;
using System.IO;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Common.Infrastructure;
using Hrot.Presentation.DebugApi;
using Hrot.ScenarioEditor.Map;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐⭐ CE-11045 (HL-1, docs/DESIGN_Threaded_Simulation_Presentation.md §16) — a HEADLESS editor starts the existing GZH-003 gizmo group shut (<c>StartEnabled = !headless</c>) and
/// <c>GET /panels/_gizmo</c> holds a TIMED reference on its ref-counted gate (<see cref="GizmoReadLease"/>); the JSON gizmo feed is published only while the group runs. A windowed host draws every frame.
/// </summary>
public sealed class GizmoReadLeaseTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly bool _capture = PanelSnapshot.CaptureEnabled;

    public void Dispose() { PanelSnapshot.CaptureEnabled = _capture; _world.Dispose(); }

    private sealed class CountingGlobal : IGlobalStatelessGizmo
    {
        public int Draws;
        public void Draw(ISimulationView view, IDebugDrawBuilder draw) { Draws++; draw.DrawSphere(new System.Numerics.Vector3(1, 2, 3), 1f, new Rgba32(1, 2, 3, 4)); }
    }

    private (MapInteraction mi, CountingGlobal probe) Pack(bool startEnabled)
    {
        var probe = new CountingGlobal();
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world, StartEnabled = startEnabled, ContributeExtras = r => r.Stateless.RegisterGlobal(probe) });
        return (mi, probe);
    }

    /// <summary>
    /// Headless (group starts shut): no gizmo Draw; the first read opens the gate and the next tick draws; reads keep it open; 5 s without one releases it.
    /// 🔴 Red-proof: <c>StartEnabled</c> true for the headless host ⇒ the first count is 1; <c>Tick</c> never releasing ⇒ the last count rises.
    /// </summary>
    [Fact]
    public void CE11045_Headless_DrawsNothing_UntilRead_ThenFiveSeconds()
    {
        var (mi, probe) = Pack(startEnabled: false);
        long now = 0;
        var lease = new GizmoReadLease(mi.Gate, () => now);

        mi.GizmoGroup.Execute(_world, 0.016f);
        Assert.Equal(0, probe.Draws);                          // nobody reads: no gizmo Draw

        Assert.True(lease.Touch());                            // the first read after idle opens the gate ...
        Assert.False(lease.Touch());                           // ... later reads only extend it
        mi.GizmoGroup.Execute(_world, 0.016f);
        Assert.Equal(1, probe.Draws);                          // the next pulled tick draws

        now = (long)(4.9 * System.Diagnostics.Stopwatch.Frequency); lease.Tick();
        mi.GizmoGroup.Execute(_world, 0.016f);
        Assert.Equal(2, probe.Draws);                          // 4.9 s: still held
        Assert.True(lease.Held);

        lease.Touch(); now += (long)(5.2 * System.Diagnostics.Stopwatch.Frequency); lease.Tick();
        Assert.False(lease.Held);                              // 5.2 s after the last read: released
        mi.GizmoGroup.Execute(_world, 0.016f);
        Assert.Equal(2, probe.Draws);

        Assert.True(lease.Touch());                            // read again: open again
        mi.GizmoGroup.Execute(_world, 0.016f);
        Assert.Equal(3, probe.Draws);
    }

    /// <summary>A windowed host (<c>StartEnabled = true</c>, no lease) draws every frame.</summary>
    [Fact]
    public void CE11045_Windowed_DrawsEveryFrame()
    {
        var (mi, probe) = Pack(startEnabled: true);
        for (int i = 0; i < 4; i++) mi.GizmoGroup.Execute(_world, 0.016f);
        Assert.Equal(4, probe.Draws);
    }

    /// <summary>
    /// ⭐ HL-1b — the JSON feed: with no reader the map step publishes NOTHING and allocates 0 B (no <c>Describe</c>); once a read holds the gate it publishes the primitives.
    /// 🔴 Red-proof: <c>PublishGizmoFeed</c> without the <c>GizmoGroup.Enabled</c> test ⇒ the panel exists and bytes &gt; 0 before the read.
    /// </summary>
    [Fact]
    public void CE11045_TheJsonFeed_IsPublishedOnlyWhileRead()
    {
        PanelSnapshot.CaptureEnabled = true;
        var (mi, _) = Pack(startEnabled: false);
        string address = GizmoFramePanel.AddressFor("ce11045-" + Guid.NewGuid().ToString("N"));
        var step = new MapPresentationStep(mi, new StagedPresentation(), address);
        var lease = new GizmoReadLease(mi.Gate);
        for (int i = 0; i < 100; i++) mi.Buffer.DrawSphere(new System.Numerics.Vector3(i, 0, 0), 1f, new Rgba32(9, 9, 9, 9));

        step.PublishGizmoFeed(mi.Buffer);                      // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) step.PublishGizmoFeed(mi.Buffer);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Null(PanelSnapshot.TryGet(address));            // nothing published while nobody reads

        lease.Touch();
        step.PublishGizmoFeed(mi.Buffer);
        var vm = PanelSnapshot.TryGet(address);
        Assert.NotNull(vm);
        Assert.Equal(100, ((GizmoFrameViewModel)vm!).Count);
    }

    /// <summary>
    /// Per host kind (source rail — booting an editor / player needs the DDS integration harness): the editor starts its group shut when headless and builds the lease only then; the Player
    /// (which publishes its gizmo buffer over DDS) does not touch the gate. 🔴 Red-proof: <c>StartEnabled = true</c> in the editor ⇒ red.
    /// </summary>
    [Fact]
    public void CE11045_TheEditorStartsShutWhenHeadless_ThePlayerIsUntouched()
    {
        string editor = File.ReadAllText(Path.Combine(Fdp.Testing.TestRepo.Root, "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs"));
        Assert.Contains("StartEnabled = !_headless,", editor);
        Assert.Contains("if (_headless)\n            {\n                _gizmoLease = new Hrot.Presentation.DebugApi.GizmoReadLease(", editor.Replace("\r\n", "\n"));
        string player = File.ReadAllText(Path.Combine(Fdp.Testing.TestRepo.Root, "Hrot/Subsystems/Hrot.Player/PlayerApplication.cs"));
        Assert.DoesNotContain("GizmoReadLease", player);
    }
}
