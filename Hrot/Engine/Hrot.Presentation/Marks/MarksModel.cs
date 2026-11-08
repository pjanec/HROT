using System;
using System.Collections.Generic;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;

namespace Hrot.UI.Common.Marks;

/// <summary>
/// ⭐ CE-4325 (E7, 📄 docs/UX/DESIGN_Bookmarks_And_Notes.md §3) — ONE row of the Marks list: a MARK (a checkpoint, with its note when it has one) or an AAR NOTE (a time only).
/// </summary>
/// <param name="SimSeconds">Where it sits on the run's sim clock.</param>
/// <param name="WallTicks">The same moment in the recording's wall ticks (what the timeline draws and seeks by); 0 when the sim time cannot be mapped.</param>
/// <param name="Label">A mark's checkpoint label; empty for a time note.</param>
/// <param name="Note">The note on it, if any (a mark may have none — an unnamed mark).</param>
/// <param name="Title">What the row's first line says: the note's name, else the mark's label, else <i>(note)</i>.</param>
/// <param name="TimeText">The time as the list shows it (built once with the row — the window draws it every frame).</param>
public sealed record MarkRow(double SimSeconds, long WallTicks, string Label, ExerciseNote? Note, string Title, string Text, string Unit, long UnitId, string Who, string TimeText)
{
    /// <summary>A checkpoint (restorable); a time note is not.</summary>
    public bool IsMark => Label.Length > 0;
}

/// <summary>⭐ CE-4325 — the Marks list as a pure projection of the session's checkpoints and the exercise's notes (no ImGui): the window draws it, the rails read it.</summary>
public static class MarksModel
{
    /// <summary>The time as the list shows it: <c>m:ss</c>, <c>h:mm:ss</c> from an hour.</summary>
    public static string FormatTime(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        long s = (long)seconds;
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>The time with a tenth, for the popup's title (<i>Marked 12:31.4</i>).</summary>
    public static string FormatTenths(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        long tenths = (long)(seconds * 10);
        long s = tenths / 10;
        return (s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}") + "." + tenths % 10;
    }

    /// <summary>
    /// The label of the next mark: <c>Mark n · m:ss</c>, <c>n</c> counting the marks already there and bumped until the label is free (a recording refuses a duplicate).
    /// </summary>
    public static string NextMarkLabel(IReadOnlyList<CheckpointMark>? existing, double simSeconds)
    {
        int n = (existing?.Count ?? 0) + 1;
        while (true)
        {
            string label = $"Mark {n} · {FormatTime(simSeconds)}";
            bool taken = false;
            if (existing != null) for (int i = 0; i < existing.Count; i++) if (string.Equals(existing[i].Label, label, StringComparison.Ordinal)) { taken = true; break; }
            if (!taken) return label;
            n++;
        }
    }

    /// <summary>
    /// The rows, oldest moment first. Every checkpoint is a row (its note joins it by label); every note on no known checkpoint is a row of its own — a time note at its sim time, or (a mark whose
    /// checkpoint is not in this session's list, e.g. dropped by a branch) at its note's own sim time. <paramref name="search"/> keeps the rows whose title, text, unit or author contains it;
    /// <paramref name="unit"/> keeps one unit's (empty = everyone).
    /// </summary>
    public static List<MarkRow> Rows(IReadOnlyList<CheckpointMark>? checkpoints, ExerciseNote[]? notes, ReplayTimeMap? map, string? search = null, string? unit = null)
    {
        var rows = new List<MarkRow>();
        var used = new HashSet<Guid>();
        if (checkpoints != null)
            for (int i = 0; i < checkpoints.Count; i++)
            {
                var cp = checkpoints[i];
                ExerciseNote? note = null;
                if (notes != null)
                    for (int k = 0; k < notes.Length; k++) if (notes[k].OnMark && string.Equals(notes[k].Mark, cp.Label, StringComparison.Ordinal)) { note = notes[k]; used.Add(note.Id); break; }
                rows.Add(Row(cp.TotalTime, cp.WallTicks, cp.Label, note));
            }
        if (notes != null)
            for (int k = 0; k < notes.Length; k++)
            {
                var n = notes[k];
                if (used.Contains(n.Id)) continue;
                long wall = map is { HasSimTime: true } ? map.WallAt(n.AtSimSeconds) : 0;
                rows.Add(Row(n.AtSimSeconds, wall, "", n));
            }
        rows.Sort((a, b) => a.SimSeconds != b.SimSeconds ? a.SimSeconds.CompareTo(b.SimSeconds) : string.CompareOrdinal(a.Title, b.Title));
        if (!string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(unit)) rows = Filter(rows, search, unit);
        return rows;
    }

    private static MarkRow Row(double sim, long wall, string label, ExerciseNote? note)
    {
        string title = note != null && note.Name.Length > 0 ? note.Name : label.Length > 0 ? label : "(note)";
        return new MarkRow(sim, wall, label, note, title, note?.Text ?? "", note?.SubjectName ?? "", note?.SubjectId ?? 0, note?.Author ?? "", FormatTime(sim));
    }

    private static List<MarkRow> Filter(List<MarkRow> rows, string? search, string? unit)
    {
        var kept = new List<MarkRow>(rows.Count);
        foreach (var r in rows)
        {
            if (!string.IsNullOrWhiteSpace(unit) && !string.Equals(r.Unit, unit, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(search)
                && r.Title.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && r.Text.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && r.Unit.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && r.Who.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            kept.Add(r);
        }
        return kept;
    }

    /// <summary>The unit names the rows mention (for the <i>Everyone</i> combo), in first-seen order, no repeats.</summary>
    public static List<string> Units(IReadOnlyList<MarkRow> rows)
    {
        var units = new List<string>();
        foreach (var r in rows) if (r.Unit.Length > 0 && !units.Contains(r.Unit)) units.Add(r.Unit);
        return units;
    }
}
