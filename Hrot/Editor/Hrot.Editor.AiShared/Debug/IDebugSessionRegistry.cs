namespace Hrot.Editor.AiShared.Debug;

public interface IDebugSessionRegistry
{
    bool TryAcquireSession<TSession>(out TSession? session) where TSession : class, IAiDebugSession;
    void ReleaseSession(IAiDebugSession session);
    IDisposable RegisterObserver<TObserver>(TObserver observer) where TObserver : IAiTraceObserver;
    IReadOnlyList<IAiTraceObserver> ActiveObservers { get; }
    IAiDebugSession? ActiveSession { get; }
    event Action? Changed;
}
