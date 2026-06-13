namespace Hrot.Editor.AiShared.Debug;

public sealed class DebugSessionRegistry : IDebugSessionRegistry
{
    private readonly Dictionary<Type, Func<IAiDebugSession>> _factories = new();
    private readonly List<IAiTraceObserver> _observers = new();
    private readonly object _lock = new();

    public IAiDebugSession? ActiveSession { get; private set; }
    public event Action? Changed;

    public IReadOnlyList<IAiTraceObserver> ActiveObservers => _observers.AsReadOnly();

    /// <summary>
    /// Registers a factory for TSession. TryAcquireSession uses this to create sessions on demand.
    /// </summary>
    public void RegisterSessionFactory<TSession>(Func<TSession> factory)
        where TSession : class, IAiDebugSession
    {
        _factories[typeof(TSession)] = () => factory();
    }

    public bool TryAcquireSession<TSession>(out TSession? session)
        where TSession : class, IAiDebugSession
    {
        lock (_lock)
        {
            if (ActiveSession is not null)
            {
                session = null;
                return false;
            }

            if (!_factories.TryGetValue(typeof(TSession), out var factory))
            {
                session = null;
                return false;
            }

            var created = factory();
            ActiveSession = created;
            session = (TSession)created;
        }
        Changed?.Invoke();
        return true;
    }

    public void ReleaseSession(IAiDebugSession session)
    {
        bool released;
        lock (_lock)
        {
            released = ActiveSession == session;
            if (released) ActiveSession = null;
        }
        if (released)
        {
            session.Detach();
            Changed?.Invoke();
        }
    }

    public void SetActiveSession(IAiDebugSession? session)
    {
        bool changed;
        lock (_lock)
        {
            changed = !ReferenceEquals(ActiveSession, session);
            if (changed) ActiveSession = session;
        }
        if (changed) Changed?.Invoke();
    }

    public IDisposable RegisterObserver<TObserver>(TObserver observer)
        where TObserver : IAiTraceObserver
    {
        _observers.Add(observer);
        Changed?.Invoke();
        return new RemoveToken(() =>
        {
            _observers.Remove(observer);
            Changed?.Invoke();
        });
    }

    private sealed class RemoveToken : IDisposable
    {
        private Action? _remove;

        public RemoveToken(Action remove) => _remove = remove;

        public void Dispose()
        {
            _remove?.Invoke();
            _remove = null;
        }
    }
}
