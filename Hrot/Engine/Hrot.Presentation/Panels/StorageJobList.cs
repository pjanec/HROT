using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Storage;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐⭐ CE-4330 ui half (📄 docs/DESIGN_Recording_Storage.md §7.3, §8 ②) — <b>the Transfers list</b>: every job of the master's queue (fact ①, active first, then
/// the newest ended) with its icon, title, state line and <i>Cancel</i> (queued / running) or <i>Resume</i> (a failed / cancelled archive) — the Author and
/// Instructor seats only, greyed with the reason elsewhere (Q7) — and the master's refusal of the last ask. ⭐ ONE view, drawn by the status bar's popover
/// (<see cref="StorageStatusBarSection"/>) and the orchestrator panel's archive section, over the same <see cref="StorageTransfers"/>; the rows are rebuilt
/// only when the job array changes.
/// </summary>
public sealed class StorageJobList
{
    private static readonly Vector4 Red = new(1f, 0.38f, 0.38f, 1f);

    private readonly StorageTransfers _transfers;
    private StorageJob[]? _rowsFor;
    private Row[] _rows = Array.Empty<Row>();
    private readonly List<(Vector2, Vector2)> _rowButtons = new();

    /// <summary>One row of the Transfers list: the icon key, the title, the state line, and the job (for the buttons).</summary>
    public readonly record struct Row(string IconKey, string Title, string Line, StorageJob Job);

    public StorageJobList(StorageTransfers transfers) => _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));

    /// <summary>The rows as of the last job-list change.</summary>
    public IReadOnlyList<Row> Rows { get { Refresh(); return _rows; } }

    /// <summary>The rows' Cancel / Resume buttons, last frame (screen pixels, in row order).</summary>
    public IReadOnlyList<(Vector2 Min, Vector2 Max)> RowButtonRects => _rowButtons;

    /// <summary>A key's glyph in its colour, then a small gap (the colour belongs to the key, R-174).</summary>
    internal static void Glyph(string key)
    {
        if (!Icons.TryGet(key, out var g)) return;
        ImGui.PushStyleColor(ImGuiCol.Text, g.Color);
        ImGui.TextUnformatted(g.Glyph);
        ImGui.PopStyleColor();
        ImGui.SameLine(0, 5);
    }

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

    /// <summary>Draws the list in the current window: the header, the refusal, every row with its button, the seat rule.</summary>
    public void Draw()
    {
        _rowButtons.Clear();
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
    }
}
