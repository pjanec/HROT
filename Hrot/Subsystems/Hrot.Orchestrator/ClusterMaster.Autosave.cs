using System;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ CE-4099 AUTOSAVE — the MASTER's half (📄 docs/blueprints/Architect_Question_108_Run_Mode_Picker.md §4c A1–A3, §4c.1 option A).
/// Every <see cref="AutosaveEvery"/> while the cluster is EDITING a scenario: the check round (CE-4093) first — only "changed" starts an autosave,
/// which IS the save round with <see cref="ScenarioSaveHandlerPayload.Autosave"/> set (the same fan-out, handler, pull and merge; ⛔ no second save).
/// </summary>
/// <remarks>
/// ⭐ A3 — an autosave is NOT a save: it is not a save round for the unsaved check (<c>_saveRounds</c>), so it neither sets nor clears
/// <see cref="SaveFailedSinceLoad"/>; the nodes do not re-baseline (<c>BaselineAfterSaveHandler</c>); a failure only logs.
/// ⛔ Never outside <see cref="ClusterState.OperatingEdit"/> (Live, Preview, a review or a replay is a run's world, not the scenario's), never while a
/// real save is in flight, never two at once. The clock is the wall clock: while editing the sim clock is stopped.
/// </remarks>
public sealed partial class ClusterMaster
{
    /// <summary>⭐ A1 — how often an edited scenario is autosaved; <see cref="TimeSpan.Zero"/> = off. Default 2 minutes (run settings: <c>autosaveMinutes</c>).</summary>
    public TimeSpan AutosaveEvery { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The wall clock the interval is measured on (seconds); a rail replaces it.</summary>
    internal Func<double> AutosaveClock { get; set; } = UtcNowSeconds;

    /// <summary>Rails and diagnostics: autosave rounds started since the process began.</summary>
    public int AutosavesStarted { get; private set; }

    private double _autosaveDueAt = double.NaN;   // NaN = not scheduled (not editing)
    private Guid _autosaveCheck;                  // the check round this autosave waits on
    private Guid _autosaveRound;                  // the save round in flight
    private double _autosaveBusySince;            // when the pending check / round began — a lost one (a node gone, a failed check) is given up

    /// <summary>A check or round that has not answered in this long is given up (never blocks the autosave for the rest of the session).</summary>
    internal const double AutosaveGiveUpSeconds = 120;

    private void TickAutosave()
    {
        bool editing = _currentDsmState == ClusterState.OperatingEdit && !string.IsNullOrEmpty(_currentScenarioId) && AutosaveEvery > TimeSpan.Zero;
        if (!editing) { _autosaveDueAt = double.NaN; _autosaveCheck = Guid.Empty; return; }
        double now = AutosaveClock();
        if (double.IsNaN(_autosaveDueAt)) { _autosaveDueAt = now + AutosaveEvery.TotalSeconds; return; }   // the interval starts when editing does
        if (_autosaveCheck != Guid.Empty || _autosaveRound != Guid.Empty)
        {
            if (now - _autosaveBusySince < AutosaveGiveUpSeconds) return;
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4099: an autosave {0} never answered — given up.", _autosaveCheck != Guid.Empty ? "check" : "round");
            _autosaveCheck = Guid.Empty; _autosaveRound = Guid.Empty;
        }
        if (now < _autosaveDueAt) return;
        _autosaveDueAt = now + AutosaveEvery.TotalSeconds;
        if (_saveRounds.Count > 0 || _saveMerges.Count > 0) return;   // a real save is running: it covers this interval
        _autosaveCheck = Guid.NewGuid();
        _autosaveBusySince = now;
        StartUnsavedCheck(_autosaveCheck, then: OnAutosaveCheck);
    }

    /// <summary>The check round answered: "changed" ⇒ the autosave round; "unchanged" ⇒ nothing is written.</summary>
    private void OnAutosaveCheck(UnsavedCheckResult result)
    {
        _autosaveCheck = Guid.Empty;
        if (!result.Changed || _currentDsmState != ClusterState.OperatingEdit || string.IsNullOrEmpty(_currentScenarioId)) return;
        var nodeIds = Participants();
        if (nodeIds.Count == 0) return;
        var txId = Guid.NewGuid();
        _eventBus.PublishManaged(new SaveScenarioJsonBegunEvent { TransactionId = txId, ScenarioName = _currentScenarioId!, Autosave = true });
        FanOutSerializeLocal(txId, nodeIds, new Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload(_currentScenarioId!, Autosave: true));
        _autosaveRound = txId;
        _autosaveBusySince = AutosaveClock();
        AutosavesStarted++;
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4099: autosave of '{0}' → {1} node(s).", _currentScenarioId, nodeIds.Count);
    }

    /// <summary>The autosave's node round completed (a failed one only logs — A3).</summary>
    private void NoteAutosaveRoundDone(Guid txId, bool failed)
    {
        if (txId != _autosaveRound) return;
        _autosaveRound = Guid.Empty;
        if (failed) FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4099: the autosave of '{0}' failed on a node — skipped; the next interval takes it.", _currentScenarioId ?? "");
    }
}
