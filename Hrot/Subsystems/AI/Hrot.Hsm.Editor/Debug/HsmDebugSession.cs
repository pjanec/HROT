using Fdp.Core;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Hsm.Editor.Debug;

/// <summary>
/// Production implementation of IHsmDebugSession.
/// Maintains a unified in-memory ring buffer of HSM trace records.
/// GetCurrentStateSnapshot() returns null until kernel wiring is in place (Slice 3+).
/// Step-control methods are no-ops until the kernel adapter is wired.
/// </summary>
public sealed class HsmDebugSession : AiDebugSessionBase, IHsmDebugSession
{
    private const int MaxHistory = 200;

    private readonly List<HsmTraceRecord> _history = new();

    public event Action<HsmBreakpointHit>?  OnBreakpointHit;
    public event Action<HsmStateEntered>?   OnStateEntered;
    public event Action<HsmStateExited>?    OnStateExited;
    public event Action<HsmTransitionFired>? OnTransitionFired;
    public event Action<HsmEventQueued>?    OnEventQueued;
    public event Action<HsmRegionConflict>? OnRegionConflict;
    public event Action<HsmGuardEvaluated>? OnGuardEvaluated;
    public event Action<HsmTimerEvent>?     OnTimerEvent;

    // ---- IHsmDebugSession ------------------------------------------------

    // Returns null until the kernel snapshot adapter is implemented (Slice 3+).
    public HsmInstanceSnapshot? GetCurrentStateSnapshot() => null;

    public IReadOnlyList<HsmTraceRecord> GetRecentTraceHistory(int max = 100)
    {
        int start = Math.Max(0, _history.Count - max);
        return _history.GetRange(start, _history.Count - start);
    }

    // ---- Kernel adapter entry points (called by future kernel adapter) ---

    /// <summary>Records a kernel trace event and fires the appropriate typed event.</summary>
    public void RecordTrace(HsmTraceRecord record)
    {
        if (_history.Count >= MaxHistory)
            _history.RemoveAt(0);
        _history.Add(record);

        switch (record)
        {
            case HsmStateEntered    e: OnStateEntered?.Invoke(e);    break;
            case HsmStateExited     e: OnStateExited?.Invoke(e);     break;
            case HsmTransitionFired e: OnTransitionFired?.Invoke(e); break;
            case HsmEventQueued     e: OnEventQueued?.Invoke(e);     break;
            case HsmRegionConflict  e: OnRegionConflict?.Invoke(e);  break;
            case HsmGuardEvaluated  e: OnGuardEvaluated?.Invoke(e);  break;
            case HsmTimerEvent      e: OnTimerEvent?.Invoke(e);      break;
        }
    }

    /// <summary>Called by the kernel adapter when a breakpoint fires.</summary>
    public void RaiseBreakpointHit(HsmBreakpointHit hit)
    {
        IsPaused = true;
        PausedAt = hit.Breakpoint;
        PausedOnEntity = hit.Self;
        OnBreakpointHit?.Invoke(hit);
        RaiseSessionStateChanged();
    }

    // ---- AiDebugSessionBase overrides (no-ops until kernel wiring) ------

    protected override void OnContinueImpl()   { }
    protected override void OnPauseImpl()      { }
    protected override void OnStepOverImpl()   { }
    protected override void OnStepIntoImpl()   { }
    protected override void OnStepOutImpl()    { }

    protected override void OnDetachImpl() => _history.Clear();
}
