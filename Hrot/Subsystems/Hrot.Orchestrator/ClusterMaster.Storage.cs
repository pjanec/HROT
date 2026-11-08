using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ CE-4330 ② (📄 docs/DESIGN_Recording_Storage.md §7.2, Q5–Q8) — the master's storage JOBS. Every archive, import and delete-from-archive is a
/// <see cref="StorageJob"/> in ONE <see cref="StorageJobQueue"/>: one per exercise (a second request joins it), one transfer at a time, the list
/// published to every station (fact ①) on each change and at most <see cref="JobProgressPublishSeconds"/> apart while bytes move.
/// ⭐ No new operations: the existing <c>ExportArchive</c> / <c>ImportArchive</c> / <c>DeleteExercise</c> / <c>CancelOperation</c> ask.
/// </summary>
public sealed partial class ClusterMaster
{
    private readonly StorageJobQueue _storageJobs = new();
    private readonly Dictionary<Guid, ExecuteStorageOpIntent> _jobIntents = new();          // a Queued job's request, until it starts
    private readonly Dictionary<Guid, StorageTransferProgress> _jobProgress = new();        // a running transfer's counters
    private int _publishedJobsRevision;
    private double _lastJobsPublishAt = double.NegativeInfinity;

    /// <summary>A running job's numbers reach the stations at most this often.</summary>
    internal const double JobProgressPublishSeconds = 0.25;

    /// <summary>The master's storage jobs (rails, HTTP).</summary>
    public StorageJobQueue StorageJobs => _storageJobs;

    /// <summary>
    /// ⭐ CE-4330 ⑦ (R-4174) — removes an ARCHIVED exercise from the NAS (its folder); set by the host that owns the gateway (<c>OrchestratorCore</c>),
    /// like <see cref="ExerciseDeleter"/>. Returns false when the NAS holds no such exercise.
    /// </summary>
    public Func<Guid, bool>? ArchiveDeleter { get; set; }

    /// <summary>Admits an archive / import / delete-from-archive request into the queue, and starts it when the gateway is free.</summary>
    private void SubmitStorageJob(ExecuteStorageOpIntent intent, StorageJobKind kind)
    {
        var (admission, job, refusal) = _storageJobs.Submit(intent.RequestId, kind, intent.ExerciseId, intent.AskedBy);
        switch (admission)
        {
            case StorageJobQueue.Admission.Refused:
                FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4330: {0} {1} REFUSED — {2}.", kind, intent.RequestId, refusal ?? "");
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected, refusal);
                return;
            case StorageJobQueue.Admission.Joined:
                FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4330: {0} {1} joins job {2}.", kind, intent.RequestId, job!.JobId);
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.InProgress, job);   // ⭐ the answer carries the job it joined
                return;
            default:
                _jobIntents[intent.RequestId] = intent;
                PublishOpStatus(intent.RequestId, OrchestrationStatusCode.InProgress, job);
                PumpStorageJobs();
                return;
        }
    }

    /// <summary>Starts the next queued job when nothing runs; a job that ends at once (a delete, nothing to copy) lets the next start too.</summary>
    private void PumpStorageJobs()
    {
        while (_storageJobs.StartNext() is { } job)
        {
            if (!_jobIntents.Remove(job.JobId, out var intent))
            {
                EndStorageJob(job.JobId, StorageJobState.Failed, "the request was lost");
                continue;
            }
            if (StartStorageJob(job, intent)) return;   // it runs: the next one waits for its end
        }
    }

    /// <summary>Starts <paramref name="job"/>; true when it now runs in the background, false when it already ended (answered).</summary>
    private bool StartStorageJob(StorageJob job, ExecuteStorageOpIntent intent)
    {
        switch (job.Kind)
        {
            case StorageJobKind.Archive: return StartArchive(intent);
            case StorageJobKind.Import:  return StartImport(intent);
            case StorageJobKind.Delete:
            {
                bool deleted = ArchiveDeleter?.Invoke(intent.ExerciseId) ?? false;
                FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4330 ⑦: delete exercise {0} from the archive: {1}.", intent.ExerciseId, deleted ? "deleted" : "not archived");
                EndStorageJob(job.JobId, deleted ? StorageJobState.Done : StorageJobState.Failed, deleted ? null : "the archive holds no such exercise");
                return false;
            }
            default:
                EndStorageJob(job.JobId, StorageJobState.Failed, $"a {job.Kind} is not a queued job");
                return false;
        }
    }

    /// <summary>The archive's fan-out (the existing ExportArchive path, as the head job). False when it ended at once.</summary>
    private bool StartArchive(ExecuteStorageOpIntent intent)
    {
        var exportNodeIds = Participants();   // ⭐ C3 — operator rounds: in-sync nodes only
        if (exportNodeIds.Count == 0)
        {
            EndStorageJob(intent.RequestId, StorageJobState.Done, null);
            return false;
        }
        var exportCts = new CancellationTokenSource();
        _activeCancellations[intent.RequestId] = exportCts;
        var progress = new StorageTransferProgress();
        _jobProgress[intent.RequestId] = progress;
        var exportTxId = Guid.NewGuid();
        // Notify StorageProcessManager of the archive context before the fan-out.
        _eventBus.PublishManaged(new ExportArchiveBegunEvent
        {
            TransactionId    = exportTxId,
            ArchiveRequestId = intent.RequestId,
            Cts              = exportCts,
            Progress         = progress,
        });
        FanOutSerializeLocal(exportTxId, exportNodeIds, new ArchiveHandlerPayload(intent.ExerciseId));
        return true;
    }

    /// <summary>The import's push (the existing ImportArchive path, as the head job).</summary>
    private bool StartImport(ExecuteStorageOpIntent intent)
    {
        // ⭐ CE-4330 ④ (§7.2) — only the nodes that LACK the recording get it (fact ③, NodesHolding); every node holds it ⇒ the job ends at once (the cache hit, §5a)
        var all = BuildNodeDistributionTargetsForExercise(intent.ExerciseId);
        var holding = NodesHolding(intent.ExerciseId);
        var targets = new List<NodeDistributionTarget>(all.Count);
        foreach (var t in all)
        {
            bool has = false;
            for (int i = 0; i < holding.Count; i++) if (holding[i] == t.NodeId) { has = true; break; }
            if (!has) targets.Add(t);
        }
        if (targets.Count == 0)
        {
            EndStorageJob(intent.RequestId, StorageJobState.Done, null);
            return false;
        }
        var importCts = new CancellationTokenSource();
        _activeCancellations[intent.RequestId] = importCts;
        var progress = new StorageTransferProgress();
        _jobProgress[intent.RequestId] = progress;
        // Delegate the NAS prefetch to StorageProcessManager via event bus (SRP).
        _eventBus.PublishManaged(new ImportArchiveBegunEvent
        {
            RequestId  = intent.RequestId,
            ExerciseId = intent.ExerciseId.ToString(),
            Targets    = targets,
            Cts        = importCts,
            Progress   = progress,
        });
        return true;
    }

    /// <summary>
    /// Every frame: the running job's end (its own request's final status, published by the transfer), its numbers, giving way while a Live runs
    /// (Q5), the next job, and the list to the stations.
    /// </summary>
    private readonly HashSet<Guid> _autoResumed = new();   // ⑧ — exercises already resumed (or just failed) since the roster last grew
    private readonly HashSet<Guid> _cancelledArchives = new();   // ⑧ — an archive the operator CANCELLED is never resumed by itself (this session)
    private int _rosterSizeAtResume;

    /// <summary>
    /// ⭐ CE-4330 ⑧ (§3 P5 ①, §7.2 "the master restarts mid-job") — an archive cut short RESUMES BY ITSELF: an exercise still on the nodes whose NAS copy is
    /// INCOMPLETE (fact ②) and that no job holds gets a new Archive job (the pull skips the files already whole). Once per exercise — and again whenever
    /// a node joins (the missing one may be back). ⛔ Never a loop on a node that stays away: a failed resume waits for the roster to grow.
    /// </summary>
    private void ResumeIncompleteArchives()
    {
        if (_roster.ActiveNodes.Count > _rosterSizeAtResume) _autoResumed.Clear();
        _rosterSizeAtResume = _roster.ActiveNodes.Count;
        foreach (var inv in _eventBus.ReadManaged<AssetInventoryUpdateEvent>())
        {
            var onNodes = inv.UnarchivedLocalExercises;
            if (onNodes == null) continue;
            for (int i = 0; i < onNodes.Length; i++)
            {
                var item = onNodes[i];
                if (item.NasState != ArchiveNasState.Incomplete) continue;
                Guid id = (Guid)item.ExerciseId;
                if (_storageJobs.ActiveFor(id) != null || _cancelledArchives.Contains(id) || !_autoResumed.Add(id)) continue;
                FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4330 ⑧: exercise {0}'s archive is incomplete ({1} of {2}) — resumed.", id, item.NasFiles, item.NasFilesTotal);
                SubmitStorageJob(StorageIntents.Archive(id, "resume"), StorageJobKind.Archive);
            }
        }
    }

    private void TickStorageJobs()
    {
        ResumeIncompleteArchives();
        if (_storageJobs.Running is { } run)
        {
            foreach (var ev in _eventBus.ReadManaged<ClusterOpCompletedEvent>())
            {
                if (ev.RequestId != run.JobId) continue;
                if (ev.StatusCode == OrchestrationStatusCode.Success)
                {
                    EndStorageJob(run.JobId, StorageJobState.Done, null, answerOwn: false);
                    break;
                }
                if (ev.StatusCode is OrchestrationStatusCode.Rejected or OrchestrationStatusCode.Failure or OrchestrationStatusCode.Cancelled or OrchestrationStatusCode.Timeout)
                {
                    bool cancelled = _activeCancellations.TryGetValue(run.JobId, out var cts) && cts.IsCancellationRequested;
                    EndStorageJob(run.JobId, cancelled ? StorageJobState.Cancelled : StorageJobState.Failed,
                                  cancelled ? null : ev.ResultPayload as string ?? "a transfer failed", answerOwn: false);
                    break;
                }
            }
            if (_storageJobs.Running is { } still && _jobProgress.TryGetValue(still.JobId, out var p))
            {
                bool giveWay = _currentDsmState == ClusterState.OperatingLive;   // ⭐ Q5 — a Live is never slowed by a transfer: the transfer slows
                p.GiveWay = giveWay;
                // the numbers at most every JobProgressPublishSeconds (no allocation on the frames between); a give-way change at once
                if (giveWay != still.GivingWay || UtcNowSeconds() - _lastJobsPublishAt >= JobProgressPublishSeconds)
                    _storageJobs.SetProgress(still.JobId, p.FilesDone, p.FilesTotal, p.BytesDone, p.BytesTotal, giveWay);
            }
        }
        PumpStorageJobs();
        PublishStorageJobsIfChanged();
    }

    /// <summary>
    /// Ends a job and answers every request that is owed one — its joiners always; its own request too unless the transfer already answered it
    /// (<paramref name="answerOwn"/> false: the archive / import publishes its own final status).
    /// </summary>
    private void EndStorageJob(Guid jobId, StorageJobState end, string? reason, bool answerOwn = true)
    {
        if (_activeCancellations.Remove(jobId, out var cts)) cts.Dispose();
        _jobProgress.Remove(jobId);
        if (_storageJobs.Find(jobId) is { Kind: StorageJobKind.Archive } ended)
        {
            if (end == StorageJobState.Cancelled) _cancelledArchives.Add((Guid)ended.ExerciseId);
            else if (end == StorageJobState.Failed) _autoResumed.Add((Guid)ended.ExerciseId);   // ⑧ — retried when a node joins, not at once
            else _cancelledArchives.Remove((Guid)ended.ExerciseId);
        }
        var owed = _storageJobs.Finish(jobId, end, reason);
        var code = end switch
        {
            StorageJobState.Done      => OrchestrationStatusCode.Success,
            StorageJobState.Cancelled => OrchestrationStatusCode.Cancelled,
            _                         => OrchestrationStatusCode.Rejected,
        };
        for (int i = 0; i < owed.Count; i++)
            if (answerOwn || owed[i] != jobId) PublishOpStatus(owed[i], code, reason);
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4330: job {0} {1}{2}.", jobId, end, reason == null ? "" : " — " + reason);
    }

    /// <summary>
    /// ⭐ Cancel (Q7): a QUEUED job ends Cancelled here (nothing was copied); a RUNNING one is cancelled by its token (the transfer deletes its
    /// <c>.part</c> files and answers) — false then, so the caller's existing path cancels it. A joiner's request id names the job it joined.
    /// </summary>
    private bool TryCancelQueuedStorageJob(ref Guid targetId)
    {
        if (_storageJobs.JobOf(targetId) is not { IsActive: true } job) return false;
        targetId = job.JobId;
        if (job.State != StorageJobState.Queued) return false;
        _jobIntents.Remove(job.JobId);
        EndStorageJob(job.JobId, StorageJobState.Cancelled, null);
        return true;
    }

    /// <summary>The job list to the stations whenever it changed (the numbers alone change at most every <see cref="JobProgressPublishSeconds"/>, above).</summary>
    private void PublishStorageJobsIfChanged()
    {
        if (_storageJobs.Revision == _publishedJobsRevision) return;
        var jobs = new StorageJob[_storageJobs.Jobs.Count];
        for (int i = 0; i < jobs.Length; i++) jobs[i] = _storageJobs.Jobs[i];
        _eventBus.PublishManaged(new StorageJobsPublishedEvent { Jobs = jobs });
        _eventBus.PublishManaged(new StorageJobsUpdateEvent { Jobs = jobs });
        _publishedJobsRevision = _storageJobs.Revision;
        _lastJobsPublishAt = UtcNowSeconds();
    }
}
