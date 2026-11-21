using System;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Hrot.UI.Common.RunMode;

namespace Hrot.ClusterRunner.Presentation;

/// <summary>
/// ⭐⭐ CE-4330 ⑨ (📄 docs/DESIGN_Recording_Storage.md §3 P3, §5a, §8 ⑥) — <b>the quit guard of a running transfer</b>, composed ONLY on the host whose master runs it
/// (<see cref="Hrot.Presentation.DebugApi.IHostsClusterMaster.RunningTransfer"/>): closing while an archive runs asks <i>Wait · Close anyway — the exercise stays on the
/// nodes and the archive resumes next time</i> (⑧ resumes it); while an import runs, <i>Close anyway</i> cancels it (its partial files go; nothing on the NAS is touched).
/// The question goes through the run choice's ONE pending slot (<see cref="RunModeChoice.Confirm"/>), which the toolbar draws — ⛔ a deferred exit is never left without a prompt.
/// </summary>
internal sealed class TransferExitGuard : IAppExitGuard
{
    private readonly Func<StorageJob?> _running;
    private readonly RunModeChoice _choice;
    private readonly Action<Guid> _cancel;
    private bool _approved;
    private bool _asking;

    /// <param name="running">The transfer this host's master runs now.</param>
    /// <param name="choice">The run choice whose pending slot the toolbar draws.</param>
    /// <param name="cancel">Cancels a job (the master's <c>CancelOperation</c>).</param>
    internal TransferExitGuard(Func<StorageJob?> running, RunModeChoice choice, Action<Guid> cancel)
    {
        _running = running ?? throw new ArgumentNullException(nameof(running));
        _choice  = choice  ?? throw new ArgumentNullException(nameof(choice));
        _cancel  = cancel  ?? throw new ArgumentNullException(nameof(cancel));
    }

    /// <summary>The question for <paramref name="job"/> (its title and text); the ONE wording, read by the rail too.</summary>
    internal static (string Title, string Text) QuestionFor(StorageJob job)
    {
        var ex = ((Guid)job.ExerciseId).ToString("N").Substring(0, 8);
        var files = job.FilesTotal > 0 ? $" ({job.FilesDone} of {job.FilesTotal} files)" : "";
        return job.Kind == StorageJobKind.Import
            ? ("Import running", $"Exercise {ex} is still being imported{files}. Close anyway cancels the import — nothing on the NAS is touched.")
            : ("Archive running", $"Exercise {ex} is still being archived{files}. Close anyway — the exercise stays on the nodes and the archive resumes next time.");
    }

    /// <inheritdoc/>
    public ExitDisposition OnExitRequested()
    {
        if (_asking && _choice.Pending is not null) return ExitDisposition.Deferred;   // idempotent while the question is open
        _approved = false;
        _asking = false;
        if (_running() is not { } job) { _approved = true; return ExitDisposition.CanExit; }
        var (title, text) = QuestionFor(job);
        _asking = true;
        _choice.Confirm(title, text, "Wait", new RunModeConfirmation.Button("Close anyway", () =>
        {
            if (job.Kind == StorageJobKind.Import) _cancel(job.JobId);   // an import is cancelled; an archive is left to resume next time (⑧)
            _approved = true;
        }));
        return ExitDisposition.Deferred;
    }

    /// <inheritdoc/>
    public bool ExitApproved => _approved;
}
