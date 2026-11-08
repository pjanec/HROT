using System;
using Fdp.Toolkit.Orchestration;
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
    private Guid _lastAsk;
    private string? _lastRefusal;

    /// <param name="facade">The host's ONE time facade (its ClusterView holds the jobs).</param>
    /// <param name="seat">This station's seat.</param>
    /// <param name="station">This station's name (the ask's <c>AskedBy</c>: the job is then marked <i>(yours)</i>).</param>
    /// <param name="open">Opens the recording of an exercise (the host's run choice: the AAR); null = <i>Open</i> is not offered.</param>
    public StorageTransfers(ITimeTransportFacade facade, Func<Seat> seat, Func<string> station, Action<Guid>? open = null)
    {
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
