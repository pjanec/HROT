using System;
using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;

namespace Hrot.UI.Common.Storage;

/// <summary>The status bar segment's states (📄 docs/UX/recording-storage-visual.svg ①): nothing to say · a transfer in progress (queued, running, waiting, yours or another station's, slowed for a Live) · an import that just finished · an archive that stopped part-way.</summary>
public enum StorageSegmentState : byte { Idle, Active, Ready, Incomplete }

/// <summary>
/// ⭐ CE-4330 ui half (📄 docs/DESIGN_Recording_Storage.md §7.3, §8 ①) — what the status bar's transfer segment SAYS this frame, a pure value derived from the master's job list (fact ①, <c>ClusterView.StorageJobs</c>).
/// <see cref="Progress"/> is 0…1, or −1 when nothing says how far it is. <see cref="Action"/> is the segment's button (<i>Open</i> on a finished import, <i>Resume</i> on an incomplete archive), empty = none.
/// </summary>
public sealed record StorageSegment(StorageSegmentState State, string Text, float Progress, bool Yours, int Queued, bool GivingWay, StorageJobKind Kind, Guid JobId, Guid ExerciseId, string Action)
{
    /// <summary>Nothing to show: the segment draws nothing.</summary>
    public static readonly StorageSegment None = new(StorageSegmentState.Idle, "", -1f, false, 0, false, StorageJobKind.Archive, Guid.Empty, Guid.Empty, "");
}

/// <summary>
/// ⭐⭐ CE-4330 ui half (§7.3, §8 ①) — <b>the status bar's transfer segment as ONE model</b>: the head job (running, else waiting for a node, else the first queued) with its numbers and whether this station asked for it; <i>+N queued</i>; the clock mark while a Live runs
/// (the transfer gives way, Q5); and the two ended states the mockup draws — an IMPORT that just finished (<i>Exercise 5 ready</i> + <i>Open</i>, for <see cref="ReadySeconds"/>) and an ARCHIVE that stopped part-way (<i>incomplete — 3 of 4 files</i> + <i>Resume</i>, until a later job for
/// that exercise supersedes it). ⛔ It keeps no list of its own: the jobs are the master's. ⭐ HOT PATH: the segment (and its strings) is rebuilt only when the job array CHANGED (the master republishes a new array per change, the numbers at most 4×/s) or the ready window closed — 0 B per steady frame.
/// </summary>
public sealed class StorageStatus
{
    /// <summary>How long a finished import stays on the bar (the job list keeps it longer: the newest 8 ended ones).</summary>
    public const double ReadySeconds = 20.0;

    private readonly Func<StorageJob[]> _jobs;
    private readonly Func<string> _station;
    private readonly Func<double> _clock;
    private readonly Dictionary<Guid, double> _firstSeenDone = new();
    private StorageJob[]? _built;
    private StorageSegment _segment = StorageSegment.None;
    private double _readyUntil = double.PositiveInfinity;
    private bool _primed;

    /// <param name="jobs">The cluster's jobs as this host's <c>ClusterView</c> last heard them.</param>
    /// <param name="station">This station's name — a job it asked for is marked <i>(yours)</i> (<c>StorageJob.AskedBy</c>).</param>
    /// <param name="clock">Seconds on the UI's clock (the ready window).</param>
    public StorageStatus(Func<StorageJob[]> jobs, Func<string> station, Func<double> clock)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _station = station ?? throw new ArgumentNullException(nameof(station));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>The segment now (rebuilt only when the job list or the ready window changed).</summary>
    public StorageSegment Segment
    {
        get
        {
            var jobs = _jobs();
            double now = _clock();
            if (!ReferenceEquals(jobs, _built) || now >= _readyUntil) Rebuild(jobs, now);
            return _segment;
        }
    }

    private void Rebuild(StorageJob[] jobs, double now)
    {
        _built = jobs;
        // a Done import is "new" the first time THIS client sees it; the jobs already ended when the client started are history (a late joiner never flashes an old import as ready)
        for (int i = 0; i < jobs.Length; i++)
            if (!jobs[i].IsActive && !_firstSeenDone.ContainsKey(jobs[i].JobId)) _firstSeenDone[jobs[i].JobId] = _primed ? now : double.NegativeInfinity;
        _primed = true;
        _readyUntil = double.PositiveInfinity;
        _segment = Compute(jobs, now);
    }

    private StorageSegment Compute(StorageJob[] jobs, double now)
    {
        string station = _station();
        // the head: the running job, else one waiting for a node, else the first queued (the master runs ONE at a time, so there is at most one running)
        int head = -1, queued = 0;
        foreach (var pref in new[] { StorageJobState.Running, StorageJobState.WaitingForNode, StorageJobState.Queued })
        {
            for (int i = 0; i < jobs.Length && head < 0; i++) if (jobs[i].State == pref) head = i;
            if (head >= 0) break;
        }
        for (int i = 0; i < jobs.Length; i++) if (jobs[i].State == StorageJobState.Queued && i != head) queued++;
        if (head >= 0) return Active(jobs[head], queued, station);

        // nothing active: the newest ended job that still has something to say (the list is newest-ended first)
        for (int i = 0; i < jobs.Length; i++)
        {
            var j = jobs[i];
            if (j.Kind == StorageJobKind.Import && j.State == StorageJobState.Done && _firstSeenDone.TryGetValue(j.JobId, out double seen) && now - seen < ReadySeconds)
            {
                _readyUntil = seen + ReadySeconds;
                return new StorageSegment(StorageSegmentState.Ready, $"Exercise {Short(j)} ready", 1f, IsYours(j, station), 0, false, j.Kind, j.JobId, (Guid)j.ExerciseId, "Open");
            }
            if (j.Kind == StorageJobKind.Archive && j.State == StorageJobState.Failed && !SupersededBy(jobs, i))
            {
                string why = string.IsNullOrEmpty(j.Reason) ? "" : $" ({j.Reason})";
                string files = j.FilesTotal > 0 ? $" — {j.FilesDone} of {j.FilesTotal} files" : "";
                return new StorageSegment(StorageSegmentState.Incomplete, $"Archive of exercise {Short(j)} incomplete{files}{why}", j.FilesTotal > 0 ? (float)j.FilesDone / j.FilesTotal : -1f, IsYours(j, station), 0, false, j.Kind, j.JobId, (Guid)j.ExerciseId, "Resume");
            }
            if (j.Kind == StorageJobKind.Archive && j.State == StorageJobState.Failed) continue;
        }
        return StorageSegment.None;
    }

    /// <summary>A later job (a lower index = newer within the ended ones, or any active one) for the same exercise that is not itself failed: the failure is history.</summary>
    private static bool SupersededBy(StorageJob[] jobs, int failedAt)
    {
        var f = jobs[failedAt];
        for (int i = 0; i < jobs.Length; i++)
        {
            if (i == failedAt || jobs[i].ExerciseId != f.ExerciseId || jobs[i].Kind != f.Kind) continue;
            if (jobs[i].IsActive || (i < failedAt && jobs[i].State == StorageJobState.Done)) return true;
        }
        return false;
    }

    private static StorageSegment Active(StorageJob j, int queued, string station)
    {
        string verb = Verb(j.Kind);
        string text;
        if (j.State == StorageJobState.Queued) text = $"Queued — {verb.ToLowerInvariant()} exercise {Short(j)}";
        else if (j.State == StorageJobState.WaitingForNode) text = $"{verb} exercise {Short(j)} — waiting for {(j.WaitingFor > 0 ? j.WaitingFor + " node(s)" : "a node")}{(string.IsNullOrEmpty(j.Reason) ? "" : " (" + j.Reason + ")")}";
        else if (j.FilesTotal > 0) text = $"{verb} exercise {Short(j)} — {j.FilesDone} of {j.FilesTotal} files · {BytesPair(j.BytesDone, j.BytesTotal)}";
        else text = $"{verb} exercise {Short(j)} — starting";
        if (j.GivingWay && j.State == StorageJobState.Running) text += " · slowed while Live runs";
        float progress = j.BytesTotal > 0 ? Math.Clamp((float)j.BytesDone / j.BytesTotal, 0f, 1f) : j.FilesTotal > 0 ? Math.Clamp((float)j.FilesDone / j.FilesTotal, 0f, 1f) : -1f;
        return new StorageSegment(StorageSegmentState.Active, text, progress, IsYours(j, station), queued, j.GivingWay, j.Kind, j.JobId, (Guid)j.ExerciseId, "");
    }

    /// <summary>The verb of a job's kind (the segment's and the popover's words).</summary>
    public static string Verb(StorageJobKind kind) => kind switch
    {
        StorageJobKind.Archive => "Archiving",
        StorageJobKind.Import => "Importing",
        StorageJobKind.Delete => "Deleting from the archive",
        _ => "Discarding",
    };

    /// <summary>The noun of a job's kind (the popover's title: <i>Archive · exercise 3</i>).</summary>
    public static string Noun(StorageJobKind kind) => kind switch
    {
        StorageJobKind.Archive => "Archive",
        StorageJobKind.Import => "Import",
        StorageJobKind.Delete => "Delete from archive",
        _ => "Discard",
    };

    /// <summary>The exercise as the bar names it: the first 8 hex digits of its id (the master's own words for it: <i>"exercise 1a2b3c4d is being archived"</i>).</summary>
    public static string Short(StorageJob j) => ((Guid)j.ExerciseId).ToString("N").Substring(0, 8);

    /// <summary>Whether this station asked for the job.</summary>
    public static bool IsYours(StorageJob j, string station) => station.Length > 0 && string.Equals(j.AskedBy, station, StringComparison.Ordinal);

    /// <summary>Progress in bytes as the mockup words it: both numbers in the TOTAL's unit — <c>1.2 / 3.1 GB</c>, <c>0.4 / 1.4 GB</c>.</summary>
    public static string BytesPair(long done, long total)
    {
        if (total >= 1L << 30) return $"{done / (double)(1L << 30):0.0} / {total / (double)(1L << 30):0.0} GB";
        if (total >= 1L << 20) return $"{done / (double)(1L << 20):0.0} / {total / (double)(1L << 20):0.0} MB";
        if (total >= 1L << 10) return $"{done / 1024.0:0} / {total / 1024.0:0} KB";
        return $"{done} / {total} B";
    }

    /// <summary>A byte count as <c>512 KB</c> / <c>1.2 GB</c>.</summary>
    public static string Bytes(long b)
        => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1L << 20 ? $"{b / (double)(1L << 20):0.0} MB" : b >= 1L << 10 ? $"{b / 1024.0:0} KB" : $"{b} B";
}
