using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.WindowManager;

namespace Hrot.Editor.AiShared.Review;

/// <summary>
/// ⭐ R-4 / V4 (docs/UX/DESIGN_Review_Perspective.md §3 D1 / D10 / D11, mockup <c>review-perspective-visual.svg</c>) — <b>THE Review layout, defined ONCE</b> for both hosts: the perspective's name,
/// label and icon, and where each window ROLE docks around the map. A host gives the ids ITS windows have (<see cref="PresetFor"/>): the editor's <c>editor_review_search</c> and the Replay
/// Browser's <c>rb_search</c> are the same role, so the two cannot be laid out differently — and a host that lacks a role (the Replay Browser has no data-breakpoints window) simply skips it.
/// </summary>
public static class ReviewLayout
{
    /// <summary>The perspective's name — what <c>WindowManager.CurrentPerspective</c> holds, and the owner of the bundle's windows in the editor.</summary>
    public const string Perspective = "Review";

    /// <summary>The label the toolbar / menu show (the name itself).</summary>
    public const string Label = "Review";

    /// <summary>The atlas icon key of the perspective's toolbar button (a recording).</summary>
    public const string IconKey = "asset/recording";

    /// <summary>The windows the layout docks, by what they ARE (not by host id).</summary>
    public enum Role { MissionSummary, Details, FrameDiff, Search, Breakpoints, EventTimeline }

    /// <summary>Where a role docks (the mockup): rules summary left · Details right · diff / search / breakpoints tabbed under it · the Event Timeline across the bottom · the map in the middle.</summary>
    public static DockRegion RegionOf(Role role) => role switch
    {
        Role.MissionSummary => DockRegion.Left,
        Role.Details        => DockRegion.Right,
        Role.FrameDiff      => DockRegion.RightLower,
        Role.Search         => DockRegion.RightLower,
        Role.Breakpoints    => DockRegion.RightLower,
        _                   => DockRegion.Bottom,
    };

    /// <summary>The roles in the order the tab strip shows them.</summary>
    public static readonly Role[] Roles = { Role.MissionSummary, Role.Details, Role.FrameDiff, Role.Search, Role.Breakpoints, Role.EventTimeline };

    /// <summary>The layout for a host whose windows have <paramref name="ids"/> (a role with no id is left out).</summary>
    public static DockPreset PresetFor(IReadOnlyDictionary<Role, string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return new DockPreset(Roles.Where(ids.ContainsKey).Select(r => (ids[r], RegionOf(r))).ToArray());
    }

    /// <summary>
    /// Makes the windows of <paramref name="ids"/> that belong to ANOTHER perspective also show in Review (<see cref="ManagedWindow.AlsoShowIn"/>) — the editor shares its Scenario layout's
    /// Details, Event Timeline, Mission summary and data-breakpoints windows instead of a second copy. A window already owned by Review (the bundle's) is left alone.
    /// </summary>
    public static void ShareWindows(WindowManager windows, IReadOnlyDictionary<Role, string> ids)
    {
        ArgumentNullException.ThrowIfNull(windows);
        foreach (var id in ids.Values)
            if (windows.TryGetWindow(id, out var w) && w.OwningPerspective != Perspective) windows.AlsoShowWindowIn(id, Perspective);
    }

    /// <summary>Opens every window of <paramref name="ids"/> (the first time the layout is entered, the analysis windows are closed until now).</summary>
    public static void OpenWindows(WindowManager windows, IReadOnlyDictionary<Role, string> ids)
    {
        ArgumentNullException.ThrowIfNull(windows);
        foreach (var id in ids.Values) windows.ShowWindow(id);
    }
}

/// <summary>
/// ⭐ R-4 / V4 (D10) — <b>the layout switches BY ITSELF</b>: entering an AAR on the ALL-IN-ONE editor ⇒ the Review layout, remembering the one it came from; leaving AAR ⇒ that one back. ⛔ A cluster
/// station's AAR (the trainees' debrief) never switches. A switch the user makes DURING the AAR is respected — leaving still restores the layout from BEFORE the AAR. ⭐ It touches only the
/// layout (<c>TrySwitchPerspective</c>): the replay, the transport and the selection never pass through it. Driven once a frame by <see cref="Update"/>; every input is a delegate so a rail
/// drives it without a host.
/// </summary>
public sealed class ReviewLayoutSwitch
{
    private readonly Func<bool> _inAar, _allInOne, _startShowing;
    private readonly Func<string> _current;
    private readonly Func<string, bool> _trySwitch;
    private readonly Func<string, bool> _retargetStart;
    private readonly Action? _enteredReview;
    private bool _active, _pending;
    private string? _before;

    /// <param name="inAar">The run mode is AAR now.</param>
    /// <param name="allInOne">This host is the ALL-IN-ONE editor (a node with no real cluster around it) — <see langword="false"/> for a cluster station.</param>
    /// <param name="current">The current perspective.</param>
    /// <param name="trySwitch">Switches to a perspective; <see langword="false"/> = refused (Start is showing).</param>
    /// <param name="startShowing">The Start perspective is showing (the cluster is Idle).</param>
    /// <param name="retargetStart">While Start shows: what leaving Start restores becomes the given perspective.</param>
    /// <param name="enteredReview">Called once each time the switch puts the Review layout up.</param>
    public ReviewLayoutSwitch(Func<bool> inAar, Func<bool> allInOne, Func<string> current, Func<string, bool> trySwitch,
        Func<bool>? startShowing = null, Func<string, bool>? retargetStart = null, Action? enteredReview = null)
    {
        _inAar = inAar ?? throw new ArgumentNullException(nameof(inAar));
        _allInOne = allInOne ?? throw new ArgumentNullException(nameof(allInOne));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _trySwitch = trySwitch ?? throw new ArgumentNullException(nameof(trySwitch));
        _startShowing = startShowing ?? (() => false);
        _retargetStart = retargetStart ?? (_ => false);
        _enteredReview = enteredReview;
    }

    /// <summary>The layout from before the AAR the switch is in, or null.</summary>
    public string? Before => _before;

    /// <summary>Once a frame.</summary>
    public void Update()
    {
        bool aar = _inAar() && _allInOne();            // ⛔ a station's AAR is never an AAR for this switch
        if (aar)
        {
            if (!_active) { _active = true; _pending = true; _before = null; }
            if (!_pending) return;
            var now = _current();
            if (now == ReviewLayout.Perspective) { _before = now; _pending = false; return; }   // already there: nothing to put back but the same
            if (_startShowing() || !_trySwitch(ReviewLayout.Perspective)) return;               // Start refuses; try again next frame
            _before = now;
            _pending = false;
            _enteredReview?.Invoke();
            return;
        }

        if (!_active) return;
        _active = false; _pending = false;
        var before = _before; _before = null;
        if (before == null) return;                                                             // the switch never happened (Start kept refusing)
        if (_startShowing()) { _retargetStart(before); return; }                                // the AAR closed into Idle: Start restores the pre-AAR layout
        if (_current() != before) _trySwitch(before);
    }
}
