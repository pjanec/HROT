using System.Collections.Generic;
using Fdp.Toolkit.Runner;
using Hrot.UI.Common.RunMode;

namespace Hrot.ClusterRunner.Presentation;

/// <summary>
/// ⭐⭐⭐ <c>CE-4101</c> follow-up (AQ108 §2.11a ④) — <b>the ONE exit guard of the open scenario</b>, composed by <see cref="LocalWindowController"/> over the PICKED run-controls owner's
/// <see cref="ScenarioDoor"/> (the same owner rule as ▶ and the scenario menu): the exit asks the door, which decides — a run ⇒ stopped, unsaved scenario edits ⇒ <i>Save / Discard / Cancel</i> through the
/// owner's confirmation, never saved ⇒ <i>Discard / Cancel</i>, nothing open ⇒ exits at once. <i>Cancel</i> leaves <see cref="ExitApproved"/> false, so the frame loop does not take the exit.
/// ⛔ No host implements its own scenario exit guard — the editor's <c>IAppExitGuard</c> is about DOCUMENTS, a different thing to lose.
/// </summary>
internal sealed class ScenarioExitGuard : IAppExitGuard
{
    private readonly ScenarioDoor _door;
    private bool _approved;

    internal ScenarioExitGuard(ScenarioDoor door) => _door = door ?? throw new System.ArgumentNullException(nameof(door));

    /// <summary>The picked owner's door this guard asks.</summary>
    internal ScenarioDoor Door => _door;

    /// <inheritdoc/>
    public ExitDisposition OnExitRequested()
    {
        if (_door.IsBusy) return ExitDisposition.Deferred;   // a question is open / a check round is running: not asked twice (the contract: idempotent while a prompt is open)
        _approved = false;
        _door.Leave(LeaveReason.Exit, then: () => _approved = true);
        return _approved ? ExitDisposition.CanExit : ExitDisposition.Deferred;
    }

    /// <inheritdoc/>
    public bool ExitApproved => _approved;
}

/// <summary>
/// ⭐⭐ <c>CE-4101</c> follow-up — the process's exit guards as ONE ordered chain, so two questions can both appear WITHOUT either one's approval taking the exit while the other is still open
/// (the frame loop terminates as soon as ANY guard approves). Each guard is asked only after the one before it approved; a Cancel in any of them leaves the chain waiting and the exit is not taken
/// (the next exit request starts the chain again).
/// <para>⭐ <b>The order, and why</b> (built by <see cref="LocalWindowController"/>): the subsystems' guards first (the editor's unsaved DOCUMENTS — a local question whose Save is harmless and whose Discard
/// loses nothing until the exit goes out), the scenario door LAST: its approval is what stops the run / unloads the scenario, so that destructive step happens only once nothing
/// asked before it can still cancel the exit.</para>
/// </summary>
internal sealed class ExitGuardChain : IAppExitGuard
{
    private readonly IReadOnlyList<IAppExitGuard> _guards;
    private int _current = -1;
    private bool _approved;

    internal ExitGuardChain(IReadOnlyList<IAppExitGuard> guards) => _guards = guards;

    /// <summary>The guards in the order they are asked.</summary>
    internal IReadOnlyList<IAppExitGuard> Guards => _guards;

    /// <inheritdoc/>
    public ExitDisposition OnExitRequested()
    {
        _approved = false;
        return Ask(0);
    }

    /// <inheritdoc/>
    /// <remarks>Polled once a frame: when the guard being waited on approves, the NEXT one is asked; the chain approves when the last has.</remarks>
    public bool ExitApproved
    {
        get
        {
            if (_approved) return true;
            if (_current < 0 || !_guards[_current].ExitApproved) return false;
            return Ask(_current + 1) == ExitDisposition.CanExit;
        }
    }

    private ExitDisposition Ask(int from)
    {
        for (int i = from; i < _guards.Count; i++)
            if (_guards[i].OnExitRequested() == ExitDisposition.Deferred) { _current = i; return ExitDisposition.Deferred; }
        _current = -1;
        _approved = true;
        return ExitDisposition.CanExit;
    }
}
