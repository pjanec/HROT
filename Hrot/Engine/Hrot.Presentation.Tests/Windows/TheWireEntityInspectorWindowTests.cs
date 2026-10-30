using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;
using Hrot.NED.Descriptors;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐ AQ109 §5b steps 4-6 (X5) — the "Wire Entity Inspector" window every diagnostics host offers: it is registered by
/// <see cref="DiagnosticsWindowsBundle"/>, starts CLOSED, and holds its DDS readers ONLY while open.
/// 📄 docs/blueprints/Architect_Question_109_Retire_ExCon.md §6.5.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheWireEntityInspectorWindowTests
{
    private const uint PrivateDomain = 125;
    private static readonly Func<IReadOnlyList<Type>> OneType = () => new[] { typeof(EntityMaster) };

    private static WireEntityInspectorWindow NewWindow(Func<DdsParticipant?> participant)
        => new("w", "Wire", "P", participant, titleBarColor: null, types: OneType);

    [Theory]
    [InlineData("ig_", "IG", "IG")]
    [InlineData("simhost_", "SimHost", "SimHost")]
    [InlineData("cgf_", "CGF", "Scenario")]
    [InlineData("editor_", "Editor", "Scenario")]
    [InlineData("stride_", "Stride", "SimHost")]
    [InlineData("rb_", "Replay", "ReplayBrowser")]
    public void EveryHostThatComposesTheBundleOffersTheWindow_ClosedByDefault(string prefix, string title, string perspective)
    {
        var wm = new WindowManager(new Fdp.Presentation.Icons.IconAtlas(IntPtr.Zero, 512, 512));
        var c = new Vector4(0.1f, 0.2f, 0.3f, 1f);
        UiBundleHost.Compose(new IUiBundle[]
        {
            new DiagnosticsWindowsBundle(new DiagnosticsHostServices(
                prefix, title, perspective, new EntityInspectorPanel(), () => null,
                () => new Fdp.Presentation.Abstractions.InspectorState(), new EventBrowserPanel(), c,
                () => null, () => null)),
        }, new UiBundleContext(wm));

        Assert.True(wm.TryGetWindow(prefix + "wire_entities", out var w), $"no '{prefix}wire_entities' window");
        Assert.Equal($"{title} Wire Entity Inspector", w!.Title);
        Assert.Equal(perspective, w.OwningPerspective);
        Assert.Equal(c, w.TitleBarColor);
        Assert.False(w.IsOpen, "the wire inspector must start closed — its readers exist only while it is open");
    }

    [Fact]
    public void NothingIsReadWhileTheWindowIsClosed_AndTheReadersGoWhenItCloses()
    {
        using var participant = new DdsParticipant(PrivateDomain);
        var asked = 0;
        var w = NewWindow(() => { asked++; return participant; });
        var atlas = new Fdp.Presentation.Icons.IconAtlas(IntPtr.Zero, 512, 512);

        // closed: frames pass, the participant is never even asked for and no mirror exists
        for (var i = 0; i < 3; i++) w.Render("P", atlas);
        Assert.Equal(0, asked);
        Assert.Null(w.Mirror);

        // open: the mirror (and its readers) appear
        w.IsOpen = true;
        Assert.NotNull(w.SimulateDrawClientArea());
        Assert.NotNull(w.Mirror);
        Assert.Single(w.Mirror!.OpenedTypes);

        // closed again: the next frame releases them
        var mirror = w.Mirror;
        w.IsOpen = false;
        w.Render("P", atlas);
        Assert.Null(w.Mirror);
        Assert.Empty(mirror!.OpenedTypes);
    }

    [Fact]
    public void AHostWithNoParticipantSaysSoInsteadOfShowingAnEmptyWire()
    {
        var w = NewWindow(() => null);
        w.IsOpen = true;
        Assert.Null(w.SimulateDrawClientArea());   // no model — DrawClientArea prints the explanation
        Assert.Null(w.Mirror);
    }
}
