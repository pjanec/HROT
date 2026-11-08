using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Storage;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐⭐ CE-4330 ui half (📄 docs/DESIGN_Recording_Storage.md §7.3, §8 ① ②; mockup <c>docs/UX/recording-storage-visual.svg</c>) — <b>the status bar's transfer segment and the Transfers popover</b>. The segment sits at the bar's RIGHT end, LEFT of the seat · hat text
/// (⛔ never at the left end: the Review source segment owns that): the running job with its icon, words, a progress bar, <i>(yours)</i> when this station asked, <i>+N queued</i>, the clock mark while a Live runs, and — in the two ended states — a green
/// <i>Exercise 5 ready</i> + <i>Open</i> or an amber <i>Archive of exercise 2 incomplete — 3 of 4 files</i> + <i>Resume</i>. A click on it opens the popover: every job with its state and <i>Cancel</i> / <i>Resume</i> (the Author and Instructor seats; greyed with the reason elsewhere, Q7),
/// the master's refusal of the last ask shown as the reason. Composed ONCE per process by <c>LocalWindowController.ComposeRunControls</c> beside the seat section, so every host shows the SAME job (the replicated fact ①).
/// ⭐ HOT PATH: the segment is drawn every frame — its model and every string are cached until the job list changes (0 B steady); nothing draws while nothing runs.
/// </summary>
public sealed class StorageStatusBarSection
{
    /// <summary>The status-bar section id.</summary>
    public const string SectionId = "storage_transfers";

    /// <summary>Just before the seat · hat text (200): it is right-aligned and the segment reserves its width.</summary>
    public const int SortOrder = 190;

    /// <summary>The popover's ImGui id.</summary>
    public const string PopupId = "##storage_transfers_popup";

    private const float BarWidth = 90f, BarHeight = 6f, Gap = 14f;
    private static readonly Vector4 Muted = new(0.62f, 0.62f, 0.66f, 1f), Red = new(1f, 0.38f, 0.38f, 1f), Blue = new(0.35f, 0.65f, 1f, 1f);

    private readonly StorageStatus _status;
    private readonly StorageTransfers _transfers;
    private readonly Func<float> _reserveRight;
    private bool _openRequested;
    private float _measured;   // the segment's real width on the last frame it drew (0 = not yet)

    // cached per job-array reference
    private StorageJob[]? _rowsFor;
    private Row[] _rows = Array.Empty<Row>();
    private int _queuedFor = -1;
    private string _queuedText = "";

    /// <summary>One row of the Transfers list: the icon key, the title, the state line, and the job (for the buttons).</summary>
    public readonly record struct Row(string IconKey, string Title, string Line, StorageJob Job);

    /// <param name="reserveRight">The width the seat · hat text takes at the bar's right end (+ its gap): the segment ends left of it. 0 where there is no seat section.</param>
    public StorageStatusBarSection(StorageStatus status, StorageTransfers transfers, Func<float>? reserveRight = null)
    {
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        _reserveRight = reserveRight ?? (() => 0f);
    }

    /// <summary>Registers the segment on <paramref name="bar"/> for EVERY perspective (a transfer is the cluster's, not a perspective's).</summary>
    public static StorageStatusBarSection Register(StatusBarManager bar, StorageStatus status, StorageTransfers transfers, Func<float>? reserveRight = null)
    {
        var section = new StorageStatusBarSection(status, transfers, reserveRight);
        bar.RegisterSection(SectionId, SortOrder, section.Render);
        return section;
    }

    /// <summary>The segment now (the model's).</summary>
    public StorageSegment Segment => _status.Segment;

    /// <summary>The Transfers list's rows as of the last job-list change.</summary>
    public IReadOnlyList<Row> Rows { get { Refresh(); return _rows; } }

    /// <summary>Asks for the popover (the click's consequence, railable without a mouse).</summary>
    public void Open() => _openRequested = true;

    /// <summary>Whether the popover was drawn on the last frame.</summary>
    public bool PopoverDrawn { get; private set; }

    /// <summary>The segment's rectangle on the last frame (screen pixels; default = nothing drawn).</summary>
    public (Vector2 Min, Vector2 Max) SegmentRect { get; private set; }

    /// <summary>The segment's button (Open / Resume), last frame.</summary>
    public (Vector2 Min, Vector2 Max)? ActionRect { get; private set; }

    /// <summary>The "+N queued" text's rectangle, last frame.</summary>
    public (Vector2 Min, Vector2 Max)? QueuedRect { get; private set; }

    /// <summary>The popover rows' Cancel / Resume buttons, last frame (screen pixels, in row order).</summary>
    public IReadOnlyList<(Vector2 Min, Vector2 Max)> RowButtonRects => _rowButtons;
    private readonly List<(Vector2, Vector2)> _rowButtons = new();

    // ── the rows ─────────────────────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var jobs = _transfers.Jobs;
        if (ReferenceEquals(jobs, _rowsFor)) return;
        _rowsFor = jobs;
        string me = _transfers.Station;
        var rows = new Row[jobs.Length];
        for (int i = 0; i < jobs.Length; i++)
        {
            var j = jobs[i];
            string icon = j.State == StorageJobState.Failed ? "storage/incomplete" : j.Kind switch { StorageJobKind.Archive => "storage/archive", StorageJobKind.Import => "storage/import", _ => "storage/delete" };
            string by = StorageStatus.IsYours(j, me) ? "asked by this station" : j.AskedBy.Length > 0 ? "asked by " + j.AskedBy : "";
            string line = j.State switch
            {
                StorageJobState.Running => $"running · {j.FilesDone} of {j.FilesTotal} files · {StorageStatus.BytesPair(j.BytesDone, j.BytesTotal)}" + (j.GivingWay ? " · slowed while Live runs" : ""),
                StorageJobState.Queued => "queued — waits its turn",
                StorageJobState.WaitingForNode => $"waiting for {(j.WaitingFor > 0 ? j.WaitingFor + " node(s)" : "a node")}" + (string.IsNullOrEmpty(j.Reason) ? "" : " · " + j.Reason),
                StorageJobState.Done => $"done · {j.FilesTotal} files · {StorageStatus.Bytes(j.BytesTotal)}",
                StorageJobState.Failed => $"incomplete — {j.FilesDone} of {j.FilesTotal} files" + (string.IsNullOrEmpty(j.Reason) ? "" : " · " + j.Reason),
                _ => "cancelled",
            };
            rows[i] = new Row(icon, $"{StorageStatus.Noun(j.Kind)} · exercise {StorageStatus.Short(j)}", by.Length > 0 && j.State is not (StorageJobState.Failed or StorageJobState.Cancelled) ? line + " · " + by : line, j);
        }
        _rows = rows;
    }

    // ── the frame ────────────────────────────────────────────────────────────────────────────

    private static void Glyph(string key)
    {
        if (!Icons.TryGet(key, out var g)) return;
        ImGui.PushStyleColor(ImGuiCol.Text, g.Color);
        ImGui.TextUnformatted(g.Glyph);
        ImGui.PopStyleColor();
        ImGui.SameLine(0, 5);
    }

    /// <summary>Called each frame by the <see cref="StatusBarManager"/>, inside an active ImGui frame and the status-bar window.</summary>
    public void Render()
    {
        var seg = _status.Segment;
        ActionRect = null; QueuedRect = null; SegmentRect = default;
        if (seg.State == StorageSegmentState.Idle) { _measured = 0f; DrawPopover(default); return; }

        string iconKey = seg.State switch
        {
            StorageSegmentState.Ready => "storage/ready",
            StorageSegmentState.Incomplete => "storage/incomplete",
            _ => seg.Kind switch { StorageJobKind.Archive => "storage/archive", StorageJobKind.Import => "storage/import", _ => "storage/delete" },
        };
        if (seg.Queued != _queuedFor) { _queuedFor = seg.Queued; _queuedText = seg.Queued > 0 ? "+" + seg.Queued + " queued" : ""; }
        bool hasBar = seg.State == StorageSegmentState.Active;
        bool hasButton = seg.Action.Length > 0;
        var style = ImGui.GetStyle();
        float width = 24f + ImGui.CalcTextSize(seg.Text).X + (hasBar ? BarWidth + 10f : 0f) + (seg.Yours && seg.State == StorageSegmentState.Active ? ImGui.CalcTextSize("(yours)").X + 10f : 0f)
                    + (_queuedText.Length > 0 ? ImGui.CalcTextSize(_queuedText).X + 10f : 0f) + (seg.GivingWay ? 24f : 0f) + (hasButton ? ImGui.CalcTextSize(seg.Action).X + 26f : 0f);
        if (_measured > 0f) width = _measured;   // the width the segment REALLY took last frame (an estimate of every item's spacing is always a little short); the first frame of a new state draws at the estimate and settles the next
        float x = ImGui.GetWindowWidth() - style.WindowPadding.X - _reserveRight() - width - Gap;
        if (x > ImGui.GetCursorPosX()) ImGui.SetCursorPosX(x);   // right-aligned, left of the seat text; never over the sections before it

        var start = ImGui.GetCursorScreenPos();
        Glyph(iconKey);
        ImGui.TextUnformatted(seg.Text);
        bool textHovered = ImGui.IsItemHovered();
        bool textClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        if (hasBar)
        {
            ImGui.SameLine(0, 8);
            float y = ImGui.GetCursorPosY() + (ImGui.GetTextLineHeight() - BarHeight) * 0.5f;
            ImGui.SetCursorPosY(y);
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Blue);
            ImGui.ProgressBar(seg.Progress >= 0 ? seg.Progress : -1f * (float)ImGui.GetTime(), new Vector2(BarWidth, BarHeight), "");
            ImGui.PopStyleColor();
            ImGui.SetCursorPosY(y - (ImGui.GetTextLineHeight() - BarHeight) * 0.5f);
        }
        if (seg.GivingWay) { ImGui.SameLine(0, 8); Glyph("storage/giveway"); if (ImGui.IsItemHovered()) ImGui.SetTooltip("slowed while a Live runs — it speeds up again after"); ImGui.SameLine(0, 0); }
        if (seg.Yours && seg.State == StorageSegmentState.Active) { ImGui.SameLine(0, 8); ImGui.TextColored(Blue, "(yours)"); }
        if (_queuedText.Length > 0) { ImGui.SameLine(0, 10); ImGui.TextDisabled(_queuedText); QueuedRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); }
        if (hasButton)
        {
            ImGui.SameLine(0, 10);
            string? why = seg.State == StorageSegmentState.Incomplete ? _transfers.SeatRefusal : (_transfers.CanOpen ? null : "this host opens no recording");
            if (why != null) ImGui.BeginDisabled();
            if (ImGui.SmallButton(seg.State == StorageSegmentState.Incomplete ? "Resume##stor_act" : "Open##stor_act"))
            {
                if (seg.State == StorageSegmentState.Incomplete) { foreach (var j in _transfers.Jobs) if (j.JobId == seg.JobId) { _transfers.Resume(j); break; } }
                else _transfers.Open(seg.ExerciseId);
            }
            ActionRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (why != null) { ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(why); }
        }
        var end = ImGui.GetItemRectMax();
        SegmentRect = (start, new Vector2(end.X, Math.Max(end.Y, start.Y + ImGui.GetTextLineHeight())));
        _measured = end.X - start.X;
        if (textHovered) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip("Transfers — click for the list (cancel, resume)"); }
        if (textClicked) _openRequested = true;
        DrawPopover(start);
    }

    private void DrawPopover(Vector2 anchor)
    {
        // the popover can also be opened from the list of ended jobs while the segment is idle (nothing draws the segment then; Open() is the rail / menu path)
        if (_openRequested) { ImGui.OpenPopup(PopupId); _openRequested = false; }
        PopoverDrawn = false;
        _rowButtons.Clear();
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.WorkPos.X + vp.WorkSize.X - 12f, vp.WorkPos.Y + vp.WorkSize.Y - ImGui.GetFrameHeight() - 8f), ImGuiCond.Always, new Vector2(1f, 1f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(480f, 0f), new Vector2(620f, 480f));
        if (!ImGui.BeginPopup(PopupId)) return;
        PopoverDrawn = true;
        ImGui.TextDisabled("Transfers — one at a time, the rest wait in order");
        if (_transfers.LastRefusal is { } refusal) ImGui.TextColored(Red, refusal);
        ImGui.Separator();
        var rows = Rows;
        if (rows.Count == 0) ImGui.TextDisabled("no transfers");
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ImGui.PushID(i);
            Glyph(r.IconKey);
            ImGui.BeginGroup();
            ImGui.TextUnformatted(r.Title);
            ImGui.TextDisabled(r.Line);
            ImGui.EndGroup();
            float bw = 84f;
            ImGui.SameLine(ImGui.GetWindowWidth() - bw - ImGui.GetStyle().WindowPadding.X);
            bool active = r.Job.IsActive;
            bool resumable = _transfers.ResumeRefusal(r.Job) == null || (r.Job.Kind == StorageJobKind.Archive && r.Job.State is StorageJobState.Failed or StorageJobState.Cancelled);
            if (active || resumable)
            {
                string? why = active ? _transfers.CancelRefusal(r.Job) : _transfers.ResumeRefusal(r.Job);
                if (why != null) ImGui.BeginDisabled();
                if (active)
                {
                    if (ImGui.Button("Cancel##row", new Vector2(bw, 0))) _transfers.Cancel(r.Job);
                }
                else if (ImGui.Button("Resume##row", new Vector2(bw, 0))) _transfers.Resume(r.Job);
                var rmin = ImGui.GetItemRectMin(); var rmax = ImGui.GetItemRectMax();
                _rowButtons.Add((rmin, rmax));
                if (Icons.TryGet(active ? "storage/cancel" : "storage/resume", out var bi))   // the mockup's coloured glyph at the button's left (a button's text is one colour)
                    ImGui.GetWindowDrawList().AddText(new Vector2(rmin.X + 9f, rmin.Y + (rmax.Y - rmin.Y - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(bi.Color), bi.Glyph);
                if (why != null) { ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(why); }
            }
            ImGui.PopID();
        }
        ImGui.Separator();
        ImGui.TextDisabled("Cancel / Resume: the Author and Instructor seats — greyed on another seat: \"" + StorageTransfers.SeatRefusalText + "\"");
        ImGui.EndPopup();
    }
}
