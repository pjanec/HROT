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
    private readonly Func<bool>? _merged;

    /// <param name="transport">The editor's transport over <see cref="OwnPartReplay"/>.</param>
    /// <param name="inAar">The run mode is AAR now (the run-mode indicator's <c>Shown</c>).</param>
    /// <param name="merged">⭐ V6 — a participant group is open MERGED: the segment then also draws ▶ (⭐ G-7 S4: enabled — Merged plays; ⛔ was greyed with <see cref="IReplayTransport.PlayDisabledReason"/>) — the time control's own ▶ is ui's and is not touched.</param>
    public AarTransportSegment(IReplayTransport transport, Func<bool> inAar, Func<bool>? merged = null)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _inAar = inAar ?? throw new ArgumentNullException(nameof(inAar));
        _merged = merged;
        Drawer = new ReplayTransportDrawer(transport, ReplayTransportParts.AarSegment, idPrefix: "aar", seekBarMaxWidth: SeekBarMaxWidth);
        if (merged is not null)
            MergedDrawer = new ReplayTransportDrawer(transport, ReplayTransportParts.AarSegment | ReplayTransportParts.PlayPause, idPrefix: "aarm", seekBarMaxWidth: SeekBarMaxWidth);
    }

    public IReplayTransport Transport { get; }
    public ReplayTransportDrawer Drawer { get; }

    /// <summary>⭐ V6 — the profile while Merged: the segment's items plus ▶; null when the host has no group source.</summary>
    public ReplayTransportDrawer? MergedDrawer { get; }

    /// <summary>The drawer in use now.</summary>
    public ReplayTransportDrawer Current => MergedDrawer is not null && _merged!() ? MergedDrawer : Drawer;

    /// <summary>Draws the segment when the run mode is AAR; nothing otherwise.</summary>
    public void Draw()
    {
        if (!_inAar()) return;
        Current.Draw();
    }

    /// <summary>Registers the segment as its own entry of <paramref name="toolbar"/>.</summary>
    public void RegisterInto(MainToolbarManager toolbar)
    {
        ArgumentNullException.ThrowIfNull(toolbar);
        toolbar.RegisterEntry(EntryId, SortOrder, MainToolbarManager.DefaultEntryHeight, Draw);
    }
}
