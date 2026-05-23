using Fdp.Core;

namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Abstract base class providing common breakpoint management, pause state, and
/// trace observer delegation for AI subsystem debug sessions.
/// </summary>
public abstract class AiDebugSessionBase : IAiDebugSession
{
    private readonly List<Breakpoint> _breakpoints = new();
    private int _nextBreakpointId = 1;

    protected readonly AiTracerCoordinator Coordinator;

    public event Action? OnSessionStateChanged;

    protected AiDebugSessionBase(AiTracerCoordinator? coordinator = null)
    {
        Coordinator = coordinator ?? new AiTracerCoordinator();
        IsAttached = true;
    }

    public bool IsAttached { get; protected set; }

    public bool IsPaused { get; protected set; }

    public Breakpoint? PausedAt { get; protected set; }

    public Entity? PausedOnEntity { get; protected set; }

    public void Detach()
    {
        IsAttached = false;
        _breakpoints.Clear();
        OnDetachImpl();
        OnSessionStateChanged?.Invoke();
    }

    public BreakpointId SetBreakpoint(Guid assetId, Guid elementId)
    {
        var id = new BreakpointId(_nextBreakpointId++);
        var bp = new Breakpoint(id, assetId, elementId, 0, true, $"bp{id.Value}");
        _breakpoints.Add(bp);
        OnSessionStateChanged?.Invoke();
        return id;
    }

    public void ClearBreakpoint(BreakpointId id)
    {
        _breakpoints.RemoveAll(bp => bp.Id == id);
        OnSessionStateChanged?.Invoke();
    }

    public void ClearAllBreakpoints()
    {
        _breakpoints.Clear();
        OnSessionStateChanged?.Invoke();
    }

    public IReadOnlyList<Breakpoint> GetBreakpoints() => _breakpoints.AsReadOnly();

    public bool IsAnyBreakpointActive => _breakpoints.Exists(bp => bp.Enabled);

    public void Continue()
    {
        if (!IsPaused) return;
        IsPaused = false;
        PausedAt = null;
        PausedOnEntity = null;
        OnContinueImpl();
        OnSessionStateChanged?.Invoke();
    }

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;
        OnPauseImpl();
        OnSessionStateChanged?.Invoke();
    }

    public void StepOver()
    {
        OnStepOverImpl();
        OnSessionStateChanged?.Invoke();
    }

    public void StepInto()
    {
        OnStepIntoImpl();
        OnSessionStateChanged?.Invoke();
    }

    public void StepOut()
    {
        OnStepOutImpl();
        OnSessionStateChanged?.Invoke();
    }

    // IAiTraceObserver delegation
    public void BeginObservingAsset(Guid assetId, TraceLevel level) =>
        Coordinator.AddObserver(assetId, level);

    public void EndObservingAsset(Guid assetId) =>
        Coordinator.RemoveObserver(assetId);

    public virtual IReadOnlyList<Entity> GetActiveEntities(Guid assetId) =>
        Array.Empty<Entity>();

    protected abstract void OnContinueImpl();
    protected abstract void OnPauseImpl();
    protected abstract void OnStepOverImpl();
    protected abstract void OnStepIntoImpl();
    protected abstract void OnStepOutImpl();
    protected virtual void OnDetachImpl() { }

    // Allows derived classes to raise the session-state-changed event.
    protected void RaiseSessionStateChanged() => OnSessionStateChanged?.Invoke();
}
