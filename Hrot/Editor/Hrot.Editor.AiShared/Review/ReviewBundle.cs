using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.Windows.ReplayBrowser;
using Fdp.Toolkit.Runner;

namespace Hrot.Editor.AiShared.Review;

/// <summary>
/// ⭐ R-1 / V1 (docs/UX/DESIGN_Review_Perspective.md §3 D2 / D3, §4 V1) — what a host hands the review windows. The record is the design's D3
/// (<c>World · RecordingFiles · CurrentFrame · Seek · Selection · Perspective · IdPrefix</c>) plus what the four windows need TODAY: the three
/// host-fed panels they draw (the Frame diff and Replay search panels, the transport's <see cref="ReplayTimelinePanel"/> that Recording info and
/// JSON export draw) and the title-bar colour.
/// ⭐ <b>V1 reads only</b> <see cref="Perspective"/>, <see cref="IdPrefix"/>, <see cref="Colour"/> and the three panels (the RB's panels already hold the
/// recording's files, D4). ⭐ <b>V2's members</b> (optional, null today): <see cref="World"/>, <see cref="RecordingFiles"/>, <see cref="CurrentFrame"/>,
/// <see cref="Seek"/>, <see cref="Selection"/> — the editor builds its own diff / search panels from them (a search hit seeks and selects).
/// </summary>
public sealed record ReviewHostServices(
    string Perspective,
    string IdPrefix,
    Vector4 Colour,
    ComponentDiffPanel DiffPanel,
    ReplaySearchPanel SearchPanel,
    ReplayTimelinePanel TimelinePanel,
    Func<EntityRepository?>? World = null,                  // V2 — the ONE world the host shows
    Func<string[]>? RecordingFiles = null,                  // V2 — the recording's file(s) the analysis reads (D4)
    Func<long>? CurrentFrame = null,                        // V2 — the shown frame
    Action<long>? Seek = null,                              // V2 — a search hit seeks the host
    Func<int?>? Selection = null);                          // V2 — the selected entity's network id

/// <summary>
/// ⭐ R-1 / V1 (docs/UX/DESIGN_Review_Perspective.md §3 D2, §4 V1) — THE review windows as ONE bundle every host composes, as
/// <c>RulesBundle</c> is for the rules: <b>Frame diff</b> (<c>&lt;prefix&gt;diff</c>), <b>Replay search</b> (<c>&lt;prefix&gt;search</c>), <b>Recording info</b> and
/// <b>JSON export</b> (their own fixed ids). ⛔ The windows keep their classes — the bundle composes, it does not rewrite them. Built in the constructor so a host
/// can hang its File ▸ items on <see cref="RecordingInfo"/> / <see cref="JsonExport"/> before composing.
/// </summary>
public sealed class ReviewBundle : IUiBundle
{
    public ReviewBundle(ReviewHostServices host)
    {
        ArgumentNullException.ThrowIfNull(host);
        FrameDiff = new ComponentDiffWindow(host.IdPrefix + "diff", "Frame Diff Viewer", host.Perspective, host.DiffPanel, host.Colour);
        ReplaySearch = new ReplaySearchWindow(host.IdPrefix + "search", "Replay Search", host.Perspective, host.SearchPanel, host.Colour);
        RecordingInfo = new RecordingInfoWindow(host.Perspective, host.TimelinePanel, host.Colour);
        JsonExport = new JsonExportWindow(host.Perspective, host.TimelinePanel, host.Colour);
    }

    public ComponentDiffWindow FrameDiff { get; }
    public ReplaySearchWindow ReplaySearch { get; }
    public RecordingInfoWindow RecordingInfo { get; }
    public JsonExportWindow JsonExport { get; }

    /// <inheritdoc/>
    public string Name => "review";

    /// <inheritdoc/>
    public void RegisterInto(UiBundleContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ctx.Windows.RegisterWindow(RecordingInfo);
        ctx.Windows.RegisterWindow(JsonExport);
        ctx.Windows.RegisterWindow(FrameDiff);
        ctx.Windows.RegisterWindow(ReplaySearch);
    }
}
