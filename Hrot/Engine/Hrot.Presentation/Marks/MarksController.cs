using System;
using System.Collections.Generic;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;

namespace Hrot.UI.Common.Marks;

/// <summary>The unit a note is about: its network entity id and its name.</summary>
public readonly record struct MarkSubject(long Id, string Name);

/// <summary>
/// ⭐ CE-4325 (E7, R-4177) — what the mark popup shows and edits: the words of ONE mark or note, before they are sent. <see cref="Label"/> is the mark's checkpoint (empty = an AAR note at <see cref="AtSimSeconds"/>);
/// <see cref="NoteId"/> is the note being edited (empty Guid = a new one).
/// </summary>
public sealed class MarkPopupState
{
    public Guid NoteId;
    public string Label = "";
    public double AtSimSeconds;
    public string Name = "";
    public string Text = "";
    public long UnitId;
    public string UnitName = "";
    /// <summary>The seconds (the host's UI clock) it was opened at — it closes by itself 4 s later while untouched.</summary>
    public double OpenedAt;
    /// <summary>The person typed or changed something: it stays until Done / Esc.</summary>
    public bool Touched;
    /// <summary>Opened by <i>B</i> (the mark is already taken) rather than to edit an existing note / add an AAR one.</summary>
    public bool FreshMark;
    /// <summary>The unit it opened with (a fresh mark: the selection) — Done sends an edit only when the words or this changed.</summary>
    public long InitialUnitId;
    public string Title = "";
}

/// <summary>
/// ⭐⭐ CE-4325 (E7, R-4177, 📄 docs/UX/DESIGN_Bookmarks_And_Notes.md §2) — <b>the ONE client of the marks</b> every host with a timeline shares: <i>B</i> / the timeline corner's ⚑ in Live takes a mark (the existing checkpoint, named here
/// so its note can ride it) and opens the popup; in an AAR / DAR <i>Add note here</i> adds a note at a time; the Marks window edits and deletes. ⭐ Every note change is the ONE CRUD message through the facade
/// (<see cref="ITimeTransportFacade.EditNote"/>); every read is the node's <c>ClusterView</c> (<see cref="ITimeTransportFacade.Notes"/>). ⛔ It keeps no list of its own.
/// </summary>
public sealed class MarksController
{
    /// <summary>How long the popup stays while nobody touches it.</summary>
    public const double PopupSeconds = 4.0;

    private readonly ITimeTransportFacade _facade;
    private readonly Func<Seat> _seat;
    private readonly Func<string> _station;
    private readonly Func<MarkSubject?> _selected;
    private readonly Func<double> _uiClock;

    /// <param name="facade">The host's ONE time facade.</param>
    /// <param name="seat">This station's seat (the Author, Instructor and Observer seats may mark — Q4).</param>
    /// <param name="station">This station's name for the note's author (<i>Instructor-1</i>): the seat and the node.</param>
    /// <param name="selected">The unit selected on this host (Q5: the popup's unit, pre-filled), or null.</param>
    /// <param name="uiClock">Seconds on the UI's clock (the popup's 4 s).</param>
    public MarksController(ITimeTransportFacade facade, Func<Seat> seat, Func<string> station, Func<MarkSubject?> selected, Func<double> uiClock)
    {
        _facade = facade ?? throw new ArgumentNullException(nameof(facade));
        _seat = seat ?? throw new ArgumentNullException(nameof(seat));
        _station = station ?? throw new ArgumentNullException(nameof(station));
        _selected = selected ?? throw new ArgumentNullException(nameof(selected));
        _uiClock = uiClock ?? throw new ArgumentNullException(nameof(uiClock));
    }

    private MarkPopupState? _popup;

    /// <summary>The popup being shown; null = none.</summary>
    public MarkPopupState? Popup { get => _popup; private set { _popup = value; PopupChanged?.Invoke(value); } }

    /// <summary>Raised when the popup opens (with its state) or closes (null) — the popup window shows itself from it.</summary>
    public event Action<MarkPopupState?>? PopupChanged;

    /// <summary>The marks window's open request: set by <i>Add note here</i> / a row's edit so the host opens it (the window reads and clears it).</summary>
    public bool WantsMarksWindow { get; set; }

    // ── who and when ─────────────────────────────────────────────────────────────────────────

    /// <summary>The seat's refusal (null = it may mark and write notes): the Author, Instructor and Observer seats may (Seats §2.2, Q4).</summary>
    public string? SeatRefusal
    {
        get
        {
            if (_facade.NotesReadOnlyReason is { } readOnly) return readOnly;   // ⭐ CE-1467 (a) — a host with no master shows the notes read-only
            var s = _seat();
            if (s is Seat.Author or Seat.Instructor or Seat.Observer) return null;
            if (s != _refusalSeat || _refusal == null) { _refusal = $"a {s} seat cannot add marks or notes (the Author, Instructor and Observer seats can)"; _refusalSeat = s; }   // cached: the corner reads it every frame
            return _refusal;
        }
    }
    private Seat _refusalSeat = (Seat)255;
    private string? _refusal;

    /// <summary>The mode where ⚑ takes a MARK now: a recorded Live run.</summary>
    public bool InLive => _facade.Mode == TransportMode.Live;

    /// <summary>The modes where a note is added AT A TIME: a DAR and an AAR.</summary>
    public bool InReview => _facade.Mode is TransportMode.Review or TransportMode.Replay;

    /// <summary>Whether the host shows marks at all (a mode where the exercise has a timeline: Live, DAR, AAR).</summary>
    public bool Active => InLive || InReview;

    /// <summary>
    /// Why B / the ⚑ is greyed now (null = it acts): the seat · an unrecorded Live (<i>"no recording — nothing to mark"</i>, D14 / R-4168) · anywhere that is neither a run nor a recording. In an AAR / DAR the ⚑ is <i>Add note here</i> and is never greyed by the recording.
    /// </summary>
    public string? MarkDisabledReason
    {
        get
        {
            if (SeatRefusal is { } seat) return seat;
            if (InLive)
            {
                var choice = _facade.RunChoice;
                if (choice != null && !choice.RunIsRecorded) return "no recording — nothing to mark";
                return choice?.CheckpointDisabledReason;
            }
            return InReview ? null : "marks are taken in a run or added in its review";
        }
    }

    /// <summary>The ⚑'s plain tooltip by mode (the host's binding appends the reason).</summary>
    public string MarkTip => InReview ? "Add a note here (at the playhead)" : "Mark this moment (B)";

    // ── taking a mark / adding a note ────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>B</b> in Live: the moment is taken NOW (a checkpoint with an automatic label — the existing op), and the popup opens for the optional words. Ignoring the popup keeps an unnamed mark.
    /// In an AAR / DAR the same key adds a note at the playhead. Does nothing (and says why on the toast) when <see cref="MarkDisabledReason"/> says so.
    /// </summary>
    public bool Mark()
    {
        if (MarkDisabledReason is { } why) { _facade.RunChoice?.Announce($"No mark: {why}"); return false; }
        if (InReview) return AddNoteHere();
        double now = _facade.TotalTime;
        string label = MarksModel.NextMarkLabel(_facade.Session?.Checkpoints, now);
        var subject = _selected();
        // ⭐ the mark's RECORD goes out WITH it (one message): who took it and (Q5) the selected unit — an unnamed mark still lists its author (the mockup's third row); the popup's words EDIT this note.
        //   ⭐ CE-1467 (e) — the master adds the record under the label it MINTS (unique: two stations pressing B together asked for the same one); an edit keeps that label (matched by id)
        var note = new ExerciseNote(Guid.NewGuid(), label, now, "", "", _station(), subject?.Id ?? 0, subject?.Name ?? "");
        _facade.TakeMark(label, note);
        Popup = new MarkPopupState
        {
            NoteId = note.Id, Label = label, AtSimSeconds = now, UnitId = note.SubjectId, UnitName = note.SubjectName, InitialUnitId = note.SubjectId,
            OpenedAt = _uiClock(), FreshMark = true, Title = "Marked " + MarksModel.FormatTenths(now),
        };
        _facade.RunChoice?.Announce("Mark taken");
        return true;
    }

    /// <summary>⭐ <i>Add note here</i> (AAR / DAR): a note at the playhead's sim time — a time only, not restorable (R-4168). Opens the popup for its words.</summary>
    public bool AddNoteHere()
    {
        if (SeatRefusal is { } why) { _facade.RunChoice?.Announce($"No note: {why}"); return false; }
        if (!InReview) return false;
        double at = _facade.TotalTime;
        var subject = _selected();
        Popup = new MarkPopupState { AtSimSeconds = at, UnitId = subject?.Id ?? 0, UnitName = subject?.Name ?? "", OpenedAt = _uiClock(), Touched = true, Title = "Note at " + MarksModel.FormatTime(at) };
        return true;
    }

    /// <summary>Opens the popup on an existing mark's note (or a mark that has none yet) to edit its words.</summary>
    public bool Edit(MarkRow row)
    {
        if (SeatRefusal != null) return false;
        var n = row.Note;
        Popup = new MarkPopupState
        {
            NoteId = n?.Id ?? Guid.Empty, Label = row.Label, AtSimSeconds = row.SimSeconds, Name = n?.Name ?? "", Text = n?.Text ?? "",
            UnitId = n?.SubjectId ?? 0, UnitName = n?.SubjectName ?? "", OpenedAt = _uiClock(), Touched = true,
            Title = (row.IsMark ? "Mark " : "Note ") + MarksModel.FormatTime(row.SimSeconds),
        };
        return true;
    }

    /// <summary>Changes the popup's unit (a selection, or none).</summary>
    public void SetPopupUnit(MarkSubject? unit)
    {
        if (Popup == null) return;
        Popup.UnitId = unit?.Id ?? 0; Popup.UnitName = unit?.Name ?? ""; Popup.Touched = true;
    }

    /// <summary>
    /// <b>Done</b>: sends the words — an Edit of the mark's record (B already sent it), an Add for an AAR note, an Edit for an existing note — and closes. ⭐ A fresh mark with nothing typed and no unit change sends nothing.
    /// </summary>
    public void Commit()
    {
        var p = Popup;
        if (p == null) return;
        Popup = null;
        bool changed = p.Name.Length > 0 || p.Text.Length > 0 || p.UnitId != p.InitialUnitId;
        if (p.FreshMark && !changed) return;   // the mark's record is already out (B sent it): nothing to add
        var note = new ExerciseNote(p.NoteId == Guid.Empty ? Guid.NewGuid() : p.NoteId, p.Label, p.AtSimSeconds, p.Name, p.Text, _station(), p.UnitId, p.UnitName);
        _facade.EditNote(p.NoteId == Guid.Empty ? NoteEditKind.Add : NoteEditKind.Edit, note);
    }

    /// <summary>Esc / the timeout: the popup closes, the mark stays (an unnamed one).</summary>
    public void Dismiss() => Popup = null;

    /// <summary>The popup's own clock: closes it after <see cref="PopupSeconds"/> while it was never touched. Call once per frame.</summary>
    public void Tick()
    {
        if (Popup is { Touched: false } p && _uiClock() - p.OpenedAt >= PopupSeconds) Popup = null;
    }

    /// <summary>Deletes a note (never its mark: the checkpoint stays).</summary>
    public bool Delete(MarkRow row)
    {
        if (SeatRefusal != null || row.Note == null) return false;
        _facade.EditNote(NoteEditKind.Delete, row.Note);
        return true;
    }

    /// <summary>Why <i>Restart from here</i> is not offered on <paramref name="row"/> (null = it is): only a ⚑ (a checkpoint) restores, only where the host has the run choice's restore.</summary>
    public string? RestartRefusal(MarkRow row)
        => !row.IsMark ? "an AAR note has no state to restart from (R-4168)" : _facade.RunChoice?.RestoreCheckpoint is null ? "restart is offered on a host that runs scenarios" : InReview ? "go back to live first" : null;

    /// <summary>Restarts from the mark (the run choice's restore). False when <see cref="RestartRefusal"/> says no.</summary>
    public bool Restart(MarkRow row)
    {
        if (RestartRefusal(row) != null) return false;
        _facade.RunChoice!.RestoreCheckpoint!.Invoke(row.Label);
        return true;
    }

    // ── the list ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The notes the master last published.</summary>
    public ExerciseNote[] Notes => _facade.Notes;

    /// <summary>The session's checkpoints (the marks): the master's list in Live (the inactive session carries them), the recording's in a review / replay.</summary>
    public IReadOnlyList<CheckpointMark> Checkpoints => _facade.Session?.Checkpoints ?? (IReadOnlyList<CheckpointMark>)Array.Empty<CheckpointMark>();

    /// <summary>The session's wall ↔ sim map (an AAR note's moment on the timeline), or null.</summary>
    public ReplayTimeMap? TimeMap => _facade.Session is { Active: true } s ? s.TimeMap : null;
}
