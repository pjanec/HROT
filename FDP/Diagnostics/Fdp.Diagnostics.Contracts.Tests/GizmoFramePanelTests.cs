using System;
using System.Linq;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Diagnostics.Contracts.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>U-obs-3</c> — the map/gizmo feed is a PEER of the panels.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Adoption <c>U-obs-3</c>; §UML's
/// <c>DebugPrimitiveBuffer ..&gt; PanelSnapshotService : peer feed for the map</c> — the last unbuilt
/// edge on that diagram.
///
/// <para>⚠ <b>One class, like <c>PanelSnapshotTests</c> and for the same reason:</b> the snapshot is
/// process-global static state and xunit parallelises CLASSES. ⛔ These two classes would flake against
/// each other if they ran concurrently, so this one is pinned into the same collection.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class GizmoFramePanelTests
{
    /// <summary>⭐ <c>BP-485</c> — a HOST's address, never the bare kind. See <c>AddressFor</c>.</summary>
    private static readonly string Editor = GizmoFramePanel.AddressFor("editor");

    private static void Reset()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static DebugPrimitiveBuffer WithOneSphere()
    {
        var buffer = new DebugPrimitiveBuffer(capacity: 16);
        buffer.DrawSphere(new System.Numerics.Vector3(1f, 2f, 3f), 4f, new Rgba32(255, 255, 255, 255));
        return buffer;
    }

    /// <summary>
    /// ⭐⭐⭐ The gate: after a frame, <c>DumpAll()</c> carries the gizmo primitives under the feed's id —
    /// ⭐ i.e. ONE call answers "what is on screen?" for the panels AND the map.
    /// </summary>
    [Fact]
    public void AfterAFrame_DumpAllCarriesTheGizmoPrimitives()
    {
        Reset();
        Assert.DoesNotContain(Editor, PanelSnapshot.RegisteredPanels);  // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        GizmoFramePanel.Publish(WithOneSphere(), Editor);

        Assert.Contains(Editor, PanelSnapshot.RegisteredPanels);

        var dump = PanelSnapshot.DumpAll();
        var feed = dump[Editor];
        Assert.NotNull(feed);
        Assert.Equal(1, feed!["count"]!.GetValue<int>());
        Assert.False(feed["truncated"]!.GetValue<bool>());

        // ⭐ Real content, not just shape: the sphere's own fields survived the projection.
        var first = feed["primitives"]!.AsArray()[0]!;
        Assert.Equal("Sphere", first["shape"]!.GetValue<string>());
        Assert.Equal(4f, first["radius"]!.GetValue<float>());
        Assert.Equal(1f, first["center"]!["x"]!.GetValue<float>());

        Reset();
    }

    /// <summary>⭐ The opt-in rule, same as every panel: DECLARED always, PUBLISHED only when capturing.</summary>
    [Fact]
    public void WithCaptureOff_PublishesNothing_ButStaysInstrumented()
    {
        Reset();

        var vm = GizmoFramePanel.Publish(WithOneSphere(), Editor);

        Assert.Contains(Editor, PanelSnapshot.RegisteredPanels);
        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Equal(1, vm.Count);          // ⭐ the model is still BUILT and returned to the caller

        Reset();
    }

    /// <summary>
    /// ⛔⛔ <b>TRUNCATION IS REPORTED, NEVER SILENT.</b> ⚠ A reader that cannot tell a full frame from a
    /// clipped one would take "no more primitives" from an arbitrary cap — 📌 the same
    /// no-silent-caps rule the workflow guidance states for dropped work.
    /// </summary>
    [Fact]
    public void WhenTheFrameExceedsTheCap_ItSaysSo()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        var buffer = new DebugPrimitiveBuffer(capacity: 16);
        for (int i = 0; i < 5; i++)
            buffer.DrawSphere(new System.Numerics.Vector3(i, 0f, 0f), 1f, new Rgba32(255, 255, 255, 255));

        var vm = GizmoFramePanel.Publish(buffer, Editor, max: 2);

        Assert.Equal(5, vm.Count);
        Assert.Equal(2, vm.Emitted);
        Assert.True(vm.Truncated);
        Assert.Equal(2, vm.Dump()["primitives"]!.AsArray().Count);

        Reset();
    }

    /// <summary>
    /// ⚠⚠ <b>The ORDER trap, railed because it fails SILENTLY and looks healthy.</b> The buffer's
    /// <c>Clear</c>/<c>EndFrame</c> resets the transient write cursor ⇒ publishing AFTER it registers an
    /// empty frame — the id present, the model well-formed, <c>count: 0</c>. ⭐ Nothing about that reads
    /// as broken, which is exactly why the production call site puts the publish first.
    /// </summary>
    [Fact]
    public void PublishingAfterTheBufferIsReset_CapturesAnEmptyFrame()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        var buffer = WithOneSphere();
        buffer.Clear();                       // ⛔ the wrong order, on purpose

        var vm = GizmoFramePanel.Publish(buffer, Editor);

        Assert.Equal(0, vm.Count);
        Assert.Empty(vm.Dump()["primitives"]!.AsArray());

        Reset();
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-485</c> — TWO HOSTS' MAP FEEDS STAY INDIVIDUALLY ADDRESSABLE.</b>
    ///
    /// <para>⛔⛔ The first cut defaulted the ADDRESS to the KIND, so every host would have written under
    /// the literal <c>"_gizmo"</c> and overwritten the previous one. ⚠ Invisible with one publisher —
    /// 📌 <b>the same blind spot <c>U1d</c> hit</b>: for a singleton, address and kind are one string.
    /// ⇒ ⭐ this rail is the multi-host case that makes the distinction observable, and it is the
    /// sibling of <c>PanelSnapshotTests</c>' own three-live-watches rail.</para>
    /// </summary>
    [Fact]
    public void TwoHostsFeeds_DoNotOverwriteEachOther()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        var cgf = GizmoFramePanel.AddressFor("cgf");
        Assert.NotEqual(Editor, cgf);                       // ⛔ anti-vacuity

        var editorBuffer = WithOneSphere();
        var cgfBuffer    = new DebugPrimitiveBuffer(capacity: 16);
        cgfBuffer.DrawSphere(new System.Numerics.Vector3(9f, 9f, 9f), 1f, new Rgba32(1, 2, 3, 4));
        cgfBuffer.DrawSphere(new System.Numerics.Vector3(8f, 8f, 8f), 1f, new Rgba32(1, 2, 3, 4));

        GizmoFramePanel.Publish(editorBuffer, Editor);
        GizmoFramePanel.Publish(cgfBuffer,    cgf);

        // ⭐ Both present, and each carries ITS OWN frame — not the last writer's.
        Assert.Equal(1, PanelSnapshot.TryGet(Editor)!.Dump()["count"]!.GetValue<int>());
        Assert.Equal(2, PanelSnapshot.TryGet(cgf)!.Dump()["count"]!.GetValue<int>());

        // ⭐⭐ …and they share the KIND, which is what a cross-host conformance diff groups by.
        Assert.Equal(GizmoFrameViewModel.Kind, PanelSnapshot.TryGet(Editor)!.PanelKind);
        Assert.Equal(GizmoFrameViewModel.Kind, PanelSnapshot.TryGet(cgf)!.PanelKind);

        Reset();
    }

    /// <summary>⛔ A feed with no host address is refused rather than silently sharing one.</summary>
    [Fact]
    public void PublishingWithoutAHostAddress_IsRefused()
    {
        Reset();
        Assert.Throws<ArgumentException>(() => GizmoFramePanel.Publish(WithOneSphere(), " "));
        Assert.Throws<ArgumentException>(() => GizmoFramePanel.AddressFor(""));
        Reset();
    }
}
