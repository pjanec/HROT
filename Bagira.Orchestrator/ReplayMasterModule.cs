using System;

namespace Bagira.Orchestrator;

/// <summary>
/// Orchestrator-side module that controls the cluster time scale during
/// Live-from-Replay transitions (CGF1-S0305).
///
/// <para>
/// Before the orchestrator issues a <c>PrepareLive</c> command from
/// <see cref="Bagira.BDC.SSTD.Orchestration.DSMState.RunningReplay"/>, it calls
/// <see cref="FreezeTime"/> to hard-freeze the time scale to <c>0.0</c>.
/// Once all nodes have ACKed the transition, it calls <see cref="RestoreTime"/> to
/// resume the previously-active time scale.
/// </para>
///
/// <para>
/// The module is wired into <see cref="DrillMaster"/> via
/// <see cref="DrillMaster.SetReplayMasterModule"/>.  The hosting application
/// (e.g. <c>OrchestratorSubsystem</c>) creates the module with the
/// <c>setTimeScale</c> and <c>getTimeScale</c> callbacks that delegate to the
/// active <c>ITimeController</c>.
/// </para>
/// </summary>
public sealed class ReplayMasterModule
{
    private readonly Action<float> _setTimeScale;
    private readonly Func<float>   _getTimeScale;
    private float _savedScale = 1.0f;

    /// <param name="setTimeScale">
    /// Callback that sets the time scale on the active time controller.
    /// Receives <c>0.0f</c> to freeze and the saved scale to resume.
    /// </param>
    /// <param name="getTimeScale">
    /// Callback that reads the current time scale from the active time controller.
    /// Used to capture the scale before freezing so it can be restored later.
    /// </param>
    public ReplayMasterModule(Action<float> setTimeScale, Func<float> getTimeScale)
    {
        _setTimeScale = setTimeScale ?? throw new ArgumentNullException(nameof(setTimeScale));
        _getTimeScale = getTimeScale ?? throw new ArgumentNullException(nameof(getTimeScale));
    }

    /// <summary>
    /// Saves the current time scale and sets it to <c>0.0f</c> (hard freeze).
    /// Call before fan-out of <c>PrepareLive</c> from <c>RunningReplay</c>.
    /// </summary>
    public void FreezeTime()
    {
        _savedScale = _getTimeScale();
        _setTimeScale(0.0f);
    }

    /// <summary>
    /// Restores the time scale that was saved by the last <see cref="FreezeTime"/> call.
    /// Call after all nodes have ACKed the branch transition.
    /// </summary>
    public void RestoreTime()
    {
        _setTimeScale(_savedScale);
    }

    /// <summary>
    /// The time scale currently reported by the active time controller.
    /// </summary>
    public float CurrentTimeScale => _getTimeScale();

    /// <summary>
    /// The time scale that was active when <see cref="FreezeTime"/> was last called.
    /// Returns <c>1.0f</c> if the time was never frozen.
    /// </summary>
    public float SavedTimeScale => _savedScale;
}
