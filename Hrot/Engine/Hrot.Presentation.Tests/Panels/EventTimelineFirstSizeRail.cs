using System;
using System.Numerics;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Presentation.Windows.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ CE-4290 (Replay Browser UX B3) — the Event Timeline opens at a USABLE size on a fresh layout. It opened auto-sized to its content (~90 × 50 px),
/// which clips every lane row: the lanes "looked empty" and the State group was "not among the drawn groups" while the index held the events.
/// 🔴 Red-proof (compiles): the <c>BeforeBegin</c> override emptied ⇒ the window auto-fits to its content, not FirstSize ⇒ red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class EventTimelineFirstSizeRail
{
    [SkippableFact]
    public void CE4290_TheEventTimeline_OpensAtAUsableSize_NotAutoSizedToNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var window = new EventTimelineWindow("rail_evt_first_" + Guid.NewGuid().ToString("N"), "Event Timeline", "Scenario", new TimelinePanel(), () => null,
            new Hrot.UI.Common.Adapters.ClusterTimelineCursor(() => null, () => null),
            () => Array.Empty<TimelineLaneDescriptor>(), Vector4.One) { IsOpen = true };
        var atlas = new IconAtlas(IntPtr.Zero, 512, 512);
        using var frame = UiFrameHarness.Begin(1400, 600);
        frame.StepN(3, () => window.Render("Scenario", atlas));
        // ⚠ The harness does not reproduce the ~90 × 50 auto-fit seen live (a saved layout, or the first frames drawn before the recording loaded); so the rail
        //   pins the MECHANISM: on a fresh layout the window opens at exactly FirstSize, not at whatever its content auto-fits to.
        Assert.Equal(EventTimelineWindow.FirstSize, window.LastWindowSize);
    }
}
