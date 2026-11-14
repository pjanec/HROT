using System;
using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.RunMode;
using NodeEditor.UI.Picker;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.Storage;

/// <summary>
/// ⭐⭐ CE-4330 ui half (📄 docs/DESIGN_Recording_Storage.md §7.3, §7.4 Q7, §8 ②) — <b>the Transfers list and what a station may do to it</b>: the master's jobs (<see cref="ITimeTransportFacade.StorageJobs"/>), <i>Cancel</i> (a queued or running job) and <i>Resume</i> (a failed archive: the SAME
/// ask again — the pull skips the files already whole) for the Author and Instructor seats only (greyed on another seat with the reason), the segment's <i>Open</i> (the finished import's AAR), and the master's REFUSAL of an ask shown as the reason
/// (<i>"exercise 1a2b3c4d is being archived"</i>). Every ask is the ONE construction (<see cref="StorageIntents"/>); ⛔ it keeps no job list and decides nothing the master decides.
/// </summary>
public sealed class StorageTransfers
{
    /// <summary>The refusal words on a seat that may not cancel / resume (Q7).</summary>
    public const string SeatRefusalText = "needs the Author or Instructor seat";

    private readonly ITimeTransportFacade _facade;
    private readonly Func<Seat> _seat;
    private readonly Func<string> _station;
    private readonly Action<Guid>? _open;
    private readonly Action<string, string, RunModeConfirmation.Button>? _confirm;
    private Guid _lastAsk;
    private string? _lastRefusal;

    /// <param name="facade">The host's ONE time facade (its ClusterView holds the jobs).</param>
    /// <param name="seat">This station's seat.</param>
    /// <param name="station">This station's name (the ask's <c>AskedBy</c>: the job is then marked <i>(yours)</i>).</param>
    /// <param name="open">Opens the recording of an exercise (the host's run choice: the AAR); null = <i>Open</i> is not offered.</param>
    /// <param name="confirm">⭐ CE-4330 ⑦ — asks a confirmation (title, text, the action button; Cancel is added) through the host's ONE pending slot
    /// (<see cref="RunModeChoice.Confirm"/>); null = <i>Delete from archive</i> is not offered.</param>
    public StorageTransfers(ITimeTransportFacade facade, Func<Seat> seat, Func<string> station, Action<Guid>? open = null,
                            Action<string, string, RunModeConfirmation.Button>? confirm = null)
    {
        _confirm = confirm;
        _facade = facade ?? throw new ArgumentNullException(nameof(facade));
        _seat = seat ?? throw new ArgumentNullException(nameof(seat));
        _station = station ?? throw new ArgumentNullException(nameof(station));
        _open = open;
    }

    /// <summary>The cluster's jobs, active first then the newest ended (the master's order).</summary>
    public StorageJob[] Jobs => _facade.StorageJobs;

    /// <summary>This station's name.</summary>
    public string Station => _station();

    /// <summary>Why Cancel / Resume are greyed on this seat (Q7), or null.</summary>
    public string? SeatRefusal => _seat() is Seat.Author or Seat.Instructor ? null : SeatRefusalText;

    /// <summary>Why <i>Cancel</i> is not offered on <paramref name="job"/> (null = it is): the seat first, then the state (an ended job has nothing to cancel).</summary>
    public string? CancelRefusal(StorageJob job) => SeatRefusal ?? (job.IsActive ? null : "the transfer has ended");

    /// <summary>Why <i>Resume</i> is not offered on <paramref name="job"/> (null = it is): the seat first, then only a FAILED or CANCELLED archive resumes.</summary>
    public string? ResumeRefusal(StorageJob job)
        => SeatRefusal ?? (job.Kind == StorageJobKind.Archive && job.State is StorageJobState.Failed or StorageJobState.Cancelled ? null : "only an archive that stopped part-way resumes");

    /// <summary>Cancels a queued / running job (<c>CancelOperation(JobId)</c>). False (and nothing is sent) when <see cref="CancelRefusal"/> says no.</summary>
    public bool Cancel(StorageJob job)
    {
        if (CancelRefusal(job) != null) return false;
        _facade.CancelStorageJob(job.JobId);
        return true;
    }

    /// <summary>Resumes a failed archive: <c>ExportArchive</c> again (the pull skips the files already whole). The master's answer — a refusal — is read through <see cref="LastRefusal"/>.</summary>
    public bool Resume(StorageJob job)
    {
        if (ResumeRefusal(job) != null) return false;
        var ask = StorageIntents.Archive((Guid)job.ExerciseId, _station());
        _lastAsk = _facade.AskStorage(ask);
        _lastRefusal = null;
        return true;
    }

    /// <summary>⭐ CE-4330 ⑦ (§8 ⑦, R-4174) — why <i>Delete from archive</i> is greyed on <paramref name="item"/> (null = it can be asked): the seat, then an
    /// exercise with nothing on the NAS, then a transfer of it queued or running (the master refuses that too).</summary>
    public string? DeleteFromArchiveRefusal(ExerciseInventoryItem item)
    {
        if (SeatRefusal is { } seat) return seat;
        if (item.NasState == ArchiveNasState.None) return "not archived — nothing on the NAS";
        var jobs = _facade.StorageJobs;
        for (int i = 0; i < jobs.Length; i++)
            if (jobs[i].IsActive && jobs[i].ExerciseId == item.ExerciseId) return "a transfer of this run is queued or running";
        return null;
    }

    /// <summary>⭐ CE-4330 ⑦ — the confirmation's text (mockup ⑦): what goes, and that nothing else holds it.</summary>
    public static string DeleteFromArchiveText(ExerciseInventoryItem item)
    {
        var scenario = string.IsNullOrWhiteSpace(item.ScenarioId) ? "this exercise" : item.ScenarioId;
        var files = item.NasFilesTotal > 0 ? $"{item.NasFilesTotal} files. " : "";
        return $"Delete {scenario} · exercise #{((Guid)item.ExerciseId).ToString("N").Substring(0, 8)} from the NAS?\n{files}The cached copies on this cluster go too.\nThis cannot be undone — nothing else holds this recording.";
    }

    /// <summary>⭐ CE-4330 ⑦ — the picker row's actions for <paramref name="item"/>: <i>Delete from archive…</i> on a run the NAS holds (greyed with the reason when it
    /// cannot be asked now); none on a run that was never archived, or where this host asks no confirmation.</summary>
    public IReadOnlyList<PickerRowAction>? ActionsFor(ExerciseInventoryItem item)
    {
        if (_confirm is null || item.NasState == ArchiveNasState.None) return null;
        return new[] { new PickerRowAction("storage/delete", "Delete from archive…", () => AskDeleteFromArchive(item), DeleteFromArchiveRefusal(item)) };
    }

    /// <summary>⭐ CE-4330 ⑦ — asks <i>Delete from the archive</i>; its button sends <see cref="StorageIntents.DeleteFromArchive"/> (the master's answer is read through
    /// <see cref="LastRefusal"/>). False (nothing asked) when <see cref="DeleteFromArchiveRefusal"/> says no or the host asks no confirmation.</summary>
    public bool AskDeleteFromArchive(ExerciseInventoryItem item)
    {
        if (_confirm is null || DeleteFromArchiveRefusal(item) != null) return false;
        var exercise = (Guid)item.ExerciseId;
        _confirm("Delete from the archive", DeleteFromArchiveText(item), new RunModeConfirmation.Button("Delete from archive", () =>
        {
            _lastAsk = _facade.AskStorage(StorageIntents.DeleteFromArchive(exercise, _station()));
            _lastRefusal = null;
        }));
        return true;
    }

    // ── ④ the import a station waits on (§5a; mockup ④) ─────────────────────────────────────────────────────────────────────────

    private Guid? _importing;
    private string _importTitle = "";
    private Guid _staleImport;   // the exercise's newest import job when this wait began: an OLD finished one never opens the run
    private Guid _importAsk;

    /// <summary>⭐ CE-4330 ④ — the exercise this station is waiting on in the import dialog, or null (none, or sent to the background).</summary>
    public Guid? Importing => _importing;

    /// <summary>⭐ CE-4330 ④ — the dialog's title line: <i>"Importing Recon · exercise #… from the NAS"</i>.</summary>
    public string ImportTitle => _importTitle;

    /// <summary>⭐ CE-4330 ④ (§5) — opening a run the NAS holds and this station does not: the import is ASKED (or, when one already runs for it, JOINED) and the
    /// dialog waits on it; the run opens by itself once it is in. False (nothing done) where this host opens no recording.</summary>
    public bool OpenOrImport(ExerciseInventoryItem item)
    {
        if (_open is null) return false;
        var exercise = (Guid)item.ExerciseId;
        _importing = exercise;
        _importTitle = $"Importing {(string.IsNullOrWhiteSpace(item.ScenarioId) ? "the exercise" : item.ScenarioId)} · exercise #{exercise.ToString("N").Substring(0, 8)} from the NAS";
        var newest = NewestImport(exercise);
        _staleImport = newest is { IsActive: false } old ? old.JobId : Guid.Empty;
        _importAsk = Guid.Empty;
        if (newest is not { IsActive: true })
        {
            _importAsk = _facade.AskStorage(StorageIntents.Import(exercise, _station()));
            _lastAsk = _importAsk;
            _lastRefusal = null;
        }
        return true;
    }

    /// <summary>⭐ CE-4330 ④ — the import job the dialog shows (the exercise's newest, never the one that had ended before the wait began), or null while the
    /// master has not listed it yet.</summary>
    public StorageJob? ImportJob => _importing is { } ex && NewestImport(ex) is { } j && j.JobId != _staleImport ? j : null;

    /// <summary>⭐ CE-4330 ④ — why the import did not happen (the master's refusal of the ask, or the job's failure), or null.</summary>
    public string? ImportFailure
    {
        get
        {
            if (_importing is null) return null;
            if (_importAsk != Guid.Empty && _facade.StorageAskRefusal(_importAsk) is { } refused) return refused;
            return ImportJob is { State: StorageJobState.Failed or StorageJobState.Cancelled } j
                ? (string.IsNullOrEmpty(j.Reason) ? (j.State == StorageJobState.Cancelled ? "the import was cancelled" : "the import failed") : j.Reason)
                : null;
        }
    }

    /// <summary>⭐ CE-4330 ④ — called each frame: the import the dialog waits on is in ⇒ the run opens BY ITSELF (the existing loading screen follows) and the dialog closes.</summary>
    public void UpdateImport()
    {
        if (_importing is not { } ex || ImportJob is not { State: StorageJobState.Done } done) return;
        if (!string.IsNullOrEmpty(done.Reason)) return;   // ⭐ CE-4331 ③ — placed on a DIFFERENT cluster: the dialog asks Start / Cancel first (PlacementNote)
        _importing = null;
        _open!(ex);
    }

    /// <summary>⭐ CE-4331 ③ (§9a; backend note 2) — the import is in but the recordings were PLACED on this cluster (some not played, some nodes only listen): the
    /// master's words, which the dialog shows with <i>Start AAR</i> / <i>Cancel</i>. Null while importing, or when every recording plays on its own node.</summary>
    public string? PlacementNote => ImportJob is { State: StorageJobState.Done, Reason: { Length: > 0 } why } ? why : null;

    /// <summary>⭐ CE-4331 ③ — <i>Start AAR</i> after the placement note: the run opens. False when there is no note to answer.</summary>
    public bool StartAfterPlacement()
    {
        if (_importing is not { } ex || PlacementNote is null) return false;
        _importing = null;
        _open!(ex);
        return true;
    }

    /// <summary>⭐ CE-4330 ④ — <i>Continue in background</i>: the dialog closes, the import goes on; the status bar shows it and offers <i>Open</i> when it is in (never by itself).</summary>
    public void ContinueInBackground() => _importing = null;

    /// <summary>⭐ CE-4330 ④ — why the dialog's <i>Cancel</i> is greyed (null = it can cancel): it stops the job for every station that joined it, so the seat rule (Q7) holds.</summary>
    public string? CancelImportRefusal => ImportJob is { IsActive: true } j ? CancelRefusal(j) : null;

    /// <summary>⭐ CE-4330 ④ — <i>Cancel</i>: the running import is cancelled (its partial files go; the NAS is not touched) and the dialog closes; an import that already
    /// ended or failed just closes. False (nothing done) when the seat may not cancel.</summary>
    public bool CancelImport()
    {
        if (_importing is null || CancelImportRefusal != null) return false;
        if (ImportJob is { IsActive: true } j) Cancel(j);
        _importing = null;
        return true;
    }

    private StorageJob? NewestImport(Guid exercise)
    {
        var jobs = _facade.StorageJobs;   // the master's order: active first, then the newest ended
        for (int i = 0; i < jobs.Length; i++)
            if (jobs[i].Kind == StorageJobKind.Import && (Guid)jobs[i].ExerciseId == exercise) return jobs[i];
        return null;
    }

    /// <summary>Opens a finished import's recording (the AAR) — false where the host offers no open.</summary>
    public bool Open(Guid exercise)
    {
        if (_open == null || exercise == Guid.Empty) return false;
        _open(exercise);
        return true;
    }

    /// <summary>Whether <i>Open</i> is offered at all (the host passed its open).</summary>
    public bool CanOpen => _open != null;

    /// <summary>The master's refusal of the last ask this station made (<i>"exercise … is being archived"</i>), or null: read off the op's outcome (the cluster view keeps the newest 32).</summary>
    public string? LastRefusal
    {
        get
        {
            if (_lastAsk == Guid.Empty || _lastRefusal != null) return _lastRefusal;
            return _lastRefusal = _facade.StorageAskRefusal(_lastAsk);
        }
    }
}
