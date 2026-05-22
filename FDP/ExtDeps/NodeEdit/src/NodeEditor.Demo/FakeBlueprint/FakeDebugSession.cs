using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>
/// Scripted debug session that cycles through nodes to simulate execution visualization.
/// </summary>
public sealed class FakeDebugSession : IDebugSession
{
    private readonly HashSet<NodeId> _recentlyExecuted = new();
    private readonly HashSet<NodeId> _breakpoints      = new();
    private readonly HashSet<PinId>  _watchedPins      = new();
    private readonly NodeId[]        _cycle;
    private int                      _cycleIndex;
    private double                   _lastAdvance;

    public bool      IsAttached  { get; private set; }
    public bool      IsPaused    { get; private set; }
    public NodeId?   CurrentlyExecutingNode { get; private set; }
    public IReadOnlySet<NodeId> RecentlyExecutedNodes => _recentlyExecuted;
    public IReadOnlySet<NodeId> Breakpoints           => _breakpoints;
    public IReadOnlySet<PinId>  WatchedPins           => _watchedPins;

    public event System.Action? StateChanged;

    public FakeDebugSession(params NodeId[] cycleNodes)
    {
        _cycle = cycleNodes.Length > 0 ? cycleNodes : Array.Empty<NodeId>();
    }

    public void Attach()
    {
        IsAttached  = true;
        _cycleIndex = 0;
        _lastAdvance = 0;
        IsPaused    = false;
        if (_cycle.Length > 0)
            CurrentlyExecutingNode = _cycle[0];
        StateChanged?.Invoke();
    }

    public void Detach()
    {
        IsAttached             = false;
        IsPaused               = false;
        CurrentlyExecutingNode = null;
        _recentlyExecuted.Clear();
        StateChanged?.Invoke();
    }

    /// <summary>Call once per frame with elapsed seconds.</summary>
    public void Update(double nowSeconds)
    {
        if (!IsAttached || IsPaused || _cycle.Length == 0) return;

        if (nowSeconds - _lastAdvance > 0.5)
        {
            _lastAdvance = nowSeconds;
            if (CurrentlyExecutingNode is { } prev)
                _recentlyExecuted.Add(prev);

            _cycleIndex = (_cycleIndex + 1) % _cycle.Length;
            CurrentlyExecutingNode = _cycle[_cycleIndex];

            if (_breakpoints.Contains(_cycle[_cycleIndex]))
            {
                IsPaused = true;
                StateChanged?.Invoke();
                return;
            }

            // Prune recently-executed after a few steps
            if (_recentlyExecuted.Count > 4)
            {
                var oldest = _recentlyExecuted.First();
                _recentlyExecuted.Remove(oldest);
            }
        }
    }

    public void ToggleBreakpoint(NodeId node)
    {
        if (!_breakpoints.Add(node)) _breakpoints.Remove(node);
        StateChanged?.Invoke();
    }

    public void ToggleWatch(PinId pin)
    {
        if (!_watchedPins.Add(pin)) _watchedPins.Remove(pin);
        StateChanged?.Invoke();
    }

    public void Continue()
    {
        if (!IsPaused) return;
        IsPaused = false;
        StateChanged?.Invoke();
    }

    public void StepOver()   { /* single step not implemented in demo */ }
    public void StepInto()   { /* single step not implemented in demo */ }
    public void StepOut()    { /* single step not implemented in demo */ }

    public object? GetWatchValue(PinId pin) => null;
}
