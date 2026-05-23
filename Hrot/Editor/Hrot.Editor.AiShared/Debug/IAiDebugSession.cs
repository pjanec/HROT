using Fdp.Core;

namespace Hrot.Editor.AiShared.Debug;

public interface IAiDebugSession : IAiTraceObserver
{
    bool IsAttached { get; }
    void Detach();

    BreakpointId SetBreakpoint(Guid assetId, Guid elementId);
    void ClearBreakpoint(BreakpointId id);
    void ClearAllBreakpoints();
    IReadOnlyList<Breakpoint> GetBreakpoints();
    bool IsAnyBreakpointActive { get; }

    bool IsPaused { get; }
    Breakpoint? PausedAt { get; }
    Entity? PausedOnEntity { get; }
    void Continue();
    void StepOver();
    void StepInto();
    void StepOut();
    void Pause();

    event Action? OnSessionStateChanged;
}
