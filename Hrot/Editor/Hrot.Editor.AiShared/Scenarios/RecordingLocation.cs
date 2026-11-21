using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐⭐ CE-4330 ui ⑤ (docs/DESIGN_Recording_Storage.md §7.3, §8 ⑤; mockup <c>docs/UX/recording-storage-visual.svg</c>) — WHERE a recording is, as the
/// picker row's chips and hint, from the replicated facts every station reads: the inventory item's NAS state (fact ②), the master's storage
/// jobs (fact ①) and this node's own local recordings (fact ③). ⭐ Pure — a rail runs it with no ImGui.
/// <list type="bullet">
///   <item><i>on the nodes</i> (amber) — not archived yet: the only copy, never removed by itself (§6)</item>
///   <item><i>NAS</i> (grey) — the archive is complete; <i>cached here</i> (green) when this node also holds it</item>
///   <item><i>NAS incomplete N / M</i> (amber) — an archive cut part-way (the copy on the nodes stays)</item>
///   <item>an active job on it: <i>archiving N / M</i> · <i>importing — job #…</i> · <i>deleting</i></item>
/// </list>
/// The hint says what OPENING it does: <i>opens at once</i> (a copy is on the nodes or here) · <i>joins the import</i> · <i>imports first</i>.
/// </summary>
public static class RecordingLocation
{
    /// <summary>The chips of <paramref name="item"/>'s row.</summary>
    public static IReadOnlyList<PickerChip> Chips(ExerciseInventoryItem item, StorageJob? job, bool holdsHere)
    {
        var chips = new List<PickerChip>(3);
        switch (item.NasState)
        {
            case ArchiveNasState.Complete:
                chips.Add(new PickerChip("storage/nas", "NAS"));
                if (holdsHere) chips.Add(new PickerChip("storage/cached", "cached here"));
                break;
            case ArchiveNasState.Incomplete:
                chips.Add(new PickerChip("storage/incomplete", item.NasFilesTotal > 0 ? $"NAS incomplete {item.NasFiles} / {item.NasFilesTotal}" : "NAS incomplete"));
                chips.Add(new PickerChip("storage/onnodes", "on the nodes"));
                break;
            default:
                chips.Add(new PickerChip("storage/onnodes", "on the nodes"));
                break;
        }
        if (job is { IsActive: true } j)
            chips.Add(j.Kind switch
            {
                StorageJobKind.Archive => new PickerChip("storage/archive", j.FilesTotal > 0 ? $"archiving {j.FilesDone} / {j.FilesTotal}" : "archiving"),
                StorageJobKind.Import => new PickerChip("storage/import", $"importing — job {Short(j.JobId)}"),
                _ => new PickerChip("storage/delete", "deleting"),
            });
        return chips;
    }

    /// <summary>What opening <paramref name="item"/>'s row does.</summary>
    public static string Hint(ExerciseInventoryItem item, StorageJob? job, bool holdsHere)
    {
        if (job is { IsActive: true, Kind: StorageJobKind.Import }) return "joins the import";
        if (item.NasState == ArchiveNasState.Complete && !holdsHere) return "imports first";
        return "opens at once";
    }

    /// <summary>⭐ CE-4330 ④ — whether opening it IMPORTS first: an import of it is running (join it), or the NAS holds it and this station does not.</summary>
    public static bool NeedsImport(ExerciseInventoryItem item, StorageJob? job, bool holdsHere)
        => job is { IsActive: true, Kind: StorageJobKind.Import } || (item.NasState == ArchiveNasState.Complete && !holdsHere);

    /// <summary>The ACTIVE job on <paramref name="exercise"/>, else null (the master keeps one job per exercise, §7.2).</summary>
    public static StorageJob? JobOn(IReadOnlyList<StorageJob>? jobs, ExerciseId exercise)
    {
        if (jobs is null) return null;
        for (int i = 0; i < jobs.Count; i++)
            if (jobs[i].IsActive && jobs[i].ExerciseId == exercise) return jobs[i];
        return null;
    }

    /// <summary>Whether this node's local recordings (fact ③, <c>"&lt;exercise&gt;:&lt;bytes&gt;"</c>) hold <paramref name="exercise"/>.</summary>
    public static bool Holds(IReadOnlyList<string>? local, Guid exercise)
    {
        if (local is null) return false;
        for (int i = 0; i < local.Count; i++)
        {
            var s = local[i];
            int colon = s.IndexOf(':');
            if (Guid.TryParse(colon < 0 ? s : s.Substring(0, colon), out var id) && id == exercise) return true;
        }
        return false;
    }

    /// <summary>A job's short name, as the master's own messages name an exercise: its first 8 hex digits.</summary>
    public static string Short(Guid id) => "#" + id.ToString("N").Substring(0, 8);
}
