namespace Hrot.Editor.AiShared.Debug;

public interface IDebugSessionRegistry
{
    bool TryAcquireSession<TSession>(out TSession? session) where TSession : class, IAiDebugSession;
    void ReleaseSession(IAiDebugSession session);
    IDisposable RegisterObserver<TObserver>(TObserver observer) where TObserver : IAiTraceObserver;
    IReadOnlyList<IAiTraceObserver> ActiveObservers { get; }
    IAiDebugSession? ActiveSession { get; }

    /// <summary>
    /// Directly sets the active session WITHOUT any attach/detach side effects, firing <see cref="Changed"/>
    /// when the value changes. Used by the composition root to make UI surfaces (e.g. the main toolbar) mirror
    /// the active document's debug session. Unlike <see cref="ReleaseSession"/> this never calls Detach(), so it
    /// is safe for eagerly-attached, long-lived sessions (the blueprint session).
    /// </summary>
    void SetActiveSession(IAiDebugSession? session);

    event Action? Changed;
}
