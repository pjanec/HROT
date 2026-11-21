using System;
using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ CE-4330 ② (📄 docs/DESIGN_Recording_Storage.md §7.2, Q6) — the master's ONE queue of storage jobs. Pure state, frame thread only; the master
/// starts what <see cref="StartNext"/> hands it and reports how it ended (<see cref="Finish"/>).
/// <list type="bullet">
/// <item>⭐ <b>one job per exercise</b> — a request for an exercise that already has an ACTIVE job of the same kind JOINS it (same <see cref="StorageJob.JobId"/>);
///   a different kind while one is active is refused with the reason (a discard while it is archived, an import while it is deleted …)</item>
/// <item>⭐ <b>one transfer at a time</b> — at most one job is Running / WaitingForNode; the rest wait Queued, in order</item>
/// <item>finished jobs stay listed (the newest <see cref="KeepFinished"/>) so every station sees how its job ended</item>
/// </list>
/// </summary>
public sealed class StorageJobQueue
{
    /// <summary>How many ended jobs stay on the list.</summary>
    public const int KeepFinished = 8;

    private readonly List<StorageJob> _jobs = new();                         // active in queue order, then ended (newest last)
    private readonly Dictionary<Guid, List<Guid>> _joiners = new();          // job id → the OTHER request ids that joined it
    private readonly Dictionary<Guid, Guid> _jobOfRequest = new();           // any request id → its job id

    /// <summary>Bumped on every change — the master republishes the list when it moves.</summary>
    public int Revision { get; private set; }

    /// <summary>Every job, active ones first in queue order.</summary>
    public IReadOnlyList<StorageJob> Jobs => _jobs;

    /// <summary>What <see cref="Submit"/> did with a request.</summary>
    public enum Admission { Queued, Joined, Refused }

    /// <summary>Admits a request: queued as a new job, joined to the exercise's active job, or refused (the reason says why).</summary>
    public (Admission Admission, StorageJob? Job, string? Refusal) Submit(Guid requestId, StorageJobKind kind, Guid exerciseId, string? askedBy)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("a request id is required", nameof(requestId));
        if (ActiveFor(exerciseId) is { } active)
        {
            if (active.Kind != kind)
                return (Admission.Refused, active, $"exercise {Short(exerciseId)} is being {Doing(active.Kind)}");
            if (!_joiners.TryGetValue(active.JobId, out var list)) _joiners[active.JobId] = list = new List<Guid>();
            if (requestId != active.JobId && !list.Contains(requestId)) list.Add(requestId);
            _jobOfRequest[requestId] = active.JobId;
            return (Admission.Joined, active, null);
        }
        var job = new StorageJob(requestId, kind, (Fdp.Core.ExerciseId)exerciseId, StorageJobState.Queued, AskedBy: askedBy ?? "");
        _jobs.Insert(ActiveCount(), job);
        _jobOfRequest[requestId] = requestId;
        Revision++;
        return (Admission.Queued, job, null);
    }

    /// <summary>The job that runs now (Running or WaitingForNode), or null.</summary>
    public StorageJob? Running
    {
        get
        {
            for (int i = 0; i < _jobs.Count; i++)
                if (_jobs[i].State is StorageJobState.Running or StorageJobState.WaitingForNode) return _jobs[i];
            return null;
        }
    }

    /// <summary>When nothing runs: the first Queued job, now Running (the master starts it). Null when one runs or none waits.</summary>
    public StorageJob? StartNext()
    {
        if (Running != null) return null;
        for (int i = 0; i < _jobs.Count; i++)
        {
            if (_jobs[i].State != StorageJobState.Queued) continue;
            _jobs[i] = _jobs[i] with { State = StorageJobState.Running };
            Revision++;
            return _jobs[i];
        }
        return null;
    }

    /// <summary>The active job holding <paramref name="exerciseId"/>, or null.</summary>
    public StorageJob? ActiveFor(Guid exerciseId)
    {
        for (int i = 0; i < _jobs.Count; i++)
            if (_jobs[i].IsActive && (Guid)_jobs[i].ExerciseId == exerciseId) return _jobs[i];
        return null;
    }

    /// <summary>The job a request id belongs to (its own, or the one it joined).</summary>
    public StorageJob? JobOf(Guid requestId)
        => _jobOfRequest.TryGetValue(requestId, out var jobId) ? Find(jobId) : null;

    public StorageJob? Find(Guid jobId)
    {
        for (int i = 0; i < _jobs.Count; i++)
            if (_jobs[i].JobId == jobId) return _jobs[i];
        return null;
    }

    /// <summary>Sets a running job's numbers and whether it gives way; allocates only when something changed.</summary>
    public void SetProgress(Guid jobId, int filesDone, int filesTotal, long bytesDone, long bytesTotal, bool givingWay)
    {
        for (int i = 0; i < _jobs.Count; i++)
        {
            var j = _jobs[i];
            if (j.JobId != jobId || !j.IsActive) continue;
            if (j.FilesDone == filesDone && j.FilesTotal == filesTotal && j.BytesDone == bytesDone && j.BytesTotal == bytesTotal && j.GivingWay == givingWay) return;
            _jobs[i] = j with { FilesDone = filesDone, FilesTotal = filesTotal, BytesDone = bytesDone, BytesTotal = bytesTotal, GivingWay = givingWay };
            Revision++;
            return;
        }
    }

    /// <summary>
    /// Ends <paramref name="jobId"/> in <paramref name="end"/> (Done / Failed / Cancelled) and returns every request id that is owed an answer: the
    /// job's own first, then its joiners. Empty when the job is unknown or already ended.
    /// </summary>
    public IReadOnlyList<Guid> Finish(Guid jobId, StorageJobState end, string? reason = null)
    {
        if (end is StorageJobState.Queued or StorageJobState.Running or StorageJobState.WaitingForNode)
            throw new ArgumentException("a job ends Done, Failed or Cancelled", nameof(end));
        for (int i = 0; i < _jobs.Count; i++)
        {
            if (_jobs[i].JobId != jobId || !_jobs[i].IsActive) continue;
            var ended = _jobs[i] with { State = end, Reason = reason ?? _jobs[i].Reason, GivingWay = false };
            _jobs.RemoveAt(i);
            _jobs.Add(ended);
            int finished = _jobs.Count - ActiveCount();
            while (finished-- > KeepFinished) Forget(ActiveCount());
            var owed = new List<Guid> { jobId };
            if (_joiners.Remove(jobId, out var others)) owed.AddRange(others);
            Revision++;
            return owed;
        }
        return Array.Empty<Guid>();
    }

    private void Forget(int index)
    {
        var gone = _jobs[index].JobId;
        _jobs.RemoveAt(index);
        var stale = new List<Guid>();
        foreach (var kv in _jobOfRequest) if (kv.Value == gone) stale.Add(kv.Key);
        foreach (var r in stale) _jobOfRequest.Remove(r);
    }

    private int ActiveCount()
    {
        int n = 0;
        for (int i = 0; i < _jobs.Count; i++) if (_jobs[i].IsActive) n++;
        return n;
    }

    private static string Short(Guid id) => id.ToString("N").Substring(0, 8);

    private static string Doing(StorageJobKind kind) => kind switch
    {
        StorageJobKind.Archive => "archived",
        StorageJobKind.Import  => "imported",
        StorageJobKind.Discard => "discarded",
        _                      => "deleted",
    };
}
