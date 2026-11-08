using System;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;

namespace Hrot.Editor.Review;

/// <summary>
/// ⭐ R-3 / V3 (docs/UX/DESIGN_Review_Perspective.md §3 D8, §4 V3) — <b>the editor's AAR segment of the ONE replay transport</b>: its OWN main-toolbar entry (ui's <c>MainToolbarTimeControlSection</c> is
/// untouched), drawn by the SAME <see cref="ReplayTransportDrawer"/> the Replay Browser uses, with the <see cref="ReplayTransportParts.AarSegment"/> profile — history ◀ ▶, step back with hold-to-scrub,
/// the seek bar — because the time control beside it already draws ▶ ⏸ ■, the rate, the axis and step forward. Shown only while the run mode is AAR (draws nothing otherwise).
/// </summary>
public sealed class AarTransportSegment
{
    /// <summary>The toolbar entry id.</summary>
    public const string EntryId = "AarTransport";
    /// <summary>After the time-control group (0–9) and its closing separator (10), before the problems count (15) and the perspective group (20–29).</summary>
    public const int SortOrder = 12;
    /// <summary>The seek bar's widest span: the segment shares the bar with the perspective group and the menus' right side.</summary>
    public const float SeekBarMaxWidth = 360f;

    private readonly Func<bool> _inAar;

    /// <param name="transport">The editor's transport over <see cref="OwnPartReplay"/>.</param>
    /// <param name="inAar">The run mode is AAR now (the run-mode indicator's <c>Shown</c>).</param>
    public AarTransportSegment(IReplayTransport transport, Func<bool> inAar)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _inAar = inAar ?? throw new ArgumentNullException(nameof(inAar));
        Drawer = new ReplayTransportDrawer(transport, ReplayTransportParts.AarSegment, idPrefix: "aar", seekBarMaxWidth: SeekBarMaxWidth);
    }

    public IReplayTransport Transport { get; }
    public ReplayTransportDrawer Drawer { get; }

    /// <summary>Draws the segment when the run mode is AAR; nothing otherwise.</summary>
    public void Draw()
    {
        if (!_inAar()) return;
        Drawer.Draw();
    }

    /// <summary>Registers the segment as its own entry of <paramref name="toolbar"/>.</summary>
    public void RegisterInto(MainToolbarManager toolbar)
    {
        ArgumentNullException.ThrowIfNull(toolbar);
        toolbar.RegisterEntry(EntryId, SortOrder, MainToolbarManager.DefaultEntryHeight, Draw);
    }
}
