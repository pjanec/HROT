using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Marks;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Presentation.Windows;

/// <summary>⭐ CE-4325 (E7) — the whole of what the <see cref="MarksWindow"/> shows, this frame: the rows (one text line each) and the controls' state.</summary>
public sealed record MarksWindowViewModel(string PanelId, string PanelKind, string Search, string Unit, IReadOnlyList<string> Rows, string? EmptyText, string? AddNoteDisabledReason) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ CE-4325 (E7, R-4177, 📄 docs/UX/DESIGN_Bookmarks_And_Notes.md §3, <c>recording-marks-visual.svg</c> ③) — <b>Marks</b>: every ⚑ mark (a checkpoint) and every AAR note of the exercise on show, with its words, unit and author; a search box,
/// an <i>Everyone</i> unit filter, <i>Add note here</i> (a DAR / AAR). Click a row: seek there and select its unit; right-click: <i>Edit note</i> · <i>Restart from here</i> (a ⚑ only) · <i>Delete the note</i>.
/// A thin <see cref="ManagedWindow"/> over <see cref="MarksModel"/> and <see cref="MarksController"/> — ⛔ it keeps no list: the rows are the master's notes and the session's checkpoints.
/// </summary>
public sealed class MarksWindow : ManagedWindow
{
    public const string Kind = "marks";

    private readonly MarksController _marks;
    private readonly Func<ITimelineCursor?> _cursor;
    private readonly Action<long>? _selectUnit;
    private string _search = "", _unit = "";

    // the rows are rebuilt only when their inputs CHANGED (reference compare): the window draws every frame, 0 B steady
    private List<MarkRow> _rows = new();
    private System.Collections.Generic.List<string> _units = new();
    private ExerciseNote[]? _notesFor;
    private IReadOnlyList<CheckpointMark>? _marksFor;
    private ReplayTimeMap? _mapFor;
    private string? _searchFor, _unitFor;

    /// <param name="cursor">The host's timeline cursor — a click on a row seeks through it (null / no seek: the row only selects its unit).</param>
    /// <param name="selectUnit">Selects a unit on the host (its network entity id) — the click's second half.</param>
    public MarksWindow(string id, string perspective, Vector4 titleBarColor, MarksController marks, Func<ITimelineCursor?> cursor, Action<long>? selectUnit = null)
        : base(id, "Marks", perspective, WindowScope.PerspectiveBound)
    {
        _marks = marks ?? throw new ArgumentNullException(nameof(marks));
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _selectUnit = selectUnit;
        TitleBarColor = titleBarColor;
        IsOpen = false;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>The search text; setting it filters the rows.</summary>
    public string Search { get => _search; set => _search = value ?? ""; }

    /// <summary>The unit filter (empty = everyone).</summary>
    public string UnitFilter { get => _unit; set => _unit = value ?? ""; }

    /// <summary>The rows this frame (rebuilt only when the notes, the marks, the map or a filter changed).</summary>
    public IReadOnlyList<MarkRow> Rows
    {
        get
        {
            var notes = _marks.Notes; var marks = _marks.Checkpoints; var map = _marks.TimeMap;
            if (!ReferenceEquals(notes, _notesFor) || !ReferenceEquals(marks, _marksFor) || !ReferenceEquals(map, _mapFor) || _search != _searchFor || _unit != _unitFor)
            {
                _rows = MarksModel.Rows(marks, notes, map, _search, _unit);
                if (_unit.Length == 0 && _search.Length == 0) _units = MarksModel.Units(_rows);   // the combo lists the units of the UNfiltered list
                _notesFor = notes; _marksFor = marks; _mapFor = map; _searchFor = _search; _unitFor = _unit;
            }
            return _rows;
        }
    }

    /// <summary>Why <i>Add note here</i> is greyed (null = it acts): only in a DAR / AAR, and by seat.</summary>
    public string? AddNoteDisabledReason => _marks.SeatRefusal ?? (_marks.InReview ? null : "notes are added at a time in a DAR or an AAR — in Live press B to mark the moment");

    /// <summary>The empty list's words.</summary>
    public string? EmptyText => Rows.Count == 0 ? (_search.Length > 0 || _unit.Length > 0 ? "no mark matches" : "no marks yet — press B in Live to mark a moment") : null;

    internal MarksWindowViewModel SimulateDrawClientArea()
    {
        var rows = Rows;
        var lines = new List<string>(rows.Count);
        foreach (var r in rows) lines.Add($"{(r.IsMark ? "flag" : "note")} {r.TimeText} | {r.Title} | {(r.Text.Length > 0 ? r.Text : "—")} | {(r.Unit.Length > 0 ? r.Unit : "—")} | {r.Who}");
        var vm = new MarksWindowViewModel(Id, Kind, _search, _unit, lines, EmptyText, AddNoteDisabledReason);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>Where the last frame drew each row (screen pixels) — for the frame rails that press one.</summary>
    public IReadOnlyList<(Vector2 Min, Vector2 Max)> LastRowRects => _rowRects;
    private readonly List<(Vector2, Vector2)> _rowRects = new();

    /// <summary>Where the last frame drew <i>Add note here</i>.</summary>
    public (Vector2 Min, Vector2 Max)? LastAddNoteRect { get; private set; }

    /// <summary>A row was pressed: seek to it (where the cursor can) and select its unit.</summary>
    public void Activate(MarkRow row)
    {
        if (_cursor() is { CanSeek: true } c && row.WallTicks != 0) c.Seek(row.WallTicks);
        if (row.UnitId != 0) _selectUnit?.Invoke(row.UnitId);
    }

    protected override void BeforeBegin() => ImGui.SetNextWindowSize(new Vector2(640f, 320f), ImGuiCond.FirstUseEver);

    protected override void DrawClientArea()
    {
        var rows = Rows;
        _marks.WantsMarksWindow = false;

        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##marks_search", "Search marks...", ref _search, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.BeginCombo("##marks_unit", _unit.Length == 0 ? "Everyone" : _unit))
        {
            if (ImGui.Selectable("Everyone", _unit.Length == 0)) _unit = "";
            for (int i = 0; i < _units.Count; i++) if (ImGui.Selectable(_units[i], _unit == _units[i])) _unit = _units[i];
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        var why = AddNoteDisabledReason;
        if (why != null) ImGui.BeginDisabled();
        if (Icons.TryGet("mark/note", out var noteIcon)) { ImGui.PushStyleColor(ImGuiCol.Text, noteIcon.Color); ImGui.TextUnformatted(noteIcon.Glyph); ImGui.PopStyleColor(); ImGui.SameLine(0, 4); }
        if (ImGui.Button("Add note here##marks_add")) _marks.AddNoteHere();
        LastAddNoteRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (why != null) ImGui.EndDisabled();
        if (why != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(why);

        _rowRects.Clear();
        if (rows.Count == 0) { ImGui.TextDisabled(EmptyText ?? ""); return; }
        if (ImGui.BeginTable("##marks_table", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp, new Vector2(0, ImGui.GetContentRegionAvail().Y - 22f)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("time", ImGuiTableColumnFlags.WidthFixed, 64f);
            ImGui.TableSetupColumn("name · note", ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableSetupColumn("unit", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("who", ImGuiTableColumnFlags.WidthStretch, 1.7f);
            ImGui.TableHeadersRow();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.PushID(i);
                bool pressed = ImGui.Selectable("##row", false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap, new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2f));
                _rowRects.Add((ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
                if (pressed) Activate(r);
                if (ImGui.BeginPopupContextItem("##row_menu"))
                {
                    var seat = _marks.SeatRefusal;
                    if (seat != null) ImGui.BeginDisabled();
                    if (ImGui.MenuItem("Edit note")) _marks.Edit(r);
                    if (seat != null) ImGui.EndDisabled();
                    var restart = _marks.RestartRefusal(r);
                    if (restart != null) ImGui.BeginDisabled();
                    if (ImGui.MenuItem("Restart from here")) _marks.Restart(r);
                    if (restart != null) { ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(restart); }
                    if (seat != null || r.Note == null) ImGui.BeginDisabled();
                    if (ImGui.MenuItem("Delete the note")) _marks.Delete(r);
                    if (seat != null || r.Note == null) ImGui.EndDisabled();
                    ImGui.EndPopup();
                }
                ImGui.PopID();
                ImGui.SameLine(0, 0);
                if (Icons.TryGet(r.IsMark ? "mark/flag" : "mark/note", out var g)) { ImGui.PushStyleColor(ImGuiCol.Text, g.Color); ImGui.TextUnformatted(g.Glyph); ImGui.PopStyleColor(); ImGui.SameLine(0, 4); }
                ImGui.TextUnformatted(r.TimeText);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(r.Title);
                ImGui.TextDisabled(r.Text.Length > 0 ? r.Text : "—");
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(r.Unit.Length > 0 ? r.Unit : "—");
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(r.Who);
            }
            ImGui.EndTable();
        }
        ImGui.TextDisabled("click: seek (and select the unit) · right-click: Edit · Restart from here · Delete");
    }
}

/// <summary>
/// ⭐⭐ CE-4325 (E7, R-4177, §3 ②) — <b>the mark popup</b>: <i>B</i> has already taken the moment; this asks only for the OPTIONAL words — name, note, unit (the selected one, changeable) — and closes by itself 4 s after
/// it was last touched. Esc or the timeout keeps an unnamed mark; <i>Done</i> sends the words (the ONE CRUD message). It also edits an existing note and writes an AAR one. A window that shows itself when the controller opens a popup.
/// </summary>
public sealed class MarkPopupWindow : ManagedWindow
{
    public const string Kind = "mark-popup";

    private readonly MarksController _marks;
    private readonly Func<IReadOnlyList<MarkSubject>> _candidates;
    private int _hintSecond = -1;
    private string _hint = "";

    /// <param name="candidates">The units the unit combo offers (the host's current selection); the list is read only while the popup is open.</param>
    public MarkPopupWindow(string id, string perspective, Vector4 titleBarColor, MarksController marks, Func<IReadOnlyList<MarkSubject>> candidates)
        : base(id, "Mark", perspective, WindowScope.PerspectiveBound)
    {
        _marks = marks ?? throw new ArgumentNullException(nameof(marks));
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        TitleBarColor = titleBarColor;
        IsOpen = false;
        _marks.PopupChanged += p => IsOpen = p != null;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    protected override ImGuiWindowFlags ExtraFlags => ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings;

    /// <summary>Where the last frame drew <i>Done</i> (screen pixels) — for a frame rail.</summary>
    public (Vector2 Min, Vector2 Max)? LastDoneRect { get; private set; }

    /// <summary>The name / note / unit fields' rectangles (screen pixels, last frame) — the rail asserts they sit in one column.</summary>
    public (Vector2 Name, Vector2 Note, Vector2 Unit)? LastFieldMins { get; private set; }

    protected override void BeforeBegin()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.WorkPos.X + vp.WorkSize.X * 0.5f, vp.WorkPos.Y + vp.WorkSize.Y - 150f), ImGuiCond.Appearing, new Vector2(0.5f, 1f));
        ImGui.SetNextWindowSize(new Vector2(460f, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.97f);
    }

    protected override void DrawClientArea()
    {
        _marks.Tick();
        var p = _marks.Popup;
        if (p == null) { IsOpen = false; return; }
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)) p.Touched = true;   // looking at it keeps it
        if (Icons.TryGet(p.Label.Length > 0 ? "mark/flag" : "mark/note", out var flag)) { ImGui.PushStyleColor(ImGuiCol.Text, flag.Color); ImGui.TextUnformatted(flag.Glyph); ImGui.PopStyleColor(); ImGui.SameLine(0, 6); }
        ImGui.TextUnformatted(p.Title);
        if (!p.Touched)
        {
            int left = (int)Math.Ceiling(Math.Max(0, MarksController.PopupSeconds - (ImGui.GetTime() - p.OpenedAt)));
            if (left != _hintSecond) { _hintSecond = left; _hint = $"closes by itself in {left} s — the mark stays"; }
            ImGui.SameLine(); ImGui.TextDisabled(_hint);
        }
        ImGui.Separator();

        const float LabelW = 56f;
        ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Name"); ImGui.SameLine(LabelW);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##mk_name", ref p.Name, 120)) p.Touched = true;
        var nameMin = ImGui.GetItemRectMin();
        ImGui.TextUnformatted("Note"); ImGui.SameLine(LabelW);
        if (ImGui.InputTextMultiline("##mk_text", ref p.Text, 1000, new Vector2(-1, 56f))) p.Touched = true;
        var noteMin = ImGui.GetItemRectMin();
        ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Unit"); ImGui.SameLine(LabelW);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("##mk_unit", p.UnitName.Length > 0 ? p.UnitName : "(none)"))
        {
            p.Touched = true;
            if (ImGui.Selectable("(none)", p.UnitId == 0)) _marks.SetPopupUnit(null);
            var c = _candidates();
            for (int i = 0; i < c.Count; i++) if (ImGui.Selectable(c[i].Name, p.UnitId == c[i].Id)) _marks.SetPopupUnit(c[i]);
            ImGui.EndCombo();
        }
        var unitMin = ImGui.GetItemRectMin();
        LastFieldMins = (nameMin, noteMin, unitMin);
        ImGui.SameLine(); ImGui.TextDisabled(p.FreshMark ? "the selected one, pre-filled" : "");

        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 100f);
        if (ImGui.Button("Done##mk_done", new Vector2(88f, 0))) _marks.Commit();
        LastDoneRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape)) _marks.Dismiss();
    }
}
