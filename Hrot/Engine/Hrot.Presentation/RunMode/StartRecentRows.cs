using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using NodeEditor.Core.Widgets;
using NodeEditor.UI.Picker;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐ <c>CE-3414</c> (AQ108 §0 D31; scripts/gen-live-session-storyboard.py panel ①) — <b>the Start screen's Recent list</b>: the user's recent scenarios (the ONE <c>recent.Scenario</c> list, <see cref="RecentStore"/>), each with
/// <i>● Live</i> (the briefing — a Live load lands paused) · <i>pen Edit</i> (author seat only) · <i>▶ AAR</i> (the scenario's latest run). Built on the Hrot side and handed to <see cref="StartSurfaceWindow"/>, which owns no action.
/// Every button is a verb of the owner's ONE guarded door (<see cref="ScenarioDoor"/>: Stop &amp; Load, Save / Discard / Cancel come for free). ⭐ R-2084: the surface asks every frame in Idle — the rows are rebuilt ONLY when an input changed
/// (the recent list's contents, the inventory arrays, the seat, the minute) and the cached list is returned otherwise.
/// </summary>
public sealed class StartRecentRows
{
    public const int MaxRows = 5;
    public const string NeverRun = "never run";
    public const string NeedsAuthorSeat = "editing needs an author seat";
    public const string NoSession = "No scenario session on this host";

    private readonly RunModeChoice _choice;
    private readonly RecentStore _recents;
    private readonly string _context;
    private readonly Func<string, string> _keyOf;
    private readonly Func<Seat> _seat;
    private readonly Func<DateTime> _now;

    private IReadOnlyList<StartSurfaceWindow.StartRecentRow> _rows = Array.Empty<StartSurfaceWindow.StartRecentRow>();
    private readonly List<string> _seenRecent = new();
    private string[]? _seenScenarios;
    private ExerciseInventoryItem[]? _seenLocal, _seenArchived, _seenUnarchived;
    private Seat _seenSeat = (Seat)255;
    private long _seenMinute = long.MinValue;
    private bool _seenDoor;

    /// <param name="recentContext">The Recent list of scenarios (<c>AssetRecents.ContextFor(AssetKind.Scenario)</c>).</param>
    /// <param name="keyOf">A scenario NAME → the key it is remembered by (<c>AssetRecents.ScenarioKey</c>: its asset id).</param>
    /// <param name="seat">The station's seat (the <c>StationMode</c> singleton of the owner's world; Author when there is none).</param>
    public StartRecentRows(RunModeChoice choice, RecentStore recents, string recentContext, Func<string, string> keyOf, Func<Seat> seat, Func<DateTime>? now = null)
    {
        _choice = choice ?? throw new ArgumentNullException(nameof(choice));
        _recents = recents ?? throw new ArgumentNullException(nameof(recents));
        _context = recentContext;
        _keyOf = keyOf ?? throw new ArgumentNullException(nameof(keyOf));
        _seat = seat ?? throw new ArgumentNullException(nameof(seat));
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>The owner\'s choice these rows act through (a rail asserts the composition passed the picked owner\'s).</summary>
    public RunModeChoice Choice => _choice;

    /// <summary>The rows now: the cached list unless an input changed. 0 B on a steady frame.</summary>
    public IReadOnlyList<StartSurfaceWindow.StartRecentRow> Rows()
    {
        var recent = _recents.GetRecent(_context);
        var scenarios = _choice.InventoryScenarios;
        var (local, archived, unarchived) = _choice.InventoryExercises;
        var seat = _seat();
        long minute = _now().Ticks / TimeSpan.TicksPerMinute;
        bool hasDoor = _choice.Door is not null;
        if (!Changed(recent, scenarios, local, archived, unarchived, seat, minute, hasDoor)) return _rows;

        _seenRecent.Clear();
        for (int i = 0; i < recent.Count; i++) _seenRecent.Add(recent[i]);
        _seenScenarios = scenarios; _seenLocal = local; _seenArchived = archived; _seenUnarchived = unarchived; _seenSeat = seat; _seenMinute = minute; _seenDoor = hasDoor;
        _rows = Build(recent, scenarios, local, archived, unarchived, seat);
        return _rows;
    }

    private bool Changed(IReadOnlyList<string> recent, string[] scenarios, ExerciseInventoryItem[] local, ExerciseInventoryItem[] archived, ExerciseInventoryItem[] unarchived, Seat seat, long minute, bool hasDoor)
    {
        if (!ReferenceEquals(scenarios, _seenScenarios) || !ReferenceEquals(local, _seenLocal) || !ReferenceEquals(archived, _seenArchived) || !ReferenceEquals(unarchived, _seenUnarchived)) return true;
        if (seat != _seenSeat || minute != _seenMinute || hasDoor != _seenDoor || recent.Count != _seenRecent.Count) return true;
        for (int i = 0; i < recent.Count; i++)
            if (!string.Equals(recent[i], _seenRecent[i], StringComparison.Ordinal)) return true;
        return false;
    }

    private IReadOnlyList<StartSurfaceWindow.StartRecentRow> Build(IReadOnlyList<string> recent, string[] scenarios, ExerciseInventoryItem[] local, ExerciseInventoryItem[] archived, ExerciseInventoryItem[] unarchived, Seat seat)
    {
        var rows = new List<StartSurfaceWindow.StartRecentRow>();
        var door = _choice.Door;
        var now = _now();
        for (int r = 0; r < recent.Count && rows.Count < MaxRows; r++)
        {
            string? name = null;   // the key → the scenario's name: the inventory's names, each through the ONE key function
            for (int s = 0; s < scenarios.Length && name is null; s++)
                if (string.Equals(_keyOf(scenarios[s]), recent[r], StringComparison.Ordinal)) name = scenarios[s];
            if (name is null) continue;   // no longer in the inventory: not shown

            var latest = LatestRun(name, local, archived, unarchived);
            string sub = latest is { } run ? "Live · " + Ago(now - run.StartTimeUtc) : NeverRun;
            string scenario = name;
            Guid? exercise = latest is { } l ? (Guid)l.ExerciseId : null;
            bool author = seat == Seat.Author;
            string? noDoor = door is null ? NoSession : null;

            var live = new StartSurfaceWindow.StartButton("Live", door is not null, () => { if (_choice.OpenAndGoLiveDisabledReason is null) door!.LoadLive(scenario); },
                noDoor, new IconGlyph(Icons.Get("mode/live").Glyph, IconPalette.Red));
            var edit = new StartSurfaceWindow.StartButton("Edit", door is not null && author, () => door!.Open(scenario),
                noDoor ?? (author ? null : NeedsAuthorSeat), new IconGlyph(Icons.Get("mode/edit").Glyph, IconPalette.Pending));
            var aar = new StartSurfaceWindow.StartButton("AAR", door is not null && exercise is not null, () => door!.OpenRecording(exercise!.Value),
                noDoor ?? (exercise is null ? NeverRun : null), new IconGlyph(Icons.Get("mode/replay").Glyph, IconPalette.Blue));
            rows.Add(new StartSurfaceWindow.StartRecentRow(name, sub, live, edit, aar));
        }
        return rows;
    }

    /// <summary>The newest recording of <paramref name="scenario"/> over the inventory's three lists (max <c>StartTimeUtc</c>); null when it never ran.</summary>
    private static ExerciseInventoryItem? LatestRun(string scenario, ExerciseInventoryItem[] a, ExerciseInventoryItem[] b, ExerciseInventoryItem[] c)
    {
        ExerciseInventoryItem? best = null;
        Scan(a); Scan(b); Scan(c);
        return best;
        void Scan(ExerciseInventoryItem[] list)
        {
            for (int i = 0; i < list.Length; i++)
                if (string.Equals(list[i].ScenarioId, scenario, StringComparison.Ordinal) && (best is null || list[i].StartTimeUtc > best.Value.StartTimeUtc)) best = list[i];
        }
    }

    /// <summary>"just now" · "10 min ago" · "3 h ago" · "2 days ago".</summary>
    public static string Ago(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return (int)age.TotalMinutes + " min ago";
        if (age < TimeSpan.FromDays(1)) return (int)age.TotalHours + " h ago";
        int days = (int)age.TotalDays;
        return days + (days == 1 ? " day ago" : " days ago");
    }
}
